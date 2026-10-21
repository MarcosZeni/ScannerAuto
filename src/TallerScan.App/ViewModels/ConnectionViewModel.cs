using System.Collections.ObjectModel;
using System.Globalization;
using TallerScan.App.Mvvm;
using TallerScan.App.Services;
using TallerScan.Core.Contracts;
using TallerScan.Core.Models;
using TallerScan.Infrastructure.Sessions;
using TallerScan.Infrastructure.Simulation;

namespace TallerScan.App.ViewModels;

public sealed record TransportOption(TransportKind Kind, string Name, bool Available, string Hint)
{
    public string Label => Available ? Name : $"{Name} (no disponible en este equipo)";

    public override string ToString() => Label;
}

public sealed record ScenarioOption(string Id, string Name, string Description)
{
    public override string ToString() => Name;
}

public sealed class ConnectionViewModel : ObservableObject
{
    private readonly DiagnosticSession _session;
    private readonly IAdapterDiscovery _discovery;
    private readonly SessionContext _context;
    private CancellationTokenSource? _connectCts;

    public ConnectionViewModel(DiagnosticSession session, ITransportFactory transports, IAdapterDiscovery discovery, SessionContext context)
    {
        _session = session;
        _discovery = discovery;
        _context = context;

        Transports =
        [
            new(TransportKind.BluetoothClassic, "Bluetooth", transports.IsAvailable(TransportKind.BluetoothClassic),
                "Vinculá primero el adaptador en Ajustes → Bluetooth del equipo (PIN habitual 1234 o 0000). Después tocá \"Buscar adaptadores\"."),
            new(TransportKind.Wifi, "Wi-Fi", transports.IsAvailable(TransportKind.Wifi),
                "Conectá el equipo a la red Wi-Fi del adaptador (suele llamarse WiFi_OBDII). Para el emulador de la PC: 127.0.0.1 en la misma PC, 10.0.2.2 desde el emulador de Android, o la IP de la PC desde el celular."),
            new(TransportKind.Simulation, "Simulador (sin auto)", true,
                "Auto simulado para practicar y probar la app. Los datos no son reales."),
        ];
        Scenarios = TallerScan.Infrastructure.Simulation.Scenarios.Catalog.Select(s => new ScenarioOption(s.Id, s.Name, s.Description)).ToList();
        _selectedTransport = Transports.First(t => t.Available);
        _selectedScenario = Scenarios.First(s => s.Id == "falla-encendido");

        ConnectCommand = new AsyncCommand(ConnectAsync, () => !IsBusy && !IsConnected);
        DisconnectCommand = new AsyncCommand(DisconnectAsync, () => IsConnected || IsBusy);
        RetryCommand = new AsyncCommand(RetryAsync, () => !IsBusy && _session.State == SessionState.DetectingVehicle);
        FindAdaptersCommand = new AsyncCommand(FindAdaptersAsync, () => !IsBusy);
        TraceCommand = new AsyncCommand(() => Shell.Current.GoToAsync("traza"));
        EngineOffCommand = new AsyncCommand(() => SetEngine(EngineMode.Off));
        EngineIdleCommand = new AsyncCommand(() => SetEngine(EngineMode.Idle));
        EngineDriveCommand = new AsyncCommand(() => SetEngine(EngineMode.Driving));

        _session.StateChanged += _ => OnUi(Refresh);
        _session.ConnectionLost += message => OnUi(() =>
        {
            Message = $"Se perdió la conexión: {message} Los últimos valores quedan como históricos.";
            Refresh();
        });
        Refresh();
    }

    public IReadOnlyList<TransportOption> Transports { get; }

    public IReadOnlyList<ScenarioOption> Scenarios { get; }

    public ObservableCollection<DiscoveredAdapter> Adapters { get; } = [];

    private TransportOption _selectedTransport;
    public TransportOption SelectedTransport
    {
        get => _selectedTransport;
        set
        {
            if (value is null || !Set(ref _selectedTransport, value))
                return;
            Raise(nameof(IsSimulation));
            Raise(nameof(IsBluetooth));
            Raise(nameof(IsWifi));
            Raise(nameof(TransportHint));
        }
    }

    public bool IsSimulation => SelectedTransport.Kind == TransportKind.Simulation;
    public bool IsBluetooth => SelectedTransport.Kind == TransportKind.BluetoothClassic;
    public bool IsWifi => SelectedTransport.Kind == TransportKind.Wifi;
    public string TransportHint => SelectedTransport.Hint;

    private ScenarioOption _selectedScenario;
    public ScenarioOption SelectedScenario
    {
        get => _selectedScenario;
        set
        {
            if (value is not null)
                Set(ref _selectedScenario, value);
        }
    }

    private DiscoveredAdapter? _selectedAdapter;
    public DiscoveredAdapter? SelectedAdapter { get => _selectedAdapter; set => Set(ref _selectedAdapter, value); }

    private string? _adapterProblem;
    public string? AdapterProblem { get => _adapterProblem; set { Set(ref _adapterProblem, value); Raise(nameof(HasAdapterProblem)); } }
    public bool HasAdapterProblem => !string.IsNullOrEmpty(AdapterProblem);

    private string _wifiHost = Preferences.Default.Get("wifi_host", "192.168.0.10");
    public string WifiHost { get => _wifiHost; set => Set(ref _wifiHost, value); }

    private string _wifiPort = Preferences.Default.Get("wifi_port", "35000");
    public string WifiPort { get => _wifiPort; set => Set(ref _wifiPort, value); }

    public string Plate
    {
        get => _context.Plate;
        set { _context.Plate = value; Raise(); }
    }

    public string Vehicle
    {
        get => _context.Vehicle;
        set { _context.Vehicle = value; Raise(); }
    }

    private bool _isBusy;
    public bool IsBusy { get => _isBusy; set { Set(ref _isBusy, value); RaiseCommands(); } }

    public bool IsConnected => _session.State is SessionState.Ready or SessionState.DetectingVehicle;

    public bool IsReady => _session.State == SessionState.Ready;

    public bool CanRetry => _session.State == SessionState.DetectingVehicle;

    private string _message = "";
    public string Message { get => _message; set { Set(ref _message, value); Raise(nameof(HasMessage)); } }
    public bool HasMessage => !string.IsNullOrEmpty(Message);

    public string StateText => _session.State switch
    {
        SessionState.Disconnected => "Desconectado",
        SessionState.OpeningTransport => "Abriendo conexión…",
        SessionState.InitializingAdapter => "Iniciando adaptador…",
        SessionState.DetectingVehicle => "Adaptador conectado · el auto no responde",
        SessionState.DiscoveringCapabilities => "Leyendo qué datos tiene el auto…",
        SessionState.Ready => _session.IsSimulated ? "Conectado · SIMULACIÓN" : "Conectado al auto",
        SessionState.Faulted => "Sin conexión",
        _ => "",
    };

    public Color StateColor => _session.State switch
    {
        SessionState.Ready => _session.IsSimulated ? Color.FromArgb("#8A5A00") : Color.FromArgb("#1E7A46"),
        SessionState.Faulted => Color.FromArgb("#B42318"),
        SessionState.Disconnected => Color.FromArgb("#56616B"),
        _ => Color.FromArgb("#1F4E79"),
    };

    public bool HasResult => _session.Report is not null && _session.State != SessionState.Disconnected;
    public string AdapterText => _session.Report?.AdapterIdentifier ?? "—";
    public string TransportText => _session.TransportDescription ?? "—";
    public string ProtocolText => _session.Report?.Protocol ?? "—";
    public string VoltageText => _session.Report?.AdapterVoltage is double v ? v.ToString("0.0", CultureInfo.CurrentCulture) + " V (medida por el adaptador)" : "—";
    public string VinText => _session.Vin.FirstOrDefault(v => v.Vin is not null)?.Vin ?? (IsReady ? "El auto no informa VIN" : "—");
    public string ModulesText => _session.Capabilities.Count == 0
        ? "—"
        : string.Join("\n", _session.Capabilities.Select(c => $"{c.Origin.DisplayName}: {c.SupportedMode01Pids.Count(p => !TallerScan.Core.Diagnostics.PidCatalog.IsSupportPid(p))} datos disponibles"));
    public bool IsSimulatedSession => _session.IsSimulated && IsConnected;

    public AsyncCommand ConnectCommand { get; }
    public AsyncCommand DisconnectCommand { get; }
    public AsyncCommand RetryCommand { get; }
    public AsyncCommand FindAdaptersCommand { get; }
    public AsyncCommand TraceCommand { get; }
    public AsyncCommand EngineOffCommand { get; }
    public AsyncCommand EngineIdleCommand { get; }
    public AsyncCommand EngineDriveCommand { get; }

    // Control del motor del auto simulado (para probar borrado y actuadores, que piden motor apagado).
    public string EngineText => _session.Simulator?.Engine.Mode switch
    {
        EngineMode.Off => "Motor simulado: apagado (contacto dado)",
        EngineMode.Idle => "Motor simulado: en ralentí",
        EngineMode.Driving => "Motor simulado: andando",
        _ => "",
    };

    private Task SetEngine(EngineMode mode)
    {
        if (_session.Simulator is { } sim)
            sim.Engine.Mode = mode;
        Raise(nameof(EngineText));
        return Task.CompletedTask;
    }

    private void RaiseCommands()
    {
        ConnectCommand.RaiseCanExecuteChanged();
        DisconnectCommand.RaiseCanExecuteChanged();
        RetryCommand.RaiseCanExecuteChanged();
        FindAdaptersCommand.RaiseCanExecuteChanged();
    }

    private void Refresh()
    {
        foreach (var name in new[]
        {
            nameof(StateText), nameof(StateColor), nameof(IsConnected), nameof(IsReady), nameof(CanRetry), nameof(HasResult),
            nameof(AdapterText), nameof(TransportText), nameof(ProtocolText), nameof(VoltageText), nameof(VinText),
            nameof(ModulesText), nameof(IsSimulatedSession), nameof(EngineText),
        })
            Raise(name);
        RaiseCommands();
    }

    private AdapterEndpoint? BuildEndpoint()
    {
        switch (SelectedTransport.Kind)
        {
            case TransportKind.Simulation:
                return new SimulatedEndpoint(SelectedScenario.Id);
            case TransportKind.Wifi:
                if (string.IsNullOrWhiteSpace(WifiHost) || !int.TryParse(WifiPort, out int port) || port is < 1 or > 65535)
                {
                    Message = "Revisar la dirección y el puerto del adaptador Wi-Fi.";
                    return null;
                }
                Preferences.Default.Set("wifi_host", WifiHost.Trim());
                Preferences.Default.Set("wifi_port", WifiPort.Trim());
                return new WifiEndpoint(WifiHost.Trim(), port);
            case TransportKind.BluetoothClassic:
                if (SelectedAdapter is null)
                {
                    Message = "Elegí un adaptador de la lista (tocá \"Buscar adaptadores\").";
                    return null;
                }
                Preferences.Default.Set("bt_last", SelectedAdapter.Endpoint is ClassicEndpoint c ? c.DeviceId : "");
                return SelectedAdapter.Endpoint;
            default:
                Message = "Ese tipo de conexión todavía no está disponible.";
                return null;
        }
    }

    private async Task ConnectAsync()
    {
        Message = "";
        var endpoint = BuildEndpoint();
        if (endpoint is null)
            return;
        IsBusy = true;
        _context.ClearReadings();
        _connectCts = new CancellationTokenSource();
        try
        {
            var report = await _session.ConnectAsync(endpoint, _connectCts.Token);
            Message = report switch
            {
                { AdapterConnected: false } => report.Message ?? "No se pudo conectar con el adaptador.",
                { VehicleResponding: false } => report.Message ?? "El adaptador respondió, pero el auto no.",
                _ => report.Message ?? "",
            };
        }
        finally
        {
            IsBusy = false;
            _connectCts = null;
            Refresh();
        }
    }

    private async Task RetryAsync()
    {
        IsBusy = true;
        Message = "";
        try
        {
            var report = await _session.RetryVehicleAsync(CancellationToken.None);
            if (!report.VehicleResponding)
                Message = report.Message ?? "El auto sigue sin responder.";
        }
        finally
        {
            IsBusy = false;
            Refresh();
        }
    }

    private async Task DisconnectAsync()
    {
        _connectCts?.Cancel();
        await _session.DisconnectAsync();
        Message = "";
        Refresh();
    }

    private async Task FindAdaptersAsync()
    {
        IsBusy = true;
        AdapterProblem = null;
        try
        {
            var result = await _discovery.ListPairedAsync(CancellationToken.None);
            Adapters.Clear();
            foreach (var a in result.Adapters)
                Adapters.Add(a);
            AdapterProblem = result.Problem ?? (Adapters.Count == 0 ? "No hay adaptadores vinculados. Vinculalo en los ajustes de Bluetooth del equipo." : null);
            var last = Preferences.Default.Get("bt_last", "");
            SelectedAdapter = Adapters.FirstOrDefault(a => a.Endpoint is ClassicEndpoint c && c.DeviceId == last)
                              ?? Adapters.FirstOrDefault(a => a.DisplayName.Contains("OBD", StringComparison.OrdinalIgnoreCase))
                              ?? Adapters.FirstOrDefault();
        }
        finally
        {
            IsBusy = false;
        }
    }
}
