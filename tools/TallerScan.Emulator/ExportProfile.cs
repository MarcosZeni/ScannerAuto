using TallerScan.Infrastructure.Actuators;
using TallerScan.Infrastructure.Simulation;

namespace TallerScan.Emulator;

// `--exportar-perfil <archivo>`: escribe el perfil del auto simulado como JSON, que sirve de
// plantilla para armar perfiles reales (perfiles/README.md).
public static class ExportProfile
{
    public static async Task RunAsync(string path)
    {
        var json = ProfileStore.Serialize(SimulatedActuators.Profile with
        {
            Id = "ejemplo-simulado",
            Name = "Ejemplo (auto simulado)",
            Source = "Plantilla exportada del simulador: identificadores inventados, no sirven para un auto real.",
            VinPrefixes = [],
        });
        await File.WriteAllTextAsync(path, json);
        Console.WriteLine($"Perfil de ejemplo escrito en {path}");
    }
}
