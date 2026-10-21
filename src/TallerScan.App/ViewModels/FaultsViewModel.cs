using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using TallerScan.App.Mvvm;
using TallerScan.App.Services;
using TallerScan.Core.Diagnostics;
using TallerScan.Core.Models;
using TallerScan.Infrastructure.History;
using TallerScan.Infrastructure.Reporting;
using TallerScan.Infrastructure.Sessions;

namespace TallerScan.App.ViewModels;

// Una tarjeta: un código de falla, o una consulta que no tuvo respuesta válida
// (eso también se muestra: "no respondió" no es lo mismo que "sin fallas").
public sealed record FaultItem(string Title, string Badge, Color BadgeColor, string Body, string Footer, bool IsCode);

public sealed class ModuleClearItem(TallerScan.Core.Actuators.ModuleClear clear, Func<TallerScan.Core.Actuators.ModuleClear, Task> run)
{
    public string Label => $"Borrar fallas de {clear.ModuleName} (por módulo)";
    public AsyncCommand Command { get; } = new(() => run(clear));
}

// Un módulo encontrado en el barrido por CAN, con sus fallas y el botón de borrado.
public sealed class ModuleItem(ScannedModule module, Func<ScannedModule, Task> clear)
{
    public ScannedModule Module => module;

    public string Title => module.DisplayName;

    public string Subtitle
    {
        get
        {
            var parts = new List<string> { module.Address.Name.StartsWith("Módulo", StringComparison.Ordinal) ? $"pedido {module.Address.RequestId}" : module.Address.Name };
            parts.Add(module.Dialect == ModuleDialect.Kwp ? "KWP2000" : "UDS");
            if (module.SystemHint is { } hint)
                parts.Add("por sus códigos: " + hint);
            return string.Join(" · ", parts);
        }
    }

    public string Status => module.Status switch
    {
        ReadStatus.Success when module.Dtcs.Count == 0 => "Sin fallas",
        ReadStatus.Success => $"{module.Dtcs.Count} falla(s)",
        _ => "Contestó pero no se pudieron leer sus fallas: " + (module.Message ?? SessionRecordBuilder.StatusName(module.Status)),
    };

    public Color StatusColor => module.Status != ReadStatus.Success ? Color.FromArgb("#8A5A00")
        : module.Dtcs.Count == 0 ? Color.FromArgb("#1E7A46") : Color.FromArgb("#B42318");

    public string Codes => string.Join("\n", module.Dtcs.Select(d =>
    {
        var info = DtcCatalog.Describe(d.Code);
        var text = info.Description ?? (info.IsGeneric ? "descripción no disponible en el catálogo" : "código propio del fabricante");
        return $"{d.Display}  {text}  ({d.StatusText})";
    }));

    public bool HasDtcs => module.Dtcs.Count > 0;

    public AsyncCommand ClearCommand { get; } = new(() => clear(module));
}

public sealed class FaultsViewModel : ObservableObject
{
    private readonly DiagnosticSession _session;
    private readonly SessionContext _context;
    private readonly JsonHistoryStore _history;
    private CancellationTokenSource? _scanCts;

    public FaultsViewModel(DiagnosticSession session, SessionContext context, JsonHistoryStore history)
    {
        _session = session;
        _context = context;
        _history = history;
        ReadCommand = new AsyncCommand(ReadAsync, () => !IsBusy && _session.State == SessionState.Ready);
        SaveCommand = new AsyncCommand(SaveAsync, () => !IsBusy && _session.LastScan is not null);
        ShareCommand = new AsyncCommand(ShareAsync, () => !IsBusy && _session.LastScan is not null);
        ClearCommand = new AsyncCommand(ClearAsync, () => !IsBusy && IsReady && HasScan);
        ScanModulesCommand = new AsyncCommand(ScanModulesAsync, () => !IsBusy && CanScanModules);
        CancelScanCommand = new AsyncCommand(() => { _scanCts?.Cancel(); return Task.CompletedTask; }, () => IsScanningModules);
        ClearAllModulesCommand = new AsyncCommand(ClearAllModulesAsync, () => !IsBusy && IsReady && Modules.Any(m => m.HasDtcs));
        _session.StateChanged += _ => OnUi(() =>
        {
            if (_session.State == SessionState.OpeningTransport)
                Clear();
            RefreshProfile();
            RefreshModules();
        });
        _session.ModuleScanChanged += () => OnUi(RefreshModules);
        // La pestaña se crea al abrirla: la sesión puede estar conectada desde antes.
        RefreshProfile();
        RefreshModules();
    }

    // Barrido de módulos (ABS, airbag, caja, carrocería) sin perfil, solo por CAN.
    public ObservableCollection<ModuleItem> Modules { get; } = [];

    public bool CanScanModules => _session.CanScanModules;

    public bool HasModules => Modules.Count > 0;

    public bool HasModuleDtcs => Modules.Any(m => m.HasDtcs);

    private bool _isScanningModules;
    public bool IsScanningModules
    {
        get => _isScanningModules;
        set { Set(ref _isScanningModules, value); CancelScanCommand.RaiseCanExecuteChanged(); }
    }

    private string _scanProgress = "";
    public string ScanProgress { get => _scanProgress; set => Set(ref _scanProgress, value); }

    private string _modulesSummary = "";
    public string ModulesSummary { get => _modulesSummary; set { Set(ref _modulesSummary, value); Raise(nameof(HasModulesSummary)); } }
    public bool HasModulesSummary => ModulesSummary.Length > 0;

    public AsyncCommand ScanModulesCommand { get; }
    public AsyncCommand CancelScanCommand { get; }
    public AsyncCommand ClearAllModulesCommand { get; }

    private void RefreshModules()
    {
        Modules.Clear();
        if (_session.State == SessionState.Ready && _session.LastModuleScan is { } scan)
        {
            foreach (var m in scan.Modules)
                Modules.Add(new ModuleItem(m, ClearModuleDtcsAsync));
            var text = $"Se consultaron {scan.Probed} de {scan.Total} direcciones; respondieron {scan.Modules.Count} módulo(s) con {scan.DtcCount} falla(s).";
            if (scan.Message is not null)
                text += " " + scan.Message;
            ModulesSummary = text;
        }
        else
        {
            ModulesSummary = "";
        }
        Raise(nameof(CanScanModules));
        Raise(nameof(HasModules));
        Raise(nameof(HasModuleDtcs));
        RaiseCommands();
    }

    private async Task ScanModulesAsync()
    {
        IsBusy = true;
        IsScanningModules = true;
        _scanCts = new CancellationTokenSource();
        try
        {
            var progress = new Progress<ModuleScanProgress>(p =>
                ScanProgress = p.Current.Length == 0
                    ? $"Listo: {p.Found} módulo(s)."
                    : $"Buscando módulos… {p.Probed} de {p.Total} direcciones ({p.Current}). Encontrados: {p.Found}.");
            ScanProgress = "Buscando módulos…";
            var result = await _session.ScanModulesAsync(progress, _scanCts.Token);
            if (result.Modules.Count == 0 && result.Completed)
                await Ui.AlertAsync("Barrido de módulos", "Ningún módulo contestó fuera de los de OBD2. Puede ser que el auto tenga una puerta de enlace que no deja pasar el diagnóstico por la ficha, o que use direcciones fuera del rango probado.");
        }
        finally
        {
            _scanCts.Dispose();
            _scanCts = null;
            IsScanningModules = false;
            ScanProgress = "";
            IsBusy = false;
        }
    }

    private async Task ClearModuleDtcsAsync(ScannedModule module)
    {
        var ok = await Ui.ConfirmAsync(
            $"Borrar fallas: {module.DisplayName}",
            $"Se le pide al módulo que borre sus {module.Dtcs.Count} falla(s) por su propio protocolo. " +
            "Si pide sesión extendida, la app la abre y la cierra sola. Hacelo con el motor apagado y el contacto dado. " +
            "El diagnóstico actual se guarda antes como evidencia.",
            "Borrar");
        if (!ok)
            return;
        IsBusy = true;
        try
        {
            await _history.SaveAsync(BuildRecord());
            var result = await _session.ClearModuleDtcsAsync(module, CancellationToken.None);
            LastClearText = $"Último borrado {DateTime.Now:HH:mm}: {result.Message}";
            await RereadObdAsync();
            await Ui.AlertAsync("Borrado de fallas", result.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ClearAllModulesAsync()
    {
        var targets = Modules.Where(m => m.HasDtcs).Select(m => m.Module).ToList();
        var ok = await Ui.ConfirmAsync(
            "Borrar las fallas de todos los módulos",
            $"Se le pide el borrado, uno por uno, a {targets.Count} módulo(s): {string.Join(", ", targets.Select(t => t.DisplayName))}. " +
            "Un airbag con datos de choque u otro módulo con una falla presente no lo van a aceptar, y se informa. " +
            "Hacelo con el motor apagado y el contacto dado. El diagnóstico actual se guarda antes como evidencia.",
            "Borrar todo");
        if (!ok)
            return;
        IsBusy = true;
        try
        {
            await _history.SaveAsync(BuildRecord());
            var lines = new List<string>();
            foreach (var module in targets)
            {
                var result = await _session.ClearModuleDtcsAsync(module, CancellationToken.None);
                lines.Add((result.Accepted ? "✔ " : "✘ ") + result.Message);
            }
            LastClearText = $"Último borrado {DateTime.Now:HH:mm}: {lines.Count(l => l.StartsWith('✔'))} de {targets.Count} módulo(s) aceptaron.";
            await RereadObdAsync();
            await Ui.AlertAsync("Borrado de fallas", string.Join("\n\n", lines));
        }
        finally
        {
            IsBusy = false;
        }
    }

    // Motor y caja también contestan al OBD2 genérico: la lista de arriba se actualiza para que no queden códigos ya borrados.
    private async Task RereadObdAsync()
    {
        if (HasScan && _session.State == SessionState.Ready)
            Show(await _session.ScanDtcsAsync(CancellationToken.None));
    }

    private void RefreshProfile()
    {
        ModuleClears.Clear();
        if (_session.State == SessionState.Ready && _session.Profile is { } profile)
            foreach (var mc in profile.ModuleClears)
                ModuleClears.Add(new ModuleClearItem(mc, ClearModuleAsync));
        Raise(nameof(HasModuleClears));
        Raise(nameof(IsReady));
        Raise(nameof(IsSimulated));
        RaiseCommands();
    }

    public ObservableCollection<FaultItem> Items { get; } = [];

    // Borrados por módulo que ofrece el perfil (caja, ABS...), para los que ignoran el modo 04.
    public ObservableCollection<ModuleClearItem> ModuleClears { get; } = [];
    public bool HasModuleClears => ModuleClears.Count > 0 && HasScan;

    private string _lastClearText = "";
    public string LastClearText { get => _lastClearText; set { Set(ref _lastClearText, value); Raise(nameof(HasLastClear)); } }
    public bool HasLastClear => LastClearText.Length > 0;

    public bool IsReady => _session.State == SessionState.Ready;

    public bool IsSimulated => _session.IsSimulated && _session.State != SessionState.Disconnected;

    private bool _isBusy;
    public bool IsBusy { get => _isBusy; set { Set(ref _isBusy, value); RaiseCommands(); } }

    private string _milText = "Todavía no se leyeron las fallas.";
    public string MilText { get => _milText; set => Set(ref _milText, value); }

    private Color _milColor = Color.FromArgb("#56616B");
    public Color MilColor { get => _milColor; set => Set(ref _milColor, value); }

    private string _summary = "";
    public string Summary { get => _summary; set => Set(ref _summary, value); }

    private string _monitors = "";
    public string Monitors { get => _monitors; set { Set(ref _monitors, value); Raise(nameof(HasMonitors)); } }
    public bool HasMonitors => Monitors.Length > 0;

    private string _freezeFrame = "";
    public string FreezeFrame { get => _freezeFrame; set { Set(ref _freezeFrame, value); Raise(nameof(HasFreezeFrame)); } }
    public bool HasFreezeFrame => FreezeFrame.Length > 0;

    private bool _hasScan;
    public bool HasScan { get => _hasScan; set { Set(ref _hasScan, value); Raise(nameof(HasModuleClears)); } }

    public string Notes
    {
        get => _context.Notes;
        set { _context.Notes = value; Raise(); }
    }

    public AsyncCommand ReadCommand { get; }
    public AsyncCommand SaveCommand { get; }
    public AsyncCommand ShareCommand { get; }
    public AsyncCommand ClearCommand { get; }

    private void RaiseCommands()
    {
        ReadCommand.RaiseCanExecuteChanged();
        SaveCommand.RaiseCanExecuteChanged();
        ShareCommand.RaiseCanExecuteChanged();
        ClearCommand.RaiseCanExecuteChanged();
        ScanModulesCommand.RaiseCanExecuteChanged();
        ClearAllModulesCommand.RaiseCanExecuteChanged();
    }

    private void Clear()
    {
        Items.Clear();
        HasScan = false;
        LastClearText = "";
        MilText = "Todavía no se leyeron las fallas.";
        MilColor = Color.FromArgb("#56616B");
        Summary = "";
        Monitors = "";
        FreezeFrame = "";
    }

    private static Color KindColor(DtcKind kind) => kind switch
    {
        DtcKind.Stored => Color.FromArgb("#B42318"),
        DtcKind.Pending => Color.FromArgb("#8A5A00"),
        _ => Color.FromArgb("#6B3FA0"),
    };

    private async Task ReadAsync()
    {
        IsBusy = true;
        try
        {
            var scan = await _session.ScanDtcsAsync(CancellationToken.None);
            Show(scan);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void Show(DtcScan scan)
    {
        Items.Clear();
        var all = scan.Stored.Concat(scan.Pending).Concat(scan.Permanent).ToList();
        int codes = 0;
        foreach (var result in all)
        {
            var kind = SessionRecordBuilder.KindName(result.Kind);
            foreach (var code in result.Codes)
            {
                codes++;
                var info = DtcCatalog.Describe(code);
                var body = info.Description ?? (info.IsGeneric
                    ? "Descripción no disponible en el catálogo."
                    : "Código propio del fabricante: buscar la descripción para esta marca.");
                Items.Add(new FaultItem(code, kind, KindColor(result.Kind), body, $"{info.System} · {result.Origin.DisplayName}", true));
            }
        }
        // Consultas sin respuesta válida, al final y aparte.
        foreach (var result in all.Where(r => r.Status != ReadStatus.Success))
        {
            Items.Add(new FaultItem(
                SessionRecordBuilder.KindName(result.Kind) + "s",
                SessionRecordBuilder.StatusName(result.Status),
                Color.FromArgb("#56616B"),
                result.Message ?? "La consulta no tuvo una respuesta válida.",
                result.Origin.DisplayName,
                false));
        }

        var milOn = scan.Monitors.Any(m => m.MilOn == true);
        var milKnown = scan.Monitors.Any(m => m.Status == ReadStatus.Success);
        MilText = !milKnown ? "Luz de falla: no se pudo consultar" : milOn ? "Luz de falla: ENCENDIDA" : "Luz de falla: apagada";
        MilColor = !milKnown ? Color.FromArgb("#56616B") : milOn ? Color.FromArgb("#B42318") : Color.FromArgb("#1E7A46");

        int stored = scan.Stored.Sum(r => r.Codes.Count);
        int pending = scan.Pending.Sum(r => r.Codes.Count);
        int permanent = scan.Permanent.Sum(r => r.Codes.Count);
        Summary = codes == 0
            ? "Sin códigos en las consultas que respondieron."
            : $"{stored} almacenada(s) · {pending} pendiente(s) · {permanent} permanente(s)";

        var monitors = new StringBuilder();
        foreach (var m in scan.Monitors.Where(m => m.Status == ReadStatus.Success))
        {
            if (scan.Monitors.Count > 1)
                monitors.AppendLine(m.Origin.DisplayName);
            foreach (var t in m.Monitors.Where(t => t.Available))
                monitors.AppendLine($"{(t.Complete ? "✔" : "…")}  {t.Name}: {(t.Complete ? "completo" : "incompleto")}");
        }
        Monitors = monitors.ToString().TrimEnd();

        var ff = new StringBuilder();
        foreach (var frame in scan.FreezeFrames.Where(f => f.Status == ReadStatus.Success && f.CausingDtc is not null))
        {
            ff.AppendLine($"Capturado por {frame.CausingDtc} ({frame.Origin.DisplayName}):");
            // Solo lo que la captura tiene: un PID ausente no es un dato.
            foreach (var r in frame.Readings.Where(r => r.Status == ReadStatus.Success))
            {
                var def = PidCatalog.Find(r.Pid);
                var value = r.Value is double v && def is not null ? $"{def.Format(v)} {r.Unit}" : "—";
                ff.AppendLine($"  {def?.Name ?? r.Pid.ToString("X2")}: {value}");
            }
        }
        FreezeFrame = ff.ToString().TrimEnd();
        HasScan = true;
        RaiseCommands();
    }

    private SessionRecord BuildRecord()
    {
        var record = SessionRecordBuilder.From(_session, _context.LastReadings(), _context.Plate, _context.Vehicle, _context.Notes);
        record.Device = $"{DeviceInfo.Platform} {DeviceInfo.Model}";
        record.AppVersion = AppInfo.VersionString;
        return record;
    }

    private int CurrentCodeCount() =>
        _session.LastScan is { } s ? s.Stored.Concat(s.Pending).Sum(r => r.Codes.Count) : 0;

    // Borrado genérico (modo 04): confirmar, guardar evidencia, borrar, releer.
    private async Task ClearAsync()
    {
        int count = CurrentCodeCount();
        var ok = await Ui.ConfirmAsync(
            "Borrar fallas de motor",
            $"Se van a borrar {count} código(s) almacenado(s) y pendiente(s), el freeze frame y el historial de monitores de emisiones: " +
            "el auto queda \"no listo\" para la VTV hasta completar un ciclo de manejo.\n\n" +
            "Las fallas permanentes no se borran con esto: las quita la ECU sola cuando comprueba la reparación.\n\n" +
            "Hacelo con el motor apagado y el contacto dado. El diagnóstico actual se guarda antes como evidencia.",
            "Borrar");
        if (!ok)
            return;

        IsBusy = true;
        try
        {
            await _history.SaveAsync(BuildRecord());
            var result = await _session.ClearDtcsAsync(CancellationToken.None);
            if (_session.LastScan is { } scan)
                Show(scan);
            string summary;
            if (!result.AnyAccepted)
            {
                var why = string.Join("\n", result.Modules.Select(m => $"{m.Origin.DisplayName}: {m.Message}"));
                summary = "Ningún módulo aceptó el borrado.\n\n" + why;
            }
            else
            {
                summary = $"Antes: {result.CodesBefore.Count} código(s). Después: {result.CodesAfter.Count}.";
                if (result.CodesAfter.Count > 0)
                    summary += "\n\nQuedaron: " + string.Join(", ", result.CodesAfter) + ".\nPueden ser de un módulo que no acepta el borrado genérico (ver abajo si hay borrado por módulo) o fallas presentes que vuelven enseguida.";
            }
            LastClearText = $"Último borrado {result.AtUtc.ToLocalTime():HH:mm}: {summary.Split('\n')[0]}";
            await Ui.AlertAsync("Borrado de fallas", summary);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ClearModuleAsync(TallerScan.Core.Actuators.ModuleClear clear)
    {
        var ok = await Ui.ConfirmAsync(
            $"Borrar fallas: {clear.ModuleName}",
            $"Se le pide al módulo \"{clear.ModuleName}\" que borre sus fallas por su propio protocolo. " +
            "Hacelo con el motor apagado y el contacto dado. El diagnóstico actual se guarda antes como evidencia.",
            "Borrar");
        if (!ok)
            return;
        IsBusy = true;
        try
        {
            await _history.SaveAsync(BuildRecord());
            var scan = await _session.ClearModuleAsync(clear, CancellationToken.None);
            Show(scan);
            var last = _session.Actions.LastOrDefault();
            LastClearText = $"Último borrado {DateTime.Now:HH:mm}: {last?.Detail}";
            await Ui.AlertAsync("Borrado de fallas", last?.Detail ?? "Sin respuesta.");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task SaveAsync()
    {
        if (string.IsNullOrWhiteSpace(_context.Plate)
            && !await Ui.ConfirmAsync("Sin patente", "No cargaste la patente en Conexión. ¿Guardar igual?", "Guardar"))
            return;
        var record = BuildRecord();
        await _history.SaveAsync(record);
        await Ui.AlertAsync("Guardado", $"Diagnóstico guardado en el historial ({record.CreatedUtc.ToLocalTime().ToString("dd/MM HH:mm", CultureInfo.CurrentCulture)}).");
    }

    private async Task ShareAsync()
    {
        var record = BuildRecord();
        var html = HtmlReport.Build(record);
        await Ui.ShareTextFileAsync($"diagnostico-{Ui.SafeFileName(record.Plate)}-{record.CreatedUtc.ToLocalTime():yyyyMMdd-HHmm}.html", html, "Informe de diagnóstico");
    }
}
