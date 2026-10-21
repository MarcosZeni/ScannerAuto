using TallerScan.App.Views;

namespace TallerScan.App;

public partial class AppShell : Shell
{
    public AppShell()
    {
        InitializeComponent();
        Routing.RegisterRoute("detalle", typeof(HistoryDetailPage));
        // La traza técnica se abre desde Conexión; no ocupa una pestaña.
        Routing.RegisterRoute("traza", typeof(TracePage));
    }
}
