using System.Text.RegularExpressions;

namespace TallerScan.Core.Safety;

// Qué puede salir al adaptador en este momento. Por defecto, solo lectura. Los flujos
// que escriben (borrar fallas, pruebas de actuadores) abren un alcance con la lista
// exacta de comandos que necesitan y lo cierran al terminar: nunca hay "modo libre".
public sealed class CommandScope
{
    public static readonly CommandScope ReadOnly = new("Solo lectura", []);

    // Borrado genérico de fallas de motor (modo 04). Lo abre la sesión con confirmación del usuario.
    public static readonly CommandScope ClearDtc = new("Borrar fallas", ["04"]);

    private readonly HashSet<string> _extra;

    public CommandScope(string name, IEnumerable<string> extraCommands)
    {
        Name = name;
        _extra = new HashSet<string>(extraCommands, StringComparer.Ordinal);
    }

    public string Name { get; }

    public bool Contains(string command) => _extra.Contains(command);

    public int Count => _extra.Count;
}

// Lista cerrada de comandos que pueden llegar al adaptador. Se valida justo antes
// de escribir en el transporte: si algo no está acá ni en el alcance abierto, no sale,
// aunque lo pida otra capa. Sin consola libre ni hexadecimal arbitrario.
public static partial class CommandPolicy
{
    // Comandos AT del ELM327 aprobados siempre: identificación y configuración de la sesión.
    private static readonly HashSet<string> AllowedAt = new(StringComparer.Ordinal)
    {
        "ATZ", "ATWS", "ATD", "ATI", "AT@1", "ATRV", "ATDP", "ATDPN",
        "ATE0", "ATE1", "ATL0", "ATL1", "ATS0", "ATS1", "ATH0", "ATH1",
        "ATSP0", "ATAT0", "ATAT1", "ATAT2", "ATCAF1", "ATPC",
    };

    // Modo 01 y 02 con PID; modo 02 lleva además el número de frame.
    [GeneratedRegex("^01[0-9A-F]{2}$")]
    private static partial Regex Mode01();

    [GeneratedRegex("^02[0-9A-F]{2}00$")]
    private static partial Regex Mode02();

    // ATST xx (timeout del adaptador, en múltiplos de 4 ms).
    [GeneratedRegex("^ATST[0-9A-F]{2}$")]
    private static partial Regex AtTimeout();

    private static readonly HashSet<string> AllowedObd = new(StringComparer.Ordinal)
    {
        "03", "07", "0A",           // fallas almacenadas, pendientes y permanentes
        "0900", "0902", "090A",     // modo 09: soporte, VIN y nombre de la ECU
    };

    public const int MaxLength = 16;

    public static bool IsAllowed(string command, out string? reason) => IsAllowed(command, CommandScope.ReadOnly, out reason);

    public static bool IsAllowed(string command, CommandScope scope, out string? reason)
    {
        reason = null;
        if (string.IsNullOrEmpty(command))
        {
            reason = "Comando vacío: el ELM327 repetiría el anterior.";
            return false;
        }
        if (command.Length > MaxLength)
        {
            reason = "Comando demasiado largo.";
            return false;
        }
        foreach (char c in command)
        {
            if (!(c is >= '0' and <= '9' or >= 'A' and <= 'Z' or '@'))
            {
                reason = "Caracter no permitido (solo mayúsculas, dígitos y @, sin espacios ni saltos de línea).";
                return false;
            }
        }
        if (command.StartsWith("AT", StringComparison.Ordinal))
        {
            if (AllowedAt.Contains(command) || AtTimeout().IsMatch(command) || scope.Contains(command))
                return true;
            reason = $"Comando AT no aprobado: {command}";
            return false;
        }
        if (AllowedObd.Contains(command) || Mode01().IsMatch(command) || Mode02().IsMatch(command))
            return true;
        if (scope.Contains(command))
            return true;

        reason = command switch
        {
            "04" => "Borrar fallas (modo 04) solo desde el botón \"Borrar fallas\", con confirmación.",
            _ when command.StartsWith("08", StringComparison.Ordinal) => "Pruebas de componentes (modo 08) bloqueadas.",
            _ when scope == CommandScope.ReadOnly => $"Comando no aprobado: {command}",
            _ => $"Comando fuera del alcance \"{scope.Name}\": {command}"
        };
        return false;
    }
}
