using TallerScan.Core.Actuators;

namespace TallerScan.Core.Models;

// Con qué "idioma" de diagnóstico contestó un módulo: UDS (ISO 14229, servicio 19)
// o KWP2000 sobre CAN (ISO 14230, servicio 18). Define cómo se leen y se borran sus fallas.
public enum ModuleDialect { Uds, Kwp }

// Una falla informada por un módulo con direccionamiento físico. El estado es el byte
// que define ISO 14229 (bits: 0 falla ahora, 2 pendiente, 3 confirmada, 5 falló desde el
// último borrado, 7 luz encendida). En KWP el byte tiene otro reparto: se traduce al armar.
public sealed record ModuleDtc(string Code, byte FailureType, byte Status)
{
    public bool IsActive => (Status & 0x01) != 0;
    public bool IsPending => (Status & 0x04) != 0;
    public bool IsConfirmed => (Status & 0x08) != 0;
    public bool FailedSinceClear => (Status & 0x20) != 0;
    public bool WarningLamp => (Status & 0x80) != 0;

    // "P0171" o "P0171-1C" cuando el módulo informa el tipo de falla (tercer byte).
    public string Display => FailureType == 0 ? Code : $"{Code}-{FailureType:X2}";

    public string StatusText
    {
        get
        {
            var parts = new List<string>();
            if (IsActive) parts.Add("presente ahora");
            if (IsConfirmed) parts.Add("confirmada");
            if (IsPending && !IsConfirmed) parts.Add("pendiente");
            if (!IsActive && !IsConfirmed && !IsPending && FailedSinceClear) parts.Add("histórica (ocurrió desde el último borrado)");
            if (WarningLamp) parts.Add("luz encendida");
            return parts.Count == 0 ? $"estado 0x{Status:X2}" : string.Join(", ", parts);
        }
    }
}

// Un módulo que contestó durante el barrido: a qué dirección se le habló, desde cuál
// respondió, qué idioma usa y qué fallas informó. Status distinto de Success = contestó
// pero no se pudieron leer sus fallas (Message dice por qué).
public sealed record ScannedModule(
    ModuleAddress Address,
    string? Name,
    ModuleDialect Dialect,
    IReadOnlyList<ModuleDtc> Dtcs,
    ReadStatus Status,
    string? Message)
{
    // Qué sistema sugieren sus códigos (no es una identificación: es lo que las letras dicen).
    public string? SystemHint
    {
        get
        {
            if (Dtcs.Count == 0)
                return null;
            var letters = Dtcs.Select(d => d.Code[0]).Distinct().ToList();
            if (letters.Count != 1)
                return null;
            return letters[0] switch
            {
                'C' => "chasis (ABS, dirección, suspensión)",
                'B' => "carrocería (airbag, confort, tablero)",
                'U' => "red de comunicación",
                'P' => "tren motriz",
                _ => null,
            };
        }
    }

    public string DisplayName => Name is null ? $"Módulo {Address.ResponseId}" : $"{Name} ({Address.ResponseId})";
}

public sealed record ModuleScanProgress(int Probed, int Total, string Current, int Found);

public sealed record ModuleScanResult(
    DateTimeOffset AtUtc,
    int Probed,
    int Total,
    bool Completed,
    IReadOnlyList<ScannedModule> Modules,
    string? Message)
{
    public int DtcCount => Modules.Sum(m => m.Dtcs.Count);
}

// Resultado de pedirle a un módulo que borre sus fallas, con la relectura posterior.
public sealed record ModuleClearResult(ModuleAddress Address, bool Accepted, string Message, ScannedModule After);
