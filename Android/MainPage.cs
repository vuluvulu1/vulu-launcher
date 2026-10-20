namespace VuluLauncher
{
    /// <summary>Tek sayfa: tam ekran HybridWebView içinde launcher arayüzü.</summary>
    public sealed class MainPage : ContentPage
    {
        private readonly HybridWebView _webView;
        private readonly AndroidHost _host;
        private readonly Bridge _bridge;
        private readonly LauncherApp _app;

        internal static MainPage? Current { get; private set; }

        public MainPage()
        {
            Current = this;
            BackgroundColor = Color.FromArgb("#121010");
            // Android 15+ uygulamayı ekranın kenarlarına kadar çizer; arayüz durum çubuğunun, gezinme çubuğunun ve
            // klavyenin altında kalmasın (klavye açılınca sayfa kısalır, yazı kutusu görünür kalır)
            SafeAreaEdges = SafeAreaEdges.All;

            _webView = new HybridWebView { HybridRoot = "wwwroot", DefaultFile = "index.html" };
            _host = new AndroidHost(_webView);
            _bridge = new Bridge(_host.SendToUi);
            _app = new LauncherApp(_bridge, _host);
            _app.RegisterCommands();

            _webView.RawMessageReceived += (_, e) => { if (e.Message != null) _bridge.HandleMessage(e.Message); };
            LocalFileServer.Attach(_webView);

            Content = _webView;
        }

        /// <summary>Bildirime tıklanınca MainActivity çağırır.</summary>
        internal void NotificationClicked(string? tag) => _app.OnNotificationClicked(tag);

        /// <summary>Geri tuşu önce arayüzdeki açık pencereyi/menüyü kapatır; arayüz işlemezse uygulama arka plana gider.</summary>
        protected override bool OnBackButtonPressed()
        {
            _bridge.Emit("backPressed");
            return true;
        }
    }
}
