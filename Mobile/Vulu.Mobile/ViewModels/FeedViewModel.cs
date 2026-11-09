using System.Collections.ObjectModel;
using System.Text.Json;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VuluMobile.Services;

namespace VuluMobile.ViewModels
{
    /// <summary>Akıştaki bir ekran görüntüsü.</summary>
    internal sealed partial class ShotItem : ObservableObject
    {
        public ShotItem(JsonElement s)
        {
            Id = s.GetProperty("id").GetString() ?? "";
            Author = s.GetProperty("author").GetString() ?? "";
            Caption = Str(s, "caption");
            string pack = Str(s, "packName"), mc = Str(s, "mcVersion");
            Where = string.Join(" · ", new[] { pack, mc }.Where(x => x.Length > 0));
            int w = s.TryGetProperty("width", out var wi) && wi.TryGetInt32(out var wv) ? wv : 16;
            int h = s.TryGetProperty("height", out var hi) && hi.TryGetInt32(out var hv) ? hv : 9;
            Ratio = w > 0 && h > 0 ? Math.Clamp((double)h / w, 0.3, 1.8) : 9 / 16.0;
            _likes = s.GetProperty("likes").GetInt32();
            _liked = s.GetProperty("liked").GetBoolean();
            _commentCount = s.GetProperty("commentCount").GetInt32();
            CanDelete = s.TryGetProperty("canDelete", out var d) && d.ValueKind == JsonValueKind.True;
            if (s.TryGetProperty("createdAt", out var c) && c.TryDate(out var at)) CreatedAt = at;
        }

        private static string Str(JsonElement e, string n) => e.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

        public string Id { get; }
        public string Author { get; }
        public string Caption { get; }
        public bool HasCaption => Caption.Length > 0;
        public string Where { get; }
        /// <summary>Yükseklik / genişlik: resim gelmeden yer ayrılsın, liste zıplamasın.</summary>
        public double Ratio { get; }
        public DateTime CreatedAt { get; }
        public string When => ViewModels.When.Short(CreatedAt.ToLocalTime());
        public bool CanDelete { get; }

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(LikeText))]
        private int _likes;
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(LikeText))]
        private bool _liked;
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(CommentText))]
        private int _commentCount;
        public string LikeText => (Liked ? "♥ " : "♡ ") + Likes;
        public string CommentText => "💬 " + CommentCount;

        [ObservableProperty] private Bitmap? _thumb;
        private bool _loading;

        /// <summary>Önizleme ilk gösterilişte indirilir (sayfadaki hepsi birden değil).</summary>
        public async void LoadThumb()
        {
            if (_loading || Thumb != null) return;
            _loading = true;
            try { Thumb = await Images.GetAsync($"/shots/{Id}/thumb", 720); }
            catch { _loading = false; }
        }
    }

    internal sealed class CommentItem(JsonElement c)
    {
        public string Id { get; } = c.GetProperty("id").GetString() ?? "";
        public string Author { get; } = c.GetProperty("author").GetString() ?? "";
        public string Text { get; } = c.GetProperty("text").GetString() ?? "";
        public string When { get; } = c.TryGetProperty("createdAt", out var a) && a.TryDate(out var t) ? ViewModels.When.Short(t.ToLocalTime()) : "";
    }

    /// <summary>
    /// Akış: launcher'dan paylaşılan ekran görüntüleri, yeniden eskiye. Beğeni, yorum; dokununca tam boy resim
    /// ve yorumlar açılır (<see cref="Open"/>). Paylaşma şimdilik launcher'dan.
    /// </summary>
    internal sealed partial class FeedViewModel(Session session) : ObservableObject, IBackHandler
    {
        public ObservableCollection<ShotItem> Shots { get; } = new();
        [ObservableProperty] private string? _status;
        [ObservableProperty] private bool _loading;
        [ObservableProperty] private bool _more;
        [ObservableProperty] private ShotDetailViewModel? _open;
        private bool _loaded;
        public bool IsEmpty => !Loading && Shots.Count == 0 && Status == null;

        public async Task EnsureLoadedAsync()
        {
            if (_loaded) return;
            await RefreshAsync();
        }

        [RelayCommand]
        public async Task RefreshAsync()
        {
            if (Loading) return;
            Loading = true;
            try
            {
                var page = await session.Api.GetAsync<JsonElement>("/shots");
                Shots.Clear();
                foreach (var s in page.GetProperty("shots").EnumerateArray()) Shots.Add(new ShotItem(s));
                More = page.GetProperty("more").GetBoolean();
                Status = null;
                _loaded = true;
            }
            catch (Exception ex) { Status = ex.Message; }
            finally { Loading = false; OnPropertyChanged(nameof(IsEmpty)); }
        }

        [RelayCommand]
        private async Task LoadMoreAsync()
        {
            if (Loading || !More || Shots.Count == 0) return;
            Loading = true;
            try
            {
                string before = Uri.EscapeDataString(Shots[^1].CreatedAt.ToUniversalTime().ToString("o"));
                var page = await session.Api.GetAsync<JsonElement>($"/shots?before={before}");
                foreach (var s in page.GetProperty("shots").EnumerateArray()) Shots.Add(new ShotItem(s));
                More = page.GetProperty("more").GetBoolean();
            }
            catch (Exception ex) { Status = ex.Message; }
            finally { Loading = false; }
        }

        [RelayCommand]
        private async Task LikeAsync(ShotItem shot)
        {
            bool like = !shot.Liked;
            shot.Liked = like;                 // hemen tepki; sunucu cevabıyla düzeltilir
            shot.Likes += like ? 1 : -1;
            try
            {
                var r = await session.Api.PostAsync<JsonElement>($"/shots/{shot.Id}/like", new { like });
                shot.Likes = r.GetProperty("likes").GetInt32();
                shot.Liked = r.GetProperty("liked").GetBoolean();
            }
            catch (Exception ex)
            {
                shot.Liked = !like;
                shot.Likes += like ? -1 : 1;
                Status = ex.Message;
            }
        }

        [RelayCommand] private void Show(ShotItem shot) => Open = new ShotDetailViewModel(session, shot, this);

        internal void Removed(ShotItem shot)
        {
            Shots.Remove(shot);
            Open = null;
            OnPropertyChanged(nameof(IsEmpty));
        }

        [RelayCommand] private void CloseDetail() => Open = null;

        /// <summary>Bildirimden: o ekran görüntüsü listede yoksa sunucudan alınır.</summary>
        public async Task OpenByIdAsync(string id)
        {
            var shot = Shots.FirstOrDefault(s => s.Id == id);
            if (shot == null)
            {
                try { shot = new ShotItem(await session.Api.GetAsync<JsonElement>($"/shots/{Uri.EscapeDataString(id)}")); }
                catch (Exception ex) { Status = ex.Message; return; }
            }
            Open = new ShotDetailViewModel(session, shot, this);
        }

        // ── Paylaşma ──────────────────────────────────────────────────────
        [ObservableProperty] private bool _sharing;
        [ObservableProperty] private Bitmap? _sharePreview;
        [ObservableProperty] private string _shareCaption = "";
        [ObservableProperty] private string? _shareStatus;
        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(ShareSendCommand))]
        private bool _shareBusy;
        private byte[]? _shareImage, _shareThumb;
        private int _shareWidth, _shareHeight;

        /// <summary>Galeriden resim seçilir, sunucunun sınırlarına göre küçültülür ve önizlenir.</summary>
        [RelayCommand]
        private async Task ShareAsync()
        {
            try
            {
                byte[]? picked = await Picker.PickImageAsync("Paylaşılacak ekran görüntüsü");
                if (picked == null) return;
                ShareBusy = true;
                Sharing = true;
                ShareStatus = "Hazırlanıyor…";
                var (image, thumb) = await Task.Run(() => (ImageTools.Jpeg(picked, 1920, 1_150_000), ImageTools.Jpeg(picked, 480, 150_000)));
                _shareImage = image.Data; _shareThumb = thumb.Data;
                _shareWidth = image.Width; _shareHeight = image.Height;
                using (var ms = new MemoryStream(thumb.Data)) SharePreview = new Bitmap(ms);
                ShareStatus = null;
            }
            catch (Exception ex) { ShareStatus = ex.Message; }
            finally { ShareBusy = false; }
        }

        private bool CanShareSend => !ShareBusy && _shareImage != null;

        [RelayCommand(CanExecute = nameof(CanShareSend))]
        private async Task ShareSendAsync()
        {
            ShareBusy = true;
            ShareStatus = "Gönderiliyor…";
            try
            {
                var shot = await session.Api.PostAsync<JsonElement>("/shots", new
                {
                    image = Convert.ToBase64String(_shareImage!),
                    thumb = Convert.ToBase64String(_shareThumb!),
                    caption = ShareCaption.Trim(),
                    packName = "",
                    mcVersion = "",
                    width = _shareWidth,
                    height = _shareHeight,
                });
                Shots.Insert(0, new ShotItem(shot));
                OnPropertyChanged(nameof(IsEmpty));
                ShareCancel();
            }
            catch (Exception ex) { ShareStatus = ex.Message; }
            finally { ShareBusy = false; }
        }

        [RelayCommand]
        private void ShareCancel()
        {
            Sharing = false;
            SharePreview = null;
            ShareCaption = "";
            ShareStatus = null;
            _shareImage = _shareThumb = null;
            ShareSendCommand.NotifyCanExecuteChanged();
        }

        public bool GoBack()
        {
            if (Sharing) { ShareCancel(); return true; }
            if (Open == null) return false;
            Open = null;
            return true;
        }
    }

    /// <summary>Tam boy ekran görüntüsü ve yorumları.</summary>
    internal sealed partial class ShotDetailViewModel : ObservableObject
    {
        private readonly Session _session;
        private readonly FeedViewModel _feed;

        public ShotDetailViewModel(Session session, ShotItem shot, FeedViewModel feed)
        {
            _session = session;
            _feed = feed;
            Shot = shot;
            Image = shot.Thumb;
            _ = LoadAsync();
        }

        public ShotItem Shot { get; }
        public FeedViewModel Feed => _feed;
        [ObservableProperty] private Bitmap? _image;
        public ObservableCollection<CommentItem> Comments { get; } = new();
        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(CommentCommand))]
        private string _draft = "";
        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(CommentCommand))]
        private bool _busy;
        [ObservableProperty] private string? _status;
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(DeleteText))]
        private bool _confirmDelete;
        public string DeleteText => ConfirmDelete ? "Emin misin?" : "Sil";

        private async Task LoadAsync()
        {
            var image = Images.GetAsync($"/shots/{Shot.Id}/image", 1600);
            try
            {
                var list = await _session.Api.GetAsync<JsonElement>($"/shots/{Shot.Id}/comments");
                Comments.Clear();
                foreach (var c in list.EnumerateArray()) Comments.Add(new CommentItem(c));
            }
            catch (Exception ex) { Status = ex.Message; }
            try { Image = await image ?? Image; } catch { }
        }

        private bool CanComment => !Busy && Draft.Trim().Length > 0;

        [RelayCommand(CanExecute = nameof(CanComment))]
        private async Task CommentAsync()
        {
            Busy = true;
            try
            {
                var c = await _session.Api.PostAsync<JsonElement>($"/shots/{Shot.Id}/comments", new { text = Draft.Trim() });
                Comments.Add(new CommentItem(c));
                if (c.TryGetProperty("commentCount", out var n)) Shot.CommentCount = n.GetInt32();
                Draft = "";
                Status = null;
            }
            catch (Exception ex) { Status = ex.Message; }
            finally { Busy = false; }
        }

        [RelayCommand]
        private async Task DeleteAsync()
        {
            if (!ConfirmDelete) { ConfirmDelete = true; return; }
            try
            {
                await _session.Api.DeleteAsync<JsonElement>($"/shots/{Shot.Id}");
                _feed.Removed(Shot);
            }
            catch (Exception ex) { Status = ex.Message; }
        }
    }
}
