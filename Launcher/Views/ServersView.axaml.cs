using Avalonia.Controls;

namespace VuluLauncher.Views
{
    internal sealed partial class ServersView : UserControl
    {
        public ServersView()
        {
            InitializeComponent();
            Themes.Attach(this);   // Ufkun öbür ucu: sayfanın kendi stillerinin üstüne
        }
    }
}
