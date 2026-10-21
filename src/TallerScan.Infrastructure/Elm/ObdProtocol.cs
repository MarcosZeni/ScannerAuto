namespace TallerScan.Infrastructure.Elm;

// Protocolo detectado por el adaptador (ATDPN). Define cómo se leen las cabeceras.
public sealed record ObdProtocol(char Number, string Description)
{
    public static readonly ObdProtocol Unknown = new('0', "Desconocido");

    // 6-9: ISO 15765-4 (CAN). B y C son CAN definidos por el usuario.
    public bool IsCan => Number is '6' or '7' or '8' or '9' or 'B' or 'C';

    public bool IsCan29 => Number is '7' or '9';

    // 1-5: J1850 PWM/VPW, ISO 9141-2 y KWP: cabecera de 3 bytes y checksum al final.
    public bool IsLegacy => Number is '1' or '2' or '3' or '4' or '5';

    public static ObdProtocol FromDpn(string? dpn, string? description)
    {
        // ATDPN devuelve "A6" si fue detectado en automático, o "6".
        var text = (dpn ?? "").Trim().ToUpperInvariant();
        if (text.StartsWith('A'))
            text = text[1..];
        char n = text.Length > 0 ? text[0] : '0';
        var desc = description?.Trim() ?? "";
        if (desc.StartsWith("AUTO,", StringComparison.OrdinalIgnoreCase))
            desc = desc[5..].Trim();
        return new ObdProtocol(n, desc.Length > 0 ? desc : NameOf(n));
    }

    public static string NameOf(char n) => n switch
    {
        '1' => "SAE J1850 PWM",
        '2' => "SAE J1850 VPW",
        '3' => "ISO 9141-2",
        '4' => "ISO 14230-4 (KWP 5 baudios)",
        '5' => "ISO 14230-4 (KWP rápido)",
        '6' => "ISO 15765-4 (CAN 11 bits, 500 kbaud)",
        '7' => "ISO 15765-4 (CAN 29 bits, 500 kbaud)",
        '8' => "ISO 15765-4 (CAN 11 bits, 250 kbaud)",
        '9' => "ISO 15765-4 (CAN 29 bits, 250 kbaud)",
        'A' => "SAE J1939 (CAN 29 bits, 250 kbaud)",
        _ => "Desconocido"
    };
}
