using System.Diagnostics;
using System.Text;
using System.Threading.Channels;
using TallerScan.Core.Contracts;
using TallerScan.Core.Models;
using TallerScan.Core.Safety;

namespace TallerScan.Infrastructure.Elm;

// Dueño único del transporte: un comando a la vez, cada respuesta termina en '>'.
// Las lecturas del transporte pueden traer fragmentos o varias respuestas juntas;
// acá se acumulan hasta el prompt.
public sealed class ElmLink : IAsyncDisposable
{
    public const int MaxResponseChars = 16 * 1024;

    private readonly IObdTransport _transport;
    private readonly TraceLog _trace;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Channel<string> _responses = Channel.CreateUnbounded<string>(new UnboundedChannelOptions { SingleReader = true, SingleWriter = true });
    private readonly CancellationTokenSource _stop = new();
    private Task? _reader;
    // Comandos que vencieron sin respuesta: su '>' puede llegar tarde y no debe
    // tomarse como respuesta del comando siguiente.
    private int _orphans;
    private volatile bool _open;

    public ElmLink(IObdTransport transport, TraceLog trace)
    {
        _transport = transport;
        _trace = trace;
    }

    public bool IsOpen => _open;

    // Alcance de escritura abierto por la sesión (borrar fallas, prueba de actuadores).
    // Fuera de esos flujos queda en solo lectura.
    public volatile CommandScope Scope = CommandScope.ReadOnly;

    public string TransportDescription => _transport.Description;

    // Se dispara una sola vez cuando se corta el transporte (null si fue un cierre pedido).
    public event Action<Exception?>? Closed;

    public async Task OpenAsync(CancellationToken cancellationToken)
    {
        await _transport.OpenAsync(cancellationToken);
        _open = true;
        _reader = Task.Run(() => ReadLoopAsync(_stop.Token));
    }

    private async Task ReadLoopAsync(CancellationToken ct)
    {
        var buffer = new byte[1024];
        var current = new StringBuilder();
        bool overflow = false;
        Exception? error = null;
        try
        {
            while (!ct.IsCancellationRequested)
            {
                int n = await _transport.ReadAsync(buffer, ct);
                if (n == 0)
                {
                    error = new IOException("El adaptador cerró la conexión.");
                    break;
                }
                for (int i = 0; i < n; i++)
                {
                    char c = (char)buffer[i];
                    if (c == '\0')
                        continue;
                    if (c == '>')
                    {
                        _responses.Writer.TryWrite(overflow ? "BUFFER FULL" : current.ToString());
                        current.Clear();
                        overflow = false;
                    }
                    else if (current.Length < MaxResponseChars)
                        current.Append(c);
                    else
                        overflow = true;
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            error = ex;
        }
        _open = false;
        _responses.Writer.TryComplete();
        if (!ct.IsCancellationRequested)
            Closed?.Invoke(error);
    }

    public async Task<ElmResponse> SendAsync(string command, TimeSpan timeout, CancellationToken cancellationToken)
    {
        if (!CommandPolicy.IsAllowed(command, Scope, out var reason))
        {
            // Se registra la denegación sin mandar nada al adaptador.
            var blocked = _trace.Add(command, reason ?? "", "BLOQUEADO", 0);
            return new ElmResponse(command, "", [], ReadStatus.Blocked, reason, 0, blocked.Id.ToString());
        }
        if (!_open)
            return new ElmResponse(command, "", [], ReadStatus.CommunicationError, "No hay conexión con el adaptador.", 0, null);

        await _gate.WaitAsync(cancellationToken);
        try
        {
            await DrainAsync(cancellationToken);

            var sw = Stopwatch.StartNew();
            await _transport.WriteAsync(Encoding.ASCII.GetBytes(command + "\r"), cancellationToken);

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(timeout);
            try
            {
                var raw = await _responses.Reader.ReadAsync(timeoutCts.Token);
                var entry = _trace.Add(command, raw.Trim(), "OK", sw.ElapsedMilliseconds);
                var parsed = ElmTextParser.Parse(command, raw, sw.ElapsedMilliseconds, entry.Id.ToString());
                return parsed;
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                _orphans++;
                var entry = _trace.Add(command, "", "TIMEOUT", sw.ElapsedMilliseconds);
                return new ElmResponse(command, "", [], ReadStatus.Timeout, $"Sin respuesta en {timeout.TotalSeconds:0.#} s.", sw.ElapsedMilliseconds, entry.Id.ToString());
            }
            catch (OperationCanceledException)
            {
                _orphans++;
                throw;
            }
            catch (ChannelClosedException)
            {
                var entry = _trace.Add(command, "", "CONEXIÓN CERRADA", sw.ElapsedMilliseconds);
                return new ElmResponse(command, "", [], ReadStatus.CommunicationError, "Se cortó la conexión con el adaptador.", sw.ElapsedMilliseconds, entry.Id.ToString());
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    // Descarta respuestas tardías de comandos vencidos o cancelados, con espera acotada.
    private async Task DrainAsync(CancellationToken ct)
    {
        while (_orphans > 0)
        {
            using var wait = CancellationTokenSource.CreateLinkedTokenSource(ct);
            wait.CancelAfter(TimeSpan.FromMilliseconds(1500));
            try
            {
                var late = await _responses.Reader.ReadAsync(wait.Token);
                _trace.Add("(tardía)", late.Trim(), "DESCARTADA", 0);
                _orphans--;
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                // No llegó: se asume perdida para no bloquear la sesión.
                _orphans = 0;
            }
            catch (ChannelClosedException)
            {
                _orphans = 0;
            }
        }
        while (_responses.Reader.TryRead(out var stray))
            _trace.Add("(sin pedido)", stray.Trim(), "DESCARTADA", 0);
    }

    public async ValueTask DisposeAsync()
    {
        _open = false;
        await _stop.CancelAsync();
        try
        {
            await _transport.DisposeAsync();
        }
        catch
        {
            // Cerrar un transporte ya caído puede fallar; no hay nada más que hacer.
        }
        if (_reader is not null)
        {
            try
            {
                await _reader.WaitAsync(TimeSpan.FromSeconds(2));
            }
            catch
            {
                // El lector terminó con error o no respondió a tiempo: ya está cancelado.
            }
        }
        _stop.Dispose();
    }
}
