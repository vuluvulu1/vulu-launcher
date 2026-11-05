using Avalonia.Controls;
using Avalonia.Interactivity;
using VuluLauncher.ViewModels;

namespace VuluLauncher.Views
{
    internal sealed partial class AdminView : UserControl
    {
        public AdminView()
        {
            InitializeComponent();
            // Şarkı adı ve sanatçı kutudan çıkınca kaydedilir
            AddHandler(LostFocusEvent, (_, e) =>
            {
                if (e.Source is TextBox { DataContext: SongRow song } box && box.Classes.Contains("song")) song.SaveCommand.Execute(null);
            }, RoutingStrategies.Bubble, handledEventsToo: true);
        }
    }
}
