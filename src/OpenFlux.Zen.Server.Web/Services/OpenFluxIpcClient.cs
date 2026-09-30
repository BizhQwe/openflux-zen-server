using System.Buffers.Binary;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace OpenFlux.Zen.Server.Services;

public sealed class IpcCookiesRequestPayload
{
    [JsonPropertyName("transport")]
    public string Transport { get; set; } = "";

    [JsonPropertyName("url")]
    public string Url { get; set; } = "";

    [JsonPropertyName("reason")]
    public string Reason { get; set; } = "";

    [JsonPropertyName("remote")]
    public bool Remote { get; set; }

    [JsonPropertyName("proxy")]
    public string? Proxy { get; set; }
}

public sealed class IpcCookiesOfferPayload
{
    [JsonPropertyName("transport")]
    public string Transport { get; set; } = "";

    [JsonPropertyName("jar")]
    public Dictionary<string, string> Jar { get; set; } = new();

    [JsonPropertyName("domain")]
    public string? Domain { get; set; }

    [JsonPropertyName("remote")]
    public bool Remote { get; set; }
}

public sealed class IpcStatusPayload
{
    [JsonPropertyName("running")]
    public bool Running { get; set; }

    [JsonPropertyName("connected")]
    public bool Connected { get; set; }

    [JsonPropertyName("bytes_in")]
    public ulong BytesIn { get; set; }

    [JsonPropertyName("bytes_out")]
    public ulong BytesOut { get; set; }

    [JsonPropertyName("uptime_ms")]
    public long UptimeMs { get; set; }

    [JsonPropertyName("active")]
    public string? Active { get; set; }
}

public sealed class OpenFluxIpcClient : IAsyncDisposable
{
    public const byte MsgCookiesRequest = 0x01;
    public const byte MsgCookiesOffer = 0x02;
    public const byte MsgStatus = 0x03;
    public const byte MsgCommand = 0x04;
    public const byte MsgLog = 0x05;

    private readonly ILogger _logger;
    private readonly string _socketPath;
    private Socket? _socket;
    private NetworkStream? _stream;
    private CancellationTokenSource? _cts;
    private Task? _readLoopTask;
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private volatile bool _disposed;

    public event Func<IpcCookiesRequestPayload, Task>? OnCaptchaRequired;
    public event Func<IpcStatusPayload, Task>? OnStatusReceived;
    public event Action? OnDisconnected;

    public bool IsConnected => _socket != null && _socket.Connected && !_disposed;
    public string SocketPath => _socketPath;

    public OpenFluxIpcClient(string socketPath, ILogger logger)
    {
        _socketPath = socketPath;
        _logger = logger;
    }

    /// <summary>
    /// Connects to OpenFlux Unix Domain Socket server with retries.
    /// </summary>
    public async Task<bool> StartAsync(CancellationToken cancellationToken)
    {
        if (!Socket.OSSupportsUnixDomainSockets)
        {
            _logger.LogWarning("Current OS or environment does not support Unix domain sockets for IPC");
            return false;
        }

        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var ct = _cts.Token;

        var timeout = TimeSpan.FromSeconds(6);
        var start = DateTime.UtcNow;

        while (!ct.IsCancellationRequested && (DateTime.UtcNow - start) < timeout)
        {
            if (File.Exists(_socketPath))
            {
                try
                {
                    var endpoint = new UnixDomainSocketEndPoint(_socketPath);
                    var sock = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
                    await sock.ConnectAsync(endpoint, ct);

                    _socket = sock;
                    _stream = new NetworkStream(sock, ownsSocket: false);
                    _logger.LogInformation("Connected to OpenFlux IPC socket at {SocketPath}", _socketPath);

                    _readLoopTask = Task.Run(() => ReadLoopAsync(ct), ct);
                    return true;
                }
                catch (Exception ex)
                {
                    _logger.LogTrace(ex, "IPC socket connection attempt pending at {SocketPath}", _socketPath);
                }
            }

            await Task.Delay(250, ct);
        }

        _logger.LogDebug("Could not connect to OpenFlux IPC socket at {SocketPath} within timeout", _socketPath);
        return false;
    }

    private async Task ReadLoopAsync(CancellationToken ct)
    {
        var lenBuf = new byte[4];

        try
        {
            while (!ct.IsCancellationRequested && _stream != null)
            {
                // 1. Read 4 bytes big-endian length
                await ReadExactAsync(_stream, lenBuf, 0, 4, ct);
                uint length = BinaryPrimitives.ReadUInt32BigEndian(lenBuf);

                if (length == 0 || length > (1 << 20))
                {
                    _logger.LogWarning("IPC received invalid frame length {Length}, closing", length);
                    break;
                }

                // 2. Read type (1 byte) + payload (length - 1 bytes)
                var frameBuf = new byte[length];
                await ReadExactAsync(_stream, frameBuf, 0, (int)length, ct);

                byte msgType = frameBuf[0];
                var payloadBytes = frameBuf.AsMemory(1);

                switch (msgType)
                {
                    case MsgCookiesRequest:
                        try
                        {
                            var req = JsonSerializer.Deserialize<IpcCookiesRequestPayload>(payloadBytes.Span);
                            if (req != null && OnCaptchaRequired != null)
                            {
                                _ = Task.Run(async () =>
                                {
                                    try { await OnCaptchaRequired.Invoke(req); } catch { }
                                });
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Failed to deserialize IPC CookiesRequest payload");
                        }
                        break;

                    case MsgStatus:
                        try
                        {
                            var stat = JsonSerializer.Deserialize<IpcStatusPayload>(payloadBytes.Span);
                            if (stat != null && OnStatusReceived != null)
                            {
                                _ = Task.Run(async () =>
                                {
                                    try { await OnStatusReceived.Invoke(stat); } catch { }
                                });
                            }
                        }
                        catch { }
                        break;

                    default:
                        _logger.LogTrace("IPC received unhandled frame type 0x{Type:X2} (payload: {Bytes} bytes)", msgType, payloadBytes.Length);
                        break;
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            if (!_disposed)
            {
                _logger.LogDebug(ex, "IPC connection closed or failed at {SocketPath}", _socketPath);
            }
        }
        finally
        {
            if (!_disposed)
            {
                try { OnDisconnected?.Invoke(); } catch { }
            }
        }
    }

    private static async Task ReadExactAsync(Stream stream, byte[] buffer, int offset, int count, CancellationToken ct)
    {
        int totalRead = 0;
        while (totalRead < count)
        {
            int read = await stream.ReadAsync(buffer.AsMemory(offset + totalRead, count - totalRead), ct);
            if (read == 0)
            {
                throw new EndOfStreamException("IPC socket closed prematurely");
            }
            totalRead += read;
        }
    }

    /// <summary>
    /// Sends a MsgCookiesOffer frame to OpenFlux to instantly apply cookies to the running transport.
    /// </summary>
    public async Task<bool> SendCookiesOfferAsync(string transport, Dictionary<string, string> jar, bool remote = false)
    {
        if (_disposed || _stream == null || _socket == null || !_socket.Connected)
        {
            return false;
        }

        var offer = new IpcCookiesOfferPayload
        {
            Transport = transport,
            Jar = jar,
            Remote = remote
        };

        var json = JsonSerializer.Serialize(offer);
        var payloadBytes = Encoding.UTF8.GetBytes(json);

        // Frame: [4 bytes length = 1 + payloadLength][1 byte type = 0x02][payload]
        uint length = (uint)(1 + payloadBytes.Length);
        var frame = new byte[4 + 1 + payloadBytes.Length];
        BinaryPrimitives.WriteUInt32BigEndian(frame.AsSpan(0, 4), length);
        frame[4] = MsgCookiesOffer;
        Buffer.BlockCopy(payloadBytes, 0, frame, 5, payloadBytes.Length);

        await _sendLock.WaitAsync();
        try
        {
            if (_stream == null) return false;
            await _stream.WriteAsync(frame);
            await _stream.FlushAsync();
            _logger.LogInformation("Sent MsgCookiesOffer ({Count} cookies for {Transport}) to OpenFlux via IPC", jar.Count, transport);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to send MsgCookiesOffer via IPC to {SocketPath}", _socketPath);
            return false;
        }
        finally
        {
            _sendLock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        try { _cts?.Cancel(); } catch { }
        try { _stream?.Dispose(); } catch { }
        try { _socket?.Dispose(); } catch { }
        _sendLock.Dispose();

        if (_readLoopTask != null)
        {
            try { await _readLoopTask; } catch { }
        }
    }
}
