using System.Globalization;
using System.Text;
using TallerScan.Core.Contracts;
using TallerScan.Core.Diagnostics;
using TallerScan.Core.Models;
using TallerScan.Core.Safety;
using TallerScan.Infrastructure.Modules;
using TallerScan.Infrastructure.Uds;

namespace TallerScan.Infrastructure.Elm;

// Backend de solo lectura sobre un adaptador ELM327 (o compatible), por cualquier transporte.
// El simulador usa este mismo backend contra un ELM327 emulado: así el parser se ejercita igual.
public sealed class Elm327Backend(ITransportFactory transports, TraceLog trace) : IReadOnlyObdBackend
{
    private static readonly TimeSpan AtTimeout = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan ResetTimeout = TimeSpan.FromSeconds(4);
    private static readonly TimeSpan ObdTimeout = TimeSpan.FromSeconds(3);
    // La primera consulta puede tardar: el adaptador prueba protocolos ("SEARCHING...").
    private static readonly TimeSpan SearchTimeout = TimeSpan.FromSeconds(20);

    private ElmLink? _link;
    private bool _headers = true;
    private readonly Dictionary<string, string> _ecuNames = [];

    public ObdProtocol Protocol { get; private set; } = ObdProtocol.Unknown;

    public string? AdapterId { get; private set; }

    // Se corta el transporte sin haberlo pedido (Bluetooth fuera de alcance, Wi-Fi caído...).
    public event Action<string>? ConnectionLost;

    public async Task<ConnectionReport> ConnectAsync(AdapterSettings settings, CancellationToken cancellationToken)
    {
        if (_link is not null)
            throw new InvalidOperationException("Ya hay una conexión abierta: desconectar primero.");

        var transport = transports.Create(settings.Endpoint);
        var link = new ElmLink(transport, trace);
        try
        {
            using var openCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            openCts.CancelAfter(TimeSpan.FromSeconds(15));
            await link.OpenAsync(openCts.Token);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            await link.DisposeAsync();
            var message = ex is OperationCanceledException ? "No se pudo abrir la conexión (tiempo agotado)." : $"No se pudo abrir la conexión: {ex.Message}";
            return new ConnectionReport(false, false, null, null, null, message);
        }

        _link = link;
        link.Closed += error => ConnectionLost?.Invoke(error?.Message ?? "Se cortó la conexión con el adaptador.");

        // Reinicio del adaptador. ATZ reinicia el ELM, no toca nada del auto.
        var reset = await link.SendAsync("ATZ", ResetTimeout, cancellationToken);
        if (reset.Status == ReadStatus.Timeout)
            reset = await link.SendAsync("ATWS", ResetTimeout, cancellationToken);
        if (reset.Status is ReadStatus.Timeout or ReadStatus.CommunicationError)
            return await FailAsync("El adaptador no responde. Revisar que sea un ELM327 y que esté encendido.");

        AdapterId = reset.Lines.FirstOrDefault(l => l.Contains("ELM", StringComparison.OrdinalIgnoreCase) || l.Contains("STN", StringComparison.OrdinalIgnoreCase))
                    ?? reset.Lines.LastOrDefault();

        foreach (var cmd in new[] { "ATE0", "ATL0", "ATS1", "ATAT1", "ATSP0" })
            await link.SendAsync(cmd, AtTimeout, cancellationToken);

        // Con cabeceras se sabe qué ECU respondió. Si el clon no lo acepta, se sigue sin origen.
        var h = await link.SendAsync("ATH1", AtTimeout, cancellationToken);
        _headers = h.Ok && h.Text.Contains("OK", StringComparison.OrdinalIgnoreCase);

        double? voltage = await ReadAdapterVoltageAsync(cancellationToken);

        var probe = await link.SendAsync("0100", SearchTimeout, cancellationToken);
        if (!probe.Ok)
        {
            var why = probe.Status switch
            {
                ReadStatus.NoData or ReadStatus.CommunicationError => probe.Error + " Verificar contacto dado y ficha bien conectada.",
                ReadStatus.Timeout => "El auto no respondió a tiempo. Verificar contacto dado.",
                _ => probe.Error ?? "El auto no respondió."
            };
            return new ConnectionReport(true, false, AdapterId, null, voltage, why);
        }

        var dpn = await link.SendAsync("ATDPN", AtTimeout, cancellationToken);
        var dp = await link.SendAsync("ATDP", AtTimeout, cancellationToken);
        Protocol = ObdProtocol.FromDpn(dpn.Lines.FirstOrDefault(), dp.Lines.FirstOrDefault());

        var assembled = FrameAssembler.Assemble(probe.Lines, Protocol, _headers);
        if (assembled.Messages.Count == 0)
            return new ConnectionReport(true, false, AdapterId, Protocol.Description, voltage, "La respuesta del auto no se pudo interpretar.");

        return new ConnectionReport(true, true, AdapterId, Protocol.Description, voltage,
            _headers ? null : "El adaptador no muestra cabeceras: no se puede distinguir qué módulo responde.");
    }

    private async Task<ConnectionReport> FailAsync(string message)
    {
        // El enlace se cerró: para la sesión el adaptador no quedó conectado.
        await DisconnectAsync(CancellationToken.None);
        return new ConnectionReport(false, false, AdapterId, null, null, message);
    }

    private ElmLink Link => _link ?? throw new InvalidOperationException("No hay conexión.");

    private EcuOrigin OriginOf(string? address) =>
        address is null ? EcuOrigin.Unknown : new EcuOrigin(address, _ecuNames.GetValueOrDefault(address));

    // Manda una consulta y devuelve mensajes por ECU con el servicio esperado.
    private async Task<(ElmResponse Response, AssembledResponse Assembled)> QueryAsync(string command, TimeSpan timeout, CancellationToken ct)
    {
        var response = await Link.SendAsync(command, timeout, ct);
        if (!response.Ok)
            return (response, new AssembledResponse([], []));
        return (response, FrameAssembler.Assemble(response.Lines, Protocol, _headers));
    }

    private static ReadStatus NegativeOrInvalid(byte[] payload, byte service) =>
        payload.Length >= 2 && payload[0] == 0x7F && payload[1] == service ? ReadStatus.Unsupported : ReadStatus.InvalidResponse;

    public async Task<IReadOnlyList<EcuCapabilities>> DiscoverAsync(CancellationToken cancellationToken)
    {
        var supported = new Dictionary<string, HashSet<byte>>();
        byte basePid = 0x00;
        var lastStatus = ReadStatus.Success;
        while (true)
        {
            var (response, assembled) = await QueryAsync($"01{basePid:X2}", basePid == 0 ? SearchTimeout : ObdTimeout, cancellationToken);
            if (!response.Ok)
            {
                if (basePid == 0)
                    return [new EcuCapabilities(EcuOrigin.Unknown, new HashSet<byte>(), response.Status)];
                break;
            }
            bool next = false;
            foreach (var msg in assembled.Messages)
            {
                var p = msg.Payload;
                if (p.Length < 6 || p[0] != 0x41 || p[1] != basePid)
                    continue;
                var key = msg.Address ?? "";
                if (!supported.TryGetValue(key, out var set))
                    supported[key] = set = [];
                var block = SupportMask.Parse(basePid, p.AsSpan(2, 4));
                set.UnionWith(block);
                if (block.Contains((byte)(basePid + 0x20)))
                    next = true;
            }
            if (!next || basePid >= 0xC0)
                break;
            basePid += 0x20;
        }

        await TryReadEcuNamesAsync(cancellationToken);

        if (supported.Count == 0)
            return [new EcuCapabilities(EcuOrigin.Unknown, new HashSet<byte>(), ReadStatus.InvalidResponse)];

        return supported
            .OrderBy(kv => kv.Key, StringComparer.Ordinal)
            .Select(kv => new EcuCapabilities(OriginOf(kv.Key == "" ? null : kv.Key), kv.Value, lastStatus))
            .ToList();
    }

    // Modo 09 PID 0A: nombre que la propia ECU informa ("ECM-EngineControl"). Opcional.
    private async Task TryReadEcuNamesAsync(CancellationToken ct)
    {
        if (!_headers)
            return;
        var (response, assembled) = await QueryAsync("090A", ObdTimeout, ct);
        if (!response.Ok)
            return;
        foreach (var msg in assembled.Messages)
        {
            var p = msg.Payload;
            if (msg.Address is null || p.Length < 4 || p[0] != 0x49 || p[1] != 0x0A)
                continue;
            var name = Encoding.ASCII.GetString(p[3..].Where(b => b >= 0x20 && b < 0x7F).ToArray()).Trim();
            if (name.Length > 0)
                _ecuNames[msg.Address] = name;
        }
    }

    public async Task<IReadOnlyList<PidReading>> ReadPidAsync(byte pid, CancellationToken cancellationToken)
    {
        var def = PidCatalog.Find(pid);
        string unit = def?.Unit ?? "";
        var (response, assembled) = await QueryAsync($"01{pid:X2}", ObdTimeout, cancellationToken);
        var now = DateTimeOffset.UtcNow;
        if (!response.Ok)
            return [new PidReading(EcuOrigin.Unknown, pid, null, unit, response.Status, now, response.TraceId)];

        var result = new List<PidReading>();
        foreach (var msg in assembled.Messages)
        {
            var p = msg.Payload;
            var origin = OriginOf(msg.Address);
            if (p.Length >= 2 && p[0] == 0x41 && p[1] == pid && PidCatalog.TryDecode(pid, p.AsSpan(2), out double value))
                result.Add(new PidReading(origin, pid, value, unit, ReadStatus.Success, now, response.TraceId));
            else
                result.Add(new PidReading(origin, pid, null, unit, NegativeOrInvalid(p, 0x01), now, response.TraceId));
        }
        foreach (var problem in assembled.Problems)
            result.Add(new PidReading(OriginOf(problem.Address), pid, null, unit, ReadStatus.InvalidResponse, now, response.TraceId));
        if (result.Count == 0)
            result.Add(new PidReading(EcuOrigin.Unknown, pid, null, unit, ReadStatus.InvalidResponse, now, response.TraceId));
        return result;
    }

    public async Task<IReadOnlyList<DtcModuleResult>> ReadDtcsAsync(DtcKind kind, CancellationToken cancellationToken)
    {
        var (command, service) = kind switch
        {
            DtcKind.Stored => ("03", (byte)0x43),
            DtcKind.Pending => ("07", (byte)0x47),
            _ => ("0A", (byte)0x4A),
        };
        var (response, assembled) = await QueryAsync(command, ObdTimeout, cancellationToken);
        var now = DateTimeOffset.UtcNow;
        if (!response.Ok)
        {
            var message = response.Status == ReadStatus.NoData
                ? "Ningún módulo respondió: puede no soportar esta consulta. No equivale a \"sin fallas\"."
                : response.Error;
            return [new DtcModuleResult(EcuOrigin.Unknown, kind, [], response.Status, now, response.TraceId, message)];
        }

        // Agrupar por origen: en K-Line una ECU puede mandar varias líneas de 3 códigos.
        var byOrigin = new Dictionary<string, (List<string> Codes, ReadStatus Status, string? Message)>();
        foreach (var msg in assembled.Messages)
        {
            var key = msg.Address ?? "";
            if (!byOrigin.TryGetValue(key, out var entry))
                entry = ([], ReadStatus.Success, null);
            var p = msg.Payload;
            if (p.Length < 1 || p[0] != service)
            {
                entry.Status = NegativeOrInvalid(p, (byte)(service - 0x40));
                entry.Message = entry.Status == ReadStatus.Unsupported ? "El módulo no soporta esta consulta." : "Respuesta inesperada.";
            }
            else if (!TryDecodeDtcs(p, Protocol.IsLegacy, entry.Codes))
            {
                entry.Status = ReadStatus.InvalidResponse;
                entry.Message = "La cantidad de códigos no coincide con los bytes recibidos.";
            }
            byOrigin[key] = entry;
        }
        foreach (var problem in assembled.Problems)
        {
            var key = problem.Address ?? "";
            byOrigin[key] = (byOrigin.TryGetValue(key, out var e) ? e.Codes : [], ReadStatus.InvalidResponse, problem.Problem);
        }

        if (byOrigin.Count == 0)
            return [new DtcModuleResult(EcuOrigin.Unknown, kind, [], ReadStatus.InvalidResponse, now, response.TraceId, "Respuesta vacía.")];

        return byOrigin
            .OrderBy(kv => kv.Key, StringComparer.Ordinal)
            .Select(kv => new DtcModuleResult(OriginOf(kv.Key == "" ? null : kv.Key), kind,
                kv.Value.Codes.Distinct().ToList(), kv.Value.Status, now, response.TraceId, kv.Value.Message))
            .ToList();
    }

    // CAN: [servicio, cantidad, pares...]. K-Line/J1850: [servicio, 3 pares] por línea, sin cantidad.
    internal static bool TryDecodeDtcs(byte[] payload, bool legacy, List<string> codes)
    {
        int start;
        if (legacy)
        {
            start = 1;
        }
        else
        {
            if (payload.Length < 2)
                return false;
            int count = payload[1];
            if (payload.Length < 2 + count * 2)
                return false;
            for (int i = 0; i < count; i++)
            {
                var code = DtcCodec.Decode(payload[2 + i * 2], payload[3 + i * 2]);
                if (code is not null)
                    codes.Add(code);
            }
            return true;
        }
        for (int i = start; i + 1 < payload.Length; i += 2)
        {
            var code = DtcCodec.Decode(payload[i], payload[i + 1]);
            if (code is not null)
                codes.Add(code);
        }
        return true;
    }

    public async Task<IReadOnlyList<VinResult>> ReadVinAsync(CancellationToken cancellationToken)
    {
        var (response, assembled) = await QueryAsync("0902", ObdTimeout, cancellationToken);
        if (!response.Ok)
            return [new VinResult(EcuOrigin.Unknown, null, response.Status, response.TraceId)];

        var results = new List<VinResult>();
        foreach (var group in assembled.Messages.GroupBy(m => m.Address ?? ""))
        {
            var origin = OriginOf(group.Key == "" ? null : group.Key);
            var data = new List<byte>();
            bool valid = true;
            if (Protocol.IsLegacy)
            {
                // Cinco mensajes "49 02 nn b1 b2 b3 b4", ordenados por nn.
                foreach (var m in group.OrderBy(m => m.Payload.Length > 2 ? m.Payload[2] : 0))
                {
                    if (m.Payload.Length < 7 || m.Payload[0] != 0x49 || m.Payload[1] != 0x02) { valid = false; break; }
                    data.AddRange(m.Payload[3..7]);
                }
            }
            else
            {
                var m = group.First();
                if (m.Payload.Length < 4 || m.Payload[0] != 0x49 || m.Payload[1] != 0x02)
                    valid = false;
                else
                    data.AddRange(m.Payload[3..]);
            }
            var vin = valid ? Encoding.ASCII.GetString(data.Where(b => b is >= 0x20 and < 0x7F).ToArray()).Trim() : null;
            if (vin is { Length: > 17 })
                vin = vin[^17..];
            bool ok = VinValidator.LooksValid(vin);
            results.Add(new VinResult(origin, ok ? vin : null, ok ? ReadStatus.Success : ReadStatus.InvalidResponse, response.TraceId));
        }
        if (results.Count == 0)
            results.Add(new VinResult(EcuOrigin.Unknown, null, ReadStatus.InvalidResponse, response.TraceId));
        return results;
    }

    public async Task<IReadOnlyList<MonitorStatus>> ReadMonitorStatusAsync(CancellationToken cancellationToken)
    {
        var (response, assembled) = await QueryAsync("0101", ObdTimeout, cancellationToken);
        if (!response.Ok)
            return [new MonitorStatus(EcuOrigin.Unknown, response.Status, null, null, null, [])];
        var list = new List<MonitorStatus>();
        foreach (var msg in assembled.Messages)
        {
            var p = msg.Payload;
            var origin = OriginOf(msg.Address);
            if (p.Length >= 6 && p[0] == 0x41 && p[1] == 0x01)
                list.Add(MonitorDecoder.Decode(origin, p.AsSpan(2)));
            else
                list.Add(new MonitorStatus(origin, NegativeOrInvalid(p, 0x01), null, null, null, []));
        }
        return list.Count > 0 ? list : [new MonitorStatus(EcuOrigin.Unknown, ReadStatus.InvalidResponse, null, null, null, [])];
    }

    public async Task<IReadOnlyList<FreezeFrameResult>> ReadFreezeFrameAsync(IReadOnlyCollection<byte> pids, CancellationToken cancellationToken)
    {
        // PID 02 del modo 02: código que provocó la captura. 0000 = no hay captura.
        var (response, assembled) = await QueryAsync("020200", ObdTimeout, cancellationToken);
        if (!response.Ok)
            return [new FreezeFrameResult(EcuOrigin.Unknown, null, [], response.Status)];

        var frames = new List<FreezeFrameResult>();
        foreach (var msg in assembled.Messages)
        {
            var p = msg.Payload;
            var origin = OriginOf(msg.Address);
            if (p.Length < 5 || p[0] != 0x42 || p[1] != 0x02)
            {
                frames.Add(new FreezeFrameResult(origin, null, [], NegativeOrInvalid(p, 0x02)));
                continue;
            }
            var dtc = DtcCodec.Decode(p[3], p[4]);
            if (dtc is null)
            {
                frames.Add(new FreezeFrameResult(origin, null, [], ReadStatus.NoData));
                continue;
            }
            var readings = new List<PidReading>();
            foreach (var pid in pids)
            {
                var (r, a) = await QueryAsync($"02{pid:X2}00", ObdTimeout, cancellationToken);
                var now = DateTimeOffset.UtcNow;
                string unit = PidCatalog.Find(pid)?.Unit ?? "";
                var own = a.Messages.FirstOrDefault(m => m.Address == msg.Address);
                if (r.Ok && own is { Payload: var fp } && fp.Length >= 3 && fp[0] == 0x42 && fp[1] == pid
                    && PidCatalog.TryDecode(pid, fp.AsSpan(3), out double v))
                    readings.Add(new PidReading(origin, pid, v, unit, ReadStatus.Success, now, r.TraceId));
                else
                    readings.Add(new PidReading(origin, pid, null, unit, r.Ok ? ReadStatus.Unsupported : r.Status, now, r.TraceId));
            }
            frames.Add(new FreezeFrameResult(origin, dtc, readings, ReadStatus.Success));
        }
        return frames.Count > 0 ? frames : [new FreezeFrameResult(EcuOrigin.Unknown, null, [], ReadStatus.InvalidResponse)];
    }

    // Abre un alcance de escritura mientras dura el flujo que lo pide y lo cierra siempre.
    public IDisposable OpenScope(CommandScope scope)
    {
        var link = Link;
        link.Scope = scope;
        return new ScopeCloser(link);
    }

    private sealed class ScopeCloser(ElmLink link) : IDisposable
    {
        public void Dispose() => link.Scope = CommandScope.ReadOnly;
    }

    public UdsClient CreateUds() => new(Link, Protocol);

    // Barrido de módulos sin perfil (ABS, airbag, caja...). Los nombres del modo 09 0A se reaprovechan.
    public ModuleScanner CreateModuleScanner() => new(Link, Protocol, _headers, address => _ecuNames.GetValueOrDefault(address));

    // Modo 04: borra fallas, freeze frame y monitores de emisiones en todos los módulos
    // que lo soportan. Solo se llama desde el flujo con confirmación; el alcance lo abre el llamador.
    public async Task<IReadOnlyList<ClearModuleResult>> ClearDtcsAsync(CancellationToken cancellationToken)
    {
        var (response, assembled) = await QueryAsync("04", SearchTimeout, cancellationToken);
        if (!response.Ok)
        {
            var message = response.Status switch
            {
                ReadStatus.NoData => "Ningún módulo aceptó el borrado (NO DATA).",
                ReadStatus.Blocked => response.Error,
                _ => response.Error,
            };
            return [new ClearModuleResult(EcuOrigin.Unknown, response.Status, message)];
        }
        var results = new List<ClearModuleResult>();
        foreach (var msg in assembled.Messages)
        {
            var p = msg.Payload;
            var origin = OriginOf(msg.Address);
            if (p.Length >= 1 && p[0] == 0x44)
                results.Add(new ClearModuleResult(origin, ReadStatus.Success, null));
            else if (p.Length >= 3 && p[0] == 0x7F && p[1] == 0x04)
                results.Add(new ClearModuleResult(origin, ReadStatus.Unsupported, UdsClient.Describe(p[2])));
            else
                results.Add(new ClearModuleResult(origin, ReadStatus.InvalidResponse, "Respuesta inesperada."));
        }
        return results.Count > 0 ? results : [new ClearModuleResult(EcuOrigin.Unknown, ReadStatus.InvalidResponse, "Respuesta vacía.")];
    }

    public async Task<double?> ReadAdapterVoltageAsync(CancellationToken cancellationToken)
    {
        var r = await Link.SendAsync("ATRV", AtTimeout, cancellationToken);
        if (!r.Ok)
            return null;
        var text = r.Text.Replace("V", "", StringComparison.OrdinalIgnoreCase).Trim();
        return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : null;
    }

    public async Task DisconnectAsync(CancellationToken cancellationToken)
    {
        var link = _link;
        _link = null;
        Protocol = ObdProtocol.Unknown;
        _ecuNames.Clear();
        if (link is not null)
            await link.DisposeAsync();
    }

    public async ValueTask DisposeAsync() => await DisconnectAsync(CancellationToken.None);
}
