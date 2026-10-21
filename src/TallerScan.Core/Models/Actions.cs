namespace TallerScan.Core.Models;

// Resultado del borrado de fallas (modo 04) por módulo que respondió.
public sealed record ClearModuleResult(EcuOrigin Origin, ReadStatus Status, string? Message);

public sealed record ClearResult(
    DateTimeOffset AtUtc,
    IReadOnlyList<ClearModuleResult> Modules,
    // Códigos que había antes y los que quedaron después (releídos).
    IReadOnlyList<string> CodesBefore,
    IReadOnlyList<string> CodesAfter,
    string? TraceId)
{
    public bool AnyAccepted => Modules.Any(m => m.Status == ReadStatus.Success);
}
