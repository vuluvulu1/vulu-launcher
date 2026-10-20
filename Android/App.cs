namespace VuluLauncher
{
    public sealed class App : Application
    {
        protected override Window CreateWindow(IActivationState? activationState) =>
            new(new MainPage()) { Title = "vulu Launcher" };
    }
}
