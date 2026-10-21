using TallerScan.Core.Diagnostics;
using TallerScan.Core.Models;
using TallerScan.Infrastructure.Sessions;

namespace TallerScan.Infrastructure.History;

// Lo que se guarda de un diagnóstico. Tipos simples para que el JSON sea estable
// y se pueda pasar del celular a la PC (exportar/importar).
public sealed class SessionRecord
{
    public const int CurrentSchema = 1;

    public int Schema { get; set; } = CurrentSchema;
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow;
    public string Device { get; set; } = "";
    public string AppVersion { get; set; } = "";
    public bool Simulated { get; set; }

    // Vehículo según el operador y VIN leído: se guardan por separado.
    public string Plate { get; set; } = "";
    public string VehicleDescription { get; set; } = "";
    public string? VinRead { get; set; }

    public string? Transport { get; set; }
    public string? Adapter { get; set; }
    public string? Protocol { get; set; }
    public double? AdapterVoltage { get; set; }

    public bool? MilOn { get; set; }
    public List<DtcRecord> Dtcs { get; set; } = [];
    public List<QueryRecord> Queries { get; set; } = [];
    public List<MonitorRecord> Monitors { get; set; } = [];
    public List<FreezeFrameRecord> FreezeFrames { get; set; } = [];
    public List<ReadingRecord> Readings { get; set; } = [];

    // Lo que la app le hizo al auto en esta sesión (borrados, pruebas de actuadores).
    public List<ActionRecord> Actions { get; set; } = [];

    // Barrido de módulos por CAN (ABS, airbag, caja...): solo los que contestaron.
    public ModuleScanRecord? ModuleScan { get; set; }

    public string Notes { get; set; } = "";
}

public sealed record ActionRecord(DateTimeOffset AtUtc, string Kind, string Detail, bool Ok);

public sealed record ModuleScanRecord(DateTimeOffset AtUtc, int Probed, int Total, bool Completed, string? Message, List<ModuleRecord> Modules);

public sealed record ModuleRecord(string Address, string Name, string Dialect, string Status, string? Message, List<ModuleDtcRecord> Dtcs);

public sealed record ModuleDtcRecord(string Code, string? Description, string StatusText);

public sealed record DtcRecord(string Kind, string Origin, string Code, string? Description, string Source);

// Una por consulta y origen, incluso con cero códigos o con error.
public sealed record QueryRecord(string Kind, string Origin, string Status, int Count, string? Message);

public sealed record MonitorRecord(string Origin, string Name, bool Available, bool Complete);

public sealed record FreezeFrameRecord(string Origin, string? Dtc, List<ReadingRecord> Readings);

public sealed record ReadingRecord(string Origin, string Pid, string Name, double? Value, string Unit, string Status);

public static class SessionRecordBuilder
{
    public static string KindName(DtcKind kind) => kind switch
    {
        DtcKind.Stored => "Almacenada",
        DtcKind.Pending => "Pendiente",
        _ => "Permanente",
    };

    public static string StatusName(ReadStatus status) => status switch
    {
        ReadStatus.Success => "OK",
        ReadStatus.NoData => "Sin respuesta (NO DATA)",
        ReadStatus.Unsupported => "No soportado",
        ReadStatus.Timeout => "Tiempo agotado",
        ReadStatus.InvalidResponse => "Respuesta inválida",
        ReadStatus.CommunicationError => "Error de comunicación",
        ReadStatus.Cancelled => "Cancelado",
        ReadStatus.Blocked => "Bloqueado",
        _ => "No consultado",
    };

    public static ReadingRecord Reading(PidReading r)
    {
        var def = PidCatalog.Find(r.Pid);
        return new ReadingRecord(r.Origin.DisplayName, r.Pid.ToString("X2"), def?.Name ?? $"PID {r.Pid:X2}", r.Value, r.Unit, StatusName(r.Status));
    }

    // Arma el registro con lo que la sesión tiene en este momento.
    public static SessionRecord From(DiagnosticSession session, IEnumerable<PidReading> lastReadings, string plate, string vehicle, string notes)
    {
        var record = new SessionRecord
        {
            Simulated = session.IsSimulated,
            Plate = plate.Trim().ToUpperInvariant(),
            VehicleDescription = vehicle.Trim(),
            Notes = notes,
            VinRead = session.Vin.FirstOrDefault(v => v.Vin is not null)?.Vin,
            Transport = session.TransportDescription,
            Adapter = session.Report?.AdapterIdentifier,
            Protocol = session.Report?.Protocol,
            AdapterVoltage = session.Report?.AdapterVoltage,
        };

        if (session.LastScan is { } scan)
        {
            foreach (var result in scan.Stored.Concat(scan.Pending).Concat(scan.Permanent))
            {
                var kind = KindName(result.Kind);
                record.Queries.Add(new QueryRecord(kind, result.Origin.DisplayName, StatusName(result.Status), result.Codes.Count, result.Message));
                foreach (var code in result.Codes)
                {
                    var info = DtcCatalog.Describe(code);
                    record.Dtcs.Add(new DtcRecord(kind, result.Origin.DisplayName, code, info.Description, info.Source));
                }
            }
            foreach (var m in scan.Monitors.Where(m => m.Status == ReadStatus.Success))
            {
                record.MilOn = (record.MilOn ?? false) || m.MilOn == true;
                foreach (var t in m.Monitors.Where(t => t.Available))
                    record.Monitors.Add(new MonitorRecord(m.Origin.DisplayName, t.Name, t.Available, t.Complete));
            }
            foreach (var ff in scan.FreezeFrames.Where(f => f.Status == ReadStatus.Success))
                record.FreezeFrames.Add(new FreezeFrameRecord(ff.Origin.DisplayName, ff.CausingDtc, ff.Readings.Where(r => r.Status == ReadStatus.Success).Select(Reading).ToList()));
        }

        record.Readings = lastReadings.Where(r => r.Status == ReadStatus.Success).Select(Reading).ToList();
        record.Actions = session.Actions.Select(a => new ActionRecord(a.AtUtc, a.Kind, a.Detail, a.Ok)).ToList();

        if (session.LastModuleScan is { } modules)
        {
            record.ModuleScan = new ModuleScanRecord(modules.AtUtc, modules.Probed, modules.Total, modules.Completed, modules.Message,
                modules.Modules.Select(m => new ModuleRecord(
                    m.Address.ResponseId,
                    m.Name ?? m.Address.Name,
                    m.Dialect == ModuleDialect.Kwp ? "KWP2000" : "UDS",
                    StatusName(m.Status),
                    m.Message,
                    m.Dtcs.Select(d => new ModuleDtcRecord(d.Display, DtcCatalog.Describe(d.Code).Description, d.StatusText)).ToList())).ToList());
        }
        return record;
    }
}
