using System.Net.Sockets;
using TallerScan.Core.Contracts;

namespace TallerScan.Infrastructure.Transports;

// Base para transportes que terminan en un Stream (TCP, socket Bluetooth de Android,
// StreamSocket de Windows). Cada plataforma solo implementa cómo abrirlo.
public abstract class StreamTransport : IObdTransport
{
    private Stream? _stream;

    public abstract string Description { get; }

    protected abstract Task<Stream> OpenStreamAsync(CancellationToken cancellationToken);

    protected virtual ValueTask CloseExtraAsync() => ValueTask.CompletedTask;

    public async Task OpenAsync(CancellationToken cancellationToken)
    {
        if (_stream is not null)
            throw new InvalidOperationException("El transporte ya está abierto.");
        _stream = await OpenStreamAsync(cancellationToken);
    }

    public async Task WriteAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken)
    {
        var s = _stream ?? throw new InvalidOperationException("El transporte no está abierto.");
        await s.WriteAsync(data, cancellationToken);
        await s.FlushAsync(cancellationToken);
    }

    public ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken)
    {
        var s = _stream ?? throw new InvalidOperationException("El transporte no está abierto.");
        return s.ReadAsync(buffer, cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        var s = _stream;
        _stream = null;
        if (s is not null)
            await s.DisposeAsync();
        await CloseExtraAsync();
        GC.SuppressFinalize(this);
    }
}

// Adaptadores Wi-Fi (suelen ser 192.168.0.10:35000) y el emulador de TallerScan.
public sealed class TcpTransport(string host, int port) : StreamTransport
{
    private TcpClient? _client;

    public override string Description => $"Wi-Fi {host}:{port}";

    protected override async Task<Stream> OpenStreamAsync(CancellationToken cancellationToken)
    {
        _client = new TcpClient { NoDelay = true };
        await _client.ConnectAsync(host, port, cancellationToken);
        return _client.GetStream();
    }

    protected override ValueTask CloseExtraAsync()
    {
        _client?.Dispose();
        _client = null;
        return ValueTask.CompletedTask;
    }
}
