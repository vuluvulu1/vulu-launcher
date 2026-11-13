using Avalonia.Controls;

namespace VuluLauncher.Views
{
    internal sealed partial class ProfileView : UserControl
    {
        public ProfileView()
        {
            InitializeComponent();
            Themes.Attach(this);   // Ufkun öbür ucu: sayfanın kendi stillerinin üstüne
        }
    }
}
