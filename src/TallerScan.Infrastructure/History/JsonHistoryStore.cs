using System.Text.Json;
using System.Text.Json.Serialization;

namespace TallerScan.Infrastructure.History;

public sealed record SessionSummary(Guid Id, DateTimeOffset CreatedUtc, string Plate, string Vehicle, int DtcCount, bool Simulated, bool? MilOn);

// Historial local: un archivo JSON por diagnóstico en la carpeta de datos de la app.
// Sin servidor ni internet. Se escribe a un temporal y se renombra para no dejar
// archivos a medias si la app se cierra en el medio.
public sealed class JsonHistoryStore(string directory)
{
    public const long MaxImportBytes = 5 * 1024 * 1024;

    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private string PathOf(Guid id) => Path.Combine(directory, $"{id:N}.json");

    public async Task SaveAsync(SessionRecord record, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(directory);
        var tmp = PathOf(record.Id) + ".tmp";
        await using (var stream = File.Create(tmp))
            await JsonSerializer.SerializeAsync(stream, record, Options, cancellationToken);
        File.Move(tmp, PathOf(record.Id), overwrite: true);
    }

    public async Task<IReadOnlyList<SessionSummary>> ListAsync(CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(directory))
            return [];
        var list = new List<SessionSummary>();
        foreach (var file in Directory.EnumerateFiles(directory, "*.json"))
        {
            var record = await TryReadAsync(file, cancellationToken);
            if (record is not null)
                list.Add(new SessionSummary(record.Id, record.CreatedUtc, record.Plate, record.VehicleDescription,
                    record.Dtcs.Select(d => d.Code).Concat(record.ModuleScan?.Modules.SelectMany(m => m.Dtcs.Select(d => d.Code)) ?? []).Distinct().Count(),
                    record.Simulated, record.MilOn));
        }
        return list.OrderByDescending(s => s.CreatedUtc).ToList();
    }

    public Task<SessionRecord?> LoadAsync(Guid id, CancellationToken cancellationToken = default) =>
        TryReadAsync(PathOf(id), cancellationToken);

    public void Delete(Guid id)
    {
        if (File.Exists(PathOf(id)))
            File.Delete(PathOf(id));
    }

    public string FileOf(Guid id) => PathOf(id);

    private static async Task<SessionRecord?> TryReadAsync(string file, CancellationToken ct)
    {
        try
        {
            await using var stream = File.OpenRead(file);
            return await JsonSerializer.DeserializeAsync<SessionRecord>(stream, Options, ct);
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    // Importa un diagnóstico exportado desde otro equipo. Si ya existe (mismo Id)
    // no se pisa: se informa y se deja el que estaba.
    public async Task<ImportResult> ImportAsync(Stream source, CancellationToken cancellationToken = default)
    {
        using var buffer = new MemoryStream();
        await source.CopyToAsync(buffer, cancellationToken);
        if (buffer.Length > MaxImportBytes)
            return new ImportResult(false, "El archivo es demasiado grande.");
        buffer.Position = 0;
        SessionRecord? record;
        try
        {
            record = await JsonSerializer.DeserializeAsync<SessionRecord>(buffer, Options, cancellationToken);
        }
        catch (JsonException)
        {
            return new ImportResult(false, "El archivo no es un diagnóstico de TallerScan.");
        }
        if (record is null || record.Id == Guid.Empty)
            return new ImportResult(false, "El archivo no es un diagnóstico de TallerScan.");
        if (record.Schema > SessionRecord.CurrentSchema)
            return new ImportResult(false, "El diagnóstico es de una versión más nueva de la app.");
        if (File.Exists(PathOf(record.Id)))
            return new ImportResult(false, "Ese diagnóstico ya está en el historial.");
        await SaveAsync(record, cancellationToken);
        return new ImportResult(true, $"Importado: {record.Plate} del {record.CreatedUtc.ToLocalTime():dd/MM/yyyy HH:mm}.");
    }
}

public sealed record ImportResult(bool Ok, string Message);
