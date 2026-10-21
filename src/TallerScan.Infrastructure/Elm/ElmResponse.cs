using TallerScan.Core.Models;

namespace TallerScan.Infrastructure.Elm;

// Respuesta completa del adaptador a un comando (todo lo que llegó antes del '>').
public sealed record ElmResponse(
    string Command,
    string Raw,
    IReadOnlyList<string> Lines,
    ReadStatus Status,
    string? Error,
    long ElapsedMs,
    string? TraceId)
{
    public bool Ok => Status == ReadStatus.Success;

    public string Text => string.Join(" ", Lines);
}

public static class ElmTextParser
{
    // Errores que informa el ELM327 en lugar de datos. El texto original se conserva.
    private static readonly (string Token, ReadStatus Status, string Message)[] Errors =
    [
        ("NO DATA", ReadStatus.NoData, "Sin respuesta de la ECU (NO DATA)."),
        ("UNABLE TO CONNECT", ReadStatus.CommunicationError, "El adaptador no logra hablar con el auto (UNABLE TO CONNECT). ¿Contacto dado?"),
        ("BUS INIT: ...ERROR", ReadStatus.CommunicationError, "Falló la inicialización del bus (BUS INIT ERROR)."),
        ("STOPPED", ReadStatus.CommunicationError, "Consulta interrumpida (STOPPED)."),
        ("CAN ERROR", ReadStatus.CommunicationError, "Error en el bus CAN (CAN ERROR)."),
        ("BUS ERROR", ReadStatus.CommunicationError, "Error en el bus (BUS ERROR)."),
        ("BUS BUSY", ReadStatus.CommunicationError, "Bus ocupado (BUS BUSY)."),
        ("FB ERROR", ReadStatus.CommunicationError, "Error de realimentación (FB ERROR)."),
        ("<DATA ERROR", ReadStatus.InvalidResponse, "Datos con error de checksum (DATA ERROR)."),
        ("DATA ERROR", ReadStatus.InvalidResponse, "Datos con error (DATA ERROR)."),
        ("BUFFER FULL", ReadStatus.CommunicationError, "Se llenó el buffer del adaptador (BUFFER FULL)."),
        ("LV RESET", ReadStatus.CommunicationError, "El adaptador se reinició por baja tensión (LV RESET)."),
        ("ACT ALERT", ReadStatus.CommunicationError, "El adaptador entró en reposo por inactividad (ACT ALERT)."),
        ("ERR", ReadStatus.CommunicationError, "Error interno del adaptador."),
    ];

    public static ElmResponse Parse(string command, string raw, long elapsedMs, string? traceId)
    {
        var lines = new List<string>();
        foreach (var part in raw.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var line = part.Trim().Replace("\0", "");
            if (line.Length == 0)
                continue;
            // Eco del comando (si ATE0 no se aplicó todavía, p. ej. justo después de ATZ).
            if (string.Equals(line.Replace(" ", ""), command, StringComparison.OrdinalIgnoreCase))
                continue;
            if (line.StartsWith("SEARCHING", StringComparison.OrdinalIgnoreCase))
                continue;
            if (line.StartsWith("BUS INIT", StringComparison.OrdinalIgnoreCase) && !line.Contains("ERROR", StringComparison.OrdinalIgnoreCase))
                continue;
            lines.Add(line);
        }

        foreach (var line in lines)
        {
            var upper = line.ToUpperInvariant();
            if (upper == "?")
                return new ElmResponse(command, raw, lines, ReadStatus.InvalidResponse, "El adaptador no entendió el comando (?).", elapsedMs, traceId);
            foreach (var (token, status, message) in Errors)
            {
                // "ERR" solo cuenta como ERRnn al principio de la línea (ERR94, etc.).
                bool hit = token == "ERR"
                    ? upper.Length >= 4 && upper.StartsWith("ERR", StringComparison.Ordinal) && char.IsDigit(upper[3])
                    : upper.Contains(token, StringComparison.Ordinal);
                if (hit)
                    return new ElmResponse(command, raw, lines, status, message, elapsedMs, traceId);
            }
        }

        if (lines.Count == 0 && !command.StartsWith("AT", StringComparison.Ordinal))
            return new ElmResponse(command, raw, lines, ReadStatus.NoData, "Respuesta vacía.", elapsedMs, traceId);

        return new ElmResponse(command, raw, lines, ReadStatus.Success, null, elapsedMs, traceId);
    }

    // Convierte "41 0C 1A F8" o "410C1AF8" a bytes. Devuelve null si hay algo que no es hex.
    public static byte[]? ParseHex(string text)
    {
        var compact = text.Replace(" ", "");
        if (compact.Length == 0 || compact.Length % 2 != 0)
            return null;
        var bytes = new byte[compact.Length / 2];
        for (int i = 0; i < bytes.Length; i++)
        {
            if (!Uri.IsHexDigit(compact[2 * i]) || !Uri.IsHexDigit(compact[2 * i + 1]))
                return null;
            bytes[i] = Convert.ToByte(compact.Substring(2 * i, 2), 16);
        }
        return bytes;
    }
}
