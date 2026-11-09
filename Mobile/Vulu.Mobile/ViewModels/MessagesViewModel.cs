using System.Collections.ObjectModel;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VuluMobile.Services;

namespace VuluMobile.ViewModels
{
    /// <summary>Sol taraftaki yazışma listesinin bir satırı.</summary>
    internal sealed partial class ConversationItem(string username) : ObservableObject
    {
        public string Username { get; } = username;
        [ObservableProperty] private DateTime _lastAt;
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(HasUnread))]
        private int _unread;
        public bool HasUnread => Unread > 0;
        public string When => ViewModels.When.Short(LastAt);
        partial void OnLastAtChanged(DateTime value) => OnPropertyChanged(nameof(When));
    }

    /// <summary>Sohbetteki bir balon.</summary>
    internal sealed class MessageItem(ChatMessage m)
    {
        public string Id { get; } = m.Id;
        public bool Mine { get; } = m.Mine;
        public bool Theirs => !Mine;
        public DateTime CreatedAt { get; } = m.CreatedAt;
        /// <summary>Çözülemeyen mesajda nedeni italik gösterilir.</summary>
        public string Text { get; } = m.Text ?? m.Problem ?? "";
        public bool Failed { get; } = m.Text == null;
        public string Time { get; } = When.Clock(m.CreatedAt);
    }

    /// <summary>
    /// Mesajlar sekmesi: yazışma listesi; birine dokununca sohbet açılır (<see cref="Chat"/>). Mesajlar telefonda
    /// çözülür, sunucu içeriği göremez. Yeni mesajlar <see cref="Messenger"/>'ın yoklamasıyla gelir.
    /// </summary>
    internal sealed partial class MessagesViewModel : ObservableObject, IBackHandler
    {
        private readonly Session _session;

        public MessagesViewModel(Session session)
        {
            _session = session;
            session.Messages.Received += OnReceived;
            session.Messages.StateChanged += () => { OnPropertyChanged(nameof(SetupError)); OnPropertyChanged(nameof(Preparing)); };
        }

        public ObservableCollection<ConversationItem> Conversations { get; } = new();
        [ObservableProperty] private ChatViewModel? _chat;
        [ObservableProperty] private string? _status;
        [ObservableProperty] private bool _loading;
        public bool IsEmpty => !Loading && Conversations.Count == 0 && Status == null;
        public string? SetupError => _session.Messages.Error;
        public bool Preparing => !_session.Messages.Ready && _session.Messages.Error == null;

        [RelayCommand]
        public async Task RefreshAsync()
        {
            if (Loading) return;
            Loading = true;
            try
            {
                var list = await _session.Api.GetAsync<JsonElement>("/messages/conversations");
                Conversations.Clear();
                foreach (var c in list.EnumerateArray())
                {
                    var item = new ConversationItem(c.GetProperty("username").GetString() ?? "") { Unread = c.GetProperty("unread").GetInt32() };
                    if (c.TryGetProperty("lastAt", out var at) && at.TryGetDateTime(out var t)) item.LastAt = t.ToLocalTime();
                    Conversations.Add(item);
                }
                Status = null;
            }
            catch (Exception ex) { Status = ex.Message; }
            finally { Loading = false; OnPropertyChanged(nameof(IsEmpty)); }
        }

        [RelayCommand] private void Open(ConversationItem item) => OpenWith(item.Username);

        /// <summary>Arkadaşlar sekmesinden "Mesaj gönder" de buraya gelir.</summary>
        public void OpenWith(string username)
        {
            Chat = new ChatViewModel(_session, username, () => Chat = null);
            if (Conversations.FirstOrDefault(c => c.Username.Equals(username, StringComparison.OrdinalIgnoreCase)) is { } c) c.Unread = 0;
        }

        partial void OnChatChanged(ChatViewModel? value)
        {
            if (value == null) _ = RefreshAsync();
        }

        public bool GoBack()
        {
            if (Chat == null) return false;
            Chat = null;
            return true;
        }

        private void OnReceived(IReadOnlyList<ChatMessage> messages)
        {
            Chat?.Add(messages);
            foreach (var group in messages.GroupBy(m => m.Peer, StringComparer.OrdinalIgnoreCase))
            {
                bool open = Chat != null && Chat.Peer.Equals(group.Key, StringComparison.OrdinalIgnoreCase);
                var item = Conversations.FirstOrDefault(c => c.Username.Equals(group.Key, StringComparison.OrdinalIgnoreCase));
                if (item == null) { item = new ConversationItem(group.Key); Conversations.Insert(0, item); }
                else if (Conversations.IndexOf(item) > 0) Conversations.Move(Conversations.IndexOf(item), 0);
                item.LastAt = group.Max(m => m.CreatedAt);
                if (!open) item.Unread += group.Count(m => !m.Mine);
            }
            OnPropertyChanged(nameof(IsEmpty));
        }
    }

    /// <summary>Bir kişiyle sohbet.</summary>
    internal sealed partial class ChatViewModel : ObservableObject
    {
        private readonly Session _session;
        private readonly Action _close;
        private bool _more;

        public ChatViewModel(Session session, string peer, Action close)
        {
            _session = session;
            _close = close;
            Peer = peer;
            _ = LoadAsync();
        }

        public string Peer { get; }
        public ObservableCollection<MessageItem> Messages { get; } = new();
        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(SendCommand))]
        private string _draft = "";
        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(SendCommand))]
        private bool _sending;
        [ObservableProperty] private string? _status;
        [ObservableProperty] private bool _keyChanged;
        [ObservableProperty] private bool _loading = true;
        [ObservableProperty] private bool _canLoadOlder;

        /// <summary>Yeni mesaj eklendi: görünüm en alta kayar.</summary>
        public event Action? ScrollToEnd;

        private async Task LoadAsync()
        {
            // Anahtar girişten hemen sonra hazırlanıyor olabilir (şifreden türetme birkaç saniye sürer)
            for (int i = 0; i < 20 && !_session.Messages.Ready && _session.Messages.Error == null; i++) await Task.Delay(500);
            if (!_session.Messages.Ready) { Status = _session.Messages.Error ?? "Mesajlaşma hazırlanamadı."; Loading = false; return; }
            try
            {
                var h = await _session.Messages.HistoryAsync(Peer);
                Messages.Clear();
                foreach (var m in h.Messages) Messages.Add(new MessageItem(m));
                KeyChanged = h.KeyChanged;
                Status = h.PeerError;
                _more = h.More;
                CanLoadOlder = _more;
                ScrollToEnd?.Invoke();
                int unread = h.Messages.Count(m => !m.Mine && !m.Read);
                if (unread > 0) await _session.Messages.MarkReadAsync(Peer);
            }
            catch (Exception ex) { Status = ex.Message; }
            finally { Loading = false; }
        }

        [RelayCommand]
        private async Task LoadOlderAsync()
        {
            if (!_more || Messages.Count == 0) return;
            try
            {
                var h = await _session.Messages.HistoryAsync(Peer, Messages[0].CreatedAt);
                for (int i = h.Messages.Count - 1; i >= 0; i--) Messages.Insert(0, new MessageItem(h.Messages[i]));
                _more = h.More;
                CanLoadOlder = _more;
            }
            catch (Exception ex) { Status = ex.Message; }
        }

        /// <summary>Yoklamayla gelen mesajlardan bu kişiye ait olanlar.</summary>
        public void Add(IReadOnlyList<ChatMessage> messages)
        {
            bool any = false;
            foreach (var m in messages.Where(m => m.Peer.Equals(Peer, StringComparison.OrdinalIgnoreCase)))
            {
                if (Messages.Any(x => x.Id == m.Id)) continue;
                Messages.Add(new MessageItem(m));
                any = true;
            }
            if (!any) return;
            ScrollToEnd?.Invoke();
            _ = _session.Messages.MarkReadAsync(Peer);
        }

        private bool CanSend => !Sending && Draft.Trim().Length > 0;

        [RelayCommand(CanExecute = nameof(CanSend))]
        private async Task SendAsync()
        {
            Sending = true;
            try
            {
                var sent = await _session.Messages.SendAsync(Peer, Draft);
                if (!Messages.Any(x => x.Id == sent.Id)) Messages.Add(new MessageItem(sent));
                Draft = "";
                Status = null;
                ScrollToEnd?.Invoke();
            }
            catch (Exception ex) { Status = ex.Message; }
            finally { Sending = false; }
        }

        [RelayCommand]
        private void AcceptKey()
        {
            _session.Messages.AcceptKeyChange(Peer);
            KeyChanged = false;
        }

        [RelayCommand] private void Close() => _close();
    }
}
