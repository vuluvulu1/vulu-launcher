namespace Updater
{
    internal static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            ApplicationConfiguration.Initialize();

            string downloadUrl = args.Length > 0 ? args[0] : "";
            string launcherPath = args.Length > 1 ? args[1] : "";

            Application.Run(new UpdaterForm(downloadUrl, launcherPath));
        }
    }
}