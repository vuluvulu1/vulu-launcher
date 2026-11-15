using Avalonia.Controls;
using VuluLauncher.ViewModels;

namespace VuluLauncher.Views
{
    internal sealed partial class WorldView : UserControl
    {
        public WorldView()
        {
            InitializeComponent();
            Themes.Attach(this);   // Ufkun öbür ucu: sayfanın kendi stillerinin üstüne
            var map = this.FindControl<WorldMapControl>("MapView")!;
            map.HoverChanged += text => { if (DataContext is WorldViewModel vm) vm.Hover = text; };
            DataContextChanged += (_, _) =>
            {
                if (DataContext is not WorldViewModel vm) return;
                vm.Map = map;
                map.TileSource = vm.LoadTileAsync;
            };
        }
    }
}
