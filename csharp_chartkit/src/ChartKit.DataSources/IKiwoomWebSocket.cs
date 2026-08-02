using System.Net.WebSockets;

namespace ChartKit.CSharp.DataSources;

internal interface IKiwoomWebSocket : IDisposable
{
    WebSocketState State { get; }

    Task ConnectAsync(Uri uri, CancellationToken cancellationToken);

    ValueTask SendAsync(
        ReadOnlyMemory<byte> buffer,
        WebSocketMessageType messageType,
        bool endOfMessage,
        CancellationToken cancellationToken);

    ValueTask<ValueWebSocketReceiveResult> ReceiveAsync(
        Memory<byte> buffer,
        CancellationToken cancellationToken);
}

internal sealed class ClientKiwoomWebSocket : IKiwoomWebSocket
{
    private readonly ClientWebSocket _inner = new();

    public WebSocketState State => _inner.State;

    public Task ConnectAsync(Uri uri, CancellationToken cancellationToken) =>
        _inner.ConnectAsync(uri, cancellationToken);

    public ValueTask SendAsync(
        ReadOnlyMemory<byte> buffer,
        WebSocketMessageType messageType,
        bool endOfMessage,
        CancellationToken cancellationToken) =>
        _inner.SendAsync(
            buffer,
            messageType,
            endOfMessage,
            cancellationToken);

    public ValueTask<ValueWebSocketReceiveResult> ReceiveAsync(
        Memory<byte> buffer,
        CancellationToken cancellationToken) =>
        _inner.ReceiveAsync(buffer, cancellationToken);

    public void Dispose() => _inner.Dispose();
}
