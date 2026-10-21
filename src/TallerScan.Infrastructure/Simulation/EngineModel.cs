namespace TallerScan.Infrastructure.Simulation;

public enum EngineMode { Idle, Driving, Off }

// Motor simulado: valores que cambian con el tiempo para que la pantalla de sensores
// se mueva como en un auto real (calentamiento, ralentí, oscilación de la sonda).
// Datos sintéticos: nunca se mezclan con diagnósticos reales.
public sealed class EngineModel
{
    private readonly Func<TimeSpan> _clock;
    private readonly Random _random;

    public EngineModel(Func<TimeSpan> clock, int seed = 1234)
    {
        _clock = clock;
        _random = new Random(seed);
    }

    public EngineMode Mode { get; set; } = EngineMode.Idle;

    // Corrección larga fija (mezcla pobre = positivo).
    public double LongTermTrimBias { get; set; }

    // Inestabilidad extra del ralentí, en rpm (fallas de encendido).
    public double MisfireJitter { get; set; }

    public double CoolantTarget { get; set; } = 90;

    public double Ambient { get; set; } = 22;

    public double Seconds => _clock().TotalSeconds;

    private double Noise(double amplitude) => (_random.NextDouble() * 2 - 1) * amplitude;

    // Estado de los actuadores forzados por UDS (DID → valor). El electro enfría.
    private readonly Dictionary<ushort, byte> _actuators = [];
    private double _fanSince = -1;

    public bool GaugeSweepRunning { get; set; }

    public void SetActuator(ushort did, byte value)
    {
        _actuators[did] = value;
        if (did == SimulatedActuators.Fan)
            _fanSince = value > 0 ? Seconds : -1;
    }

    public byte Actuator(ushort did) => _actuators.GetValueOrDefault(did);

    // Baja 1 °C cada 2 s en baja y cada segundo en alta, hasta 15 °C menos.
    private double FanCooling =>
        _fanSince < 0 ? 0 : Math.Min(15, (Seconds - _fanSince) * (Actuator(SimulatedActuators.Fan) == 2 ? 1.0 : 0.5));

    public double Coolant => Math.Max(Ambient, Math.Min(CoolantTarget, Ambient + Seconds / 240.0 * (CoolantTarget - Ambient)) - FanCooling);

    public double Rpm => Mode switch
    {
        // El barrido del tablero lleva el cuentavueltas a tope y lo baja, con el motor apagado.
        EngineMode.Off when GaugeSweepRunning => 4000 + 4000 * Math.Sin(Seconds * 2),
        EngineMode.Off => 0,
        EngineMode.Driving => 2500 + 600 * Math.Sin(Seconds / 3) + Noise(40),
        _ => 850 + (Coolant < 60 ? 250 : 0) + Noise(25) + Noise(MisfireJitter),
    };

    public double Speed => Mode == EngineMode.Driving ? Math.Max(0, 50 + 20 * Math.Sin(Seconds / 5) + Noise(1)) : 0;

    public double Throttle => Mode switch { EngineMode.Driving => 30 + 10 * Math.Sin(Seconds / 3), EngineMode.Off => 0, _ => 14 + Noise(0.5) };

    public double Load => Mode switch { EngineMode.Driving => 55 + Noise(5), EngineMode.Off => 0, _ => 22 + Noise(2) };

    public double Map => Mode switch { EngineMode.Driving => 70 + Noise(4), EngineMode.Off => 101, _ => 32 + Noise(1.5) };

    public double Maf => Rpm * Load / 100 * 0.012;

    public double ShortTermTrim => Mode == EngineMode.Off ? 0 : Noise(3) + LongTermTrimBias / 4;

    public double LongTermTrim => LongTermTrimBias;

    // Sonda de banda angosta oscilando ~1 Hz entre 0,1 y 0,8 V.
    public double O2Voltage => Mode == EngineMode.Off ? 0.45 : 0.45 + 0.35 * Math.Sin(Seconds * 2 * Math.PI) - LongTermTrimBias / 100;

    public double TimingAdvance => Mode == EngineMode.Driving ? 25 + Noise(2) : 10 + Noise(1);

    public double IntakeTemp => Ambient + 8 + Noise(0.5);

    public double ModuleVoltage => Mode == EngineMode.Off ? 12.4 : 14.1 + Noise(0.05);

    public double OilTemp => Math.Max(Ambient, Coolant - 5);

    public double CatalystTemp => Mode == EngineMode.Off ? Ambient : 380 + Rpm / 10;

    public double FuelRate => Mode == EngineMode.Off ? 0 : Rpm * Load / 100 * 0.004;

    public double FuelLevel => 63;
}
