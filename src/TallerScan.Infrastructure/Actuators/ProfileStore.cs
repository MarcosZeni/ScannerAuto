using System.Text.Json;
using System.Text.Json.Serialization;
using TallerScan.Core.Actuators;
using TallerScan.Infrastructure.Simulation;

namespace TallerScan.Infrastructure.Actuators;

// Perfiles disponibles: el del auto simulado (en código) más los cargados de archivos JSON
// (`perfiles/*.json` en los datos de la app). Un perfil de un modelo real se arma con datos
// verificados en ese auto (ver docs/PERFILES - Fuentes y plan.md); nunca se adivina.
public sealed class ProfileStore
{
    public static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    // Servicios que un perfil puede mandar. Cualquier otro (27 seguridad, 2E escritura,
    // 34/36 programación, 11 reset) se rechaza al cargar: un perfil no puede ampliar la política.
    private static readonly string[] AllowedStepServices = ["2F", "31", "30"];

    private readonly List<ActuatorProfile> _profiles = [SimulatedActuators.Profile];
    private readonly Lock _lock = new();

    public IReadOnlyList<ActuatorProfile> All
    {
        get { lock (_lock) return [.. _profiles]; }
    }

    public ActuatorProfile Simulated => SimulatedActuators.Profile;

    public static string Serialize(ActuatorProfile profile) => JsonSerializer.Serialize(profile, Json);

    // Devuelve el perfil cargado o lanza ArgumentException con el motivo, para mostrarlo.
    public ActuatorProfile Load(string json)
    {
        ActuatorProfile? profile;
        try
        {
            profile = JsonSerializer.Deserialize<ActuatorProfile>(json, Json);
        }
        catch (JsonException ex)
        {
            throw new ArgumentException("El archivo no es un perfil válido: " + ex.Message);
        }
        if (profile is null)
            throw new ArgumentException("El archivo está vacío.");
        Validate(profile);
        lock (_lock)
        {
            _profiles.RemoveAll(p => p.Id == profile.Id);
            _profiles.Add(profile);
        }
        return profile;
    }

    // Sincrónico a propósito: se llama al armar la app, en el hilo de interfaz, donde un
    // await bloqueado se traba (deadlock). Son pocos archivos chicos.
    public void LoadDirectory(string directory, Action<string, string>? onError = null)
    {
        if (!Directory.Exists(directory))
            return;
        foreach (var file in Directory.EnumerateFiles(directory, "*.json"))
        {
            try
            {
                Load(File.ReadAllText(file));
            }
            catch (Exception ex) when (ex is ArgumentException or IOException)
            {
                onError?.Invoke(Path.GetFileName(file), ex.Message);
            }
        }
    }

    public static void Validate(ActuatorProfile p)
    {
        if (string.IsNullOrWhiteSpace(p.Id) || p.Id == SimulatedActuators.Profile.Id)
            throw new ArgumentException("El perfil necesita un Id propio.");
        if (string.IsNullOrWhiteSpace(p.Name) || string.IsNullOrWhiteSpace(p.Source))
            throw new ArgumentException("El perfil necesita Name y Source (de dónde salieron los datos).");
        if (p.Modules is null || p.Modules.Count == 0)
            throw new ArgumentException("El perfil no define módulos.");
        foreach (var m in p.Modules)
        {
            if (!IsHex(m.RequestId, 3) && !IsHex(m.RequestId, 8))
                throw new ArgumentException($"Módulo {m.Name}: RequestId tiene que ser 3 (11 bits) u 8 (29 bits) dígitos hex.");
            if (m.ResponseId.Length != m.RequestId.Length || !IsHex(m.ResponseId, m.ResponseId.Length))
                throw new ArgumentException($"Módulo {m.Name}: ResponseId inválido.");
        }
        if (!IsHex(p.SessionOpen, 4) || !p.SessionOpen.StartsWith("10", StringComparison.Ordinal))
            throw new ArgumentException("SessionOpen tiene que ser 10xx.");
        if (!IsHex(p.TesterPresent, 4) || !p.TesterPresent.StartsWith("3E", StringComparison.Ordinal))
            throw new ArgumentException("TesterPresent tiene que ser 3Exx.");
        var ids = new HashSet<string>();
        foreach (var t in p.Tests ?? [])
        {
            if (!ids.Add(t.Id))
                throw new ArgumentException($"Prueba repetida: {t.Id}");
            if (p.Modules.All(m => m.Name != t.ModuleName))
                throw new ArgumentException($"Prueba {t.Id}: el módulo \"{t.ModuleName}\" no está definido.");
            if (t.Steps is null || t.Steps.Count == 0)
                throw new ArgumentException($"Prueba {t.Id}: sin pasos.");
            if (t.MaxDuration <= TimeSpan.Zero || t.MaxDuration > TimeSpan.FromMinutes(5))
                throw new ArgumentException($"Prueba {t.Id}: MaxDuration tiene que estar entre 1 s y 5 min.");
            if (t.StopStep is null)
                throw new ArgumentException($"Prueba {t.Id}: falta un paso con IsStop (apagar / devolver el control).");
            foreach (var s in t.Steps)
            {
                if (s.Payload is null || s.Payload.Length < 4 || s.Payload.Length > 14 || s.Payload.Length % 2 != 0 || !IsHex(s.Payload, s.Payload.Length))
                    throw new ArgumentException($"Prueba {t.Id}, paso {s.Name}: Payload hex de 2 a 7 bytes.");
                if (!AllowedStepServices.Contains(s.Payload[..2]))
                    throw new ArgumentException($"Prueba {t.Id}, paso {s.Name}: servicio {s.Payload[..2]} no permitido (solo 2F, 31 y 30).");
                // 2F DID PP (mínimo 4 bytes), 31 01 RID (4 bytes), 30 LID PP (3 bytes).
                int min = s.Payload[..2] == "30" ? 6 : 8;
                if (s.Payload.Length < min)
                    throw new ArgumentException($"Prueba {t.Id}, paso {s.Name}: Payload demasiado corto para el servicio {s.Payload[..2]}.");
            }
            if (t.StatusDid is not null && !IsHex(t.StatusDid, 4))
                throw new ArgumentException($"Prueba {t.Id}: StatusDid tiene que ser 4 dígitos hex.");
        }
        foreach (var c in p.ModuleClears ?? [])
        {
            if (p.Modules.All(m => m.Name != c.ModuleName))
                throw new ArgumentException($"Borrado: el módulo \"{c.ModuleName}\" no está definido.");
            if (!c.Payload.StartsWith("14", StringComparison.Ordinal) || !IsHex(c.Payload, c.Payload.Length) || c.Payload.Length > 8)
                throw new ArgumentException($"Borrado de {c.ModuleName}: el pedido tiene que ser 14xxxxxx.");
        }
    }

    private static bool IsHex(string? s, int length) =>
        s is not null && s.Length == length && s.All(c => c is >= '0' and <= '9' or >= 'A' and <= 'F');

    // Perfil que aplica solo: el simulado en simulación; en un auto real, por prefijo de VIN.
    public ActuatorProfile? Match(bool simulated, string? vin)
    {
        if (simulated)
            return SimulatedActuators.Profile;
        if (string.IsNullOrEmpty(vin))
            return null;
        lock (_lock)
            return _profiles.FirstOrDefault(p => p.VinPrefixes is { Count: > 0 } prefixes
                && prefixes.Any(prefix => vin.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)));
    }

    // Perfiles que se pueden elegir a mano para un auto real (nunca el simulado).
    public IReadOnlyList<ActuatorProfile> Selectable
    {
        get { lock (_lock) return _profiles.Where(p => p.Id != SimulatedActuators.Profile.Id).ToList(); }
    }
}
