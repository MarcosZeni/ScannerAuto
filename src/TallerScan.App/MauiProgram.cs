using TallerScan.App.Platform;
using TallerScan.App.Services;
using TallerScan.App.ViewModels;
using TallerScan.App.Views;
using TallerScan.Core.Contracts;
using TallerScan.Infrastructure.Actuators;
using TallerScan.Infrastructure.Elm;
using TallerScan.Infrastructure.History;
using TallerScan.Infrastructure.Sessions;
using TallerScan.Infrastructure.Transports;

namespace TallerScan.App;

public static class MauiProgram
{
    // Datos de la app (historial y perfiles). En Windows sin empaquetar, MAUI devuelve una
    // carpeta con nombre raro ("Datos de programa"): se usa %LOCALAPPDATA%\TallerScan, que se entiende.
    public static string DataDirectory =>
        DeviceInfo.Platform == DevicePlatform.WinUI
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TallerScan")
            : FileSystem.AppDataDirectory;

    public static string ProfilesDirectory => Path.Combine(DataDirectory, "perfiles");

    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder.UseMauiApp<App>();

        var services = builder.Services;

        // Núcleo: un solo registro de tráfico, una sola sesión (un adaptador por equipo).
        services.AddSingleton<TraceLog>();
        services.AddSingleton<ITransportFactory>(_ => new TransportFactory(endpoint => new BluetoothClassicTransport(endpoint)));
        services.AddSingleton<IAdapterDiscovery, PlatformBluetooth>();
        // Perfiles de actuadores: el simulado más los JSON importados a la carpeta de datos.
        services.AddSingleton(_ =>
        {
            var store = new ProfileStore();
            store.LoadDirectory(ProfilesDirectory);
            return store;
        });
        services.AddSingleton<DiagnosticSession>();
        services.AddSingleton(_ => new JsonHistoryStore(Path.Combine(DataDirectory, "historial")));
        services.AddSingleton<SessionContext>();

        services.AddSingleton<ConnectionViewModel>();
        services.AddSingleton<FaultsViewModel>();
        services.AddSingleton<LiveViewModel>();
        services.AddSingleton<HistoryViewModel>();
        services.AddSingleton<TraceViewModel>();
        services.AddSingleton<ActuatorsViewModel>();
        services.AddTransient<HistoryDetailViewModel>();

        services.AddSingleton<AppShell>();
        services.AddSingleton<ConnectionPage>();
        services.AddSingleton<FaultsPage>();
        services.AddSingleton<LivePage>();
        services.AddSingleton<HistoryPage>();
        services.AddTransient<TracePage>();
        services.AddSingleton<ActuatorsPage>();
        services.AddTransient<HistoryDetailPage>();

        return builder.Build();
    }
}
