using TallerScan.Core.Actuators;
using TallerScan.Core.Contracts;
using TallerScan.Core.Diagnostics;
using TallerScan.Core.Models;
using TallerScan.Core.Safety;
using TallerScan.Infrastructure.Actuators;
using TallerScan.Infrastructure.Elm;
using TallerScan.Infrastructure.Modules;
using TallerScan.Infrastructure.Simulation;
using TallerScan.Infrastructure.Transports;

namespace TallerScan.Infrastructure.Sessions;

// Algo que la app le hizo al auto (borrado, prueba de actuador): queda en el historial.
public sealed record SessionAction(DateTimeOffset AtUtc, string Kind, string Detail, bool Ok);

public sealed record DtcScan(
    DateTimeOffset AtUtc,
    IReadOnlyList<DtcModuleResult> Stored,
    IReadOnlyList<DtcModuleResult> Pending,
    IReadOnlyList<DtcModuleResult> Permanent,
    IReadOnlyList<MonitorStatus> Monitors,
    IReadOnlyList<FreezeFrameResult> FreezeFrames);

public sealed record LiveSample(DateTimeOffset AtUtc, string Origin, byte Pid, double? Value, string Unit, ReadStatus Status);

// Coordinador único de la sesión: una conexión, un backend, un worker de lectura en vivo.
// Cada conexión tiene un número de generación; lo que llegue de una generación vieja se descarta.
public sealed class DiagnosticSession(ITransportFactory transports, TraceLog trace, ProfileStore? profiles = null) : IAsyncDisposable
{
    private readonly ProfileStore _profiles = profiles ?? new ProfileStore();

    public ProfileStore Profiles => _profiles;

    public const int MaxRecordedSamples = 100_000;

    private static readonly TimeSpan MinCycle = TimeSpan.FromMilliseconds(250);

    private Elm327Backend? _backend;
    private int _generation;
    private CancellationTokenSource? _liveCts;
    private Task? _liveTask;
    private readonly List<LiveSample> _recorded = [];
    private readonly Lock _recordLock = new();

    public SessionState State { get; private set; } = SessionState.Disconnected;

    public AdapterEndpoint? Endpoint { get; private set; }

    public bool IsSimulated => Endpoint is SimulatedEndpoint;

    public ConnectionReport? Report { get; private set; }

    public IReadOnlyList<EcuCapabilities> Capabilities { get; private set; } = [];

    public IReadOnlyList<VinResult> Vin { get; private set; } = [];

    public DtcScan? LastScan { get; private set; }

    public string? TransportDescription { get; private set; }

    public bool IsLive => _liveTask is { IsCompleted: false };

    public bool IsRecording { get; private set; }

    // Lecturas por segundo efectivas del último ciclo (no la deseada).
    public double LiveRate { get; private set; }

    public TraceLog Trace => trace;

    // Perfil de pruebas/borrado por módulo que aplica al auto conectado (null = no hay perfil).
    public ActuatorProfile? Profile { get; private set; }

    // true si el mecánico eligió el perfil a mano (no coincide por VIN): hay que verificar que corresponda.
    public bool ProfileIsManual { get; private set; }

    public string? VinText => Vin.FirstOrDefault(v => v.Vin is not null)?.Vin;

    // Elegir un perfil a mano para un auto real. Solo con la sesión lista y sin pruebas abiertas.
    public void SelectProfile(ActuatorProfile? profile)
    {
        if (State != SessionState.Ready || Actuators is not null)
            throw new InvalidOperationException("Para cambiar el perfil la sesión tiene que estar lista y sin pruebas abiertas.");
        if (IsSimulated)
            return;
        Profile = profile;
        ProfileIsManual = profile is not null;
        ProfileChanged?.Invoke();
    }

    public event Action? ProfileChanged;

    public ActuatorRunner? Actuators { get; private set; }

    public ClearResult? LastClear { get; private set; }

    // Último barrido de módulos (ABS, airbag, caja...) de esta conexión.
    public ModuleScanResult? LastModuleScan { get; private set; }

    public event Action? ModuleScanChanged;

    public bool CanScanModules => State == SessionState.Ready && _backend is { Protocol.IsCan: true };

    private readonly List<SessionAction> _actions = [];

    public IReadOnlyList<SessionAction> Actions
    {
        get { lock (_actions) return [.. _actions]; }
    }

    // Control del auto simulado (motor apagado/ralentí/andando); null fuera de la simulación.
    public Elm327Emulator? Simulator => IsSimulated && State != SessionState.Disconnected ? (transports as TransportFactory)?.CurrentEmulator : null;

    private void AddAction(string kind, string detail, bool ok)
    {
        lock (_actions)
            _actions.Add(new SessionAction(DateTimeOffset.UtcNow, kind, detail, ok));
        ActionAdded?.Invoke();
    }

    public event Action? ActionAdded;

    public event Action<SessionState>? StateChanged;

    public event Action<PidReading>? LiveReading;

    public event Action<string>? ConnectionLost;

    public IReadOnlySet<byte> SupportedPids =>
        Capabilities.SelectMany(c => c.SupportedMode01Pids).ToHashSet();

    private void SetState(SessionState state)
    {
        State = state;
        StateChanged?.Invoke(state);
    }

    public async Task<ConnectionReport> ConnectAsync(AdapterEndpoint endpoint, CancellationToken cancellationToken)
    {
        await DisconnectAsync();
        int gen = Interlocked.Increment(ref _generation);
        Endpoint = endpoint;
        Report = null;
        Capabilities = [];
        Vin = [];
        Profile = null;
        LastClear = null;
        LastModuleScan = null;
        lock (_actions)
            _actions.Clear();
        LastScan = null;
        SetState(SessionState.OpeningTransport);

        var backend = new Elm327Backend(transports, trace);
        backend.ConnectionLost += message =>
        {
            if (gen != _generation)
                return;
            StopLive();
            SetState(SessionState.Faulted);
            ConnectionLost?.Invoke(message);
        };
        _backend = backend;

        try
        {
            SetState(SessionState.InitializingAdapter);
            var report = await backend.ConnectAsync(new AdapterSettings(endpoint), cancellationToken);
            if (gen != _generation)
                return report;
            Report = report;
            TransportDescription = endpoint switch
            {
                SimulatedEndpoint s => $"Simulación ({s.ScenarioId})",
                WifiEndpoint w => $"Wi-Fi {w.Host}:{w.Port}",
                ClassicEndpoint c => $"Bluetooth {c.DisplayName}",
                _ => endpoint.Kind.ToString(),
            };

            if (!report.AdapterConnected)
            {
                await DisconnectAsync();
                Report = report;
                SetState(SessionState.Faulted);
                return report;
            }
            if (!report.VehicleResponding)
            {
                // El adaptador quedó conectado: se puede reintentar dando contacto.
                SetState(SessionState.DetectingVehicle);
                return report;
            }

            SetState(SessionState.DiscoveringCapabilities);
            Capabilities = await backend.DiscoverAsync(cancellationToken);
            Vin = await backend.ReadVinAsync(cancellationToken);
            Profile = _profiles.Match(IsSimulated, VinText);
            ProfileIsManual = false;
            SetState(SessionState.Ready);
            return report;
        }
        catch (OperationCanceledException)
        {
            await DisconnectAsync();
            throw;
        }
        catch (Exception ex)
        {
            await DisconnectAsync();
            Report = new ConnectionReport(false, false, null, null, null, ex.Message);
            SetState(SessionState.Faulted);
            return Report;
        }
    }

    // Reintenta encontrar el auto con el adaptador ya conectado (después de dar contacto).
    public async Task<ConnectionReport> RetryVehicleAsync(CancellationToken cancellationToken)
    {
        if (Endpoint is null)
            throw new InvalidOperationException("No hay adaptador elegido.");
        return await ConnectAsync(Endpoint, cancellationToken);
    }

    private Elm327Backend ReadyBackend =>
        State == SessionState.Ready && _backend is not null
            ? _backend
            : throw new InvalidOperationException("La sesión no está lista.");

    public async Task<DtcScan> ScanDtcsAsync(CancellationToken cancellationToken)
    {
        var backend = ReadyBackend;
        int gen = _generation;
        // Las consultas genéricas van a todos los módulos: se cierra el modo de pruebas si estaba abierto.
        await EndActuatorsAsync();
        var stored = await backend.ReadDtcsAsync(DtcKind.Stored, cancellationToken);
        var pending = await backend.ReadDtcsAsync(DtcKind.Pending, cancellationToken);
        var permanent = await backend.ReadDtcsAsync(DtcKind.Permanent, cancellationToken);
        var monitors = await backend.ReadMonitorStatusAsync(cancellationToken);

        // Freeze frame solo de los PIDs que el auto dice soportar.
        var ffPids = new byte[] { 0x04, 0x05, 0x06, 0x07, 0x0B, 0x0C, 0x0D, 0x0F, 0x11 }
            .Where(SupportedPids.Contains).ToList();
        var freeze = await backend.ReadFreezeFrameAsync(ffPids, cancellationToken);

        var scan = new DtcScan(DateTimeOffset.UtcNow, stored, pending, permanent, monitors, freeze);
        if (gen == _generation)
            LastScan = scan;
        return scan;
    }

    private static List<string> CodesOf(DtcScan scan) =>
        scan.Stored.Concat(scan.Pending).SelectMany(r => r.Codes.Select(c => $"{c} ({r.Origin.Address ?? "?"})")).Distinct().ToList();

    // Borrado genérico (modo 04). El llamador ya confirmó con el usuario y guardó la evidencia.
    // Se relee después: lo que queda es lo que la ECU no quiso o no pudo borrar.
    public async Task<ClearResult> ClearDtcsAsync(CancellationToken cancellationToken)
    {
        var backend = ReadyBackend;
        int gen = _generation;
        StopLive();
        if (Actuators is not null)
            await EndActuatorsAsync();

        var before = LastScan ?? await ScanDtcsAsync(cancellationToken);
        IReadOnlyList<ClearModuleResult> modules;
        using (backend.OpenScope(CommandScope.ClearDtc))
            modules = await backend.ClearDtcsAsync(cancellationToken);

        // Las ECU tardan un momento en actualizar la memoria.
        await Task.Delay(800, cancellationToken);
        var after = await ScanDtcsAsync(cancellationToken);
        var result = new ClearResult(DateTimeOffset.UtcNow, modules, CodesOf(before), CodesOf(after), null);
        if (gen == _generation)
        {
            LastClear = result;
            var accepted = modules.Where(m => m.Status == ReadStatus.Success).Select(m => m.Origin.DisplayName).ToList();
            var detail = result.AnyAccepted
                ? $"Aceptaron: {string.Join(", ", accepted)}. Antes: {result.CodesBefore.Count} código(s); después: {result.CodesAfter.Count}."
                : "Ningún módulo aceptó el borrado: " + string.Join("; ", modules.Select(m => $"{m.Origin.DisplayName}: {m.Message}"));
            AddAction("Borrado de fallas (modo 04)", detail, result.AnyAccepted);
        }
        return result;
    }

    // Abre el modo de pruebas de actuadores del perfil. Pausa la lectura en vivo: el
    // adaptador queda direccionado a un módulo mientras dura.
    public ActuatorRunner StartActuators()
    {
        var backend = ReadyBackend;
        if (Profile is null)
            throw new InvalidOperationException("No hay perfil de pruebas para este vehículo.");
        if (Actuators is not null)
            return Actuators;
        StopLive();
        var runner = new ActuatorRunner(backend, Profile);
        runner.EventRaised += e => AddAction(e.Test, $"{e.Step}: {e.Message}", e.Ok);
        Actuators = runner;
        ActuatorsChanged?.Invoke();
        return runner;
    }

    public async Task EndActuatorsAsync()
    {
        var runner = Actuators;
        Actuators = null;
        if (runner is null)
            return;
        await runner.DisposeAsync();
        ActuatorsChanged?.Invoke();
    }

    public event Action? ActuatorsChanged;

    // Borrado por módulo (UDS) para los que ignoran el modo 04, según el perfil. Relee después.
    public async Task<DtcScan> ClearModuleAsync(ModuleClear clear, CancellationToken cancellationToken)
    {
        var runner = StartActuators();
        try
        {
            await runner.ClearModuleAsync(clear, cancellationToken);
        }
        finally
        {
            await EndActuatorsAsync();
        }
        await Task.Delay(500, cancellationToken);
        return await ScanDtcsAsync(cancellationToken);
    }

    // Barrido de módulos sin perfil: solo lectura (19 02 / 18 a cada dirección). Pausa la lectura
    // en vivo y cierra las pruebas: el adaptador queda direccionado mientras dura.
    public async Task<ModuleScanResult> ScanModulesAsync(IProgress<ModuleScanProgress>? progress, CancellationToken cancellationToken)
    {
        var backend = ReadyBackend;
        int gen = _generation;
        StopLive();
        await EndActuatorsAsync();
        var scanner = backend.CreateModuleScanner();
        ModuleScanResult result;
        using (backend.OpenScope(ModuleScanner.ScanScope(backend.Protocol)))
            result = await scanner.ScanAsync(progress, cancellationToken);
        if (gen == _generation)
        {
            LastModuleScan = result;
            ModuleScanChanged?.Invoke();
        }
        return result;
    }

    // Borrado de un módulo encontrado en el barrido. El llamador ya confirmó y guardó la evidencia.
    public async Task<ModuleClearResult> ClearModuleDtcsAsync(ScannedModule module, CancellationToken cancellationToken)
    {
        var backend = ReadyBackend;
        int gen = _generation;
        StopLive();
        await EndActuatorsAsync();
        var scanner = backend.CreateModuleScanner();
        ModuleClearResult result;
        using (backend.OpenScope(ModuleScanner.ClearScope(module.Address)))
            result = await scanner.ClearAsync(module, cancellationToken);
        if (gen == _generation)
        {
            AddAction($"Borrado de fallas por módulo ({module.DisplayName})", result.Message, result.Accepted);
            if (LastModuleScan is { } scan)
            {
                var modules = scan.Modules.Select(m => m.Address == module.Address ? result.After : m).ToList();
                LastModuleScan = scan with { Modules = modules };
            }
            ModuleScanChanged?.Invoke();
        }
        return result;
    }

    public Task<double?> ReadAdapterVoltageAsync(CancellationToken cancellationToken) =>
        ReadyBackend.ReadAdapterVoltageAsync(cancellationToken);

    public void StartLive(IReadOnlyCollection<byte> pids)
    {
        var backend = ReadyBackend;
        if (Actuators is not null)
            throw new InvalidOperationException("Salir del modo de pruebas antes de leer sensores.");
        StopLive();
        var list = pids.Where(p => PidCatalog.Find(p) is not null).Distinct().ToList();
        if (list.Count == 0)
            return;
        _liveCts = new CancellationTokenSource();
        int gen = _generation;
        var ct = _liveCts.Token;
        _liveTask = Task.Run(() => LiveLoopAsync(backend, gen, list, ct));
    }

    private async Task LiveLoopAsync(Elm327Backend backend, int gen, List<byte> pids, CancellationToken ct)
    {
        // Los rápidos (RPM, mariposa...) en cada vuelta; temperaturas y tensión cada 3 s.
        var slowEvery = TimeSpan.FromSeconds(3);
        var lastSlow = DateTimeOffset.MinValue;
        int consecutiveErrors = 0;
        try
        {
            while (!ct.IsCancellationRequested && gen == _generation)
            {
                var cycleStart = DateTimeOffset.UtcNow;
                bool slowDue = cycleStart - lastSlow >= slowEvery;
                int reads = 0;
                foreach (var pid in pids)
                {
                    var def = PidCatalog.Find(pid)!;
                    if (def.Group == PidGroup.Slow && !slowDue)
                        continue;
                    var readings = await backend.ReadPidAsync(pid, ct);
                    if (gen != _generation || ct.IsCancellationRequested)
                        return;
                    reads++;
                    foreach (var r in readings)
                    {
                        LiveReading?.Invoke(r);
                        if (IsRecording)
                            Record(r);
                    }
                    consecutiveErrors = readings.All(r => r.Status == ReadStatus.CommunicationError) ? consecutiveErrors + 1 : 0;
                    if (consecutiveErrors >= 5)
                        return;
                }
                if (slowDue)
                    lastSlow = cycleStart;
                // Vuelta mínima de 250 ms: con un adaptador rápido no tiene sentido pedir
                // más de 4 veces por segundo (gasta batería y no se llega a leer en pantalla).
                var busy = DateTimeOffset.UtcNow - cycleStart;
                var wait = MinCycle - busy;
                await Task.Delay(wait > TimeSpan.FromMilliseconds(30) ? wait : TimeSpan.FromMilliseconds(30), ct);
                var elapsed = (DateTimeOffset.UtcNow - cycleStart).TotalSeconds;
                if (elapsed > 0 && reads > 0)
                    LiveRate = reads / elapsed;
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (InvalidOperationException)
        {
            // Se desconectó en el medio: la sesión ya informa el estado.
        }
    }

    public void StopLive()
    {
        _liveCts?.Cancel();
        _liveCts = null;
        _liveTask = null;
    }

    public void StartRecording()
    {
        lock (_recordLock)
            _recorded.Clear();
        IsRecording = true;
    }

    public IReadOnlyList<LiveSample> StopRecording()
    {
        IsRecording = false;
        lock (_recordLock)
            return [.. _recorded];
    }

    private void Record(PidReading r)
    {
        lock (_recordLock)
        {
            if (_recorded.Count >= MaxRecordedSamples)
                _recorded.RemoveAt(0);
            _recorded.Add(new LiveSample(r.ReceivedAtUtc, r.Origin.DisplayName, r.Pid, r.Value, r.Unit, r.Status));
        }
    }

    public async Task DisconnectAsync()
    {
        Interlocked.Increment(ref _generation);
        StopLive();
        // Antes de soltar el adaptador se apaga lo que haya quedado forzado.
        await EndActuatorsAsync();
        IsRecording = false;
        var backend = _backend;
        _backend = null;
        // Capacidades, VIN y último escaneo quedan hasta la próxima conexión: se pueden guardar después.
        if (backend is not null)
        {
            try
            {
                await backend.DisposeAsync();
            }
            catch
            {
                // El transporte ya estaba caído.
            }
        }
        if (State != SessionState.Disconnected)
            SetState(SessionState.Disconnected);
    }

    public async ValueTask DisposeAsync() => await DisconnectAsync();
}
