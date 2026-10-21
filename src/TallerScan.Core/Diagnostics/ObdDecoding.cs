using TallerScan.Core.Models;

namespace TallerScan.Core.Diagnostics;

public static class SupportMask
{
    // Bloque de 4 bytes A B C D de la respuesta a 01 00/20/40...: el bit más alto
    // de A es el PID base+1 y el más bajo de D es base+0x20 (que indica si sigue otro bloque).
    public static IReadOnlySet<byte> Parse(byte basePid, ReadOnlySpan<byte> data)
    {
        var result = new HashSet<byte>();
        if (data.Length < 4)
            return result;
        uint mask = ((uint)data[0] << 24) | ((uint)data[1] << 16) | ((uint)data[2] << 8) | data[3];
        for (int offset = 1; offset <= 32; offset++)
        {
            if ((mask & (1u << (32 - offset))) != 0 && basePid + offset <= 0xFF)
                result.Add((byte)(basePid + offset));
        }
        return result;
    }

    public static byte[] Encode(byte basePid, IEnumerable<byte> supported)
    {
        uint mask = 0;
        foreach (var pid in supported)
        {
            int offset = pid - basePid;
            if (offset is >= 1 and <= 32)
                mask |= 1u << (32 - offset);
        }
        return [(byte)(mask >> 24), (byte)(mask >> 16), (byte)(mask >> 8), (byte)mask];
    }
}

public static class DtcCodec
{
    private static readonly char[] Letters = ['P', 'C', 'B', 'U'];

    // Dos bytes → "P0301". 00 00 es relleno, no la falla P0000.
    public static string? Decode(byte a, byte b)
    {
        if (a == 0 && b == 0)
            return null;
        char letter = Letters[a >> 6];
        int d1 = (a >> 4) & 0x03;
        int d2 = a & 0x0F;
        return $"{letter}{d1}{d2:X}{b >> 4:X}{b & 0x0F:X}";
    }

    // Formato de 3 bytes de UDS (ISO 14229, servicio 19): los dos primeros son los de
    // siempre y el tercero es el tipo de falla (00 = sin detalle).
    public static ModuleDtc? DecodeUds(byte a, byte b, byte failureType, byte status)
    {
        var code = Decode(a, b);
        return code is null ? null : new ModuleDtc(code, failureType, status);
    }

    // KWP2000 (ISO 14230-3, servicio 18): dos bytes de código y un byte de estado con otro
    // reparto (bit 7 luz, bit 6 presente ahora, bit 5 almacenada). Se traduce al de UDS.
    public static ModuleDtc? DecodeKwp(byte a, byte b, byte kwpStatus)
    {
        var code = Decode(a, b);
        if (code is null)
            return null;
        byte status = 0;
        if ((kwpStatus & 0x40) != 0) status |= 0x01;
        if ((kwpStatus & 0x20) != 0) status |= 0x08;
        if ((kwpStatus & 0x80) != 0) status |= 0x80;
        if (status == 0) status = 0x20;
        return new ModuleDtc(code, 0, status);
    }

    public static (byte A, byte B) Encode(string code)
    {
        if (!IsValid(code))
            throw new ArgumentException($"Código inválido: {code}", nameof(code));
        int letter = Array.IndexOf(Letters, char.ToUpperInvariant(code[0]));
        int d1 = code[1] - '0';
        int d2 = Convert.ToInt32(code[2].ToString(), 16);
        int b = Convert.ToInt32(code[3..5], 16);
        return ((byte)((letter << 6) | (d1 << 4) | d2), (byte)b);
    }

    public static bool IsValid(string? code) =>
        code is { Length: 5 }
        && "PCBUpcbu".Contains(code[0])
        && code[1] is >= '0' and <= '3'
        && Uri.IsHexDigit(code[2]) && Uri.IsHexDigit(code[3]) && Uri.IsHexDigit(code[4]);

    // El segundo carácter dice si es genérico (SAE) o del fabricante.
    public static bool IsGeneric(string code) =>
        code[0] switch
        {
            'P' => code[1] is '0' or '2' || (code[1] == '3' && code[2] is >= '4' and <= '9'),
            _ => code[1] is '0' or '3'
        };

    public static string SystemName(string code) => code[0] switch
    {
        'P' => "Tren motriz",
        'C' => "Chasis",
        'B' => "Carrocería",
        'U' => "Red de comunicación",
        _ => "Desconocido"
    };
}

public static class MonitorDecoder
{
    // Respuesta al modo 01 PID 01 (4 bytes A B C D).
    public static MonitorStatus Decode(EcuOrigin origin, ReadOnlySpan<byte> d)
    {
        if (d.Length < 4)
            return new MonitorStatus(origin, ReadStatus.InvalidResponse, null, null, null, []);

        bool mil = (d[0] & 0x80) != 0;
        int count = d[0] & 0x7F;
        bool diesel = (d[1] & 0x08) != 0;
        var list = new List<MonitorTest>
        {
            // En B: bits 0-2 disponibles, bits 4-6 incompletos.
            new("Fallas de encendido", (d[1] & 0x01) != 0, (d[1] & 0x10) == 0),
            new("Sistema de combustible", (d[1] & 0x02) != 0, (d[1] & 0x20) == 0),
            new("Componentes", (d[1] & 0x04) != 0, (d[1] & 0x40) == 0),
        };

        string[] names = diesel
            ? ["Catalizador NMHC", "NOx / SCR", "Reservado", "Presión de sobrealimentación", "Reservado", "Sensor de gases de escape", "Filtro de partículas", "EGR / VVT"]
            : ["Catalizador", "Catalizador calefaccionado", "Sistema evaporativo", "Aire secundario", "Refrigerante del A/A", "Sonda de oxígeno", "Calefactor de la sonda", "EGR / VVT"];

        for (int bit = 0; bit < 8; bit++)
        {
            if (names[bit] == "Reservado")
                continue;
            bool available = (d[2] & (1 << bit)) != 0;
            bool complete = (d[3] & (1 << bit)) == 0;
            list.Add(new MonitorTest(names[bit], available, complete));
        }
        return new MonitorStatus(origin, ReadStatus.Success, mil, count, diesel, list);
    }
}

public static class VinValidator
{
    private const string Allowed = "ABCDEFGHJKLMNPRSTUVWXYZ0123456789";

    // Valida forma (17 caracteres, sin I/O/Q). El dígito verificador solo es
    // obligatorio en Norteamérica, así que no se exige.
    public static bool LooksValid(string? vin) =>
        vin is { Length: 17 } && vin.All(c => Allowed.Contains(c));
}
