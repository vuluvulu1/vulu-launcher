using System.Runtime.InteropServices;

namespace VuluLauncher
{
    /// <summary>
    /// Profil resmini bildirim kartı için hazırlar: Windows'un resim bileşeniyle (WIC) çözer (PNG, JPEG, GIF, WebP),
    /// kare olacak şekilde ortadan kırpıp küçültür ve kenarı yumuşak bir daireye keser. Sonuç, AlphaBlend ile çizilen
    /// önceden çarpılmış alfalı 32 bit bir GDI resmidir; çağıran işi bitince DeleteObject ile siler.
    /// Çözülemezse IntPtr.Zero döner (kartta baş harf görünür).
    /// </summary>
    internal static class AvatarBitmap
    {
        private static readonly Guid FactoryClsid = new("cacaf262-9370-4615-a13b-9f5539da4c0a");
        private static readonly Guid Pbgra32 = new("6fddc324-4e03-4bfe-b185-3d77768dc910");

        public static IntPtr Load(string path, int size)
        {
            try
            {
                if (!File.Exists(path) || size <= 0) return IntPtr.Zero;
                var factory = (IWICImagingFactory)Activator.CreateInstance(Type.GetTypeFromCLSID(FactoryClsid)!)!;
                if (factory.CreateDecoderFromFilename(path, IntPtr.Zero, 0x80000000 /* GENERIC_READ */, 1 /* OnLoad */, out var decoder) != 0) return IntPtr.Zero;
                if (decoder.GetFrame(0, out var frame) != 0) return IntPtr.Zero;

                if (factory.CreateFormatConverter(out var converter) != 0) return IntPtr.Zero;
                var format = Pbgra32;
                if (converter.Initialize((IWICBitmapSource)frame, ref format, 0, IntPtr.Zero, 0, 0) != 0) return IntPtr.Zero;
                converter.GetSize(out uint width, out uint height);
                if (width == 0 || height == 0) return IntPtr.Zero;

                // Kısa kenar "size" olacak şekilde küçültülür, sonra ortadan kare kırpılır
                double scale = (double)size / Math.Min(width, height);
                uint sw = Math.Max((uint)size, (uint)Math.Round(width * scale)), sh = Math.Max((uint)size, (uint)Math.Round(height * scale));
                if (factory.CreateBitmapScaler(out var scaler) != 0) return IntPtr.Zero;
                if (scaler.Initialize((IWICBitmapSource)converter, sw, sh, 3 /* Fant */) != 0) return IntPtr.Zero;

                var header = new BITMAPINFOHEADER
                {
                    biSize = Marshal.SizeOf<BITMAPINFOHEADER>(), biWidth = size, biHeight = -size, biPlanes = 1, biBitCount = 32,
                };
                IntPtr bitmap = CreateDIBSection(IntPtr.Zero, ref header, 0, out IntPtr bits, IntPtr.Zero, 0);
                if (bitmap == IntPtr.Zero) return IntPtr.Zero;

                var crop = new WICRect { X = (int)(sw - size) / 2, Y = (int)(sh - size) / 2, Width = size, Height = size };
                IntPtr rect = Marshal.AllocHGlobal(Marshal.SizeOf<WICRect>());
                try
                {
                    Marshal.StructureToPtr(crop, rect, false);
                    if (scaler.CopyPixels(rect, (uint)size * 4, (uint)(size * size * 4), bits) != 0) { DeleteObject(bitmap); return IntPtr.Zero; }
                }
                finally { Marshal.FreeHGlobal(rect); }

                CutCircle(bits, size);
                return bitmap;
            }
            catch { return IntPtr.Zero; }
        }

        /// <summary>Dairenin dışını saydam yapar; kenar bir piksel boyunca yumuşar (önceden çarpılmış alfa: dört kanal ölçeklenir).</summary>
        private static void CutCircle(IntPtr bits, int size)
        {
            var pixels = new byte[size * size * 4];
            Marshal.Copy(bits, pixels, 0, pixels.Length);
            double c = (size - 1) / 2.0, radius = size / 2.0;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    double d = Math.Sqrt((x - c) * (x - c) + (y - c) * (y - c));
                    double coverage = Math.Clamp(radius - d, 0, 1);
                    if (coverage >= 1) continue;
                    int i = (y * size + x) * 4;
                    for (int k = 0; k < 4; k++) pixels[i + k] = (byte)(pixels[i + k] * coverage);
                }
            Marshal.Copy(pixels, 0, bits, pixels.Length);
        }

        // ── WIC ───────────────────────────────────────────────────────────
        // Yalnızca kullanılan yöntemler gerçek imzalarıyla; aradaki yöntemler sıra (vtable) korunsun diye yer tutucu.

        [StructLayout(LayoutKind.Sequential)] private struct WICRect { public int X, Y, Width, Height; }

        [ComImport, Guid("ec5ec8a9-c395-4314-9c77-54d7a935ff70"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IWICImagingFactory
        {
            [PreserveSig] int CreateDecoderFromFilename([MarshalAs(UnmanagedType.LPWStr)] string name, IntPtr vendor, uint access, int options, out IWICBitmapDecoder decoder);
            void CreateDecoderFromStream();
            void CreateDecoderFromFileHandle();
            void CreateComponentInfo();
            void CreateDecoder();
            void CreateEncoder();
            void CreatePalette();
            [PreserveSig] int CreateFormatConverter(out IWICFormatConverter converter);
            [PreserveSig] int CreateBitmapScaler(out IWICBitmapScaler scaler);
        }

        [ComImport, Guid("9edde9e7-8dee-47ea-99df-e6faf2ed44bf"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IWICBitmapDecoder
        {
            void QueryCapability();
            void Initialize();
            void GetContainerFormat();
            void GetDecoderInfo();
            void CopyPalette();
            void GetMetadataQueryReader();
            void GetPreview();
            void GetColorContexts();
            void GetThumbnail();
            void GetFrameCount();
            [PreserveSig] int GetFrame(uint index, out IWICBitmapFrameDecode frame);
        }

        [ComImport, Guid("00000120-a8f2-4877-ba0a-fd2b6645fb94"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IWICBitmapSource
        {
            void GetSize(out uint width, out uint height);
            void GetPixelFormat();
            void GetResolution();
            void CopyPalette();
            [PreserveSig] int CopyPixels(IntPtr rect, uint stride, uint bufferSize, IntPtr buffer);
        }

        [ComImport, Guid("3b16811b-6a43-4ec9-a813-3d930c13b940"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IWICBitmapFrameDecode
        {
            void GetSize(out uint width, out uint height);
            void GetPixelFormat();
            void GetResolution();
            void CopyPalette();
            [PreserveSig] int CopyPixels(IntPtr rect, uint stride, uint bufferSize, IntPtr buffer);
        }

        [ComImport, Guid("00000301-a8f2-4877-ba0a-fd2b6645fb94"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IWICFormatConverter
        {
            void GetSize(out uint width, out uint height);
            void GetPixelFormat();
            void GetResolution();
            void CopyPalette();
            [PreserveSig] int CopyPixels(IntPtr rect, uint stride, uint bufferSize, IntPtr buffer);
            [PreserveSig] int Initialize(IWICBitmapSource source, ref Guid destinationFormat, int dither, IntPtr palette, double alphaThreshold, int paletteTranslate);
        }

        [ComImport, Guid("00000302-a8f2-4877-ba0a-fd2b6645fb94"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IWICBitmapScaler
        {
            void GetSize(out uint width, out uint height);
            void GetPixelFormat();
            void GetResolution();
            void CopyPalette();
            [PreserveSig] int CopyPixels(IntPtr rect, uint stride, uint bufferSize, IntPtr buffer);
            [PreserveSig] int Initialize(IWICBitmapSource source, uint width, uint height, int mode);
        }

        // ── GDI ───────────────────────────────────────────────────────────
        [StructLayout(LayoutKind.Sequential)]
        private struct BITMAPINFOHEADER
        {
            public int biSize, biWidth, biHeight;
            public short biPlanes, biBitCount;
            public int biCompression, biSizeImage, biXPelsPerMeter, biYPelsPerMeter, biClrUsed, biClrImportant;
        }

        [DllImport("gdi32.dll")] private static extern IntPtr CreateDIBSection(IntPtr dc, ref BITMAPINFOHEADER header, uint usage, out IntPtr bits, IntPtr section, uint offset);
        [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr obj);
    }
}
