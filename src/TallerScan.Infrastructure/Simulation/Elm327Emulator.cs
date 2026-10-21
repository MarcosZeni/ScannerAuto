using System.Diagnostics;
using System.Globalization;
using System.Text;
using TallerScan.Infrastructure.Elm;

namespace TallerScan.Infrastructure.Simulation;

// Emula un adaptador ELM327 v1.5 conectado a un auto simulado: recibe el texto
// del comando y devuelve exactamente lo que imprimiría el adaptador, prompt '>' incluido.
// Lo usan el modo simulación de la app y el emulador TCP (tools/TallerScan.Emulator).
public sealed class Elm327Emulator
{
    private readonly Lock _lock = new();
    private bool _echo, _linefeeds, _spaces, _headers, _protocolFound;
    private char _protocolSetting;
    // Cabecera del pedido (ATSH) y byte de prioridad (ATCP) para 29 bits. null = funcional (7DF).
    private string? _header;
    private string _priority = "18";
    // Filtro de recepción: null = automático (respuestas OBD o "pedido + 8"), como el ELM real.
    private (uint Filter, uint Mask)? _receive;

    // Los clones viejos no aceptan ATCF/ATCM: con false responden "?" y la app usa ATCRA.
    public bool SupportsWideFilter { get; set; } = true;

    public Elm327Emulator(Scenario scenario, Func<TimeSpan>? clock = null, int seed = 1234)
    {
        var sw = Stopwatch.StartNew();
        Engine = new EngineModel(clock ?? (() => sw.Elapsed), seed);
        LoadScenario(scenario);
    }

    public Scenario Scenario { get; private set; } = null!;

    public EngineModel Engine { get; }

    public void LoadScenario(Scenario scenario)
    {
        lock (_lock)
        {
            Scenario = scenario;
            Engine.LongTermTrimBias = 0;
            Engine.MisfireJitter = 0;
            Engine.CoolantTarget = 90;
            scenario.ConfigureEngine?.Invoke(Engine);
            ResetSettings();
        }
    }

    private void ResetSettings()
    {
        _echo = true;
        _linefeeds = true;
        _spaces = true;
        _headers = false;
        _protocolSetting = '0';
        _protocolFound = false;
        _header = null;
        _priority = "18";
        _receive = null;
    }

    // ¿Pasa el filtro de recepción una respuesta con esta cabecera? En automático el ELM
    // acepta las respuestas OBD (7E8–7EF / 18DAF1xx) y, con cabecera física, "pedido + 8".
    private bool Accepts(SimulatedEcu ecu, bool can29)
    {
        uint id = Convert.ToUInt32(can29 ? ecu.CanId29 : ecu.CanId11, 16);
        if (_receive is { } r)
            return (id & r.Mask) == (r.Filter & r.Mask);
        if (can29)
            return (id & 0xFFFFFF00) == 0x18DAF100;
        if (id is >= 0x7E8 and <= 0x7EF)
            return true;
        var target = TargetRequestId;
        return target is not null && id == Convert.ToUInt32(target, 16) + 8;
    }

    // A qué módulo va dirigido el pedido actual: null = a todos (funcional).
    private string? TargetRequestId
    {
        get
        {
            if (_header is null)
                return null;
            var full = _header.Length == 6 ? _priority + _header : _header;
            return full is "7DF" or "18DB33F1" ? null : full;
        }
    }

    private string Eol => _linefeeds ? "\r\n" : "\r";

    public string Process(string input)
    {
        lock (_lock)
        {
            var command = input.Replace(" ", "").Trim().ToUpperInvariant();
            // El ELM hace eco mientras recibe: ATE0 todavía aparece repetido.
            bool echo = _echo;
            var lines = Handle(command);
            var sb = new StringBuilder();
            if (echo)
                sb.Append(input.Trim()).Append(Eol);
            foreach (var line in lines)
                sb.Append(line).Append(Eol);
            sb.Append(Eol).Append('>');
            return sb.ToString();
        }
    }

    private List<string> Handle(string cmd)
    {
        if (cmd.Length == 0)
            return ["?"];
        if (cmd.StartsWith("AT", StringComparison.Ordinal))
            return HandleAt(cmd[2..]);
        if (cmd.Length % 2 != 0 || !cmd.All(Uri.IsHexDigit))
            return ["?"];
        return HandleObd(ElmTextParser.ParseHex(cmd)!);
    }

    private List<string> HandleAt(string at)
    {
        switch (at)
        {
            case "Z":
            case "WS":
                ResetSettings();
                return ["", "ELM327 v1.5"];
            case "D":
                ResetSettings();
                return ["OK"];
            case "I":
                return ["ELM327 v1.5"];
            case "@1":
                return ["TallerScan Emulador"];
            case "RV":
                var v = Scenario.AdapterVoltage + (Scenario.IgnitionOn ? 0 : -1.8);
                return [v.ToString("0.0", CultureInfo.InvariantCulture) + "V"];
            case "DP":
                return [(_protocolSetting == '0' ? "AUTO, " : "") + ObdProtocol.NameOf(Scenario.Protocol)];
            case "DPN":
                return [(_protocolSetting == '0' ? "A" : "") + Scenario.Protocol];
            case "E0": _echo = false; return ["OK"];
            case "E1": _echo = true; return ["OK"];
            case "L0": _linefeeds = false; return ["OK"];
            case "L1": _linefeeds = true; return ["OK"];
            case "S0": _spaces = false; return ["OK"];
            case "S1": _spaces = true; return ["OK"];
            case "H0": _headers = false; return ["OK"];
            case "H1": _headers = true; return ["OK"];
            case "PC":
                _protocolFound = false;
                return ["OK"];
            case "AR":
                _receive = null;
                return ["OK"];
        }
        if (at.StartsWith("CRA", StringComparison.Ordinal) && at.Length is 6 or 11 && at[3..].All(Uri.IsHexDigit))
        {
            _receive = (Convert.ToUInt32(at[3..], 16), at.Length == 6 ? 0x7FFu : 0x1FFFFFFFu);
            return ["OK"];
        }
        if ((at.StartsWith("CF", StringComparison.Ordinal) || at.StartsWith("CM", StringComparison.Ordinal)) && at.Length is 5 or 10 && at[2..].All(Uri.IsHexDigit))
        {
            if (!SupportsWideFilter)
                return ["?"];
            uint value = Convert.ToUInt32(at[2..], 16);
            var current = _receive ?? (0u, at.Length == 5 ? 0x7FFu : 0x1FFFFFFFu);
            _receive = at[1] == 'F' ? (value, current.Item2) : (current.Item1, value);
            return ["OK"];
        }
        if (at.StartsWith("SH", StringComparison.Ordinal) && at.Length is 5 or 8 && at[2..].All(Uri.IsHexDigit))
        {
            _header = at[2..];
            return ["OK"];
        }
        if (at.StartsWith("CP", StringComparison.Ordinal) && at.Length == 4)
        {
            _priority = at[2..];
            return ["OK"];
        }
        if (at.StartsWith("SP", StringComparison.Ordinal) && at.Length == 3)
        {
            _protocolSetting = at[2];
            _protocolFound = false;
            return ["OK"];
        }
        if (at.StartsWith("AT", StringComparison.Ordinal) || at.StartsWith("ST", StringComparison.Ordinal) || at.StartsWith("CAF", StringComparison.Ordinal))
            return ["OK"];
        return ["?"];
    }

    private List<string> HandleObd(byte[] request)
    {
        var output = new List<string>();
        if (!_protocolFound && _protocolSetting == '0')
            output.Add("SEARCHING...");

        if (!Scenario.IgnitionOn)
        {
            output.Add("UNABLE TO CONNECT");
            return output;
        }
        _protocolFound = true;

        byte service = request[0];
        var args = request[1..];
        bool legacy = Scenario.Protocol is '1' or '2' or '3' or '4' or '5';
        bool can29 = Scenario.Protocol is '7' or '9';
        var target = TargetRequestId;
        bool any = false;
        foreach (var ecu in Scenario.Ecus)
        {
            // Con cabecera física solo contesta el módulo al que va dirigido el pedido;
            // con la funcional, solo los que la escuchan (ABS o airbag no).
            if (target is not null && !string.Equals(can29 ? ecu.RequestId29 : ecu.RequestId11, target, StringComparison.OrdinalIgnoreCase))
                continue;
            if (target is null && !ecu.ListensFunctional)
                continue;
            // Lo que el filtro de recepción no deja pasar, el ELM no lo muestra (NO DATA).
            if (!legacy && !Accepts(ecu, can29))
                continue;
            foreach (var payload in ecu.Respond(service, args, Engine, legacy))
            {
                any = true;
                output.AddRange(Format(ecu, payload, legacy));
            }
        }
        if (!any)
            output.Add("NO DATA");
        return output;
    }

    private string Join(IEnumerable<byte> bytes) =>
        string.Join(_spaces ? " " : "", bytes.Select(b => b.ToString("X2")));

    private IEnumerable<string> Format(SimulatedEcu ecu, byte[] payload, bool legacy)
    {
        string sep = _spaces ? " " : "";
        if (legacy)
        {
            if (!_headers)
                return [Join(payload)];
            // ISO 9141-2: 48 6B <origen> ... <checksum>
            var frame = new List<byte> { 0x48, 0x6B, Convert.ToByte(ecu.LegacyAddress, 16) };
            frame.AddRange(payload);
            byte sum = 0;
            foreach (var b in frame)
                sum += b;
            frame.Add(sum);
            return [Join(frame)];
        }

        bool can29 = Scenario.Protocol is '7' or '9';
        string header = can29
            ? (_spaces ? string.Join(" ", Enumerable.Range(0, 4).Select(i => ecu.CanId29.Substring(i * 2, 2))) : ecu.CanId29)
            : ecu.CanId11;

        if (payload.Length <= 7)
        {
            if (!_headers)
                return [Join(payload)];
            var single = new byte[8];
            single[0] = (byte)payload.Length;
            payload.CopyTo(single, 1);
            return [header + sep + Join(single)];
        }

        // Mensaje largo ISO-TP: primera trama + consecutivas (el control de flujo lo hace el ELM).
        var frames = new List<byte[]>();
        var first = new byte[8];
        first[0] = (byte)(0x10 | (payload.Length >> 8));
        first[1] = (byte)payload.Length;
        Array.Copy(payload, 0, first, 2, 6);
        frames.Add(first);
        int offset = 6, seq = 1;
        while (offset < payload.Length)
        {
            var cf = new byte[8];
            cf[0] = (byte)(0x20 | (seq & 0x0F));
            int n = Math.Min(7, payload.Length - offset);
            Array.Copy(payload, offset, cf, 1, n);
            frames.Add(cf);
            offset += n;
            seq++;
        }

        if (_headers)
            return frames.Select(f => header + sep + Join(f));

        // Sin cabeceras el ELM muestra el largo y las tramas numeradas.
        var lines = new List<string> { payload.Length.ToString("X3") };
        int index = 0, done = 0;
        foreach (var f in frames)
        {
            var data = index == 0 ? f[2..] : f[1..];
            int take = Math.Min(data.Length, payload.Length - done);
            lines.Add($"{index & 0x0F:X}:{(_spaces ? " " : "")}{Join(data[..take])}");
            done += take;
            index++;
        }
        return lines;
    }
}
