using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using VuluMobile.ViewModels;

namespace VuluMobile.Views
{
    public sealed partial class ChatView : UserControl
    {
        private ChatViewModel? _vm;

        public ChatView()
        {
            InitializeComponent();
            // Klavyedeki "gönder" / Enter mesajı yollar
            Input.KeyDown += (_, e) =>
            {
                if (e.Key != Key.Enter || _vm == null) return;
                e.Handled = true;
                if (_vm.SendCommand.CanExecute(null)) _vm.SendCommand.Execute(null);
            };
        }

        protected override void OnDataContextChanged(EventArgs e)
        {
            base.OnDataContextChanged(e);
            if (_vm != null) _vm.ScrollToEnd -= ScrollDown;
            _vm = DataContext as ChatViewModel;
            if (_vm != null) _vm.ScrollToEnd += ScrollDown;
        }

        // Yerleşim bitince en alta (yeni eklenen balonun boyu ölçülmüş olsun)
        private void ScrollDown() => Dispatcher.UIThread.Post(() => Scroller.ScrollToEnd(), DispatcherPriority.Background);
    }
}
