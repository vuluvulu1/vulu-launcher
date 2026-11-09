using System.Text.Json;
using Android.App;
using Android.Content;
using VuluLauncher;
using VuluMobile.Services;

namespace VuluMobile.Android
{
    /// <summary>
    /// Kayıt isteği bildirimindeki Onayla / Reddet düğmeleri. Uygulama kapalı olabilir: kayıtlı girişle (Keystore)
    /// oturum açılıp istek doğrudan API'ye gönderilir; sonuç bildirimde yazar. Yetkiyi sunucu yine denetler.
    /// </summary>
    [BroadcastReceiver(Exported = false)]
    public sealed class RegistrationActionReceiver : BroadcastReceiver
    {
        internal const string Approve = "com.vuluvulu.social.REGISTRATION_APPROVE";
        internal const string Reject = "com.vuluvulu.social.REGISTRATION_REJECT";
        internal const string RejectConfirmed = "com.vuluvulu.social.REGISTRATION_REJECT_CONFIRMED";

        public override void OnReceive(Context? context, Intent? intent)
        {
            if (context == null || intent?.Action == null) return;
            string id = intent.GetStringExtra(Notifications.IdExtra) ?? "";
            string username = intent.GetStringExtra("username") ?? "";
            string title = intent.GetStringExtra("title") ?? "Yeni kayıt isteği";
            string body = intent.GetStringExtra("body") ?? "";
            if (id.Length == 0) return;

            if (intent.Action == Reject)
            {
                Notifications.ShowRegistration(context, id, username, title, "Reddetmek geri alınamaz. Emin misin?", confirmReject: true);
                return;
            }
            string action = intent.Action == Approve ? "approve" : intent.Action == RejectConfirmed ? "reject" : "";
            if (action.Length == 0) return;

            Notifications.ShowRegistration(context, id, username, title, body, status: action == "approve" ? "Onaylanıyor…" : "Reddediliyor…");
            var pending = GoAsync();
            var app = context.ApplicationContext ?? context;
            _ = Task.Run(async () =>
            {
                string result;
                try
                {
                    var saved = SavedLogin.Load() ?? throw new InvalidOperationException("Önce uygulamada giriş yap.");
                    var login = new LoginService();
                    var r = await login.LoginAsync(saved.Username, saved.Password);
                    if (!r.Success) throw new InvalidOperationException(r.ErrorMessage ?? "Giriş yapılamadı.");
                    var api = new ApiClient(() => login.Token);
                    await api.PostAsync<JsonElement>($"/admin/registrations/{Uri.EscapeDataString(id)}/{action}", new { });
                    result = action == "approve" ? $"{username} onaylandı, hesabı açıldı." : $"{username} isteği reddedildi.";
                }
                catch (Exception ex) { result = "Yapılamadı: " + ex.Message; }
                try { Notifications.ShowRegistration(app, id, username, title, body, status: result); }
                finally { pending?.Finish(); }
            });
        }
    }
}
