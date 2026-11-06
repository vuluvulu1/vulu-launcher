using System.Collections.ObjectModel;
using System.Text.Json;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VuluLauncher.Native;

namespace VuluLauncher.ViewModels
{
    internal sealed partial class FeedbackEntry : ObservableObject
    {
        private readonly FeedbackModal _owner;

        public FeedbackEntry(FeedbackModal owner, JsonElement item)
        {
            _owner = owner;
            Data = item.Clone();
            Id = item.Text("id") ?? "";
            Author = item.Text("author") ?? "";
            Initial = Author.Length > 0 ? Author[..1].ToUpperInvariant() : "?";
            Kind = FeedbackModal.KindLabel(item.Text("kind"));
            IsDone = item.Text("status") == "done";
            Text = item.Text("text") ?? "";
            When = Names.Ago(item.Text("createdAt"));
            var meta = item.Get("meta");
            Meta = string.Join(" · ", new[] { meta.Text("version") is { } v ? "v" + v : null, meta.Text("platform") }.Where(x => !string.IsNullOrEmpty(x)));
            Reply = item.Text("reply");
            RepliedBy = item.Text("repliedBy") ?? "admin";
            CanDelete = item.Bool("canDelete");
            _replyDraft = Reply ?? "";
            _ = LoadAvatar((long)item.Num("authorAvatarVersion"));
        }

        public JsonElement Data { get; }
        public string Id { get; }
        public string Author { get; }
        public string Initial { get; }
        public string Kind { get; }
        public bool IsDone { get; }
        public string Text { get; }
        public string When { get; }
        public string Meta { get; }
        public bool HasMeta => Meta.Length > 0;
        public string? Reply { get; }
        public bool HasReply => !string.IsNullOrEmpty(Reply);
        public string RepliedBy { get; }
        public bool CanDelete { get; }
        public bool IsAdmin => _owner.IsAdmin;
        public string DoneText => IsDone ? "Yeniden aç" : "Yapıldı";
        public string ReplyText => HasReply ? "Yanıtı düzenle" : "Yanıtla";
        [ObservableProperty] private Bitmap? _avatar;
        [ObservableProperty] private bool _replying;
        [ObservableProperty] private string _replyDraft;

        private async Task LoadAvatar(long version) => Avatar = await Images.LoadAsync(Avatars.Url(Author, version), 64);

        [RelayCommand] private Task ToggleDone() => _owner.UpdateAsync(this, new { id = Id, status = IsDone ? "open" : "done" });
        [RelayCommand] private void StartReply() => Replying = true;
        [RelayCommand] private void CancelReply() => Replying = false;
        [RelayCommand] private Task SaveReply() => _owner.UpdateAsync(this, new { id = Id, reply = ReplyDraft });
        [RelayCommand] private Task Delete() => _owner.DeleteAsync(this);
    }

    /// <summary>
    /// Geri bildirimler: herkes yazar ve kendininkini siler; admin "yapıldı" işaretler ve kısa yanıt yazar.
    /// pack: "launcher" (launcher'ın kendisi: hata/öneri), "general" (pakete bağlı olmayan istekler) ya da paket kimliği.
    /// </summary>
    internal sealed partial class FeedbackModal : ModalViewModel
    {
        private readonly MainViewModel _main;
        private readonly string _pack;
        private readonly List<FeedbackEntry> _items = new();

        public FeedbackModal(MainViewModel main, string pack, string? packName)
        {
            _main = main;
            _pack = pack;
            bool launcher = pack == "launcher";
            Title = launcher ? "vulu Launcher" : packName ?? "Paket istekleri";
            Subtitle = launcher ? "Hata bildir ya da öneri yaz" : packName != null ? "Geri bildirim ve istekler" : "Pakete bağlı olmayan istekler";
            foreach (var (key, label) in launcher ? new[] { ("bug", "Hata"), ("idea", "Öneri") } : new[] { ("feedback", "Geri bildirim"), ("request", "İstek") })
                Kinds.Add(new Choice(key, label));
            _kind = launcher ? "bug" : packName != null ? "feedback" : "request";
            foreach (var k in Kinds) k.IsSelected = k.Key == _kind;
            EmptyText = launcher ? "Henüz bildirim yok. Bir hata gördüysen ya da aklında bir fikir varsa yaz."
                : packName != null ? "Henüz kimse yazmadı. İlk yazan sen ol." : "Henüz istek yok. Eklenmesini istediğin bir paket ya da özellik varsa yaz.";
            _ = LoadAsync();
        }

        public override double Width => 720;
        public string Title { get; }
        public string Subtitle { get; }
        public string EmptyText { get; }
        public bool IsAdmin => _main.IsAdmin;
        public ObservableCollection<Choice> Kinds { get; } = new();
        public ObservableCollection<Choice> Filters { get; } = new();
        public ObservableCollection<FeedbackEntry> Shown { get; } = new();
        [ObservableProperty] private string _kind;
        [ObservableProperty] private string _filter = "open";
        [ObservableProperty] private string _draft = "";
        [ObservableProperty] private string? _status = "Yükleniyor…";
        [ObservableProperty] private string? _error;
        public bool CanSend => Draft.Trim().Length >= 3;
        public string Counter => Draft.Length > 800 ? $"{Draft.Length}/1000" : "";
        public string Placeholder => Kind switch
        {
            "bug" => "Ne oldu? Ne yaparken oldu, her seferinde oluyor mu? Ekranda bir hata yazdıysa onu da ekle.",
            "idea" => "Launcher'da neyin olmasını ya da değişmesini istersin?",
            "feedback" => "Pakette neyi beğendin, ne sorun çıkardı? Çöken mod, eksik görev, performans…",
            _ => "Eklenmesini ya da değişmesini istediğin şey: bir mod, bir ayar, yeni bir paket…",
        };
        partial void OnDraftChanged(string value) { OnPropertyChanged(nameof(CanSend)); OnPropertyChanged(nameof(Counter)); SendCommand.NotifyCanExecuteChanged(); }
        partial void OnKindChanged(string value) => OnPropertyChanged(nameof(Placeholder));

        public static string KindLabel(string? kind) => kind switch { "bug" => "Hata", "idea" => "Öneri", "request" => "İstek", _ => "Geri bildirim" };

        private async Task LoadAsync()
        {
            try
            {
                foreach (var i in (await CoreClient.Instance.Call("feedback.list", new { pack = _pack })).Items()) _items.Add(new FeedbackEntry(this, i));
                Status = null;
            }
            catch (Exception ex) { Status = "Yüklenemedi: " + ex.Message; return; }
            Render();
        }

        private void Render()
        {
            int open = _items.Count(i => !i.IsDone);
            Filters.Clear();
            foreach (var (key, label, count) in new[] { ("open", "Açık", open), ("done", "Yapıldı", _items.Count - open), ("all", "Tümü", _items.Count) })
                Filters.Add(new Choice(key, $"{label} {count}") { IsSelected = key == Filter });
            Shown.Clear();
            foreach (var i in _items.Where(i => Filter == "all" || (Filter == "done") == i.IsDone)) Shown.Add(i);
            if (Status == null || Status.StartsWith("Bu filtrede", StringComparison.Ordinal) || Status == EmptyText)
                Status = Shown.Count > 0 ? null : _items.Count > 0 ? "Bu filtrede bir şey yok." : EmptyText;
        }

        [RelayCommand] private void PickKind(Choice kind) { Kind = kind.Key; foreach (var k in Kinds) k.IsSelected = k == kind; }
        [RelayCommand] private void PickFilter(Choice filter) { Filter = filter.Key; Render(); }

        [RelayCommand(CanExecute = nameof(CanSend))]
        private async Task Send()
        {
            Error = null;
            try
            {
                var item = await CoreClient.Instance.Call("feedback.post", new { pack = _pack, kind = Kind, text = Draft });
                _items.Insert(0, new FeedbackEntry(this, item));
                Draft = "";
                Filter = "open";
                Render();
            }
            catch (Exception ex) { Error = ex.Message; }
        }

        public async Task UpdateAsync(FeedbackEntry entry, object change)
        {
            try
            {
                var updated = await CoreClient.Instance.Call("feedback.update", change);
                _items[_items.IndexOf(entry)] = new FeedbackEntry(this, updated);
                Render();
            }
            catch (Exception ex) { await _main.AlertAsync("Kaydedilemedi", ex.Message); }
        }

        public async Task DeleteAsync(FeedbackEntry entry)
        {
            if (!await _main.ConfirmAsync("Silinsin mi?", "Bu yazı kalıcı olarak silinir.", "Sil", danger: true)) return;
            try
            {
                await CoreClient.Instance.Call("feedback.delete", new { id = entry.Id });
                _items.Remove(entry);
                Render();
            }
            catch (Exception ex) { await _main.AlertAsync("Silinemedi", ex.Message); }
        }
    }
}
