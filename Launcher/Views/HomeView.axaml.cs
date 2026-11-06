using Avalonia.Controls;
using Avalonia.Threading;
using VuluLauncher.ViewModels;

namespace VuluLauncher.Views
{
    internal sealed partial class HomeView : UserControl
    {
        /// <summary>Radyo menüsü açılınca türler ve istasyonlar yüklenir.</summary>
        private void OnRadioMenuOpened(object? sender, EventArgs e)
        {
            if (TopLevel.GetTopLevel(this)?.DataContext is MainViewModel main) _ = main.Radio.OpenMenuAsync();
        }

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
