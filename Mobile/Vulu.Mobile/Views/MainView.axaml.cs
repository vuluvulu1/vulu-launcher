using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using VuluMobile.ViewModels;

namespace VuluMobile.Views
{
    public sealed partial class MainView : UserControl
    {
        private TopLevel? _top;

        public MainView() => InitializeComponent();

        // Android geri tuşu (masaüstünde Alt+Sol da aynı olayı verir)
        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            _top = TopLevel.GetTopLevel(this);
            if (_top != null) _top.BackRequested += OnBack;
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            if (_top != null) _top.BackRequested -= OnBack;
            _top = null;
            base.OnDetachedFromVisualTree(e);
        }

        private void OnBack(object? sender, RoutedEventArgs e)
        {
            if (DataContext is MainViewModel vm && vm.GoBack()) e.Handled = true;
        }
    }
}
