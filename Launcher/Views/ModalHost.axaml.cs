using Avalonia.Controls;

namespace VuluLauncher.Views
{
    internal sealed partial class ModalHost : UserControl
    {
        public ModalHost()
        {
            InitializeComponent();
            Themes.Attach(this);   // Ufkun öbür ucu: sayfanın kendi stillerinin üstüne
        }
    }
}
