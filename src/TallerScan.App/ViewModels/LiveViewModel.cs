using System.Collections.ObjectModel;
using System.Globalization;
using TallerScan.App.Mvvm;
using TallerScan.App.Services;
using TallerScan.Core.Diagnostics;
using TallerScan.Core.Models;
using TallerScan.Infrastructure.Reporting;
using TallerScan.Infrastructure.Sessions;

namespace TallerScan.App.ViewModels;

// Un sensor en pantalla. El valor se refresca con un timer (no por cada respuesta),
// y si deja de llegar se marca como viejo: nunca se dibuja un cero inventado.
public sealed class SensorItem(PidDefinition definition) : ObservableObject
{
    private readonly Dictionary<string, PidReading> _byOrigin = [];

    public PidDefinition Definition { get; } = definition;
    public string Name => Definition.Name;
    public string Category => Definition.Category;
    public string Unit => Definition.Unit;

    private bool _isSelected;
    public bool IsSelected { get => _isSelected; set => Set(ref _isSelected, value); }

    private string _valueText = "—";
    public string ValueText { get => _valueText; private set => Set(ref _valueText, value); }

    private string _ageText = "";
    public string AgeText { get => _ageText; private set => Set(ref _ageText, value); }

    private bool _isStale;
    public bool IsStale { get => _isStale; private set => Set(ref _isStale, value); }

    public void Update(PidReading reading)
    {
        lock (_byOrigin)
        {
            // Una lectura fallida no pisa el último valor bueno: queda como histórico.
            var key = reading.Origin.Address ?? "";
            if (reading.Status == ReadStatus.Success || !_byOrigin.ContainsKey(key))
                _byOrigin[key] = reading;
        }
    }

    public void Refresh(DateTimeOffset now, bool live)
    {
        List<PidReading> readings;
        lock (_byOrigin)
            readings = [.. _byOrigin.Values];
        if (readings.Count == 0)
        {
            ValueText = "—";
            AgeText = live ? "esperando…" : "";
            IsStale = false;
            return;
        }
        string Format(PidReading r) => r.Value is double v
            ? $"{Definition.Format(v)} {Unit}".Trim()
            : Infrastructure.History.SessionRecordBuilder.StatusName(r.Status);
        ValueText = readings.Count == 1
            ? Format(readings[0])
            : string.Join("   ", readings.Select(r => $"{Format(r)} ({r.Origin.Address ?? "?"})"));
        var newest = readings.Max(r => r.ReceivedAtUtc);
        var age = now - newest;
        IsStale = age > TimeSpan.FromSeconds(Definition.Group == PidGroup.Fast ? 4 : 10) || !live;
        AgeText = age.TotalSeconds < 1 ? "ahora" : $"hace {age.TotalSeconds:0} s";
    }
}

public sealed class LiveViewModel : ObservableObject
{
    // Selección inicial: lo que más se mira en el taller.
    private static readonly byte[] DefaultPids = [0x0C, 0x05, 0x04, 0x11, 0x0D, 0x06, 0x07, 0x42];

    private readonly DiagnosticSession _session;
    private readonly SessionContext _context;
    private readonly Dictionary<byte, SensorItem> _byPid = [];
    private IDispatcherTimer? _timer;

    public LiveViewModel(DiagnosticSession session, SessionContext context)
    {
        _session = session;
        _context = context;
        StartCommand = new AsyncCommand(StartAsync, () => _session.State == SessionState.Ready && !_session.IsLive);
        StopCommand = new AsyncCommand(StopAsync, () => _session.IsLive);
        RecordCommand = new AsyncCommand(ToggleRecordingAsync, () => _session.IsLive || _session.IsRecording);
        SelectAllCommand = new AsyncCommand(() => { foreach (var s in Sensors) s.IsSelected = true; return Task.CompletedTask; });
        SelectNoneCommand = new AsyncCommand(() => { foreach (var s in Sensors) s.IsSelected = false; return Task.CompletedTask; });

        _session.StateChanged += state => OnUi(() => OnStateChanged(state));
        _session.LiveReading += reading =>
        {
            _context.Remember(reading);
            if (_byPid.TryGetValue(reading.Pid, out var item))
                item.Update(reading);
        };

        // La pestaña se crea al abrirla: puede que la sesión ya esté conectada.
        if (_session.State == SessionState.Ready)
            BuildSensors();
    }

    public ObservableCollection<SensorItem> Sensors { get; } = [];

    public bool IsReady => _session.State == SessionState.Ready;

    public bool IsLive => _session.IsLive;

    public bool IsRecording => _session.IsRecording;

    public bool IsSimulated => _session.IsSimulated && _session.State != SessionState.Disconnected;

    public string RecordText => _session.IsRecording ? "Terminar grabación y exportar" : "Grabar (CSV)";

    private string _status = "Conectá un auto para ver los sensores.";
    public string Status { get => _status; set => Set(ref _status, value); }

    public AsyncCommand StartCommand { get; }
    public AsyncCommand StopCommand { get; }
    public AsyncCommand RecordCommand { get; }
    public AsyncCommand SelectAllCommand { get; }
    public AsyncCommand SelectNoneCommand { get; }

    private void RaiseAll()
    {
        Raise(nameof(IsReady));
        Raise(nameof(IsLive));
        Raise(nameof(IsRecording));
        Raise(nameof(IsSimulated));
        Raise(nameof(RecordText));
        StartCommand.RaiseCanExecuteChanged();
        StopCommand.RaiseCanExecuteChanged();
        RecordCommand.RaiseCanExecuteChanged();
    }

    private void OnStateChanged(SessionState state)
    {
        if (state == SessionState.Ready)
            BuildSensors();
        else if (state is SessionState.Faulted or SessionState.Disconnected)
        {
            StopTimer();
            Status = state == SessionState.Faulted
                ? "Sin conexión: los valores que se ven son los últimos recibidos."
                : "Desconectado.";
            RefreshItems(live: false);
        }
        RaiseAll();
    }

    private void BuildSensors()
    {
        Sensors.Clear();
        _byPid.Clear();
        var supported = _session.SupportedPids;
        foreach (var def in PidCatalog.All.Where(d => supported.Contains(d.Pid)))
        {
            var item = new SensorItem(def) { IsSelected = DefaultPids.Contains(def.Pid) };
            _byPid[def.Pid] = item;
            Sensors.Add(item);
        }
        Status = Sensors.Count == 0
            ? "El auto no informó sensores del catálogo."
            : $"{Sensors.Count} sensores disponibles. Marcá los que quieras ver y tocá Iniciar.";
    }

    private async Task StartAsync()
    {
        var pids = Sensors.Where(s => s.IsSelected).Select(s => s.Definition.Pid).ToList();
        if (pids.Count == 0)
        {
            Status = "Marcá al menos un sensor.";
            return;
        }
        // Si quedó abierto el modo de pruebas, se cierra (apaga lo forzado) antes de leer.
        await _session.EndActuatorsAsync();
        _session.StartLive(pids);
        StartTimer();
        Status = $"Leyendo {pids.Count} sensores… cuantos más, más lenta la actualización.";
        RaiseAll();
    }

    private async Task StopAsync()
    {
        if (_session.IsRecording)
            await ToggleRecordingAsync();
        _session.StopLive();
        StopTimer();
        RefreshItems(live: false);
        Status = "Lectura detenida.";
        RaiseAll();
    }

    private async Task ToggleRecordingAsync()
    {
        if (!_session.IsRecording)
        {
            _session.StartRecording();
            RaiseAll();
            return;
        }
        var samples = _session.StopRecording();
        RaiseAll();
        if (samples.Count == 0)
        {
            await Ui.AlertAsync("Grabación", "No se grabó ninguna muestra.");
            return;
        }
        var csv = CsvExport.Samples(samples);
        var plate = Ui.SafeFileName(_context.Plate);
        await Ui.ShareTextFileAsync($"sensores-{plate}-{DateTime.Now:yyyyMMdd-HHmm}.csv", csv, "Grabación de sensores");
    }

    // Android: al salir de la app se pausa la lectura (no se promete sondeo en segundo plano).
    public void OnAppStopped()
    {
        if (!_session.IsLive)
            return;
        if (_session.IsRecording)
            _session.StopRecording();
        _session.StopLive();
        StopTimer();
        OnUi(() =>
        {
            Status = "Lectura pausada al salir de la app. Tocá Iniciar para seguir.";
            RefreshItems(live: false);
            RaiseAll();
        });
    }

    private void StartTimer()
    {
        if (_timer is not null)
            return;
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null)
            return;
        // Hasta ~3 refrescos por segundo: suficiente a la vista y liviano para el celular.
        _timer = dispatcher.CreateTimer();
        _timer.Interval = TimeSpan.FromMilliseconds(350);
        _timer.Tick += (_, _) =>
        {
            RefreshItems(live: _session.IsLive);
            if (_session.IsLive)
                Status = $"Leyendo · {_session.LiveRate.ToString("0.0", CultureInfo.CurrentCulture)} lecturas/s{(_session.IsRecording ? " · GRABANDO" : "")}";
        };
        _timer.Start();
    }

    private void StopTimer()
    {
        _timer?.Stop();
        _timer = null;
    }

    private void RefreshItems(bool live)
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var s in Sensors)
            s.Refresh(now, live && s.IsSelected);
    }
}
