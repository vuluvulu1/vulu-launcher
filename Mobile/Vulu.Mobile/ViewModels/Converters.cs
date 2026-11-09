using Avalonia.Data.Converters;
using Avalonia.Media;

namespace VuluMobile.ViewModels
{
    internal static class Converters
    {
        private static readonly IBrush Ok = new SolidColorBrush(Color.Parse("#9CC3DE"));
        private static readonly IBrush Dim = new SolidColorBrush(Color.Parse("#A1958A"));

        /// <summary>true → mavi (tamam), false → soluk.</summary>
        public static readonly IValueConverter OkOrDim = new FuncValueConverter<bool, IBrush>(v => v ? Ok : Dim);
    }
}
