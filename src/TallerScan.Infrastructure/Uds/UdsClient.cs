using TallerScan.Core.Actuators;
using TallerScan.Core.Models;
using TallerScan.Core.Safety;
using TallerScan.Infrastructure.Elm;

namespace TallerScan.Infrastructure.Uds;

public sealed record UdsResponse(bool Ok, byte[] Data, byte? Nrc, string? Message, ReadStatus Status, string? TraceId)
{
    public static UdsResponse Failed(ReadStatus status, string message, string? traceId) => new(false, [], null, message, status, traceId);
}

// Diagnóstico por direccionamiento físico (ISO 14229 sobre ISO-TP, o KWP sobre K-Line)
// a través del ELM327: cabecera del módulo, sesión extendida, "sigo acá", control de
// actuadores, rutinas y borrado por módulo. Solo manda lo que el alcance permite.
public sealed class UdsClient(ElmLink link, ObdProtocol protocol)
{
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(3);

    // Pedido funcional (a todos los módulos) para volver al estado de OBD genérico.
    private const string Functional11 = "7DF";
    private const string Functional29 = "18DB33F1";

    private ModuleAddress? _current;

    public ModuleAddress? CurrentModule => _current;

    // Todos los comandos que un perfil puede llegar a mandar. Cualquier otra cosa se bloquea.
    public static CommandScope ScopeFor(ActuatorProfile profile, string name)
    {
        var commands = new List<string> { "ATAR", "ATSH" + Functional11, "ATSH" + Functional29[2..], "ATCP18", profile.SessionOpen, profile.TesterPresent, "3E00" };
        foreach (var m in profile.Modules)
        {
            commands.Add("ATSH" + (m.Is29Bit ? m.RequestId[2..] : m.RequestId));
            commands.Add("ATCRA" + m.ResponseId);
            if (m.Is29Bit)
                commands.Add("ATCP" + m.RequestId[..2]);
        }
        foreach (var t in profile.Tests)
        {
            commands.AddRange(t.Steps.Select(s => s.Payload));
            if (t.StatusDid is not null)
                commands.Add("22" + t.StatusDid);
            // Devolver el control a la ECU (2F DID 00) siempre tiene que poder salir.
            foreach (var step in t.Steps.Where(s => s.Payload.StartsWith("2F", StringComparison.Ordinal) && s.Payload.Length >= 6))
                commands.Add(step.Payload[..6] + "00");
        }
        commands.AddRange(profile.ModuleClears.Select(c => c.Payload));
        return new CommandScope(name, commands);
    }

    public async Task<bool> AddressAsync(ModuleAddress module, CancellationToken ct)
    {
        if (_current == module)
            return true;
        var ok = true;
        if (module.Is29Bit)
        {
            ok &= (await link.SendAsync("ATCP" + module.RequestId[..2], Timeout, ct)).Ok;
            ok &= (await link.SendAsync("ATSH" + module.RequestId[2..], Timeout, ct)).Ok;
        }
        else
        {
            ok &= (await link.SendAsync("ATSH" + module.RequestId, Timeout, ct)).Ok;
        }
        ok &= (await link.SendAsync("ATCRA" + module.ResponseId, Timeout, ct)).Ok;
        _current = ok ? module : null;
        return ok;
    }

    // Vuelve al pedido funcional de OBD2 (lo que usan los modos 01/03/09).
    public async Task ReleaseAsync(CancellationToken ct)
    {
        if (_current is null)
            return;
        if (protocol.IsCan29)
        {
            await link.SendAsync("ATCP18", Timeout, ct);
            await link.SendAsync("ATSH" + Functional29[2..], Timeout, ct);
        }
        else
        {
            await link.SendAsync("ATSH" + Functional11, Timeout, ct);
        }
        await link.SendAsync("ATAR", Timeout, ct);
        _current = null;
    }

    public async Task<UdsResponse> RequestAsync(string payloadHex, CancellationToken ct, bool expectNoResponse = false)
    {
        var response = await link.SendAsync(payloadHex, Timeout, ct);
        if (response.Status == ReadStatus.Blocked)
            return UdsResponse.Failed(ReadStatus.Blocked, response.Error ?? "Comando bloqueado.", response.TraceId);
        if (response.Status == ReadStatus.NoData && expectNoResponse)
            return new UdsResponse(true, [], null, null, ReadStatus.Success, response.TraceId);
        if (!response.Ok)
            return UdsResponse.Failed(response.Status, response.Error ?? "Sin respuesta.", response.TraceId);

        var assembled = FrameAssembler.Assemble(response.Lines, protocol, headers: true);
        byte service = Convert.ToByte(payloadHex[..2], 16);
        UdsResponse? negative = null;
        foreach (var msg in assembled.Messages)
        {
            var p = msg.Payload;
            if (p.Length >= 3 && p[0] == 0x7F && p[1] == service)
            {
                // 78 = "esperá, sigo procesando": el ELM extiende la espera y la respuesta final viene después.
                if (p[2] == 0x78)
                    continue;
                negative = new UdsResponse(false, p, p[2], Describe(p[2]), ReadStatus.Unsupported, response.TraceId);
                continue;
            }
            if (p.Length >= 1 && p[0] == service + 0x40)
                return new UdsResponse(true, p, null, null, ReadStatus.Success, response.TraceId);
        }
        if (negative is not null)
            return negative;
        var problem = assembled.Problems.FirstOrDefault()?.Problem ?? "La respuesta no corresponde al pedido.";
        return UdsResponse.Failed(ReadStatus.InvalidResponse, problem, response.TraceId);
    }

    // Códigos de respuesta negativa (ISO 14229-1), explicados para el taller.
    public static string Describe(byte nrc) => nrc switch
    {
        0x10 => "La ECU rechazó el pedido (rechazo general).",
        0x11 => "La ECU no soporta este servicio.",
        0x12 => "La ECU no soporta esta función.",
        0x13 => "Pedido mal formado (longitud incorrecta).",
        0x22 => "Condiciones no cumplidas: revisar motor apagado, contacto dado, velocidad 0.",
        0x24 => "Falta un paso previo (secuencia incorrecta).",
        0x31 => "La ECU no reconoce este actuador o valor (fuera de rango).",
        0x33 => "Acceso denegado: este módulo pide desbloqueo de seguridad (no implementado).",
        0x35 => "Clave de seguridad inválida.",
        0x36 => "Demasiados intentos de desbloqueo.",
        0x37 => "Hay que esperar antes de volver a intentar.",
        0x7E or 0x7F => "No disponible en la sesión actual (hace falta sesión extendida).",
        0x78 => "La ECU pide esperar.",
        _ => $"Respuesta negativa 0x{nrc:X2}.",
    };
}
