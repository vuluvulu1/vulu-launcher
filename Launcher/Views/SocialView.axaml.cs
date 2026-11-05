using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using VuluLauncher.ViewModels;

namespace VuluLauncher.Views
{
    internal sealed partial class SocialView : UserControl
    {
        public SocialView()
        {
            InitializeComponent();
            // Enter gönderir, Shift+Enter yeni satır
            Compose.AddHandler(KeyDownEvent, (_, e) =>
            {
                if (e.Key == Key.Enter && !e.KeyModifiers.HasFlag(KeyModifiers.Shift) && DataContext is SocialViewModel vm)
                {
                    e.Handled = true;
                    vm.SendCommand.Execute(null);
                }
            }, Avalonia.Interactivity.RoutingStrategies.Tunnel);

            // Yukarı kaydırınca daha eski mesajlar yüklenir
            // (yalnızca kullanıcı yukarı kaydırırken; yüklenince görünen yer kaymaz)
            ChatScroll.ScrollChanged += async (_, e) =>
            {
                if (e.OffsetDelta.Y >= 0 || ChatScroll.Offset.Y > 40 || DataContext is not SocialViewModel vm) return;
                double before = ChatScroll.Extent.Height;
                await vm.LoadOlderAsync();
                Dispatcher.UIThread.Post(() => ChatScroll.Offset = new Avalonia.Vector(0, Math.Max(0, ChatScroll.Extent.Height - before)), DispatcherPriority.Background);
            };

            DataContextChanged += (_, _) =>
            {
                if (DataContext is SocialViewModel vm)
                    vm.ScrollToEnd = () => Dispatcher.UIThread.Post(ChatScroll.ScrollToEnd, DispatcherPriority.Background);
            };
        }
    }
}
