using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text;

namespace VuluLauncher
{
    /// <summary>Masaüstü kısayolundan gelen istek: bir paketi oyna ya da bir sunucuya katıl.</summary>
    internal sealed record StartupAction(string Kind, string Id)
    {
        /// <summary>"--play &lt;instance kimliği&gt;" ya da "--join &lt;sunucu kimliği&gt;"; yoksa null.</summary>
        public static StartupAction? Parse(string[] args)
        {
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == "--play") return Valid("play", args[i + 1]);
                if (args[i] == "--join") return Valid("join", args[i + 1]);
            }
            return null;
        }

        public static StartupAction? FromMessage(string message)
        {
            int tab = message.IndexOf('\t');
            return tab < 0 ? null : Valid(message[..tab], message[(tab + 1)..].Trim());
        }

        public string ToMessage() => $"{Kind}\t{Id}";

        private static StartupAction? Valid(string kind, string id) =>
            (kind == "play" && id.Length is > 0 and <= 120 && !id.Any(char.IsControl))
            || (kind == "join" && System.Text.RegularExpressions.Regex.IsMatch(id, "^[0-9a-f]{24}$"))
                ? new StartupAction(kind, id) : null;
    }

    /// <summary>
    /// Launcher'ın tek kopya çalışması. Kısayola tıklanınca launcher zaten açıksa yeni kopya açılmaz:
    /// istek açık olana yerel bir boru (named pipe) üzerinden iletilir, o da pencereyi öne getirir.
    /// Boru yalnızca aynı Windows kullanıcısına açıktır.
    /// </summary>
    internal sealed class SingleInstance : IDisposable
    {
        private static readonly string Key = "vuluLauncher-" + string.Concat(Environment.UserName.Where(char.IsLetterOrDigit));
        private Mutex? _mutex;
        private CancellationTokenSource? _stop;

        /// <summary>İlk kopya bu süreçse true (kilit alınır).</summary>
        public bool TryAcquire()
        {
            if (_mutex != null) return true;
            var mutex = new Mutex(true, @"Local\" + Key, out bool created);
            if (created) { _mutex = mutex; return true; }
            mutex.Dispose();
            return false;
        }

        /// <summary>Updater'a devretmeden önce: Updater'ın açacağı launcher kilidi alabilsin.</summary>
        public void Release()
        {
            if (_mutex == null) return;
            try { _mutex.ReleaseMutex(); } catch { }
            _mutex.Dispose();
            _mutex = null;
        }

        /// <summary>Açık olan kopyaya isteği (ya da yalnızca "öne gel") iletir.</summary>
        public static void Forward(StartupAction? action)
        {
            try
            {
                AllowSetForegroundWindow(-1);   // açık kopya penceresini öne getirebilsin
                using var pipe = new NamedPipeClientStream(".", Key, PipeDirection.Out, PipeOptions.CurrentUserOnly);
                pipe.Connect(3000);
                byte[] data = Encoding.UTF8.GetBytes((action?.ToMessage() ?? "show") + "\n");
                pipe.Write(data);
                pipe.Flush();
            }
            catch { /* açık kopya cevap vermiyorsa yapılacak bir şey yok */ }
        }

        /// <summary>Diğer kopyalardan gelen istekleri dinler (arka planda).</summary>
        public void Listen(Action<string> onMessage)
        {
            _stop = new CancellationTokenSource();
            var stop = _stop.Token;
            _ = Task.Run(async () =>
            {
                while (!stop.IsCancellationRequested)
                {
                    try
                    {
                        await using var server = new NamedPipeServerStream(Key, PipeDirection.In, 1, PipeTransmissionMode.Byte,
                            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                        await server.WaitForConnectionAsync(stop);
                        using var reader = new StreamReader(server, Encoding.UTF8);
                        var line = await reader.ReadLineAsync(stop);
                        if (line != null && line.Length <= 512) onMessage(line);
                    }
                    catch (OperationCanceledException) { return; }
                    catch { await Task.Delay(500, stop).ContinueWith(_ => { }); }
                }
            });
        }

        public void Dispose()
        {
            _stop?.Cancel();
            Release();
        }

        [DllImport("user32.dll")] private static extern bool AllowSetForegroundWindow(int processId);
    }
}
