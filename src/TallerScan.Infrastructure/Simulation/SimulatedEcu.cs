using System.Text;
using TallerScan.Core.Diagnostics;
using TallerScan.Core.Models;

namespace TallerScan.Infrastructure.Simulation;

// Una centralita simulada: qué PIDs soporta, qué fallas tiene y cómo responde.
public sealed class SimulatedEcu
{
    public required string CanId11 { get; init; }        // "7E8"
    public required string CanId29 { get; init; }        // "18DAF110"
    public required string LegacyAddress { get; init; }  // "10"
    public required string Name { get; init; }          // nombre que informa por 09 0A
    public bool HasEngineData { get; init; } = true;
    public string? Vin { get; init; }
    public HashSet<byte> SupportedPids { get; init; } = [];
    public List<string> Stored { get; init; } = [];
    public List<string> Pending { get; init; } = [];
    public List<string> Permanent { get; init; } = [];
    public bool SupportsPermanent { get; init; } = true;

    // Captura congelada: código que la disparó y valores en ese momento.
    public string? FreezeDtc { get; set; }
    public Dictionary<byte, byte[]> FreezeValues { get; init; } = [];

    // Qué borrados acepta: el genérico (modo 04) y/o el propio por UDS (14 FF FF FF).
    // Muchas cajas y ABS reales ignoran el 04: solo se limpian por su módulo.
    public bool ClearsWithMode04 { get; init; } = true;
    public bool ClearsWithUds { get; init; } = true;

    // Módulos que no son de emisiones (ABS, airbag, carrocería): no escuchan el pedido
    // funcional 7DF, solo su dirección física. Así son en un auto real.
    public bool ListensFunctional { get; init; } = true;

    // UDS (servicio 19) o KWP2000 sobre CAN (servicio 18), como los módulos viejos.
    public ModuleDialect Dialect { get; init; } = ModuleDialect.Uds;

    // El borrado (14) solo se acepta en sesión extendida: muchos ABS reales son así.
    public bool ClearRequiresSession { get; init; }

    // Si está, el módulo rechaza siempre el borrado con este código (p. ej. 22 en un airbag
    // con datos de choque grabados: ningún escáner lo borra).
    public byte? ClearNrc { get; init; }

    // Actuadores que la ECU deja controlar por UDS 2F (DID → estado actual).
    // Las claves son las del perfil simulado (SimulatedActuators).
    public Dictionary<ushort, byte> Actuators { get; init; } = [];

    // Sesión UDS activa (01 por defecto, 03 extendida). Los 2F exigen la extendida.
    public byte UdsSession { get; set; } = 0x01;

    // Id del pedido físico que atiende esta ECU (respuesta − 8 en 11 bits; origen/destino cruzados en 29).
    // Algunas marcas no siguen "+8" (PSA: 752 → 772): se puede fijar el pedido a mano.
    public string? RequestCanId11 { get; init; }
    public string RequestId11 => RequestCanId11 ?? (Convert.ToInt32(CanId11, 16) - 8).ToString("X3");
    public string RequestId29 => CanId29[..4] + CanId29[6..8] + CanId29[4..6];

    // Devuelve los mensajes de respuesta (cada uno empieza con el byte de servicio),
    // o lista vacía si la ECU no contesta (el ELM mostrará NO DATA).
    public IReadOnlyList<byte[]> Respond(byte service, byte[] args, EngineModel engine, bool legacy)
    {
        switch (service)
        {
            case 0x01 when args.Length >= 1:
            {
                byte pid = args[0];
                if (PidCatalog.IsSupportPid(pid))
                {
                    // Un bloque que no fue anunciado por el anterior no se contesta (NO DATA).
                    if (pid != 0 && !SupportedPids.Any(p => p > pid))
                        return [];
                    var block = SupportedPids.Where(p => p > pid && p < pid + 0x20).ToList();
                    // El último bit del bloque avisa que hay otro bloque más arriba.
                    if (pid + 0x20 <= 0xE0 && SupportedPids.Any(p => p >= pid + 0x20))
                        block.Add((byte)(pid + 0x20));
                    return [[0x41, pid, .. SupportMask.Encode(pid, block)]];
                }
                if (pid == 0x01 && SupportedPids.Contains(0x01))
                    return [[0x41, 0x01, .. MonitorBytes(engine)]];
                if (!SupportedPids.Contains(pid) || !HasEngineData)
                    return [];
                var data = EncodePid(pid, engine);
                return data is null ? [] : [[0x41, pid, .. data]];
            }
            case 0x02 when args.Length >= 2:
            {
                byte pid = args[0];
                if (pid == 0x00)
                    return [[0x42, 0x00, 0x00, .. SupportMask.Encode(0, FreezeValues.Keys.Append((byte)0x02))]];
                if (pid == 0x02)
                {
                    var (a, b) = FreezeDtc is null ? ((byte)0, (byte)0) : DtcCodec.Encode(FreezeDtc);
                    return [[0x42, 0x02, 0x00, a, b]];
                }
                return FreezeDtc is not null && FreezeValues.TryGetValue(pid, out var v) ? [[0x42, pid, 0x00, .. v]] : [];
            }
            case 0x03:
                return DtcResponse(0x43, Stored, legacy);
            case 0x07:
                return DtcResponse(0x47, Pending, legacy);
            case 0x0A:
                return SupportsPermanent ? DtcResponse(0x4A, Permanent, legacy) : [];
            case 0x04:
                if (!ClearsWithMode04)
                    return [];
                // Con el motor en marcha las ECU suelen rechazarlo (condiciones no cumplidas).
                if (engine.Mode != EngineMode.Off)
                    return [[0x7F, 0x04, 0x22]];
                ClearFaults();
                return [[0x44]];
            case 0x09 when args.Length >= 1:
                return Mode09(args[0], legacy);
            case 0x10 when args.Length >= 1:
                return UdsSessionControl(args[0]);
            case 0x3E:
                // 80 = sin respuesta positiva (el ELM mostrará NO DATA, que acá es lo esperado).
                return args.Length >= 1 && (args[0] & 0x80) != 0 ? [] : [[0x7E, 0x00]];
            case 0x22 when args.Length >= 2:
                return UdsRead((ushort)(args[0] << 8 | args[1]));
            case 0x2F when args.Length >= 3:
                return UdsIoControl((ushort)(args[0] << 8 | args[1]), args[2], args.Length > 3 ? args[3] : (byte)0, engine);
            case 0x31 when args.Length >= 3:
                return UdsRoutine(args[0], (ushort)(args[1] << 8 | args[2]), engine);
            case 0x14:
                if (!ClearsWithUds)
                    return [[0x7F, 0x14, 0x11]];
                // UDS espera 3 bytes de grupo (FF FF FF); KWP, 2 (FF 00).
                if (args.Length != (Dialect == ModuleDialect.Uds ? 3 : 2))
                    return [[0x7F, 0x14, 0x13]];
                if (ClearNrc is { } nrc)
                    return [[0x7F, 0x14, nrc]];
                if (ClearRequiresSession && UdsSession != 0x03)
                    return [[0x7F, 0x14, 0x7F]];
                ClearFaults();
                return [[0x54]];
            case 0x19 when args.Length >= 1:
                return Dialect == ModuleDialect.Uds ? UdsReadDtcs(args) : [[0x7F, 0x19, 0x11]];
            case 0x18:
                return Dialect == ModuleDialect.Kwp ? KwpReadDtcs() : [[0x7F, 0x18, 0x11]];
            default:
                return [[0x7F, service, 0x11]];
        }
    }

    private void ClearFaults()
    {
        Stored.Clear();
        Pending.Clear();
        FreezeDtc = null;
    }

    // 19 02 <máscara>: 59 02 <máscara soportada> [código(3) estado]*.
    // Almacenadas: presente + confirmada + falló desde el último borrado (+ luz si hay).
    // Pendientes: pendiente + falló desde el último borrado.
    private IReadOnlyList<byte[]> UdsReadDtcs(byte[] args)
    {
        if (args[0] != 0x02)
            return [[0x7F, 0x19, 0x12]];
        if (args.Length < 2)
            return [[0x7F, 0x19, 0x13]];
        byte mask = args[1];
        var msg = new List<byte> { 0x59, 0x02, 0xFF };
        foreach (var code in Stored)
        {
            byte status = (byte)(0x29 | (HasEngineData ? 0x80 : 0x00));
            if ((status & mask) == 0)
                continue;
            var (a, b) = DtcCodec.Encode(code);
            msg.AddRange([a, b, 0x00, status]);
        }
        foreach (var code in Pending)
        {
            const byte status = 0x24;
            if ((status & mask) == 0)
                continue;
            var (a, b) = DtcCodec.Encode(code);
            msg.AddRange([a, b, 0x00, status]);
        }
        return [msg.ToArray()];
    }

    // KWP2000: 58 <cantidad> [código(2) estado]*, estado con luz (80), presente (40) y almacenada (20).
    private IReadOnlyList<byte[]> KwpReadDtcs()
    {
        var msg = new List<byte> { 0x58, (byte)(Stored.Count + Pending.Count) };
        foreach (var code in Stored)
        {
            var (a, b) = DtcCodec.Encode(code);
            msg.AddRange([a, b, 0xE0]);
        }
        foreach (var code in Pending)
        {
            var (a, b) = DtcCodec.Encode(code);
            msg.AddRange([a, b, 0x40]);
        }
        return [msg.ToArray()];
    }

    private IReadOnlyList<byte[]> UdsSessionControl(byte session)
    {
        if (session is not (0x01 or 0x03))
            return [[0x7F, 0x10, 0x12]];
        UdsSession = session;
        // P2 = 50 ms, P2* = 5000 ms, como informa una ECU real.
        return [[0x50, session, 0x00, 0x32, 0x01, 0xF4]];
    }

    private IReadOnlyList<byte[]> UdsRead(ushort did) =>
        Actuators.TryGetValue(did, out var state)
            ? [[0x62, (byte)(did >> 8), (byte)did, state]]
            : [[0x7F, 0x22, 0x31]];

    private IReadOnlyList<byte[]> UdsIoControl(ushort did, byte parameter, byte value, EngineModel engine)
    {
        if (UdsSession != 0x03)
            return [[0x7F, 0x2F, 0x7F]];
        if (!Actuators.ContainsKey(did))
            return [[0x7F, 0x2F, 0x31]];
        switch (parameter)
        {
            case 0x00: // devolver el control a la ECU
                Actuators[did] = 0;
                engine.SetActuator(did, 0);
                return [[0x6F, (byte)(did >> 8), (byte)did, 0x00, 0x00]];
            case 0x03: // ajuste a corto plazo (forzar un estado)
                if (engine.Mode != EngineMode.Off)
                    return [[0x7F, 0x2F, 0x22]];
                if (!SimulatedActuators.IsValidValue(did, value))
                    return [[0x7F, 0x2F, 0x31]];
                Actuators[did] = value;
                engine.SetActuator(did, value);
                return [[0x6F, (byte)(did >> 8), (byte)did, 0x03, value]];
            default:
                return [[0x7F, 0x2F, 0x12]];
        }
    }

    private IReadOnlyList<byte[]> UdsRoutine(byte sub, ushort rid, EngineModel engine)
    {
        if (UdsSession != 0x03)
            return [[0x7F, 0x31, 0x7F]];
        if (rid != SimulatedActuators.RoutineGaugeSweep)
            return [[0x7F, 0x31, 0x31]];
        switch (sub)
        {
            case 0x01:
                if (engine.Mode != EngineMode.Off)
                    return [[0x7F, 0x31, 0x22]];
                engine.GaugeSweepRunning = true;
                return [[0x71, 0x01, (byte)(rid >> 8), (byte)rid, 0x00]];
            case 0x02:
                engine.GaugeSweepRunning = false;
                return [[0x71, 0x02, (byte)(rid >> 8), (byte)rid, 0x00]];
            default:
                return [[0x7F, 0x31, 0x12]];
        }
    }

    private IReadOnlyList<byte[]> Mode09(byte pid, bool legacy)
    {
        switch (pid)
        {
            case 0x00:
                var supported = new List<byte> { 0x0A };
                if (Vin is not null)
                    supported.Add(0x02);
                return [[0x49, 0x00, .. SupportMask.Encode(0, supported)]];
            case 0x02 when Vin is not null:
                var vin = Encoding.ASCII.GetBytes(Vin);
                if (!legacy)
                    return [[0x49, 0x02, 0x01, .. vin]];
                // K-Line: 5 mensajes de 4 bytes; el primero rellena con 3 ceros.
                var padded = new byte[20];
                vin.CopyTo(padded, 3);
                return Enumerable.Range(0, 5).Select(i => (byte[])[0x49, 0x02, (byte)(i + 1), .. padded[(i * 4)..(i * 4 + 4)]]).ToList();
            case 0x0A when !legacy:
                var name = new byte[20];
                Encoding.ASCII.GetBytes(Name.Length > 19 ? Name[..19] : Name).CopyTo(name, 0);
                return [[0x49, 0x0A, 0x01, .. name]];
            default:
                return [];
        }
    }

    private static IReadOnlyList<byte[]> DtcResponse(byte service, List<string> codes, bool legacy)
    {
        var pairs = codes.Select(DtcCodec.Encode).ToList();
        if (!legacy)
        {
            var msg = new List<byte> { service, (byte)pairs.Count };
            foreach (var (a, b) in pairs)
            {
                msg.Add(a);
                msg.Add(b);
            }
            return [msg.ToArray()];
        }
        // K-Line: 3 códigos por mensaje, con 00 00 de relleno; sin fallas igual responde una línea.
        var messages = new List<byte[]>();
        for (int i = 0; i == 0 || i < pairs.Count; i += 3)
        {
            var msg = new List<byte> { service };
            for (int j = i; j < i + 3; j++)
            {
                if (j < pairs.Count)
                {
                    msg.Add(pairs[j].A);
                    msg.Add(pairs[j].B);
                }
                else
                {
                    msg.Add(0);
                    msg.Add(0);
                }
            }
            messages.Add(msg.ToArray());
        }
        return messages;
    }

    private byte[] MonitorBytes(EngineModel engine)
    {
        bool mil = Stored.Count > 0;
        byte a = (byte)((mil ? 0x80 : 0) | Math.Min(Stored.Count, 0x7F));
        // Una caja automática no tiene monitores de emisiones del motor.
        if (!HasEngineData)
            return [a, 0x00, 0x00, 0x00];
        // Encendido por chispa; fallas, combustible y componentes disponibles y completos.
        byte b = 0x07;
        // Catalizador, evaporativo, sonda y calefactor disponibles.
        byte c = 0x01 | 0x04 | 0x20 | 0x40;
        // Incompletos mientras el motor está frío: catalizador y evaporativo.
        byte d = (byte)(engine.Coolant < 70 ? 0x01 | 0x04 : 0x00);
        return [a, b, c, d];
    }

    private static byte Clamp(double v) => (byte)Math.Clamp(Math.Round(v), 0, 255);

    private static byte[] Word(double v)
    {
        int w = (int)Math.Clamp(Math.Round(v), 0, 65535);
        return [(byte)(w >> 8), (byte)w];
    }

    private static byte Pct(double pct) => Clamp(pct * 255 / 100);

    private static byte Trim(double pct) => Clamp(pct * 128 / 100 + 128);

    // Inversa de las fórmulas de PidCatalog.
    public static byte[]? EncodePid(byte pid, EngineModel e) => pid switch
    {
        0x04 => [Pct(e.Load)],
        0x05 => [Clamp(e.Coolant + 40)],
        0x06 => [Trim(e.ShortTermTrim)],
        0x07 => [Trim(e.LongTermTrim)],
        0x0B => [Clamp(e.Map)],
        0x0C => Word(e.Rpm * 4),
        0x0D => [Clamp(e.Speed)],
        0x0E => [Clamp((e.TimingAdvance + 64) * 2)],
        0x0F => [Clamp(e.IntakeTemp + 40)],
        0x10 => Word(e.Maf * 100),
        0x11 => [Pct(e.Throttle)],
        0x14 => [Clamp(e.O2Voltage * 200), 0xFF],
        0x15 => [Clamp(0.65 * 200), 0xFF],
        0x1F => Word(e.Seconds),
        0x21 => Word(0),
        0x2F => [Pct(e.FuelLevel)],
        0x30 => [12],
        0x31 => Word(1520),
        0x33 => [101],
        0x3C => Word((e.CatalystTemp + 40) * 10),
        0x42 => Word(e.ModuleVoltage * 1000),
        0x44 => Word(1.0 * 65536 / 2),
        0x45 => [Pct(e.Throttle - 8)],
        0x46 => [Clamp(e.Ambient + 40)],
        0x49 => [Pct(e.Throttle + 4)],
        0x4C => [Pct(e.Throttle)],
        0x5C => [Clamp(e.OilTemp + 40)],
        0x5E => Word(e.FuelRate * 20),
        _ => null,
    };
}
