using TallerScan.Core.Actuators;

namespace TallerScan.Infrastructure.Simulation;

// Identificadores de los actuadores del auto simulado. Son inventados para la simulación:
// en un auto real cada marca usa los suyos, y van en el perfil de ese modelo.
public static class SimulatedActuators
{
    public const ushort Fan = 0x4001;          // 00 apagado, 01 baja, 02 alta
    public const ushort FuelPump = 0x4002;     // 00/01
    public const ushort AcClutch = 0x4003;     // 00/01
    public const ushort EvapPurge = 0x4004;    // 0–100 %
    public const ushort Egr = 0x4005;          // 0–100 %
    public const ushort IdleValve = 0x4006;    // 0–255 pasos
    public const ushort MilLamp = 0x4007;      // 00/01
    public const ushort Injector1 = 0x4011;    // 00/01 (pulsos de prueba)
    public const ushort Injector2 = 0x4012;
    public const ushort Injector3 = 0x4013;
    public const ushort Injector4 = 0x4014;
    public const ushort Coil1 = 0x4021;
    public const ushort Coil2 = 0x4022;
    public const ushort Coil3 = 0x4023;
    public const ushort Coil4 = 0x4024;
    public const ushort RoutineGaugeSweep = 0x0201;

    public static Dictionary<ushort, byte> Defaults() => new()
    {
        [Fan] = 0, [FuelPump] = 0, [AcClutch] = 0, [EvapPurge] = 0, [Egr] = 0, [IdleValve] = 0, [MilLamp] = 0,
        [Injector1] = 0, [Injector2] = 0, [Injector3] = 0, [Injector4] = 0,
        [Coil1] = 0, [Coil2] = 0, [Coil3] = 0, [Coil4] = 0,
    };

    public static bool IsValidValue(ushort did, byte value) => did switch
    {
        Fan => value <= 2,
        EvapPurge or Egr => value <= 100,
        IdleValve => true,
        _ => value <= 1,
    };

    private static string Hex(ushort did) => did.ToString("X4");

    private static ActuatorStep On(string name, ushort did, byte value) => new(name, $"2F{Hex(did)}03{value:X2}");

    private static ActuatorStep Off(ushort did) => new("Apagar", $"2F{Hex(did)}00", IsStop: true);

    private static ActuatorTest Switch(string id, string name, string description, ushort did, byte? feedbackPid = null) =>
        new(id, name, description, "Motor", [On("Activar", did, 1), Off(did)], ActuatorPrecondition.EngineOff, TimeSpan.FromSeconds(20), feedbackPid, Hex(did));

    // Perfil del auto simulado: el mismo tipo de pruebas que ofrece un escáner de marca.
    public static readonly ActuatorProfile Profile = new(
        Id: "simulado",
        Name: "Auto simulado (TallerScan)",
        Source: "Simulación: identificadores inventados, no sirven para un auto real.",
        Modules:
        [
            new ModuleAddress("Motor", "7E0", "7E8"),
            new ModuleAddress("Caja", "7E1", "7E9"),
        ],
        Tests:
        [
            new("electro", "Electroventilador", "Fuerza el electro en baja o en alta. Mirá que gire y cómo baja la temperatura.", "Motor",
                [On("Baja", Fan, 1), On("Alta", Fan, 2), Off(Fan)], ActuatorPrecondition.EngineOff, TimeSpan.FromSeconds(30), FeedbackPid: 0x05, StatusDid: Hex(Fan)),
            Switch("bomba", "Bomba de combustible", "Activa el relé de la bomba: se tiene que escuchar el zumbido en el tanque.", FuelPump),
            Switch("aa", "Compresor de A/A", "Acopla el embrague del compresor.", AcClutch),
            new("purga", "Válvula de purga (EVAP)", "Abre la válvula de purga del cánister al porcentaje elegido.", "Motor",
                [On("50 %", EvapPurge, 50), On("100 %", EvapPurge, 100), Off(EvapPurge)], ActuatorPrecondition.EngineOff, TimeSpan.FromSeconds(20), StatusDid: Hex(EvapPurge)),
            new("egr", "Válvula EGR", "Abre la EGR al porcentaje elegido.", "Motor",
                [On("30 %", Egr, 30), On("70 %", Egr, 70), Off(Egr)], ActuatorPrecondition.EngineOff, TimeSpan.FromSeconds(20), StatusDid: Hex(Egr)),
            new("ralenti", "Motor paso a paso del ralentí", "Mueve el actuador de ralentí a una posición fija.", "Motor",
                [On("Cerrado", IdleValve, 0), On("Medio", IdleValve, 128), On("Abierto", IdleValve, 255), Off(IdleValve)], ActuatorPrecondition.EngineOff, TimeSpan.FromSeconds(20), StatusDid: Hex(IdleValve)),
            Switch("mil", "Luz de falla (tablero)", "Enciende la luz de motor del tablero para verificar la lámpara.", MilLamp),
            new("inyectores", "Inyectores", "Pulsa un inyector por vez: se escucha el clic. No insistir: moja la bujía.", "Motor",
                [On("Cil. 1", Injector1, 1), On("Cil. 2", Injector2, 1), On("Cil. 3", Injector3, 1), On("Cil. 4", Injector4, 1), new("Detener", $"2F{Hex(Injector1)}00", IsStop: true)],
                ActuatorPrecondition.EngineOff, TimeSpan.FromSeconds(10)),
            new("bobinas", "Bobinas de encendido", "Dispara una bobina por vez (chispa de prueba).", "Motor",
                [On("Cil. 1", Coil1, 1), On("Cil. 2", Coil2, 1), On("Cil. 3", Coil3, 1), On("Cil. 4", Coil4, 1), new("Detener", $"2F{Hex(Coil1)}00", IsStop: true)],
                ActuatorPrecondition.EngineOff, TimeSpan.FromSeconds(10)),
            new("tablero", "Barrido del tablero", "Rutina: las agujas del tablero van a tope y vuelven.", "Motor",
                [new("Iniciar", $"3101{Hex(RoutineGaugeSweep)}"), new("Detener", $"3102{Hex(RoutineGaugeSweep)}", IsStop: true)],
                ActuatorPrecondition.EngineOff, TimeSpan.FromSeconds(15)),
        ],
        ModuleClears: [new ModuleClear("Caja")]);
}
