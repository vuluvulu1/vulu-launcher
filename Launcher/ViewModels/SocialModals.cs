using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Json;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VuluLauncher.Native;

namespace VuluLauncher.ViewModels
{
    /// <summary>Yorum.</summary>
    internal sealed partial class CommentItem(ShotViewerModal owner, JsonElement c, long avatarVersion)
    {
        public string Id { get; } = c.Str("id") ?? "";
        public PersonItem Author { get; } = new(c.Str("author") ?? "", avatarVersion);
        public string Text { get; } = c.Str("text") ?? "";
        public string When { get; } = Names.Ago(c.Str("createdAt"));
        public bool CanDelete { get; } = c.Bool("canDelete");
        [RelayCommand] private Task Delete() => owner.DeleteCommentAsync(this);
    }

    /// <summary>
    /// Büyük görünüm: tam boy resim, beğeni, kaydet, yorumlar; ← → ile akışta gezilir.
    /// Sonuç: kaldırılacak ShotCard ya da sohbeti açılacak kişinin adı.
    /// </summary>
    internal sealed partial class ShotViewerModal : ModalViewModel
    {
        private readonly SocialViewModel _social;
        private readonly List<ShotCard> _shots;
        private int _token;

        public ShotViewerModal(SocialViewModel social, List<ShotCard> shots, int index)
        {
            _social = social;
            _shots = shots;
            _ = ShowAsync(index);
        }

        public override double Width => 980;
        public bool Many => _shots.Count > 1;
        public ObservableCollection<CommentItem> Comments { get; } = new();
        [ObservableProperty] private ShotCard? _shot;
        [ObservableProperty] private Bitmap? _image;
        [ObservableProperty] private string? _status;
        [ObservableProperty] private string _commentsStatus = "";
        [ObservableProperty] private string _draft = "";
        [ObservableProperty] private string _when = "";
        private int _index;

        private async Task ShowAsync(int index)
        {
            _index = (index + _shots.Count) % _shots.Count;
            var shot = _shots[_index];
            int mine = ++_token;
            Shot = shot;
            Image = shot.Thumb;
            Status = "Yükleniyor…";
            When = string.Join(" · ", new[] { shot.Meta }.Where(x => x.Length > 0));
            _ = LoadCommentsAsync(shot, mine);
            try
            {
                var r = await CoreClient.Instance.Call("social.image", new { id = shot.Id });
                var full = await Images.LoadAsync(r.Str("url"));
                if (mine == _token) { Image = full ?? shot.Thumb; Status = null; }
            }
            catch (Exception ex) { if (mine == _token) Status = ex.Message; }
        }

        private async Task LoadCommentsAsync(ShotCard shot, int mine)
        {
            CommentsStatus = "Yorumlar yükleniyor…";
            Comments.Clear();
            try
            {
                var list = await CoreClient.Instance.Call("social.comments", new { id = shot.Id });
                if (mine != _token) return;
                var members = await _social.Main.MembersAsync();
                foreach (var c in list.Items()) Comments.Add(new CommentItem(this, c, members.GetValueOrDefault(c.Str("author") ?? "")));
                CommentsStatus = Comments.Count == 0 ? "Henüz yorum yok. İlk yorumu sen yaz." : "";
            }
            catch (Exception ex) { if (mine == _token) CommentsStatus = ex.Message; }
        }

        [RelayCommand] private Task Prev() => ShowAsync(_index - 1);
        [RelayCommand] private Task Next() => ShowAsync(_index + 1);
        [RelayCommand] private Task Like() => Shot?.Like() ?? Task.CompletedTask;
        [RelayCommand] private void Remove() => Close(Shot);
        [RelayCommand] private void OpenAuthor() { if (Shot != null) Close(Shot.Author.Username); }

        [RelayCommand]
        private async Task Save()
        {
            if (Shot == null) return;
            try
            {
                await CoreClient.Instance.Call("social.save", new { id = Shot.Id, author = Shot.Author.Username });
                _social.Main.ShowToast("Kaydedildi", "Resimler\\vulu Launcher klasörüne kaydedildi.");
            }
            catch (Exception ex) { await _social.Main.AlertAsync("Kaydedilemedi", ex.Message); }
        }

        [RelayCommand]
        private async Task SendComment()
        {
            if (Shot is not { } shot || Draft.Trim().Length == 0) return;
            try
            {
                var c = await CoreClient.Instance.Call("social.comment", new { id = shot.Id, text = Draft.Trim() });
                shot.CommentCount = c.Int("commentCount", shot.CommentCount + 1);
                Comments.Add(new CommentItem(this, c, (await _social.Main.MembersAsync()).GetValueOrDefault(_social.Main.Username)));
                CommentsStatus = "";
                Draft = "";
            }
            catch (Exception ex) { await _social.Main.AlertAsync("Yorum gönderilemedi", ex.Message); }
        }

        public async Task DeleteCommentAsync(CommentItem c)
        {
            if (Shot is not { } shot) return;
            try
            {
                await CoreClient.Instance.Call("social.deleteComment", new { id = shot.Id, commentId = c.Id });
                Comments.Remove(c);
                shot.CommentCount = Comments.Count;
            }
            catch (Exception ex) { await _social.Main.AlertAsync("Silinemedi", ex.Message); }
        }
    }

    /// <summary>Paylaşmadan önce: önizleme, boyut ve açıklama. Sonuç true ise paylaşıldı.</summary>
    internal sealed partial class ShareShotModal : ModalViewModel
    {
        private readonly InstanceItem _instance;
        private readonly ImageEncoder.Encoded _full, _thumb;

        public ShareShotModal(InstanceItem instance, ImageEncoder.Encoded full, ImageEncoder.Encoded thumb)
        {
            _instance = instance;
            _full = full;
            _thumb = thumb;
            Info = $"{instance.Name} · {full.Width}×{full.Height} · {HomeViewModel.FormatSize(full.Bytes / 1048576.0)}. Bütün vulu üyeleri görebilir.";
            _ = LoadPreview();
        }

        public override double Width => 560;
        public string Info { get; }
        [ObservableProperty] private Bitmap? _preview;
        [ObservableProperty] private string _caption = "";
        [ObservableProperty] private string? _error;
        [ObservableProperty] private bool _busy;

        private async Task LoadPreview() => Preview = await Images.LoadAsync(_thumb.DataUrl);

        [RelayCommand]
        private async Task Share()
        {
            Busy = true;
            Error = null;
            try
            {
                await CoreClient.Instance.Call("social.share", new
                {
                    instanceId = _instance.Id, caption = Caption, image = _full.DataUrl, thumb = _thumb.DataUrl,
                    width = _full.Width, height = _full.Height, bytes = _full.Bytes,
                });
                Close(true);
            }
            catch (Exception ex) { Error = ex.Message; Busy = false; }
        }
    }

    /// <summary>"Hangi paketten?" Sonuç seçilen InstanceItem.</summary>
    internal sealed partial class InstancePickerModal(List<InstanceItem> instances) : ModalViewModel
    {
        public List<PickRow> Rows { get; } = instances.Select(i => new PickRow(i.Name, i.Subtitle, i)).ToList();
    }

    internal sealed record PickRow(string Title, string? Sub, object Value, string Icon = "🧱");

    /// <summary>Kişi seçimi (yeni mesaj, kart gönderme). Sonuç kullanıcı adı.</summary>
    internal sealed partial class UserPickerModal : ModalViewModel
    {
        private readonly List<PersonItem> _all;

        public UserPickerModal(string title, List<PersonItem> people)
        {
            Title = title;
            _all = people;
            Filter();
        }

        public string Title { get; }
        public ObservableCollection<PersonItem> People { get; } = new();
        [ObservableProperty] private string _search = "";
        partial void OnSearchChanged(string value) => Filter();

        private void Filter()
        {
            var tr = CultureInfo.GetCultureInfo("tr-TR");
            string q = Search.Trim().ToLower(tr);
            People.Clear();
            foreach (var p in _all.Where(p => q.Length == 0 || p.Username.ToLower(tr).Contains(q)).Take(50)) People.Add(p);
        }

        [RelayCommand] private void Pick(PersonItem person) => Close(person.Username);
    }

    /// <summary>Sohbetteki "+": paylaşılacak paketi ya da sunucuyu seçtirir. Sonuç kartın JSON'u.</summary>
    internal sealed partial class CardPickerModal : ModalViewModel
    {
        public CardPickerModal() => _ = LoadAsync();

        public ObservableCollection<PickRow> Packs { get; } = new();
        public ObservableCollection<PickRow> Servers { get; } = new();
        [ObservableProperty] private string? _error;
        [ObservableProperty] private bool _noPacks, _noServers;

        private async Task LoadAsync()
        {
            var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
            try
            {
                var packs = await CoreClient.Instance.Call("packs.list");
                foreach (var p in packs.Items("packs"))
                    Packs.Add(new PickRow(p.Str("name") ?? "", $"Minecraft {p.Str("mcVersion")} · rev {p.Int("revision")}",
                        JsonSerializer.SerializeToElement(new { type = "pack", id = p.Str("id"), name = p.Str("name") }, options), "📦"));
                var servers = await CoreClient.Instance.Call("community.servers");
                foreach (var s in servers.Items())
                    Servers.Add(new PickRow(s.Str("name") ?? "", s.Str("address"),
                        JsonSerializer.SerializeToElement(new { type = "server", id = s.Str("id"), name = s.Str("name"), address = s.Str("address") }, options), "🎮"));
            }
            catch (Exception ex) { Error = ex.Message; }
            NoPacks = Packs.Count == 0;
            NoServers = Servers.Count == 0;
        }

    }

    /// <summary>Güvenlik kodu: iki tarafta aynıysa doğrulanır. Sonuç true ise "doğrula".</summary>
    internal sealed partial class SafetyModal(string user, string code, bool verified) : ModalViewModel
    {
        public string Title { get; } = $"{user} ile güvenlik kodu";
        public string Intro { get; } = $"Bu kodu {user} ile karşılaştırın (yüz yüze, sesli sohbette ya da başka bir uygulamada). İki tarafta da aynıysa mesajlarınızı yalnızca siz okuyabilirsiniz.";
        public string Code { get; } = code;
        public bool Verified { get; } = verified;
        [RelayCommand] private void Verify() => Close(true);
    }
}
