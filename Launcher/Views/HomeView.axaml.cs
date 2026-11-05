using Avalonia.Controls;
using Avalonia.Threading;
using VuluLauncher.ViewModels;

namespace VuluLauncher.Views
{
    internal sealed partial class HomeView : UserControl
    {
        public HomeView()
        {
            InitializeComponent();
            // Konsol en alttayken yeni satırlar gelince alta kayar; kullanıcı yukarı kaydırdıysa yerinde kalır
            DataContextChanged += (_, _) =>
            {
                if (DataContext is not HomeViewModel vm) return;
                vm.Console.CollectionChanged += (_, _) =>
                {
                    bool atBottom = ConsoleScroll.Offset.Y >= ConsoleScroll.Extent.Height - ConsoleScroll.Viewport.Height - 40;
                    if (atBottom) Dispatcher.UIThread.Post(ConsoleScroll.ScrollToEnd, DispatcherPriority.Background);
                };
            };
        }
    }
}
