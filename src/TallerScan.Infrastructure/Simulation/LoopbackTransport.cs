using System.Text;
using System.Threading.Channels;
using TallerScan.Core.Contracts;

namespace TallerScan.Infrastructure.Simulation;

// Transporte en memoria contra el emulador ELM327. Entrega las respuestas en
// fragmentos de tamaño variable, como llegan por Bluetooth, para ejercitar el
// armado de respuestas igual que con un adaptador real.
public sealed class LoopbackTransport(Elm327Emulator emulator, int seed = 99) : IObdTransport
{
    private readonly Channel<byte[]> _incoming = Channel.CreateUnbounded<byte[]>();
    private readonly StringBuilder _pending = new();
    private readonly Random _random = new(seed);
    private byte[]? _partial;
    private int _partialOffset;
    private bool _open;

    public string Description => $"Simulación: {emulator.Scenario.Name}";

    public Elm327Emulator Emulator => emulator;

    public Task OpenAsync(CancellationToken cancellationToken)
    {
        _open = true;
        return Task.CompletedTask;
    }

    public async Task WriteAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken)
    {
        if (!_open)
            throw new InvalidOperationException("Transporte cerrado.");
        foreach (var b in data.ToArray())
        {
            if (b == '\r')
            {
                var command = _pending.ToString();
                _pending.Clear();
                var delay = emulator.Scenario.ResponseDelayMs;
                var response = emulator.Process(command);
                if (delay > 0)
                {
                    _ = Task.Delay(delay, CancellationToken.None).ContinueWith(_ => Enqueue(response), TaskScheduler.Default);
                }
                else
                {
                    // Pequeña latencia para parecerse a un adaptador real.
                    await Task.Delay(15, cancellationToken);
                    Enqueue(response);
                }
            }
            else
            {
                _pending.Append((char)b);
            }
        }
    }

    private void Enqueue(string response)
    {
        var bytes = Encoding.ASCII.GetBytes(response);
        int offset = 0;
        while (offset < bytes.Length)
        {
            int size;
            lock (_random)
                size = Math.Min(bytes.Length - offset, _random.Next(1, 21));
            _incoming.Writer.TryWrite(bytes[offset..(offset + size)]);
            offset += size;
        }
    }

    public async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken)
    {
        if (_partial is null)
        {
            try
            {
                _partial = await _incoming.Reader.ReadAsync(cancellationToken);
                _partialOffset = 0;
            }
            catch (ChannelClosedException)
            {
                return 0;
            }
        }
        int n = Math.Min(buffer.Length, _partial.Length - _partialOffset);
        _partial.AsMemory(_partialOffset, n).CopyTo(buffer);
        _partialOffset += n;
        if (_partialOffset >= _partial.Length)
            _partial = null;
        return n;
    }

    public ValueTask DisposeAsync()
    {
        _open = false;
        _incoming.Writer.TryComplete();
        return ValueTask.CompletedTask;
    }
}
