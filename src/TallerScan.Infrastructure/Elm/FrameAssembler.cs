namespace TallerScan.Infrastructure.Elm;

// Mensaje lógico completo de una ECU: Payload empieza en el byte de servicio (41, 43, 49...).
// Address es la cabecera del emisor tal como la mostró el adaptador, o null si no hubo cabeceras.
public sealed record EcuMessage(string? Address, byte[] Payload);

public sealed record AssemblyProblem(string? Address, string Problem);

public sealed record AssembledResponse(IReadOnlyList<EcuMessage> Messages, IReadOnlyList<AssemblyProblem> Problems);

// Arma mensajes completos a partir de las líneas del adaptador. Es el único lugar
// donde se reensambla ISO-TP: nada más arriba vuelve a interpretar tramas.
public static class FrameAssembler
{
    private sealed class Pending(int total)
    {
        public int Total { get; } = total;
        public List<byte> Data { get; } = [];
        public int NextSeq { get; set; } = 1;
    }

    public static AssembledResponse Assemble(IReadOnlyList<string> lines, ObdProtocol protocol, bool headers)
    {
        if (!headers)
            return AssembleWithoutHeaders(lines);
        return protocol.IsLegacy ? AssembleLegacy(lines, protocol) : AssembleCan(lines, protocol);
    }

    private static AssembledResponse AssembleCan(IReadOnlyList<string> lines, ObdProtocol protocol)
    {
        var messages = new List<EcuMessage>();
        var problems = new List<AssemblyProblem>();
        var pending = new Dictionary<string, Pending>();

        foreach (var line in lines)
        {
            var compact = line.Replace(" ", "").ToUpperInvariant();
            // 11 bits: "7E8" (3 caracteres). 29 bits: "18DAF110" (4 bytes).
            bool is29 = protocol.IsCan29 || (!protocol.IsCan && line.Split(' ', StringSplitOptions.RemoveEmptyEntries)[0].Length == 2);
            int headerLength = is29 ? 8 : 3;
            if (compact.Length <= headerLength)
            {
                problems.Add(new AssemblyProblem(null, $"Línea demasiado corta: {line}"));
                continue;
            }
            var address = compact[..headerLength];
            var data = ElmTextParser.ParseHex(compact[headerLength..]);
            if (data is null || data.Length == 0)
            {
                problems.Add(new AssemblyProblem(address, $"Trama no hexadecimal: {line}"));
                continue;
            }

            int type = data[0] >> 4;
            switch (type)
            {
                case 0: // trama única
                    int len = data[0] & 0x0F;
                    if (len == 0 || len > data.Length - 1)
                    {
                        problems.Add(new AssemblyProblem(address, $"Longitud incoherente en trama única: {line}"));
                        break;
                    }
                    messages.Add(new EcuMessage(address, data[1..(1 + len)]));
                    break;

                case 1: // primera trama de un mensaje largo
                    if (data.Length < 3)
                    {
                        problems.Add(new AssemblyProblem(address, $"Primera trama incompleta: {line}"));
                        break;
                    }
                    if (pending.ContainsKey(address))
                        problems.Add(new AssemblyProblem(address, "Mensaje largo anterior sin terminar; se descarta."));
                    var p = new Pending(((data[0] & 0x0F) << 8) | data[1]);
                    p.Data.AddRange(data[2..]);
                    pending[address] = p;
                    break;

                case 2: // trama consecutiva
                    if (!pending.TryGetValue(address, out var cur))
                    {
                        problems.Add(new AssemblyProblem(address, $"Trama consecutiva sin primera trama: {line}"));
                        break;
                    }
                    int seq = data[0] & 0x0F;
                    if (seq != cur.NextSeq)
                    {
                        problems.Add(new AssemblyProblem(address, $"Secuencia rota: se esperaba {cur.NextSeq:X} y llegó {seq:X}."));
                        pending.Remove(address);
                        break;
                    }
                    cur.NextSeq = (cur.NextSeq + 1) & 0x0F;
                    cur.Data.AddRange(data[1..]);
                    break;

                case 3: // control de flujo: lo maneja el adaptador
                    break;

                default:
                    problems.Add(new AssemblyProblem(address, $"Tipo de trama desconocido: {line}"));
                    break;
            }

            if (pending.TryGetValue(address, out var done) && done.Data.Count >= done.Total)
            {
                messages.Add(new EcuMessage(address, done.Data.Take(done.Total).ToArray()));
                pending.Remove(address);
            }
        }

        foreach (var (address, p) in pending)
            problems.Add(new AssemblyProblem(address, $"Mensaje incompleto: llegaron {p.Data.Count} de {p.Total} bytes."));

        return new AssembledResponse(messages, problems);
    }

    private static AssembledResponse AssembleLegacy(IReadOnlyList<string> lines, ObdProtocol protocol)
    {
        var messages = new List<EcuMessage>();
        var problems = new List<AssemblyProblem>();
        // ISO 9141 y KWP terminan con un checksum simple; J1850 usa CRC (no se verifica acá).
        bool simpleChecksum = protocol.Number is '3' or '4' or '5';

        foreach (var line in lines)
        {
            var bytes = ElmTextParser.ParseHex(line);
            if (bytes is null || bytes.Length < 5)
            {
                problems.Add(new AssemblyProblem(null, $"Trama corta o inválida: {line}"));
                continue;
            }
            var address = bytes[2].ToString("X2");
            if (simpleChecksum)
            {
                byte sum = 0;
                for (int i = 0; i < bytes.Length - 1; i++)
                    sum += bytes[i];
                if (sum != bytes[^1])
                {
                    problems.Add(new AssemblyProblem(address, $"Checksum inválido: {line}"));
                    continue;
                }
            }
            messages.Add(new EcuMessage(address, bytes[3..^1]));
        }
        return new AssembledResponse(messages, problems);
    }

    // Sin cabeceras no se sabe quién respondió: el origen queda null.
    private static AssembledResponse AssembleWithoutHeaders(IReadOnlyList<string> lines)
    {
        var messages = new List<EcuMessage>();
        var problems = new List<AssemblyProblem>();
        List<byte>? multi = null;
        int total = 0;

        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            // Formato largo del ELM sin cabeceras: "014", "0: 49 02 01 ...", "1: ...".
            if (trimmed.Length == 3 && trimmed.All(Uri.IsHexDigit))
            {
                total = Convert.ToInt32(trimmed, 16);
                multi = [];
                continue;
            }
            int colon = trimmed.IndexOf(':');
            if (colon > 0 && multi is not null)
            {
                var part = ElmTextParser.ParseHex(trimmed[(colon + 1)..]);
                if (part is null)
                {
                    problems.Add(new AssemblyProblem(null, $"Trama no hexadecimal: {line}"));
                    multi = null;
                    continue;
                }
                multi.AddRange(part);
                if (multi.Count >= total)
                {
                    messages.Add(new EcuMessage(null, multi.Take(total).ToArray()));
                    multi = null;
                }
                continue;
            }
            var bytes = ElmTextParser.ParseHex(trimmed);
            if (bytes is null)
                problems.Add(new AssemblyProblem(null, $"Línea no hexadecimal: {line}"));
            else
                messages.Add(new EcuMessage(null, bytes));
        }
        if (multi is not null)
            problems.Add(new AssemblyProblem(null, $"Mensaje incompleto: llegaron {multi.Count} de {total} bytes."));
        return new AssembledResponse(messages, problems);
    }
}
