using TallerScan.Core.Models;

namespace TallerScan.Infrastructure.Simulation;

// Un auto simulado: protocolo, centralitas, fallas y comportamiento del motor.
public sealed class Scenario
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Description { get; init; }

    // Número de protocolo del ELM: '6' CAN 11 bits 500k, '7' CAN 29 bits, '3' ISO 9141-2.
    public char Protocol { get; init; } = '6';

    // Con el contacto apagado ninguna ECU responde: UNABLE TO CONNECT.
    public bool IgnitionOn { get; init; } = true;

    public int ResponseDelayMs { get; init; }

    public required List<SimulatedEcu> Ecus { get; init; }

    public Action<EngineModel>? ConfigureEngine { get; init; }

    public double AdapterVoltage { get; init; } = 14.1;
}

public static class Scenarios
{
    // PIDs típicos de una ECU de nafta moderna.
    private static HashSet<byte> GasolinePids() =>
    [
        0x01, 0x04, 0x05, 0x06, 0x07, 0x0B, 0x0C, 0x0D, 0x0E, 0x0F, 0x10, 0x11, 0x14, 0x15, 0x1F, 0x21,
        0x2F, 0x30, 0x31, 0x33, 0x3C, 0x42, 0x44, 0x45, 0x46, 0x49, 0x4C, 0x5C, 0x5E,
    ];

    // PIDs de un auto viejo con K-Line (menos datos).
    private static HashSet<byte> LegacyPids() =>
        [0x01, 0x04, 0x05, 0x06, 0x07, 0x0B, 0x0C, 0x0D, 0x0E, 0x0F, 0x11, 0x14, 0x15];

    private static SimulatedEcu Engine(string vin, string name = "ECM-EngineControl") => new()
    {
        CanId11 = "7E8",
        CanId29 = "18DAF110",
        LegacyAddress = "10",
        Name = name,
        Vin = vin,
        SupportedPids = GasolinePids(),
        Actuators = SimulatedActuators.Defaults(),
    };

    private static Dictionary<byte, byte[]> Freeze(params (byte Pid, byte[] Data)[] values) =>
        values.ToDictionary(v => v.Pid, v => v.Data);

    public static readonly IReadOnlyList<(string Id, string Name, string Description)> Catalog =
    [
        ("sano", "Auto sano (CAN)", "CAN 11 bits, sin fallas. Sirve para ver sensores."),
        ("falla-encendido", "Falla de encendido + catalizador", "P0301 y P0420 almacenadas, P0171 pendiente, P0420 permanente, luz de falla encendida y freeze frame."),
        ("mezcla-pobre", "Mezcla pobre", "P0171 y P0174, corrección larga de combustible +22 %."),
        ("multi-ecu", "Motor + caja automática", "Dos centralitas responden: motor (P0700) y transmisión (P0715)."),
        ("todos-los-modulos", "Todos los módulos (ABS, airbag, carrocería)", "Motor, caja, ABS que pide sesión para borrar, airbag con datos de choque que no se deja borrar y carrocería por KWP. Para probar el barrido de módulos."),
        ("can29", "CAN 29 bits", "Mismo auto con cabeceras extendidas de 29 bits y P0128."),
        ("kwp-viejo", "Auto viejo (ISO 9141-2)", "Protocolo K-Line, sin permanentes, cuatro fallas en dos líneas."),
        ("recalienta", "Recalentamiento", "La temperatura sube hasta 112 °C; P0217 pendiente."),
        ("contacto-apagado", "Contacto apagado", "El adaptador responde pero el auto no: UNABLE TO CONNECT."),
        ("lento", "Adaptador lento", "Respuestas de 700 ms para probar la frecuencia real de lectura."),
    ];

    public static Scenario Create(string id) => id switch
    {
        "sano" => new Scenario
        {
            Id = id, Name = "Auto sano (CAN)", Description = "",
            Ecus = [Engine("8AXSMLTSCAN000017")],
        },
        "falla-encendido" => new Scenario
        {
            Id = id, Name = "Falla de encendido + catalizador", Description = "",
            Ecus =
            [
                new SimulatedEcu
                {
                    CanId11 = "7E8", CanId29 = "18DAF110", LegacyAddress = "10", Name = "ECM-EngineControl",
                    Vin = "8AXSMLTSCAN000025", SupportedPids = GasolinePids(), Actuators = SimulatedActuators.Defaults(),
                    Stored = ["P0301", "P0420"], Pending = ["P0171"], Permanent = ["P0420"],
                    FreezeDtc = "P0301",
                    FreezeValues = Freeze((0x04, [0x5A]), (0x05, [0x7B]), (0x06, [0x8F]), (0x07, [0x94]), (0x0C, [0x0D, 0x48]), (0x0D, [0x00]), (0x11, [0x24])),
                },
            ],
            ConfigureEngine = e => { e.MisfireJitter = 120; e.LongTermTrimBias = 9; },
        },
        "mezcla-pobre" => new Scenario
        {
            Id = id, Name = "Mezcla pobre", Description = "",
            Ecus =
            [
                new SimulatedEcu
                {
                    CanId11 = "7E8", CanId29 = "18DAF110", LegacyAddress = "10", Name = "ECM-EngineControl",
                    Vin = "8AXSMLTSCAN000033", SupportedPids = GasolinePids(), Actuators = SimulatedActuators.Defaults(),
                    Stored = ["P0171", "P0174"], FreezeDtc = "P0171",
                    FreezeValues = Freeze((0x05, [0x82]), (0x06, [0x9C]), (0x07, [0xA8]), (0x0C, [0x0D, 0x80])),
                },
            ],
            ConfigureEngine = e => e.LongTermTrimBias = 22,
        },
        "multi-ecu" => new Scenario
        {
            Id = id, Name = "Motor + caja automática", Description = "",
            Ecus =
            [
                new SimulatedEcu
                {
                    CanId11 = "7E8", CanId29 = "18DAF110", LegacyAddress = "10", Name = "ECM-EngineControl",
                    Vin = "8AXSMLTSCAN000041", SupportedPids = GasolinePids(), Actuators = SimulatedActuators.Defaults(), Stored = ["P0700"],
                },
                new SimulatedEcu
                {
                    CanId11 = "7E9", CanId29 = "18DAF118", LegacyAddress = "18", Name = "TCM-TransmisionCtl",
                    HasEngineData = false, SupportedPids = [0x01], Stored = ["P0715"], Pending = ["P0730"],
                    // Como muchas cajas reales: ignora el 04 genérico, solo se limpia por su módulo.
                    ClearsWithMode04 = false,
                },
            ],
        },
        "todos-los-modulos" => new Scenario
        {
            Id = id, Name = "Todos los módulos (ABS, airbag, carrocería)", Description = "",
            Ecus =
            [
                new SimulatedEcu
                {
                    CanId11 = "7E8", CanId29 = "18DAF110", LegacyAddress = "10", Name = "ECM-EngineControl",
                    Vin = "8AXSMLTSCAN000108", SupportedPids = GasolinePids(), Actuators = SimulatedActuators.Defaults(), Stored = ["P0420"],
                },
                new SimulatedEcu
                {
                    CanId11 = "7E9", CanId29 = "18DAF118", LegacyAddress = "18", Name = "TCM-TransmisionCtl",
                    HasEngineData = false, SupportedPids = [0x01], Stored = ["P0715"], ClearsWithMode04 = false,
                },
                // ABS: no escucha el pedido funcional y solo borra en sesión extendida.
                new SimulatedEcu
                {
                    CanId11 = "7B8", CanId29 = "18DAF128", LegacyAddress = "28", Name = "ABS",
                    HasEngineData = false, ListensFunctional = false, Stored = ["C0035"], Pending = ["C0050"], ClearRequiresSession = true,
                },
                // Airbag en una dirección que no sigue "pedido + 8" (752 → 772, como PSA):
                // solo aparece con filtro amplio. Tiene datos de choque: rechaza el borrado.
                new SimulatedEcu
                {
                    CanId11 = "772", RequestCanId11 = "752", CanId29 = "18DAF140", LegacyAddress = "40", Name = "Airbag",
                    HasEngineData = false, ListensFunctional = false, Stored = ["B0001"], ClearNrc = 0x22,
                },
                // Carrocería vieja: habla KWP2000 sobre CAN (servicio 18, borrado 14 FF 00).
                new SimulatedEcu
                {
                    CanId11 = "7C8", CanId29 = "18DAF160", LegacyAddress = "60", Name = "Carroceria",
                    HasEngineData = false, ListensFunctional = false, Dialect = ModuleDialect.Kwp, Stored = ["U0155"],
                },
            ],
        },
        "can29" => new Scenario
        {
            Id = id, Name = "CAN 29 bits", Description = "", Protocol = '7',
            Ecus =
            [
                new SimulatedEcu
                {
                    CanId11 = "7E8", CanId29 = "18DAF110", LegacyAddress = "10", Name = "ECM-EngineControl",
                    Vin = "8AXSMLTSCAN000058", SupportedPids = GasolinePids(), Actuators = SimulatedActuators.Defaults(), Stored = ["P0128"],
                },
            ],
        },
        "kwp-viejo" => new Scenario
        {
            Id = id, Name = "Auto viejo (ISO 9141-2)", Description = "", Protocol = '3',
            Ecus =
            [
                new SimulatedEcu
                {
                    CanId11 = "7E8", CanId29 = "18DAF110", LegacyAddress = "10", Name = "ECU",
                    Vin = "8AXSMLTSCAN000066", SupportedPids = LegacyPids(), SupportsPermanent = false,
                    Stored = ["P0130", "P0135", "P0300", "P0505"],
                },
            ],
        },
        "recalienta" => new Scenario
        {
            Id = id, Name = "Recalentamiento", Description = "",
            Ecus =
            [
                new SimulatedEcu
                {
                    CanId11 = "7E8", CanId29 = "18DAF110", LegacyAddress = "10", Name = "ECM-EngineControl",
                    Vin = "8AXSMLTSCAN000074", SupportedPids = GasolinePids(), Actuators = SimulatedActuators.Defaults(), Pending = ["P0217"],
                },
            ],
            ConfigureEngine = e => e.CoolantTarget = 112,
        },
        "contacto-apagado" => new Scenario
        {
            Id = id, Name = "Contacto apagado", Description = "", IgnitionOn = false, AdapterVoltage = 12.3,
            Ecus = [Engine("8AXSMLTSCAN000082")],
        },
        "lento" => new Scenario
        {
            Id = id, Name = "Adaptador lento", Description = "", ResponseDelayMs = 700,
            Ecus = [Engine("8AXSMLTSCAN000090")],
        },
        _ => throw new ArgumentException($"Escenario desconocido: {id}", nameof(id)),
    };
}
