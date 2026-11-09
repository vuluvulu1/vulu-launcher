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

        /// <summary>true → italik (çözülemeyen mesaj).</summary>
        public static readonly IValueConverter ItalicIf = new FuncValueConverter<bool, FontStyle>(v => v ? FontStyle.Italic : FontStyle.Normal);

        private static readonly IBrush Accent = new SolidColorBrush(Color.Parse("#F2A33A"));
        /// <summary>true → turuncu (oyunda), false → soluk.</summary>
        public static readonly IValueConverter AccentOrDim = new FuncValueConverter<bool, IBrush>(v => v ? Accent : Dim);
    }
}
