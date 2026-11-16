#if DEBUG
using System.Diagnostics;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using Avalonia.VisualTree;
using VuluLauncher.ViewModels;

namespace VuluLauncher
{
    /// <summary>
    /// Yalnızca geliştirmede: VULU_PERF=&lt;dosya&gt; ile launcher kendini ölçer ve sonucu dosyaya yazar. Açılış süresi,
    /// sekme geçişlerinin arayüz iş parçacığındaki maliyeti, kaydırırken kare süreleri, boşta işlemci kullanımı, bellek
    /// ve arayüz iş parçacığının 50 ms'den uzun takıldığı anlar. Ölçüm bitince uygulama kapanır.
    /// </summary>
    internal static class PerfProbe
    {
        private static readonly string? OutPath = Environment.GetEnvironmentVariable("VULU_PERF") is { Length: > 0 } p ? p : null;
        private static readonly StringBuilder Report = new();
        private static readonly List<(double At, double Ms, string Phase)> Stalls = new();
        private static string _phase = "açılış";
        private static bool _firstFrame;

        public static bool Enabled => OutPath != null;

        private static double SinceStart()
        {
            using var process = Process.GetCurrentProcess();
            return (DateTime.Now - process.StartTime).TotalMilliseconds;
        }

        private static void Line(string text)
        {
            lock (Report)
            {
                Report.AppendLine(text);
                try { File.WriteAllText(OutPath!, Report.ToString()); } catch { }
            }
        }

        /// <summary>Program başında: arayüz iş parçacığı takılma izleyicisi başlar.</summary>
        public static void Start()
        {
            if (!Enabled) return;
            var thread = new Thread(() =>
            {
                var sw = Stopwatch.StartNew();
                while (true)
                {
                    double posted = sw.Elapsed.TotalMilliseconds;
                    using var done = new ManualResetEventSlim();
                    Dispatcher.UIThread.Post(() => done.Set(), DispatcherPriority.Input);
                    done.Wait();
                    double lag = sw.Elapsed.TotalMilliseconds - posted;
                    if (lag > 50) lock (Stalls) Stalls.Add((posted, lag, _phase));
                    Thread.Sleep(10);
                }
            }) { IsBackground = true, Name = "perf-lag" };
            thread.Start();
        }

        /// <summary>Açılışta bir an (süreç başlangıcından ms).</summary>
        public static void Mark(string name)
        {
            if (Enabled) Line($"{name,-18}{SinceStart(),8:0} ms");
        }

        /// <summary>Pencerenin ilk karesi çizildi.</summary>
        public static void WindowOpened(Window window)
        {
            if (!Enabled) return;
            window.RequestAnimationFrame(_ => window.RequestAnimationFrame(_ =>
            {
                if (_firstFrame) return;
                _firstFrame = true;
                Line($"ilk kare          {SinceStart(),8:0} ms");
            }));
        }

        private static Task NextFrame(TopLevel top)
        {
            var tcs = new TaskCompletionSource();
            top.RequestAnimationFrame(_ => tcs.TrySetResult());
            return tcs.Task;
        }

        /// <summary>Önizleme modunda açılış bitince ölçüm turu.</summary>
        public static async Task RunAsync(MainViewModel vm)
        {
            if (!Enabled) return;
            var window = (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.Windows.FirstOrDefault(w => w is Views.MainWindow);
            if (window == null) return;
            await Task.Delay(1500);
            await NextFrame(window); await NextFrame(window);
            Line($"kabuk hazır       {SinceStart(),8:0} ms");
            Line($"tema              {Themes.Current} (hareketli: {Views.SynthwaveBackdrop.Animated})");
            Line("");
            await Task.Delay(2500);
            var host = window.FindControl<Views.PageHost>("PageHost")!;

            Line("── sekme geçişi (UI iş parçacığı: VM + görünüm kurma + yerleşim; sonra ilk kareye kadar) ──");
            string[] order = { "discover", "servers", "social", "settings", "home", "social", "settings", "discover", "home", "social" };
            foreach (string key in order)
            {
                var tab = vm.Tabs.First(t => t.Key == key);
                _phase = "sekme:" + key;
                long before = GC.GetAllocatedBytesForCurrentThread();
                var sw = Stopwatch.StartNew();
                vm.SelectTabCommand.Execute(tab);
                window.UpdateLayout();
                double sync = sw.Elapsed.TotalMilliseconds;
                await NextFrame(window); await NextFrame(window);
                double frame = sw.Elapsed.TotalMilliseconds;
                long alloc = GC.GetAllocatedBytesForCurrentThread() - before;
                Line($"{key,-10} senkron {sync,7:0.0} ms   ilk kare {frame,7:0.0} ms   ayrılan {alloc / 1024.0 / 1024.0,6:0.0} MB   görsel öğe {CountVisuals(host.Current ?? host),5}");
                await Task.Delay(1200);
            }
            Line("");

            Line("── kaydırma (her karede 24 px, 90 kare; UI iş parçacığı kare aralıkları) ──");
            foreach (string key in new[] { "home", "discover", "social", "settings" })
            {
                vm.SelectTabCommand.Execute(vm.Tabs.First(t => t.Key == key));
                await Task.Delay(1500);
                _phase = "kaydırma:" + key;
                var scroller = host.GetVisualDescendants().OfType<ScrollViewer>()
                    .Where(s => s.IsEffectivelyVisible && s.Extent.Height > s.Viewport.Height + 50)
                    .OrderByDescending(s => s.Viewport.Height * s.Viewport.Width).FirstOrDefault();
                if (scroller == null) { Line($"{key,-10} kaydırılacak liste yok"); continue; }
                var times = new List<double>();
                using var proc = Process.GetCurrentProcess();
                var cpuStart = proc.TotalProcessorTime;
                var sw = Stopwatch.StartNew();
                double last = 0;
                scroller.Offset = new Vector(0, 0);
                for (int i = 0; i < 90; i++)
                {
                    double y = scroller.Offset.Y + 24;
                    if (y > scroller.Extent.Height - scroller.Viewport.Height) y = 0;
                    scroller.Offset = new Vector(scroller.Offset.X, y);
                    await NextFrame(window);
                    double now = sw.Elapsed.TotalMilliseconds;
                    times.Add(now - last); last = now;
                }
                proc.Refresh();
                double scrollCpu = (proc.TotalProcessorTime - cpuStart).TotalMilliseconds / sw.Elapsed.TotalMilliseconds * 100;
                times.RemoveAt(0);
                times.Sort();
                Line($"{key,-10} işlemci %{scrollCpu,3:0}   ort {times.Average(),5:0.0} ms   p95 {times[(int)(times.Count * 0.95)],5:0.0} ms   en uzun {times[^1],6:0.0} ms   >25 ms: {times.Count(t => t > 25)} kare   (içerik {scroller.Extent.Height:0} px)");
                scroller.Offset = new Vector(0, 0);
            }
            Line("");

            Line("── boşta (5 sn; işlemci = tüm süreç, bir çekirdeğin yüzdesi) ──");
            foreach (string key in new[] { "home", "social", "settings" })
            {
                vm.SelectTabCommand.Execute(vm.Tabs.First(t => t.Key == key));
                _phase = "boşta:" + key;
                await Task.Delay(2500);
                using var process = Process.GetCurrentProcess();
                var cpu0 = process.TotalProcessorTime;
                int layouts = 0;
                void OnLayout(object? s, EventArgs e) => layouts++;
                window.LayoutUpdated += OnLayout;
                var sw = Stopwatch.StartNew();
                await Task.Delay(5000);
                window.LayoutUpdated -= OnLayout;
                process.Refresh();
                double cpu = (process.TotalProcessorTime - cpu0).TotalMilliseconds / sw.Elapsed.TotalMilliseconds * 100;
                Line($"{key,-10} işlemci %{cpu,5:0.0}   yerleşim turu {layouts}");
            }
            Line("");

            Line("── oyun çıktısı: Oyna › Konsol açıkken 80 ms'de bir 20 satır, 4 sn (600 satır sınırı) ──");
            {
                vm.SelectTabCommand.Execute(vm.Tabs[0]);
                vm.Home.InfoTab = "log";
                await Task.Delay(1500);
                _phase = "oyun çıktısı";
                var times = new List<double>();
                var sw = Stopwatch.StartNew();
                double last = 0, worstBatch = 0;
                int n = 0;
                using var process = Process.GetCurrentProcess();
                var cpu0 = process.TotalProcessorTime;
                bool running = true;
                void Tick(TimeSpan _)
                {
                    double now = sw.Elapsed.TotalMilliseconds; times.Add(now - last); last = now;
                    if (running) window.RequestAnimationFrame(Tick);
                }
                window.RequestAnimationFrame(Tick);
                while (sw.ElapsedMilliseconds < 4000)
                {
                    var b = Stopwatch.StartNew();
                    for (int i = 0; i < 20; i++) vm.Home.AppendLog($"[Render thread/INFO]: Loaded chunk {n++} at 12, -4 (mod veri satırı örneği, biraz uzun bir metin)", "game");
                    worstBatch = Math.Max(worstBatch, b.Elapsed.TotalMilliseconds);
                    await Task.Delay(80);
                }
                running = false;
                process.Refresh();
                double cpu = (process.TotalProcessorTime - cpu0).TotalMilliseconds / sw.Elapsed.TotalMilliseconds * 100;
                times.RemoveAt(0); times.Sort();
                Line($"kare ort {times.Average(),5:0.0} ms   p95 {times[(int)(times.Count * 0.95)],5:0.0} ms   en uzun {times[^1],6:0.0} ms   >25 ms: {times.Count(t => t > 25)} / {times.Count}   en ağır 20 satır {worstBatch:0.0} ms   işlemci %{cpu:0}");
                vm.Home.InfoTab = "worlds";
            }
            Line("");

            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
            using (var process = Process.GetCurrentProcess())
                Line($"bellek            çalışma kümesi {process.WorkingSet64 / 1048576.0:0} MB   özel {process.PrivateMemorySize64 / 1048576.0:0} MB   GC yığını {GC.GetTotalMemory(false) / 1048576.0:0} MB");
            Line("");
            Line("── arayüz iş parçacığı 50 ms'den uzun takıldı ──");
            lock (Stalls)
            {
                foreach (var g in Stalls.GroupBy(s => s.Phase))
                    Line($"{g.Key,-20} {g.Count(),3} kez, en uzun {g.Max(s => s.Ms),6:0} ms, toplam {g.Sum(s => s.Ms),6:0} ms");
            }
            File.WriteAllText(OutPath!, Report.ToString());
            Environment.Exit(0);
        }

        private static int CountVisuals(Visual root) => root.GetVisualDescendants().Count();
    }
}
#endif
