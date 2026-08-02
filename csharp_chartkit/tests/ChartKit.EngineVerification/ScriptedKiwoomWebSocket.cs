using System.Net.WebSockets;
using System.Text;
using ChartKit.CSharp.DataSources;

namespace ChartKit.CSharp.EngineVerification;

internal sealed class ScriptedKiwoomWebSocket : IKiwoomWebSocket
{
    private readonly object _gate = new();
    private readonly Queue<InboundFrame> _frames;
    private readonly List<string> _sentMessages = [];
    private WebSocketState _state = WebSocketState.None;

    public ScriptedKiwoomWebSocket(params InboundFrame[] frames)
    {
        _frames = new Queue<InboundFrame>(frames);
    }

    public WebSocketState State
    {
        get
        {
            lock (_gate) return _state;
        }
    }

    public int ConnectCount { get; private set; }

    public string[] SentMessages
    {
        get
        {
            lock (_gate) return _sentMessages.ToArray();
        }
    }

    public Task ConnectAsync(Uri uri, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(uri);
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            ConnectCount++;
            _state = WebSocketState.Open;
        }
        return Task.CompletedTask;
    }

    public ValueTask SendAsync(
        ReadOnlyMemory<byte> buffer,
        WebSocketMessageType messageType,
        bool endOfMessage,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (messageType != WebSocketMessageType.Text || !endOfMessage)
            throw new InvalidOperationException(
                "Scripted Kiwoom socket accepts complete text messages only.");

        string text = Encoding.UTF8.GetString(buffer.Span);
        lock (_gate)
        {
            if (_state != WebSocketState.Open)
                throw new WebSocketException("Scripted socket is not open.");
            _sentMessages.Add(text);
        }
        return ValueTask.CompletedTask;
    }

    public async ValueTask<ValueWebSocketReceiveResult> ReceiveAsync(
        Memory<byte> buffer,
        CancellationToken cancellationToken)
    {
        InboundFrame frame;
        lock (_gate)
        {
            if (_state != WebSocketState.Open)
            {
                return new ValueWebSocketReceiveResult(
                    0,
                    WebSocketMessageType.Close,
                    true);
            }

            if (_frames.Count > 0)
            {
                frame = _frames.Dequeue();
                if (frame.IsClose)
                    _state = WebSocketState.CloseReceived;
            }
            else
            {
                frame = default;
            }
        }

        if (frame.IsClose)
        {
            return new ValueWebSocketReceiveResult(
                0,
                WebSocketMessageType.Close,
                true);
        }

        if (frame.Text is null)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken)
                .ConfigureAwait(false);
            throw new InvalidOperationException("Unreachable scripted receive state.");
        }

        byte[] bytes = Encoding.UTF8.GetBytes(frame.Text);
        if (bytes.Length > buffer.Length)
            throw new InvalidOperationException(
                $"Scripted frame length {bytes.Length} exceeds buffer {buffer.Length}.");
        bytes.AsSpan().CopyTo(buffer.Span);
        return new ValueWebSocketReceiveResult(
            bytes.Length,
            WebSocketMessageType.Text,
            true);
    }

    public void Dispose()
    {
        lock (_gate)
            _state = WebSocketState.Closed;
    }

    internal readonly record struct InboundFrame(string? Text, bool IsClose)
    {
        public static InboundFrame Message(string text) =>
            new(text ?? throw new ArgumentNullException(nameof(text)), false);

        public static InboundFrame Close => new(null, true);
    }
}
