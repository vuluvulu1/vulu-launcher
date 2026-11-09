using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;

namespace VuluMobile.Services
{
    /// <summary>Telefonun galerisinden (masaüstünde dosya penceresinden) bir resim seçtirir.</summary>
    internal static class Picker
    {
        private const long MaxBytes = 40 * 1024 * 1024;

        public static async Task<byte[]?> PickImageAsync(string title)
        {
            TopLevel? top = Application.Current?.ApplicationLifetime switch
            {
                ISingleViewApplicationLifetime single => TopLevel.GetTopLevel(single.MainView),
                IClassicDesktopStyleApplicationLifetime desktop => desktop.MainWindow,
                _ => null,
            };
            if (top == null) return null;
            var files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = title,
                AllowMultiple = false,
                FileTypeFilter = [FilePickerFileTypes.ImageAll],
            });
            if (files.Count == 0) return null;
            await using var stream = await files[0].OpenReadAsync();
            using var memory = new MemoryStream();
            var buffer = new byte[81920];
            int read;
            while ((read = await stream.ReadAsync(buffer)) > 0)
            {
                memory.Write(buffer, 0, read);
                if (memory.Length > MaxBytes) throw new InvalidOperationException("Resim çok büyük (en fazla 40 MB).");
            }
            return memory.ToArray();
        }
    }
}
