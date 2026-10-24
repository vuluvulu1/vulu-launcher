using System.Text.Json;

namespace VuluLauncher
{
    internal sealed partial class LauncherApp
    {
        // ════════════════════════════════════════════════════════════════
        // KAPATMA VE "OYUNDA" BİLDİRİMLERİ
        // ════════════════════════════════════════════════════════════════

        /// <summary>
        /// Kapat tuşu: ayar açıksa launcher bildirim alanına küçülür (pencere ve tarayıcı kapanır, bu süreç mesaj ve
        /// bildirim denetimlerini sürdürür), değilse tamamen kapanır.
        /// </summary>
        private void CloseWindow()
        {
            if (AppSettings.Current.CloseToTray && _host.CanHideToTray) _host.HideToTray();
            else _host.Close();
        }

        private readonly object _playingGate = new();
        /// <summary>Kullanıcı → en son görülen oyunu ("paket|başlangıç"). null: henüz ilk liste alınmadı.</summary>
        private Dictionary<string, string>? _playingSeen;
        private string? _playingSeenFor;

        /// <summary>
        /// Biri oyuna girince (ya da başka bir pakete geçince) bildirim. Durum yoklaması zaten giden çevrimiçi
        /// bildirimiyle aynı turda yapılır; ayrı zamanlayıcı yoktur. İlk liste yalnızca kaydedilir: launcher açılınca
        /// zaten oynayanlar için bildirim çıkmaz. Listeden bir süre düşen (bağlantısı kopan) biri aynı oyunla geri
        /// gelirse yeniden bildirilmez.
        /// </summary>
        private async Task CheckPlayingAsync()
        {
            string mode = AppSettings.Current.PlayingNotifications;
            string me = _login.Username;
            if (string.IsNullOrEmpty(me)) return;
            var list = await _api.GetAsync<JsonElement>("/users/presence");
            if (list.ValueKind != JsonValueKind.Array) return;

            var started = new List<(string User, string Instance, bool Friend)>();
            lock (_playingGate)
            {
                bool baseline = _playingSeen == null || _playingSeenFor != me;
                if (baseline) { _playingSeen = new(StringComparer.OrdinalIgnoreCase); _playingSeenFor = me; }

                foreach (var p in list.EnumerateArray())
                {
                    string? user = Str(p, "username");
                    if (string.IsNullOrEmpty(user) || user.Equals(me, StringComparison.OrdinalIgnoreCase)) continue;
                    if (Str(p, "state") != "playing") { _playingSeen!.Remove(user); continue; }

                    string instance = Str(p, "instance") is { Length: > 0 } name ? name : "Minecraft";
                    string key = instance + "|" + Str(p, "since");
                    if (_playingSeen!.TryGetValue(user, out var seen) && seen == key) continue;
                    _playingSeen[user] = key;
                    if (baseline) continue;

                    bool friend = p.TryGetProperty("friend", out var f) && f.ValueKind == JsonValueKind.True;
                    if (mode == "all" || (mode == "friends" && friend)) started.Add((user, instance, friend));
                }
            }

            // Aynı anda çok kişi girerse bildirim yağmuru olmasın
            foreach (var (user, instance, friend) in started.Take(3))
            {
                ShowNotification($"{user} oyunda", $"{instance} oynuyor", "profile:" + user);
            }

            static string? Str(JsonElement e, string name) =>
                e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
        }

        private void ResetPlayingWatch()
        {
            lock (_playingGate) { _playingSeen = null; _playingSeenFor = null; }
        }
    }
}
