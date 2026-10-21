using System.Globalization;
using System.Net;
using System.Text;
using TallerScan.Core.Diagnostics;
using TallerScan.Infrastructure.History;
using TallerScan.Infrastructure.Sessions;

namespace TallerScan.Infrastructure.Reporting;

public static class HtmlReport
{
    private static string E(string? text) => WebUtility.HtmlEncode(text ?? "");

    private static string Num(double? v) => v?.ToString("0.##", CultureInfo.GetCultureInfo("es-AR")) ?? "—";

    // Informe para el cliente o para archivar. Todo texto del usuario va escapado.
    public static string Build(SessionRecord r)
    {
        var sb = new StringBuilder();
        sb.Append("<!doctype html><html lang=\"es\"><head><meta charset=\"utf-8\">");
        sb.Append("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">");
        sb.Append($"<title>Diagnóstico {E(r.Plate)}</title><style>");
        sb.Append("body{font-family:system-ui,sans-serif;max-width:820px;margin:24px auto;padding:0 16px;color:#1d232a;background:#fff}");
        sb.Append("h1{font-size:22px;margin:0 0 4px}h2{font-size:17px;margin:28px 0 8px;border-bottom:2px solid #e8590c;padding-bottom:4px}");
        sb.Append("table{border-collapse:collapse;width:100%;font-size:14px}td,th{border-bottom:1px solid #ddd;padding:6px 4px;text-align:left;vertical-align:top}");
        sb.Append(".muted{color:#666;font-size:13px}.code{font-family:monospace;font-weight:700}.sim{background:#fff3bf;padding:8px;border-radius:6px}");
        sb.Append(".mil-on{color:#c92a2a;font-weight:700}.ok{color:#2b8a3e}</style></head><body>");

        sb.Append("<h1>TallerScan — Informe de diagnóstico</h1>");
        sb.Append($"<p class=\"muted\">{E(r.CreatedUtc.ToLocalTime().ToString("dd/MM/yyyy HH:mm"))} · Id {E(r.Id.ToString("N")[..8])}</p>");
        if (r.Simulated)
            sb.Append("<p class=\"sim\"><b>DATOS SIMULADOS.</b> Este informe no corresponde a un vehículo real.</p>");

        sb.Append("<h2>Vehículo</h2><table>");
        Row(sb, "Patente", r.Plate);
        Row(sb, "Descripción", r.VehicleDescription);
        Row(sb, "VIN leído", r.VinRead ?? "No disponible");
        Row(sb, "Protocolo", r.Protocol);
        Row(sb, "Interfaz", $"{r.Adapter} · {r.Transport}");
        Row(sb, "Tensión en la ficha (adaptador)", r.AdapterVoltage is null ? null : Num(r.AdapterVoltage) + " V");
        sb.Append("</table>");

        sb.Append("<h2>Luz de falla</h2>");
        sb.Append(r.MilOn switch
        {
            true => "<p class=\"mil-on\">Encendida</p>",
            false => "<p class=\"ok\">Apagada</p>",
            _ => "<p class=\"muted\">No se pudo consultar</p>",
        });

        sb.Append("<h2>Códigos de falla</h2>");
        if (r.Dtcs.Count == 0)
            sb.Append("<p>No se informaron códigos en las consultas que respondieron.</p>");
        else
        {
            sb.Append("<table><tr><th>Código</th><th>Tipo</th><th>Descripción</th><th>Módulo</th></tr>");
            foreach (var d in r.Dtcs)
                sb.Append($"<tr><td class=\"code\">{E(d.Code)}</td><td>{E(d.Kind)}</td><td>{E(d.Description ?? "Descripción no disponible")}</td><td>{E(d.Origin)}</td></tr>");
            sb.Append("</table>");
        }

        var failed = r.Queries.Where(q => q.Status != "OK").ToList();
        if (failed.Count > 0)
        {
            sb.Append("<h2>Consultas sin respuesta válida</h2><table><tr><th>Consulta</th><th>Módulo</th><th>Estado</th></tr>");
            foreach (var q in failed)
                sb.Append($"<tr><td>{E(q.Kind)}</td><td>{E(q.Origin)}</td><td>{E(q.Status)}{(q.Message is null ? "" : " — " + E(q.Message))}</td></tr>");
            sb.Append("</table>");
        }

        if (r.Monitors.Count > 0)
        {
            sb.Append("<h2>Monitores de emisiones</h2><table><tr><th>Monitor</th><th>Estado</th></tr>");
            foreach (var m in r.Monitors)
                sb.Append($"<tr><td>{E(m.Name)}</td><td>{(m.Complete ? "Completo" : "Incompleto")}</td></tr>");
            sb.Append("</table><p class=\"muted\">El estado de los monitores no equivale a una aprobación de VTV.</p>");
        }

        foreach (var ff in r.FreezeFrames)
        {
            sb.Append($"<h2>Datos congelados (freeze frame) — {E(ff.Dtc)}</h2><table>");
            foreach (var x in ff.Readings)
                sb.Append($"<tr><td>{E(x.Name)}</td><td>{(x.Value is null ? E(x.Status) : Num(x.Value) + " " + E(x.Unit))}</td></tr>");
            sb.Append("</table><p class=\"muted\">Condiciones guardadas cuando se registró la falla, no valores actuales.</p>");
        }

        if (r.Readings.Count > 0)
        {
            sb.Append("<h2>Últimos valores leídos</h2><table>");
            foreach (var x in r.Readings)
                sb.Append($"<tr><td>{E(x.Name)}</td><td>{Num(x.Value)} {E(x.Unit)}</td></tr>");
            sb.Append("</table>");
        }

        if (r.ModuleScan is { } ms)
        {
            sb.Append("<h2>Otros módulos (barrido por CAN)</h2>");
            sb.Append($"<p class=\"muted\">Se consultaron {ms.Probed} direcciones{(ms.Completed ? "" : " (barrido incompleto)")}; respondieron {ms.Modules.Count} módulo(s).");
            if (ms.Message is not null)
                sb.Append(' ').Append(E(ms.Message));
            sb.Append("</p>");
            if (ms.Modules.Count > 0)
            {
                sb.Append("<table><tr><th>Módulo</th><th>Código</th><th>Descripción</th><th>Estado</th></tr>");
                foreach (var m in ms.Modules)
                {
                    var module = $"{E(m.Name)} · {E(m.Address)} · {E(m.Dialect)}";
                    if (m.Dtcs.Count == 0)
                        sb.Append($"<tr><td>{module}</td><td colspan=\"3\">{(m.Status == "OK" ? "Sin fallas" : E(m.Status) + (m.Message is null ? "" : " — " + E(m.Message)))}</td></tr>");
                    foreach (var d in m.Dtcs)
                        sb.Append($"<tr><td>{module}</td><td class=\"code\">{E(d.Code)}</td><td>{E(d.Description ?? "Descripción no disponible")}</td><td>{E(d.StatusText)}</td></tr>");
                }
                sb.Append("</table>");
            }
        }

        if (r.Actions.Count > 0)
        {
            sb.Append("<h2>Acciones realizadas sobre el vehículo</h2><table><tr><th>Hora</th><th>Acción</th><th>Resultado</th></tr>");
            foreach (var a in r.Actions)
                sb.Append($"<tr><td>{E(a.AtUtc.ToLocalTime().ToString("HH:mm:ss"))}</td><td>{E(a.Kind)}</td><td>{(a.Ok ? "" : "<b>Falló:</b> ")}{E(a.Detail)}</td></tr>");
            sb.Append("</table>");
        }

        if (!string.IsNullOrWhiteSpace(r.Notes))
            sb.Append($"<h2>Observaciones del mecánico</h2><p>{E(r.Notes).Replace("\n", "<br>")}</p>");

        sb.Append("<p class=\"muted\" style=\"margin-top:32px\">Se consultaron las funciones OBD2 indicadas");
        sb.Append(r.ModuleScan is null
            ? ". No constituye un escaneo completo de todos los módulos del vehículo. "
            : " y se hizo un barrido de módulos por CAN: aparecen solo los que respondieron, sin identificación por marca. ");
        sb.Append("Las consultas fallidas o no soportadas se informan por separado. Un código orienta el diagnóstico; no indica por sí solo qué pieza cambiar.</p>");
        sb.Append("</body></html>");
        return sb.ToString();
    }

    private static void Row(StringBuilder sb, string label, string? value) =>
        sb.Append($"<tr><th>{E(label)}</th><td>{E(string.IsNullOrWhiteSpace(value) ? "—" : value)}</td></tr>");
}

public static class CsvExport
{
    // Muestras grabadas en vivo. Separador ';' y coma decimal para abrir directo en Excel en español.
    public static string Samples(IEnumerable<LiveSample> samples)
    {
        var culture = CultureInfo.GetCultureInfo("es-AR");
        var sb = new StringBuilder();
        sb.AppendLine("hora_local;origen;pid;parametro;valor;unidad;estado");
        foreach (var s in samples)
        {
            var name = PidCatalog.Find(s.Pid)?.Name ?? $"PID {s.Pid:X2}";
            sb.Append(Cell(s.AtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss.fff"))).Append(';')
              .Append(Cell(s.Origin)).Append(';')
              .Append(s.Pid.ToString("X2")).Append(';')
              .Append(Cell(name)).Append(';')
              .Append(s.Value?.ToString("0.###", culture) ?? "").Append(';')
              .Append(Cell(s.Unit)).Append(';')
              .Append(Cell(SessionRecordBuilder.StatusName(s.Status)))
              .AppendLine();
        }
        return sb.ToString();
    }

    // Texto entre comillas y sin fórmulas: una celda que empieza con = + - @ se neutraliza.
    public static string Cell(string? value)
    {
        value ??= "";
        if (value.Length > 0 && "=+-@\t\r".Contains(value[0]))
            value = "'" + value;
        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }
}
