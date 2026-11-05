using Avalonia.Controls;

namespace VuluLauncher.Views
{
    internal sealed partial class LoginView : UserControl
    {
        public LoginView()
        {
            InitializeComponent();
            // Kayıtlı şifrenin maskesine tıklanınca tamamı seçilir: yazmaya başlayan maskeyi silmekle uğraşmaz
            PasswordBox.GotFocus += (_, _) => PasswordBox.SelectAll();
            AttachedToVisualTree += (_, _) =>
                (string.IsNullOrEmpty(UsernameBox.Text) ? UsernameBox : PasswordBox).Focus();
        }
    }
}
