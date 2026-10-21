namespace TallerScan.Core.Diagnostics;

public enum PidGroup { Fast, Slow }

// Definición de un PID del modo 01. A, B... son los bytes de datos útiles
// después de quitar servicio y PID. Las fórmulas son SAE J1979; el decodificador
// es código compilado, nunca una expresión leída de un archivo.
public sealed record PidDefinition(
    byte Pid,
    string Name,
    string Unit,
    int Length,
    string Category,
    int Decimals,
    PidGroup Group,
    Func<byte[], double> Decode)
{
    public string Hex => Pid.ToString("X2");

    public string Format(double value) => value.ToString("F" + Decimals, System.Globalization.CultureInfo.CurrentCulture);
}

public static class PidCatalog
{
    private static double Pct(byte a) => a * 100.0 / 255;
    private static double Trim(byte a) => (a - 128) * 100.0 / 128;
    private static int Word(byte[] d) => d[0] * 256 + d[1];

    public const string Motor = "Motor";
    public const string Combustible = "Combustible";
    public const string Aire = "Admisión";
    public const string Temperaturas = "Temperaturas";
    public const string Electrico = "Eléctrico";
    public const string Emisiones = "Emisiones";
    public const string Historial = "Contadores";

    public static readonly IReadOnlyList<PidDefinition> All =
    [
        new(0x04, "Carga calculada del motor", "%", 1, Motor, 1, PidGroup.Fast, d => Pct(d[0])),
        new(0x05, "Temperatura del refrigerante", "°C", 1, Temperaturas, 0, PidGroup.Slow, d => d[0] - 40),
        new(0x06, "Corrección corta de combustible, banco 1", "%", 1, Combustible, 1, PidGroup.Fast, d => Trim(d[0])),
        new(0x07, "Corrección larga de combustible, banco 1", "%", 1, Combustible, 1, PidGroup.Slow, d => Trim(d[0])),
        new(0x08, "Corrección corta de combustible, banco 2", "%", 1, Combustible, 1, PidGroup.Fast, d => Trim(d[0])),
        new(0x09, "Corrección larga de combustible, banco 2", "%", 1, Combustible, 1, PidGroup.Slow, d => Trim(d[0])),
        new(0x0A, "Presión de combustible (relativa a la atmósfera)", "kPa", 1, Combustible, 0, PidGroup.Slow, d => d[0] * 3),
        new(0x0B, "Presión absoluta del múltiple de admisión", "kPa", 1, Aire, 0, PidGroup.Fast, d => d[0]),
        new(0x0C, "RPM del motor", "rpm", 2, Motor, 0, PidGroup.Fast, d => Word(d) / 4.0),
        new(0x0D, "Velocidad del vehículo", "km/h", 1, Motor, 0, PidGroup.Fast, d => d[0]),
        new(0x0E, "Avance de encendido (cilindro 1)", "°", 1, Motor, 1, PidGroup.Fast, d => d[0] / 2.0 - 64),
        new(0x0F, "Temperatura del aire de admisión", "°C", 1, Temperaturas, 0, PidGroup.Slow, d => d[0] - 40),
        new(0x10, "Caudal de aire (MAF)", "g/s", 2, Aire, 2, PidGroup.Fast, d => Word(d) / 100.0),
        new(0x11, "Posición de la mariposa", "%", 1, Aire, 1, PidGroup.Fast, d => Pct(d[0])),
        new(0x14, "Sonda O2 banco 1 sensor 1 (tensión)", "V", 2, Emisiones, 3, PidGroup.Fast, d => d[0] / 200.0),
        new(0x15, "Sonda O2 banco 1 sensor 2 (tensión)", "V", 2, Emisiones, 3, PidGroup.Fast, d => d[0] / 200.0),
        new(0x1F, "Tiempo desde el arranque", "s", 2, Historial, 0, PidGroup.Slow, d => Word(d)),
        new(0x21, "Distancia recorrida con la luz de falla encendida", "km", 2, Historial, 0, PidGroup.Slow, d => Word(d)),
        new(0x22, "Presión del riel (relativa al múltiple)", "kPa", 2, Combustible, 1, PidGroup.Slow, d => Word(d) * 0.079),
        new(0x23, "Presión del riel (directa / diésel)", "kPa", 2, Combustible, 0, PidGroup.Slow, d => Word(d) * 10),
        new(0x2C, "EGR comandada", "%", 1, Emisiones, 1, PidGroup.Slow, d => Pct(d[0])),
        new(0x2D, "Error de EGR", "%", 1, Emisiones, 1, PidGroup.Slow, d => Trim(d[0])),
        new(0x2E, "Purga evaporativa comandada", "%", 1, Emisiones, 1, PidGroup.Slow, d => Pct(d[0])),
        new(0x2F, "Nivel de combustible", "%", 1, Combustible, 1, PidGroup.Slow, d => Pct(d[0])),
        new(0x30, "Calentamientos desde el borrado de fallas", "", 1, Historial, 0, PidGroup.Slow, d => d[0]),
        new(0x31, "Distancia desde el borrado de fallas", "km", 2, Historial, 0, PidGroup.Slow, d => Word(d)),
        new(0x33, "Presión barométrica", "kPa", 1, Aire, 0, PidGroup.Slow, d => d[0]),
        new(0x3C, "Temperatura del catalizador, banco 1 sensor 1", "°C", 2, Temperaturas, 0, PidGroup.Slow, d => Word(d) / 10.0 - 40),
        new(0x42, "Tensión informada por el módulo", "V", 2, Electrico, 2, PidGroup.Slow, d => Word(d) / 1000.0),
        new(0x43, "Carga absoluta", "%", 2, Motor, 1, PidGroup.Fast, d => Word(d) * 100.0 / 255),
        new(0x44, "Relación aire/combustible comandada (lambda)", "λ", 2, Combustible, 3, PidGroup.Slow, d => Word(d) * 2.0 / 65536),
        new(0x45, "Posición relativa de la mariposa", "%", 1, Aire, 1, PidGroup.Fast, d => Pct(d[0])),
        new(0x46, "Temperatura ambiente", "°C", 1, Temperaturas, 0, PidGroup.Slow, d => d[0] - 40),
        new(0x47, "Posición absoluta de la mariposa B", "%", 1, Aire, 1, PidGroup.Fast, d => Pct(d[0])),
        new(0x49, "Posición del pedal del acelerador D", "%", 1, Motor, 1, PidGroup.Fast, d => Pct(d[0])),
        new(0x4A, "Posición del pedal del acelerador E", "%", 1, Motor, 1, PidGroup.Fast, d => Pct(d[0])),
        new(0x4C, "Mariposa comandada", "%", 1, Aire, 1, PidGroup.Fast, d => Pct(d[0])),
        new(0x4D, "Tiempo con la luz de falla encendida", "min", 2, Historial, 0, PidGroup.Slow, d => Word(d)),
        new(0x4E, "Tiempo desde el borrado de fallas", "min", 2, Historial, 0, PidGroup.Slow, d => Word(d)),
        new(0x52, "Porcentaje de etanol", "%", 1, Combustible, 1, PidGroup.Slow, d => Pct(d[0])),
        new(0x5A, "Posición relativa del pedal", "%", 1, Motor, 1, PidGroup.Fast, d => Pct(d[0])),
        new(0x5C, "Temperatura del aceite", "°C", 1, Temperaturas, 0, PidGroup.Slow, d => d[0] - 40),
        new(0x5E, "Consumo de combustible", "L/h", 2, Combustible, 2, PidGroup.Fast, d => Word(d) / 20.0),
    ];

    private static readonly Dictionary<byte, PidDefinition> ByPid = All.ToDictionary(p => p.Pid);

    public static PidDefinition? Find(byte pid) => ByPid.GetValueOrDefault(pid);

    // PIDs de soporte: 00, 20, 40, ... (cada uno informa los 32 siguientes).
    public static bool IsSupportPid(byte pid) => pid % 0x20 == 0 && pid <= 0xE0;

    // Decodifica solo si la cantidad de bytes es la esperada: una respuesta
    // incompleta no produce un valor parcial.
    public static bool TryDecode(byte pid, ReadOnlySpan<byte> data, out double value)
    {
        value = 0;
        var def = Find(pid);
        if (def is null || data.Length < def.Length)
            return false;
        value = def.Decode(data[..def.Length].ToArray());
        return true;
    }
}
