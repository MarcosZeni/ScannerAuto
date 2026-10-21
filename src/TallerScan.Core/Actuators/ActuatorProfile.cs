using System.Text.Json.Serialization;

namespace TallerScan.Core.Actuators;

// Cómo se le habla a un módulo por direccionamiento físico. En CAN de 11 bits el
// motor suele ser 7E0/7E8 y la caja 7E1/7E9; en 29 bits, 18DA10F1/18DAF110.
// Son datos del perfil del vehículo, no constantes universales.
public sealed record ModuleAddress(string Name, string RequestId, string ResponseId)
{
    [JsonIgnore]
    public bool Is29Bit => RequestId.Length == 8;
}

public enum ActuatorPrecondition
{
    // Contacto dado, motor apagado (RPM = 0). Es lo habitual para electro, bomba, inyectores.
    EngineOff,
    // Se puede hacer con el motor en marcha (p. ej. ajustes de ralentí).
    Any,
}

// Un botón de la prueba: "Baja", "Alta", "Apagar". Payload = bytes UDS completos del pedido
// (servicio incluido), en hexadecimal sin espacios. Son datos cerrados del perfil.
public sealed record ActuatorStep(string Name, string Payload, bool IsStop = false);

public sealed record ActuatorTest(
    string Id,
    string Name,
    string Description,
    string ModuleName,
    IReadOnlyList<ActuatorStep> Steps,
    ActuatorPrecondition Precondition,
    // Pasado este tiempo la app manda el paso de apagado sola.
    TimeSpan MaxDuration,
    // PID del modo 01 que conviene mirar mientras corre (p. ej. 05 refrigerante para el electro).
    byte? FeedbackPid = null,
    // DID que informa el estado actual del actuador (servicio 22), si el módulo lo ofrece.
    string? StatusDid = null)
{
    [JsonIgnore]
    public ActuatorStep? StopStep => Steps.FirstOrDefault(s => s.IsStop);
}

// Borrado de fallas por módulo (UDS 14 FF FF FF) para los módulos que no responden al modo 04.
public sealed record ModuleClear(string ModuleName, string Payload = "14FFFFFF");

// Perfil de un vehículo o familia: direcciones, cómo abrir la sesión de diagnóstico y las
// pruebas disponibles. Solo se aplica cuando el perfil coincide con el auto conectado.
public sealed record ActuatorProfile(
    string Id,
    string Name,
    string Source,
    IReadOnlyList<ModuleAddress> Modules,
    IReadOnlyList<ActuatorTest> Tests,
    IReadOnlyList<ModuleClear> ModuleClears,
    // Pedido para abrir la sesión extendida (UDS 10 03) y "sigo acá" (3E 80).
    string SessionOpen = "1003",
    string TesterPresent = "3E80",
    // Prefijos de VIN (WMI + ...) con los que este perfil se considera aplicable; vacío = solo simulación.
    IReadOnlyList<string>? VinPrefixes = null)
{
    public ModuleAddress Module(string name) =>
        Modules.FirstOrDefault(m => m.Name == name) ?? throw new InvalidOperationException($"El perfil no define el módulo {name}.");
}
