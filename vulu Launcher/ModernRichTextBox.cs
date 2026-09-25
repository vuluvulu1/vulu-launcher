using System.Runtime.InteropServices;

namespace AGLR_Launcher
{
    public class ModernRichTextBox : RichTextBox
    {
        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
        private static extern int SetWindowTheme(IntPtr hwnd, string pszSubAppName, string? pszSubIdList);

        public ModernRichTextBox()
        {
            BackColor = Color.FromArgb(7, 6, 5);
            ForeColor = Color.FromArgb(199, 187, 171);
            BorderStyle = BorderStyle.None;
            ScrollBars = RichTextBoxScrollBars.Vertical;
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            SetWindowTheme(Handle, "DarkMode_Explorer", null);
        }
    }
}
