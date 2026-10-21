using TallerScan.Core.Actuators;
using TallerScan.Core.Diagnostics;
using TallerScan.Core.Models;
using TallerScan.Core.Safety;
using TallerScan.Infrastructure.Elm;
using TallerScan.Infrastructure.Uds;

namespace TallerScan.Infrastructure.Modules;

// Barrido de módulos sin perfil: le pregunta a cada dirección CAN posible por sus fallas
// (UDS 19 02, o KWP 18 si el módulo no habla UDS) y anota quién contestó. Es lo que hace
// un escáner "todos los sistemas" para leer y borrar ABS, airbag, caja o carrocería, sin
// saber de qué marca es el auto. Lo que no da: nombres de módulos ni descripciones de
// códigos del fabricante (eso sí es por marca).
//
// Filtro de recepción: si el adaptador acepta ATCF/ATCM se escucha cualquier cabecera
// 6xx/7xx (así se encuentran los módulos que no responden en "pedido + 8"); si no, se
// usa la convención +8 con ATCRA. Al terminar, pase lo que pase, se vuelve al pedido
// funcional y al filtro automático (ATAR).
public sealed class ModuleScanner(ElmLink link, ObdProtocol protocol, bool headers, Func<string, string?> nameOf)
{
    public const string ProbeUds = "19022F";   // fallas con cualquier estado útil (presente, pendiente, confirmada, histórica)
    public const string ProbeKwp = "1800FF00"; // readDiagnosticTroubleCodesByStatus, todas
    public const string ClearUds = "14FFFFFF";
    public const string ClearKwp = "14FF00";
    private const string SessionExtended = "1003";
    private const string SessionDefault = "1001";
    private const string WideFilter11 = "ATCF600";
    private const string WideMask11 = "ATCM600";
    private const string WideFilter29 = "ATCF18DAF100";
    private const string WideMask29 = "ATCM1FFFFF00";
    // Tiempo de espera del adaptador durante el barrido (hex, × 4 ms): 100 ms, el doble del
    // P2 que exige UDS; y el valor por defecto del ELM para volver.
    private const string SweepTimeout = "ATST19";
    private const string DefaultTimeout = "ATST32";

    private static readonly TimeSpan AtTimeout = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(3);

    public bool IsSupported => protocol.IsCan;

    public static CommandScope ScanScope(ObdProtocol protocol)
    {
        var commands = new List<string> { "ATAR", "ATSH" + ModuleAddressBook.Functional11, "ATSHDB33F1", "ATCP18", SweepTimeout, DefaultTimeout, ProbeUds, ProbeKwp };
        if (protocol.IsCan29)
        {
            commands.AddRange([WideFilter29, WideMask29]);
            foreach (var t in ModuleAddressBook.Targets29())
            {
                commands.Add("ATSH" + ModuleAddressBook.Request29(t)[2..]);
                commands.Add("ATCRA" + ModuleAddressBook.Response29(t));
            }
        }
        else
        {
            commands.AddRange([WideFilter11, WideMask11]);
            foreach (var id in ModuleAddressBook.Requests11())
            {
                commands.Add($"ATSH{id:X3}");
                commands.Add("ATCRA" + ModuleAddressBook.ExpectedResponse11(id));
            }
        }
        return new CommandScope("Barrido de módulos (lectura)", commands);
    }

    // Borrado de un módulo ya encontrado: direccionarlo, borrar (abriendo sesión si hace falta) y releer.
    public static CommandScope ClearScope(ModuleAddress module)
    {
        var commands = new List<string>
        {
            "ATAR", "ATSH" + ModuleAddressBook.Functional11, "ATSHDB33F1", "ATCP18",
            ProbeUds, ProbeKwp, ClearUds, ClearKwp, SessionExtended, SessionDefault,
            "ATCRA" + module.ResponseId,
        };
        commands.Add(module.Is29Bit ? "ATSH" + module.RequestId[2..] : "ATSH" + module.RequestId);
        if (module.Is29Bit)
            commands.Add("ATCP" + module.RequestId[..2]);
        return new CommandScope($"Borrar fallas: {module.Name}", commands);
    }

    public Task<ModuleScanResult> ScanAsync(IProgress<ModuleScanProgress>? progress, CancellationToken ct) =>
        ScanAsync(Candidates(), progress, ct);

    // Con una lista propia de direcciones (tests, o un barrido corto de las direcciones OBD).
    public async Task<ModuleScanResult> ScanAsync(IReadOnlyList<ModuleAddress> candidates, IProgress<ModuleScanProgress>? progress, CancellationToken ct)
    {
        if (!IsSupported)
            return new ModuleScanResult(DateTimeOffset.UtcNow, 0, 0, false, [], "El barrido de módulos solo está disponible por CAN. En este auto (K-Line) quedan las consultas OBD2 genéricas.");

        var found = new List<ScannedModule>();
        int probed = 0;
        bool completed = false;
        string? message = null;
        try
        {
            await link.SendAsync(SweepTimeout, AtTimeout, ct);
            if (protocol.IsCan29)
                await link.SendAsync("ATCP18", AtTimeout, ct);
            // Sin cabeceras no se sabe quién contestó: solo sirve la convención +8 con filtro exacto.
            bool wide = headers && await TrySetWideFilterAsync(ct);

            foreach (var candidate in candidates)
            {
                ct.ThrowIfCancellationRequested();
                progress?.Report(new ModuleScanProgress(probed, candidates.Count, candidate.RequestId, found.Count));
                var result = await ProbeAsync(candidate, wide, ct);
                probed++;
                if (result is { Status: ReadStatus.CommunicationError, Message: var m } && m is not null && m.Contains("BUFFER FULL", StringComparison.Ordinal) && wide)
                {
                    // Un bus ruidoso llena el adaptador con el filtro amplio: se sigue con filtro exacto.
                    wide = false;
                    message = "El bus tiene mucho tráfico: se siguió con filtro exacto (solo respuestas en \"pedido + 8\").";
                    result = await ProbeAsync(candidate, wide, ct);
                }
                if (result is not null)
                    found.Add(result);
            }
            completed = true;
            progress?.Report(new ModuleScanProgress(probed, candidates.Count, "", found.Count));
        }
        catch (OperationCanceledException)
        {
            message = "Barrido cancelado: la lista muestra solo lo encontrado hasta ahí.";
        }
        finally
        {
            await RestoreAsync();
        }
        return new ModuleScanResult(DateTimeOffset.UtcNow, probed, candidates.Count, completed, found, message);
    }

    // Vuelve a leer las fallas de un módulo ya conocido (después de borrar).
    public async Task<ScannedModule> ReadAsync(ScannedModule module, CancellationToken ct)
    {
        try
        {
            if (module.Address.Is29Bit)
                await link.SendAsync("ATCP" + module.Address.RequestId[..2], AtTimeout, ct);
            var again = await ProbeAsync(module.Address, wide: false, ct, module.Dialect);
            return again is null
                ? module with { Dtcs = [], Status = ReadStatus.NoData, Message = "El módulo dejó de responder." }
                : again with { Name = module.Name };
        }
        finally
        {
            await RestoreAsync();
        }
    }

    // Pide el borrado al módulo. Si contesta "no en esta sesión", abre la sesión extendida,
    // vuelve a pedir y la cierra. Nunca manda seguridad (27) ni otra cosa.
    public async Task<ModuleClearResult> ClearAsync(ScannedModule module, CancellationToken ct)
    {
        var address = module.Address;
        bool accepted = false;
        string message;
        try
        {
            if (address.Is29Bit)
                await link.SendAsync("ATCP" + address.RequestId[..2], AtTimeout, ct);
            await AddressStrictAsync(address, ct);
            var clear = module.Dialect == ModuleDialect.Kwp ? ClearKwp : ClearUds;
            var r = await RequestAsync(clear, 0x14, ct);
            if (!r.Ok && r.Nrc is 0x7F or 0x7E && module.Dialect == ModuleDialect.Uds)
            {
                var open = await RequestAsync(SessionExtended, 0x10, ct);
                if (open.Ok)
                {
                    r = await RequestAsync(clear, 0x14, ct);
                    await RequestAsync(SessionDefault, 0x10, ct);
                }
            }
            accepted = r.Ok;
            message = r.Ok
                ? $"{module.DisplayName} aceptó el borrado."
                : $"{module.DisplayName} no aceptó el borrado: {r.Message}";
        }
        finally
        {
            await RestoreAsync();
        }
        // Las ECU tardan un momento en actualizar la memoria.
        await Task.Delay(500, ct);
        var after = await ReadAsync(module, ct);
        if (accepted && after.Dtcs.Count > 0)
            message += $" Quedaron {after.Dtcs.Count} código(s): fallas presentes que vuelven enseguida.";
        else if (accepted && after.Status == ReadStatus.Success)
            message += " Sin fallas al releer.";
        return new ModuleClearResult(address, accepted, message, after);
    }

    public List<ModuleAddress> Candidates()
    {
        var list = new List<ModuleAddress>();
        if (protocol.IsCan29)
        {
            foreach (var t in ModuleAddressBook.Targets29())
                list.Add(new ModuleAddress($"Módulo {t:X2}", ModuleAddressBook.Request29(t), ModuleAddressBook.Response29(t)));
        }
        else
        {
            foreach (var id in ModuleAddressBook.Requests11())
                list.Add(new ModuleAddress($"Módulo {id:X3}", $"{id:X3}", ModuleAddressBook.ExpectedResponse11(id)));
        }
        return list;
    }

    private async Task<bool> TrySetWideFilterAsync(CancellationToken ct)
    {
        var f = await link.SendAsync(protocol.IsCan29 ? WideFilter29 : WideFilter11, AtTimeout, ct);
        var m = await link.SendAsync(protocol.IsCan29 ? WideMask29 : WideMask11, AtTimeout, ct);
        bool ok = f.Ok && m.Ok && f.Text.Contains("OK", StringComparison.OrdinalIgnoreCase) && m.Text.Contains("OK", StringComparison.OrdinalIgnoreCase);
        if (!ok)
            await link.SendAsync("ATAR", AtTimeout, ct);
        return ok;
    }

    private async Task AddressStrictAsync(ModuleAddress address, CancellationToken ct)
    {
        await link.SendAsync("ATSH" + (address.Is29Bit ? address.RequestId[2..] : address.RequestId), AtTimeout, ct);
        await link.SendAsync("ATCRA" + address.ResponseId, AtTimeout, ct);
    }

    // Una dirección: ¿contesta alguien a 19 02? Si contesta "servicio no soportado", se prueba KWP 18.
    private async Task<ScannedModule?> ProbeAsync(ModuleAddress candidate, bool wide, CancellationToken ct, ModuleDialect? known = null)
    {
        if (wide)
            await link.SendAsync("ATSH" + (candidate.Is29Bit ? candidate.RequestId[2..] : candidate.RequestId), AtTimeout, ct);
        else
            await AddressStrictAsync(candidate, ct);

        if (known != ModuleDialect.Kwp)
        {
            var uds = await ReadDtcsAsync(candidate, ProbeUds, 0x19, ModuleDialect.Uds, ct);
            if (uds is null)
                return null;
            if (uds.Status != ReadStatus.Unsupported || known == ModuleDialect.Uds)
                return uds;
            // Contestó pero no conoce el servicio 19: puede ser KWP2000 sobre CAN.
            var kwp = await ReadDtcsAsync(candidate, ProbeKwp, 0x18, ModuleDialect.Kwp, ct);
            return kwp is { Status: ReadStatus.Success } ? kwp : uds;
        }
        return await ReadDtcsAsync(candidate, ProbeKwp, 0x18, ModuleDialect.Kwp, ct);
    }

    private async Task<ScannedModule?> ReadDtcsAsync(ModuleAddress candidate, string request, byte service, ModuleDialect dialect, CancellationToken ct)
    {
        var response = await link.SendAsync(request, ProbeTimeout, ct);
        if (response.Status is ReadStatus.NoData or ReadStatus.Timeout)
            return null;
        if (!response.Ok)
            return new ScannedModule(candidate, null, dialect, [], response.Status, response.Error);

        var assembled = FrameAssembler.Assemble(response.Lines, protocol, headers);
        foreach (var msg in assembled.Messages)
        {
            var p = msg.Payload;
            if (p.Length < 2)
                continue;
            // Con filtro amplio la cabecera real puede no ser la esperada: se guarda la que contestó.
            var address = msg.Address is null || msg.Address == candidate.ResponseId ? candidate : candidate with { ResponseId = msg.Address };
            address = address with { Name = ModuleAddressBook.Hint(address) ?? address.Name };
            var name = msg.Address is null ? null : nameOf(msg.Address);

            if (p[0] == 0x7F && p[1] == service)
            {
                if (p.Length >= 3 && p[2] == 0x78)
                    continue;
                byte nrc = p.Length >= 3 ? p[2] : (byte)0x10;
                return new ScannedModule(address, name, dialect, [], ReadStatus.Unsupported, UdsClient.Describe(nrc));
            }
            if (p[0] == service + 0x40)
            {
                var dtcs = new List<ModuleDtc>();
                if (dialect == ModuleDialect.Uds)
                {
                    // 59 02 <máscara> [código(3) estado(1)]*
                    if (p.Length < 3 || p[1] != 0x02)
                        continue;
                    for (int i = 3; i + 3 < p.Length; i += 4)
                    {
                        var d = DtcCodec.DecodeUds(p[i], p[i + 1], p[i + 2], p[i + 3]);
                        if (d is not null)
                            dtcs.Add(d);
                    }
                }
                else
                {
                    // 58 <cantidad> [código(2) estado(1)]*
                    for (int i = 2; i + 2 < p.Length; i += 3)
                    {
                        var d = DtcCodec.DecodeKwp(p[i], p[i + 1], p[i + 2]);
                        if (d is not null)
                            dtcs.Add(d);
                    }
                }
                return new ScannedModule(address, name, dialect, dtcs.DistinctBy(d => d.Display).ToList(), ReadStatus.Success, null);
            }
        }
        // Llegó algo que no es respuesta a este pedido (tráfico del bus con el filtro amplio): se ignora.
        return null;
    }

    private async Task<UdsResponse> RequestAsync(string payload, byte service, CancellationToken ct)
    {
        var response = await link.SendAsync(payload, ProbeTimeout, ct);
        if (response.Status == ReadStatus.Blocked)
            return UdsResponse.Failed(ReadStatus.Blocked, response.Error ?? "Comando bloqueado.", response.TraceId);
        if (!response.Ok)
            return UdsResponse.Failed(response.Status, response.Error ?? "Sin respuesta.", response.TraceId);
        UdsResponse? negative = null;
        foreach (var msg in FrameAssembler.Assemble(response.Lines, protocol, headers).Messages)
        {
            var p = msg.Payload;
            if (p.Length >= 3 && p[0] == 0x7F && p[1] == service)
            {
                if (p[2] == 0x78)
                    continue;
                negative = new UdsResponse(false, p, p[2], UdsClient.Describe(p[2]), ReadStatus.Unsupported, response.TraceId);
                continue;
            }
            if (p.Length >= 1 && p[0] == service + 0x40)
                return new UdsResponse(true, p, null, null, ReadStatus.Success, response.TraceId);
        }
        return negative ?? UdsResponse.Failed(ReadStatus.InvalidResponse, "La respuesta no corresponde al pedido.", response.TraceId);
    }

    // Deja el adaptador como lo usan las consultas OBD2: pedido funcional, filtro automático, tiempo por defecto.
    private async Task RestoreAsync()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(6));
        try
        {
            if (protocol.IsCan29)
            {
                await link.SendAsync("ATCP18", AtTimeout, cts.Token);
                await link.SendAsync("ATSHDB33F1", AtTimeout, cts.Token);
            }
            else
            {
                await link.SendAsync("ATSH" + ModuleAddressBook.Functional11, AtTimeout, cts.Token);
            }
            await link.SendAsync("ATAR", AtTimeout, cts.Token);
            await link.SendAsync(DefaultTimeout, AtTimeout, cts.Token);
        }
        catch (Exception)
        {
            // Sin conexión no hay nada que restaurar.
        }
    }
}
