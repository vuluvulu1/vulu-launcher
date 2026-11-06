using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Json;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VuluLauncher.Native;

namespace VuluLauncher.ViewModels
{
    internal sealed record RoleChip(string Name, string Color)
    {
        public Avalonia.Media.IBrush Brush => Avalonia.Media.Color.TryParse(Color, out var c) ? new Avalonia.Media.SolidColorBrush(c) : Avalonia.Media.Brushes.Gray;
    }
    internal sealed record PlaytimeRow(string Name, string Time, double Fraction);
    internal sealed record ProfilePack(string Name, string Meta);

    /// <summary>
    /// Profil: başkalarınınki görüntülenir; kendi profilin önce başkalarının gördüğü gibi açılır, "Profili düzenle" ile
    /// resim, banner, hakkımda, skin ve şifre düzenlenir. Sağda arkadaş listesi.
    /// </summary>
    internal sealed partial class ProfileViewModel(MainViewModel main) : ObservableObject
    {
        private readonly CoreClient _core = CoreClient.Instance;
        private int _requestId;
        private string _savedBio = "";

        public ObservableCollection<RoleChip> Roles { get; } = new();
        public ObservableCollection<PlaytimeRow> Playtime { get; } = new();
        public ObservableCollection<ProfilePack> Packs { get; } = new();
        public ObservableCollection<PersonItem> Members { get; } = new();

        [ObservableProperty] private string _username = "";
        [ObservableProperty] private string? _error;
        [ObservableProperty] private bool _loaded;
        [ObservableProperty] private Bitmap? _avatar;
        [ObservableProperty] private Bitmap? _banner;
        [ObservableProperty] private Bitmap? _skin;
        [ObservableProperty] private bool _isAdminUser;
        [ObservableProperty] private string _meta = "";
        [ObservableProperty] private string _bio = "";
        [ObservableProperty] private string _bioDraft = "";
        [ObservableProperty] private string? _bioNote;
        [ObservableProperty] private bool _hasAvatar, _hasBanner, _hasSkin, _slim;
        [ObservableProperty] private string? _playtimeTotal;
        [ObservableProperty] private string? _friendState;
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsViewingOwn), nameof(ShowEditButton))]
        private bool _editing;
        [ObservableProperty] private string _memberSearch = "";
        [ObservableProperty] private string _membersCount = "";
        // Şifre
        [ObservableProperty] private string _currentPassword = "";
        [ObservableProperty] private string _newPassword = "";
        [ObservableProperty] private string _repeatPassword = "";
        [ObservableProperty] private string? _passwordNote;
        [ObservableProperty] private bool _passwordOk;

        public bool IsOwn => Username == main.Username;
        public bool IsOther => !IsOwn;
        public bool IsViewingOwn => IsOwn && !Editing;
        public bool ShowEditButton => IsOwn && !Editing;
        public string Initial => Username.Length > 0 ? Username[..1].ToUpperInvariant() : "?";
        public bool ShowBioView => !Editing;
        public string BioText => Bio.Length > 0 ? Bio : IsOwn ? "Henüz bir şey yazmadın. \"Profili düzenle\" ile kendinden bahset." : "Henüz bir şey yazmamış.";
        public bool BioEmpty => Bio.Length == 0;
        public bool ShowSkin => HasSkin || IsOwn;
        public bool HasPlaytime => PlaytimeTotal != null;
        public bool ShowPlaytimeHint => !HasPlaytime && IsOwn;
        public string PlaytimeHint => AppSettings.Current.ShowPlaytimeOnProfile ? "Biraz oynadıktan sonra toplam süren burada görünür." : "Oynama süren profilinde gizli (Ayarlar).";
        public bool HasPacks => Packs.Count > 0;
        public string PacksTitle => $"Paylaştığı paketler ({Packs.Count})";
        public string BioCounter => $"{BioDraft.Length}/300";
        public bool CanSaveBio => BioDraft.Trim() != _savedBio;
        public string FriendText => FriendState switch { "friend" => "✓ Arkadaş", "outgoing" => "İstek gönderildi", "incoming" => "Kabul et", _ => "+ Arkadaş ekle" };
        public bool IsIncoming => FriendState == "incoming";
        public string SkinButton => HasSkin ? "Skini değiştir" : "Skin yükle";

        partial void OnBioDraftChanged(string value) { OnPropertyChanged(nameof(BioCounter)); OnPropertyChanged(nameof(CanSaveBio)); BioNote = null; }
        partial void OnEditingChanged(bool value) => OnPropertyChanged(nameof(ShowBioView));
        partial void OnMemberSearchChanged(string value) => _ = LoadMembersAsync(false);
        partial void OnFriendStateChanged(string? value) { OnPropertyChanged(nameof(FriendText)); OnPropertyChanged(nameof(IsIncoming)); }

        public async Task OpenAsync(string username)
        {
            if (username != Username) Editing = false;
            Username = username;
            int request = ++_requestId;
            Loaded = false;
            Error = null;
            _ = LoadMembersAsync(false);
            JsonElement user;
            try { user = await _core.Call("profile.get", new { username }); }
            catch (Exception ex) { if (request == _requestId) Error = "Profil alınamadı: " + ex.Message; return; }
            if (request != _requestId) return;
            await ApplyAsync(user);
        }

        private async Task ApplyAsync(JsonElement user)
        {
            foreach (var name in new[] { nameof(IsOwn), nameof(IsOther), nameof(IsViewingOwn), nameof(ShowEditButton), nameof(Initial) }) OnPropertyChanged(name);
            IsAdminUser = user.Bool("isAdmin");
            var parts = new List<string>();
            if (user.Text("createdAt") is { } created && DateTime.TryParse(created, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var at))
                parts.Add("Katıldı: " + at.ToLocalTime().ToString("d MMMM yyyy", CultureInfo.GetCultureInfo("tr-TR")));
            parts.Add(user.Text("lastSeenAt") is { } seen ? "Son görülme: " + Names.Ago(seen) : "Henüz giriş yapmadı");
            Meta = string.Join(" · ", parts);
            Roles.Clear();
            foreach (var r in user.Items("roles")) Roles.Add(new RoleChip(r.Text("name") ?? "", r.Text("color") is { Length: > 0 } c ? c : "#C9BDAF"));

            Bio = user.Text("bio") ?? "";
            _savedBio = Bio;
            BioDraft = Bio;
            OnPropertyChanged(nameof(BioText));
            OnPropertyChanged(nameof(BioEmpty));

            long avatarVersion = (long)user.Num("avatarVersion"), bannerVersion = (long)user.Num("bannerVersion");
            HasAvatar = avatarVersion > 0;
            HasBanner = bannerVersion > 0;
            Avatar = await Images.LoadAsync(Avatars.Url(Username, avatarVersion), 256);
            Banner = bannerVersion > 0 ? await Images.LoadAsync($"{Avatars.ApiBase}/users/{Uri.EscapeDataString(Username)}/banner?v={bannerVersion}", 1600) : null;

            HasSkin = user.Text("skinHash") is { Length: > 0 };
            Slim = user.Text("skinModel") == "slim";
            Skin = HasSkin && await Images.LoadBytesAsync($"{Avatars.ApiBase}/yggdrasil/textures/{user.Text("skinHash")}?c=1") is { } png
                ? SkinRenderer.RenderFront(png, Slim) : null;
            OnPropertyChanged(nameof(ShowSkin));
            OnPropertyChanged(nameof(SkinButton));

            Playtime.Clear();
            var p = user.Get("playtime");
            double total = p.Num("totalSeconds");
            PlaytimeTotal = total > 0 ? FormatPlayTime(total) : null;
            if (total > 0)
            {
                double max = Math.Max(1, p.Items("top").Select(t => t.Num("seconds")).DefaultIfEmpty(1).Max());
                foreach (var t in p.Items("top")) Playtime.Add(new PlaytimeRow(t.Text("name") ?? "", FormatPlayTime(t.Num("seconds")), Math.Max(0.04, t.Num("seconds") / max)));
            }
            OnPropertyChanged(nameof(HasPlaytime));
            OnPropertyChanged(nameof(ShowPlaytimeHint));
            OnPropertyChanged(nameof(PlaytimeHint));

            Packs.Clear();
            foreach (var pack in user.Items("packs"))
                Packs.Add(new ProfilePack(pack.Text("name") ?? "", $"{pack.Text("mcVersion")} · {Names.Loader(pack.Text("loader"))} · rev {pack.Int("revision")}"));
            OnPropertyChanged(nameof(HasPacks));
            OnPropertyChanged(nameof(PacksTitle));

            FriendState = main.Social.FriendState(Username);
            Loaded = true;
        }

        public static string FormatPlayTime(double seconds)
        {
            if (seconds < 60) return seconds > 0 ? "1 dk'dan az" : "Hiç";
            int minutes = (int)(seconds / 60), hours = minutes / 60;
            if (hours == 0) return $"{minutes} dk";
            if (hours < 10) return $"{hours} sa {minutes % 60} dk";
            return hours.ToString("N0", CultureInfo.GetCultureInfo("tr-TR")) + " sa";
        }

        private async Task LoadMembersAsync(bool force)
        {
            if (force) main.ForgetMembers();
            var members = await main.MembersAsync();
            var tr = CultureInfo.GetCultureInfo("tr-TR");
            string q = MemberSearch.Trim().ToLower(tr);
            Members.Clear();
            // Çevrimiçi olanlar (önce oynayanlar) üstte; altta oyunda olduğu yer, biyografi ya da son görülme (web: renderMembers)
            var people = members.Keys.Where(name => q.Length == 0 || name.ToLower(tr).Contains(q)).Select(main.Social.Person).ToList();
            foreach (var person in people.OrderBy(p => p.IsPlaying ? 0 : p.IsOnline ? 1 : 2).ThenBy(p => p.Username, StringComparer.Create(tr, true)))
            {
                var info = main.MemberInfo(person.Username);
                person.IsAdmin = info.Bool("isAdmin");
                person.IsYou = person.Username == main.Username;
                person.Sub = person.IsOnline ? (person.Game != null ? $"{person.Game} · {person.Rest}" : person.Rest ?? "")
                    : info.Text("bio") is { Length: > 0 } bio ? bio.ReplaceLineEndings(" ") : main.LastSeenText(person.Username);
                Members.Add(person);
            }
            MembersCount = $"{members.Count} üye";
        }

        [RelayCommand] private Task OpenMember(PersonItem person) => OpenAsync(person.Username);
        [RelayCommand] private void StartEditing() => Editing = true;
        [RelayCommand] private void StopEditing() => Editing = false;
        [RelayCommand] private void OpenSettings() => main.SelectedTab = main.Tabs.First(t => t.Key == "settings");
        [RelayCommand] private Task Message() => main.Social.OpenConversationAsync(Username);
        [RelayCommand] private Task Logout() => main.LogoutAsync();

        [RelayCommand]
        private async Task Friend()
        {
            await main.Social.FriendActionAsync(FriendState switch { "friend" => "remove", "outgoing" => "decline", "incoming" => "accept", _ => "request" }, Username);
            FriendState = main.Social.FriendState(Username);
        }

        [RelayCommand]
        private async Task DeclineFriend()
        {
            await main.Social.FriendActionAsync("decline", Username);
            FriendState = main.Social.FriendState(Username);
        }

        [RelayCommand]
        private async Task SaveBio()
        {
            try
            {
                var updated = await _core.Call("profile.saveBio", new { bio = BioDraft });
                Bio = updated.Text("bio") ?? BioDraft;
                _savedBio = Bio;
                OnPropertyChanged(nameof(CanSaveBio));
                OnPropertyChanged(nameof(BioText));
                OnPropertyChanged(nameof(BioEmpty));
                BioNote = "Kaydedildi ✓";
            }
            catch (Exception ex) { BioNote = ex.Message; }
        }

        [RelayCommand]
        private async Task ChangePassword()
        {
            PasswordOk = false;
            if (CurrentPassword.Length == 0 || NewPassword.Length == 0) { PasswordNote = "Alanları doldur."; return; }
            if (NewPassword.Length < 6) { PasswordNote = "Yeni şifre en az 6 karakter olmalı."; return; }
            if (NewPassword != RepeatPassword) { PasswordNote = "Yeni şifreler aynı değil."; return; }
            try
            {
                await _core.Call("profile.changePassword", new { currentPassword = CurrentPassword, newPassword = NewPassword });
                CurrentPassword = NewPassword = RepeatPassword = "";
                PasswordOk = true;
                PasswordNote = "Şifren değiştirildi ✓";
            }
            catch (Exception ex) { PasswordNote = ex.Message; }
        }

        // ── Resimler ─────────────────────────────────────────────────────
        [RelayCommand] private Task ChangeAvatar() => ChangeImageAsync("profile.pickAvatar", "profile.uploadAvatar", 1, 512, 900 * 1024, "Profil resmini ayarla", round: true, "Resim yüklenemedi");
        [RelayCommand] private Task ChangeBanner() => ChangeImageAsync("profile.pickBanner", "profile.uploadBanner", 4, 1600, 1400 * 1024, "Bannerı ayarla", round: false, "Banner yüklenemedi");
        [RelayCommand] private Task RemoveAvatar() => RunAsync("profile.removeAvatar", null, "Resim kaldırılamadı");
        [RelayCommand] private Task RemoveBanner() => RunAsync("profile.removeBanner", null, "Banner kaldırılamadı");
        [RelayCommand] private Task PickSkin() => RunAsync("profile.pickSkin", new { model = Slim ? "slim" : "default" }, "Skin yüklenemedi");
        [RelayCommand] private Task RemoveSkin() => RunAsync("profile.removeSkin", null, "Skin kaldırılamadı");
        [RelayCommand] private Task SetSkinModel(string model) => model == (Slim ? "slim" : "default") ? Task.CompletedTask : RunAsync("profile.setSkinModel", new { model }, "Değiştirilemedi");

        /// <summary>Resim seçtirir; GIF hareketli kalsın diye olduğu gibi, diğerleri kırpılıp WebP'ye çevrilerek yüklenir.</summary>
        private async Task ChangeImageAsync(string pick, string upload, double aspect, int outWidth, int maxBytes, string title, bool round, string fail)
        {
            try
            {
                var picked = await _core.Call(pick);
                if (picked.ValueKind != JsonValueKind.Object || picked.Text("dataUrl") is not { } dataUrl) return;   // vazgeçildi
                if (picked.Text("type") != "image/gif")
                {
                    var bytes = ImageEncoder.FromDataUrl(dataUrl);
                    var crop = new CropModal(bytes, aspect, round, title);
                    if (await main.ShowAsync(crop) is not CropModal.Region region) return;
                    var cropped = await Task.Run(() => ImageCrop.Crop(bytes, region.X, region.Y, region.W, region.H, outWidth, (int)Math.Round(outWidth / aspect)));
                    dataUrl = (await Task.Run(() => ImageEncoder.Encode(cropped, outWidth, maxBytes))).DataUrl;
                }
                await AfterChangeAsync(await _core.Call(upload, new { dataUrl }));
            }
            catch (Exception ex) { await main.AlertAsync(fail, ex.Message); }
        }

        private async Task RunAsync(string command, object? args, string fail)
        {
            try
            {
                var updated = await _core.Call(command, args);
                if (updated.ValueKind == JsonValueKind.Object) await AfterChangeAsync(updated);
            }
            catch (Exception ex) { await main.AlertAsync(fail, ex.Message); }
        }

        private async Task AfterChangeAsync(JsonElement updated)
        {
            main.ForgetMembers();
            await main.RefreshMyAvatarAsync();
            await OpenAsync(main.Username);
        }
    }

    /// <summary>
    /// Kırpma: resim sürükleyerek kaydırılır, tekerlek ya da kaydırıcıyla yakınlaştırılır; çerçeve hep resmin içinde kalır.
    /// Sonuç resimdeki bölge (piksel).
    /// </summary>
    internal sealed partial class CropModal : ModalViewModel
    {
        internal sealed record Region(double X, double Y, double W, double H);

        public CropModal(byte[] image, double aspect, bool round, string title)
        {
            using var stream = new MemoryStream(image);
            Image = new Bitmap(stream);
            Aspect = aspect;
            Round = round;
            Title = title;
            FrameWidth = aspect >= 2 ? 640 : 360;
            FrameHeight = Math.Round(FrameWidth / aspect);
            MinScale = Math.Max(FrameWidth / Image.PixelSize.Width, FrameHeight / Image.PixelSize.Height);
            _scale = MinScale;
            _x = (FrameWidth - Image.PixelSize.Width * _scale) / 2;
            _y = (FrameHeight - Image.PixelSize.Height * _scale) / 2;
        }

        public override double Width => FrameWidth + 48;
        public Bitmap Image { get; }
        public double Aspect { get; }
        public bool Round { get; }
        public string Title { get; }
        public double FrameWidth { get; }
        public double FrameHeight { get; }
        public double MinScale { get; }

        [ObservableProperty] private double _scale;
        [ObservableProperty] private double _x;
        [ObservableProperty] private double _y;
        public double ImageWidth => Image.PixelSize.Width * Scale;
        public double ImageHeight => Image.PixelSize.Height * Scale;
        public double Zoom { get => Scale / MinScale; set => ZoomTo(MinScale * value, FrameWidth / 2, FrameHeight / 2); }

        partial void OnScaleChanged(double value) { OnPropertyChanged(nameof(ImageWidth)); OnPropertyChanged(nameof(ImageHeight)); OnPropertyChanged(nameof(Zoom)); }

        /// <summary>Verilen noktayı (çerçeveye göre) sabit tutarak yakınlaştırır.</summary>
        public void ZoomTo(double next, double cx, double cy)
        {
            next = Math.Clamp(next, MinScale, MinScale * 4);
            double x = cx - (cx - X) * next / Scale, y = cy - (cy - Y) * next / Scale;
            Scale = next;
            MoveTo(x, y);
        }

        public void MoveTo(double x, double y)
        {
            X = Math.Min(0, Math.Max(FrameWidth - ImageWidth, x));
            Y = Math.Min(0, Math.Max(FrameHeight - ImageHeight, y));
        }

        [RelayCommand] private void Save() => Close(new Region(-X / Scale, -Y / Scale, FrameWidth / Scale, FrameHeight / Scale));
    }
}
