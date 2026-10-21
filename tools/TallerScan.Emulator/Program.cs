using System.Net;
using System.Net.Sockets;
using System.Text;
using TallerScan.Core.Diagnostics;
using TallerScan.Infrastructure.Simulation;

// Emulador de adaptador ELM327 Wi-Fi + auto simulado.
// La app se conecta por "Wi-Fi" a esta PC: 127.0.0.1 desde la misma PC,
// 10.0.2.2 desde el emulador de Android, o la IP de la PC desde un celular en la misma red.
//
// Uso: dotnet run --project tools/TallerScan.Emulator -- [--escenario falla-encendido] [--puerto 35000]

string scenarioId = "falla-encendido";
int port = 35000;
bool verbose = true;
EngineMode? initialMode = null;
for (int i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--escenario" when i + 1 < args.Length: scenarioId = args[++i]; break;
        case "--motor" when i + 1 < args.Length:
            initialMode = args[++i] switch { "apagado" => EngineMode.Off, "andando" => EngineMode.Driving, _ => EngineMode.Idle };
            break;
        case "--puerto" when i + 1 < args.Length: port = int.Parse(args[++i]); break;
        case "--silencioso": verbose = false; break;
        case "--exportar-perfil" when i + 1 < args.Length: await TallerScan.Emulator.ExportProfile.RunAsync(args[++i]); return;
        case "--ayuda" or "-h" or "--help": PrintHelp(); return;
    }
}

Console.OutputEncoding = Encoding.UTF8;
var emulator = new Elm327Emulator(Scenarios.Create(scenarioId), seed: Environment.TickCount);
if (initialMode is { } mode)
    emulator.Engine.Mode = mode;
var listener = new TcpListener(IPAddress.Any, port);
listener.Start();

Console.WriteLine($"Emulador ELM327 escuchando en el puerto {port}.");
Console.WriteLine($"Escenario: {emulator.Scenario.Name}");
foreach (var ip in Dns.GetHostAddresses(Dns.GetHostName()).Where(a => a.AddressFamily == AddressFamily.InterNetwork))
    Console.WriteLine($"  Desde un celular en la misma red: {ip}:{port}");
Console.WriteLine("Escribí 'ayuda' para ver los comandos.\n");

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };
_ = Task.Run(() => ServeAsync(cts.Token));

// Consola: cambiar escenario, agregar fallas, acelerar...
while (!cts.IsCancellationRequested)
{
    var line = Console.ReadLine();
    if (line is null)
    {
        await Task.Delay(Timeout.Infinite, cts.Token).ContinueWith(_ => { });
        break;
    }
    var parts = line.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
    if (parts.Length == 0)
        continue;
    var ecu = emulator.Scenario.Ecus[0];
    switch (parts[0].ToLowerInvariant())
    {
        case "ayuda":
            PrintCommands();
            break;
        case "escenarios":
            foreach (var (id, name, description) in Scenarios.Catalog)
                Console.WriteLine($"  {id,-18} {name}: {description}");
            break;
        case "escenario" when parts.Length > 1:
            try
            {
                emulator.LoadScenario(Scenarios.Create(parts[1]));
                Console.WriteLine($"Escenario: {emulator.Scenario.Name}. Reconectar desde la app.");
            }
            catch (ArgumentException ex)
            {
                Console.WriteLine(ex.Message);
            }
            break;
        case "falla" when parts.Length > 1 && DtcCodec.IsValid(parts[1].ToUpperInvariant()):
            ecu.Stored.Add(parts[1].ToUpperInvariant());
            Console.WriteLine($"Agregada almacenada {parts[1].ToUpperInvariant()}: {DtcCatalog.Describe(parts[1]).Description ?? "sin descripción"}");
            break;
        case "pendiente" when parts.Length > 1 && DtcCodec.IsValid(parts[1].ToUpperInvariant()):
            ecu.Pending.Add(parts[1].ToUpperInvariant());
            Console.WriteLine($"Agregada pendiente {parts[1].ToUpperInvariant()}.");
            break;
        case "permanente" when parts.Length > 1 && DtcCodec.IsValid(parts[1].ToUpperInvariant()):
            ecu.Permanent.Add(parts[1].ToUpperInvariant());
            Console.WriteLine($"Agregada permanente {parts[1].ToUpperInvariant()}.");
            break;
        case "limpiar":
            ecu.Stored.Clear();
            ecu.Pending.Clear();
            ecu.FreezeDtc = null;
            Console.WriteLine("Fallas almacenadas y pendientes borradas (las permanentes quedan, como en un auto real).");
            break;
        case "fallas":
            Console.WriteLine($"  Almacenadas: {string.Join(", ", ecu.Stored)}");
            Console.WriteLine($"  Pendientes:  {string.Join(", ", ecu.Pending)}");
            Console.WriteLine($"  Permanentes: {string.Join(", ", ecu.Permanent)}");
            break;
        case "andar":
            emulator.Engine.Mode = EngineMode.Driving;
            Console.WriteLine("Motor andando (RPM y velocidad variables).");
            break;
        case "ralenti":
            emulator.Engine.Mode = EngineMode.Idle;
            Console.WriteLine("Motor en ralentí.");
            break;
        case "apagar":
            emulator.Engine.Mode = EngineMode.Off;
            Console.WriteLine("Motor apagado con contacto dado.");
            break;
        case "silencio":
            verbose = !verbose;
            Console.WriteLine(verbose ? "Mostrando tráfico." : "Tráfico oculto.");
            break;
        case "salir":
            cts.Cancel();
            break;
        default:
            Console.WriteLine("Comando desconocido. Escribí 'ayuda'.");
            break;
    }
}
listener.Stop();

async Task ServeAsync(CancellationToken ct)
{
    while (!ct.IsCancellationRequested)
    {
        TcpClient client;
        try
        {
            client = await listener.AcceptTcpClientAsync(ct);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        Console.WriteLine($"[+] Conectado {client.Client.RemoteEndPoint}");
        // Un adaptador atiende a un cliente por vez, como uno real.
        await HandleClientAsync(client, ct);
        Console.WriteLine("[-] Desconectado");
    }
}

async Task HandleClientAsync(TcpClient client, CancellationToken ct)
{
    using var _ = client;
    var stream = client.GetStream();
    var buffer = new byte[256];
    var pending = new StringBuilder();
    try
    {
        while (!ct.IsCancellationRequested)
        {
            int n = await stream.ReadAsync(buffer, ct);
            if (n == 0)
                return;
            for (int i = 0; i < n; i++)
            {
                char c = (char)buffer[i];
                if (c == '\r')
                {
                    var command = pending.ToString();
                    pending.Clear();
                    var response = emulator.Process(command);
                    if (emulator.Scenario.ResponseDelayMs > 0)
                        await Task.Delay(emulator.Scenario.ResponseDelayMs, ct);
                    await stream.WriteAsync(Encoding.ASCII.GetBytes(response), ct);
                    if (verbose)
                        Console.WriteLine($"  > {command,-8} < {response.Replace("\r", " ").Replace("\n", "").Replace(">", "").Trim()}");
                }
                else if (c != '\n' && pending.Length < 64)
                {
                    pending.Append(c);
                }
            }
        }
    }
    catch (Exception ex) when (ex is IOException or OperationCanceledException or SocketException)
    {
    }
}

static void PrintHelp()
{
    Console.WriteLine("TallerScan.Emulator — adaptador ELM327 Wi-Fi simulado");
    Console.WriteLine("  --escenario <id>   (por defecto falla-encendido)");
    Console.WriteLine("  --puerto <n>       (por defecto 35000, el de los adaptadores Wi-Fi)");
    Console.WriteLine("  --motor apagado|ralenti|andando   estado inicial del motor (por defecto ralentí)");
    Console.WriteLine("  --exportar-perfil <archivo.json>  escribe el perfil de ejemplo y termina");
    Console.WriteLine("  --silencioso       no mostrar el tráfico");
    Console.WriteLine();
    foreach (var (id, name, description) in Scenarios.Catalog)
        Console.WriteLine($"  {id,-18} {name}: {description}");
}

static void PrintCommands()
{
    Console.WriteLine("  escenarios           lista los escenarios");
    Console.WriteLine("  escenario <id>       cambia de auto (reconectar desde la app)");
    Console.WriteLine("  falla P0300          agrega una falla almacenada");
    Console.WriteLine("  pendiente P0171      agrega una pendiente");
    Console.WriteLine("  permanente P0420     agrega una permanente");
    Console.WriteLine("  fallas               muestra las fallas cargadas");
    Console.WriteLine("  limpiar              borra almacenadas y pendientes");
    Console.WriteLine("  andar / ralenti / apagar   cambia el motor");
    Console.WriteLine("  silencio             muestra u oculta el tráfico");
    Console.WriteLine("  salir");
}
