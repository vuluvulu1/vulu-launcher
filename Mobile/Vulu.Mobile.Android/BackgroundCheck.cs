using System.Text.Json;
using Android.Content;
using Android.Gms.Common;
using AndroidX.Work;
using VuluLauncher;
using VuluMobile.Services;

namespace VuluMobile.Android
{
    /// <summary>
    /// Google hizmetleri olmayan telefonlarda (Huawei vb.) push gelmez. Onlar için Android 15 dakikada bir bu işi
    /// çalıştırır: kayıtlı girişle oturum açılır; yeni mesaj, arkadaşlık isteği, beğeni ve yorum varsa bildirim
    /// gösterilir. Google hizmetleri olan telefonlarda hiç planlanmaz (push yeter).
    /// </summary>
    internal static class BackgroundCheck
    {
        private const string WorkName = "vulu-sosyal-check";

        public static void Schedule(Context context)
        {
            try
            {
                var work = WorkManager.GetInstance(context);
                bool hasGoogle = GoogleApiAvailability.Instance.IsGooglePlayServicesAvailable(context) == ConnectionResult.Success;
                if (hasGoogle) { work.CancelUniqueWork(WorkName); return; }
                var request = new PeriodicWorkRequest.Builder(typeof(CheckWorker), TimeSpan.FromMinutes(15))
                    .SetConstraints(new Constraints.Builder().SetRequiredNetworkType(NetworkType.Connected!).Build())
                    .Build();
                work.EnqueueUniquePeriodicWork(WorkName, ExistingPeriodicWorkPolicy.Keep!, (PeriodicWorkRequest)request);
            }
            catch (Exception ex) { global::Android.Util.Log.Warn("vulu", "Yedek kontrol planlanamadı: " + ex.Message); }
        }
    }

    public sealed class CheckWorker(Context context, WorkerParameters parameters) : Worker(context, parameters)
    {
        public override Result DoWork()
        {
            try { RunAsync().GetAwaiter().GetResult(); }
            catch (Exception ex) { global::Android.Util.Log.Warn("vulu", "Yedek kontrol: " + ex.Message); }
            return Result.InvokeSuccess();
        }

        private async Task RunAsync()
        {
            // Uygulama açıkken zaten kendisi soruyor
            if (PushHub.InForeground && MainActivity.Current != null) return;
            var saved = SavedLogin.Load();
            if (saved == null) return;
            var login = new LoginService();
            if (!(await login.LoginAsync(saved.Value.Username, saved.Value.Password)).Success) return;
            var api = new ApiClient(() => login.Token);
            var prefs = ApplicationContext.GetSharedPreferences("vulu-check", FileCreationMode.Private)!;
            string since = prefs.GetString("since", null) ?? DateTime.UtcNow.AddMinutes(-15).ToString("o");
            string query = "?since=" + Uri.EscapeDataString(since);
            var ctx = ApplicationContext;

            var updates = await api.GetAsync<JsonElement>("/messages/updates" + query);
            foreach (var group in updates.GetProperty("messages").EnumerateArray().GroupBy(m => m.GetProperty("from").GetString() ?? ""))
            {
                int count = group.Count();
                Notifications.Show(ctx, Notifications.MessagesChannel, "message-" + group.Key, "message", group.Key, group.Key,
                    count == 1 ? "Yeni şifreli mesaj" : $"{count} yeni şifreli mesaj");
            }

            var events = await api.GetAsync<JsonElement>("/shots/events" + query);
            foreach (var e in events.GetProperty("events").EnumerateArray())
            {
                string from = e.GetProperty("from").GetString() ?? "", shot = e.GetProperty("shotId").GetString() ?? "";
                bool comment = e.GetProperty("kind").GetString() == "comment";
                Notifications.Show(ctx, Notifications.SocialChannel, $"shot-{shot}-{(comment ? "comment" : "like")}", "shot", shot,
                    comment ? "Yeni yorum" : "Yeni beğeni", comment ? $"{from}: {e.GetProperty("text").GetString()}" : $"{from} ekran görüntünü beğendi.");
            }

            // Arkadaşlık istekleri: daha önce görülmemiş gelen istekler
            var friends = await api.GetAsync<JsonElement>("/friends");
            var seen = prefs.GetStringSet("incoming", null)?.ToHashSet() ?? new HashSet<string>();
            var incoming = friends.GetProperty("incoming").EnumerateArray().Select(x => x.GetString() ?? "").Where(x => x.Length > 0).ToList();
            foreach (var name in incoming.Where(n => !seen.Contains(n)))
                Notifications.Show(ctx, Notifications.SocialChannel, "friend-" + name, "friend.request", name,
                    "Arkadaşlık isteği", $"{name} seni arkadaş olarak eklemek istiyor.");

            var edit = prefs.Edit()!;
            if (updates.TryGetProperty("now", out var now) && now.GetString() is { } n) edit.PutString("since", n);
            edit.PutStringSet("incoming", incoming);
            edit.Apply();
        }
    }
}
