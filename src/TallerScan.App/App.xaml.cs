using TallerScan.App.ViewModels;

namespace TallerScan.App;

public partial class App : Application
{
    private readonly IServiceProvider _services;

    public App(IServiceProvider services)
    {
        InitializeComponent();
        _services = services;
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        var window = new Window(_services.GetRequiredService<AppShell>()) { Title = "TallerScan" };

        if (DeviceInfo.Platform == DevicePlatform.Android)
        {
            // En el celular no se promete lectura en segundo plano: al salir se pausa.
            // La conexión queda abierta para que compartir o un permiso no la corten.
            window.Stopped += (_, _) => _services.GetRequiredService<LiveViewModel>().OnAppStopped();
        }
        else if (DeviceInfo.Platform == DevicePlatform.WinUI)
        {
            window.Width = 980;
            window.Height = 860;
            window.MinimumWidth = 420;
            window.MinimumHeight = 600;
        }
        return window;
    }
}
