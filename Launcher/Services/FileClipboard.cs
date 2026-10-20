using System.Runtime.InteropServices;

namespace VuluLauncher
{
    /// <summary>Panoya dosya koyma (CF_HDROP) ve geri dönüşüm kutusuna taşıma.</summary>
    internal static class FileClipboard
    {
        public static bool SetFiles(IReadOnlyList<string> paths)
        {
            // DROPFILES başlığı + çift sıfırla biten UTF-16 dosya listesi
            string list = string.Join('\0', paths) + "\0\0";
            byte[] chars = System.Text.Encoding.Unicode.GetBytes(list);
            const int headerSize = 20;
            IntPtr mem = GlobalAlloc(0x0042 /* GMEM_MOVEABLE | GMEM_ZEROINIT */, (UIntPtr)(headerSize + chars.Length));
            if (mem == IntPtr.Zero) return false;
            IntPtr ptr = GlobalLock(mem);
            Marshal.WriteInt32(ptr, 0, headerSize);   // pFiles
            Marshal.WriteInt32(ptr, 16, 1);           // fWide
            Marshal.Copy(chars, 0, ptr + headerSize, chars.Length);
            GlobalUnlock(mem);

            for (int attempt = 0; attempt < 5; attempt++)
            {
                if (OpenClipboard(IntPtr.Zero))
                {
                    try
                    {
                        EmptyClipboard();
                        if (SetClipboardData(15 /* CF_HDROP */, mem) != IntPtr.Zero) return true;   // bellek artık panonun
                    }
                    finally { CloseClipboard(); }
                    break;
                }
                Thread.Sleep(50);   // başka bir uygulama panoyu tutuyor
            }
            GlobalFree(mem);
            return false;
        }

        public static bool Recycle(string path)
        {
            var op = new SHFILEOPSTRUCT
            {
                wFunc = 3, // FO_DELETE
                pFrom = path + "\0\0",
                fFlags = 0x0040 | 0x0010 | 0x0004 | 0x0400, // FOF_ALLOWUNDO | FOF_NOCONFIRMATION | FOF_SILENT | FOF_NOERRORUI
            };
            return SHFileOperation(ref op) == 0 && !op.fAnyOperationsAborted && !File.Exists(path);
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct SHFILEOPSTRUCT
        {
            public IntPtr hwnd;
            public uint wFunc;
            public string pFrom;
            public string? pTo;
            public ushort fFlags;
            [MarshalAs(UnmanagedType.Bool)] public bool fAnyOperationsAborted;
            public IntPtr hNameMappings;
            public string? lpszProgressTitle;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern int SHFileOperation(ref SHFILEOPSTRUCT op);
        [DllImport("user32.dll", SetLastError = true)] private static extern bool OpenClipboard(IntPtr owner);
        [DllImport("user32.dll")] private static extern bool EmptyClipboard();
        [DllImport("user32.dll")] private static extern IntPtr SetClipboardData(uint format, IntPtr mem);
        [DllImport("user32.dll")] private static extern bool CloseClipboard();
        [DllImport("kernel32.dll")] private static extern IntPtr GlobalAlloc(uint flags, UIntPtr bytes);
        [DllImport("kernel32.dll")] private static extern IntPtr GlobalLock(IntPtr mem);
        [DllImport("kernel32.dll")] private static extern bool GlobalUnlock(IntPtr mem);
        [DllImport("kernel32.dll")] private static extern IntPtr GlobalFree(IntPtr mem);
    }
}
