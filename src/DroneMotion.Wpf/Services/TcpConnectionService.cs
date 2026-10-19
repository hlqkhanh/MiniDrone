using System.IO;
using System.Net.Sockets;
using System.Text;
using DroneMotion.Wpf.Models;

namespace DroneMotion.Wpf.Services;

public sealed class TcpConnectionService : IAsyncDisposable
{
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private TcpClient? _client;
    private CancellationTokenSource? _readCancellation;
    private Task? _readTask;

    public event Action<ProtocolMessage>? MessageReceived;
    public event Action<string>? ConnectionLost;
    public bool IsConnected => _client is not null;

    public async Task ConnectAsync(string host, int port, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(host)) throw new ArgumentException("Enter the ESP32 IP address.");
        if (port is < 1 or > 65535) throw new ArgumentOutOfRangeException(nameof(port));
        await DisconnectAsync().ConfigureAwait(false);
        var client = new TcpClient { NoDelay = true };
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        try
        {
            await client.ConnectAsync(host.Trim(), port, timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            client.Dispose();
            throw new TimeoutException("Cannot reach ESP32. Check its IP in Serial Monitor and connect both devices to the same LAN.");
        }
        catch
        {
            client.Dispose();
            throw;
        }

        _client = client;
        _readCancellation = new CancellationTokenSource();
        _readTask = ReadLoopAsync(client, _readCancellation.Token);
    }

    public async Task SendCommandAsync(string command, CancellationToken cancellationToken = default)
    {
        var client = _client ?? throw new InvalidOperationException("Wi-Fi is not connected.");
        var bytes = Encoding.ASCII.GetBytes(command.Trim() + "\n");
        await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await client.GetStream().WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
        }
        finally { _writeLock.Release(); }
    }

    public async Task DisconnectAsync()
    {
        var cancellation = _readCancellation;
        var client = _client;
        var task = _readTask;
        _client = null;
        _readCancellation = null;
        _readTask = null;
        cancellation?.Cancel();
        client?.Dispose();
        if (task is not null) await task.ConfigureAwait(false);
        cancellation?.Dispose();
    }

    private async Task ReadLoopAsync(TcpClient client, CancellationToken cancellationToken)
    {
        // Each connection gets a fresh parser; partial lines cannot cross sessions.
        var parser = new SerialProtocolParser();
        var buffer = new byte[1024];
        try
        {
            var stream = client.GetStream();
            while (!cancellationToken.IsCancellationRequested)
            {
                using var idleTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                idleTimeout.CancelAfter(TimeSpan.FromSeconds(20));
                var count = await stream.ReadAsync(buffer, idleTimeout.Token).ConfigureAwait(false);
                if (count == 0) throw new IOException("ESP32 closed the Wi-Fi connection.");
                foreach (var message in parser.Feed(Encoding.ASCII.GetString(buffer, 0, count)))
                    MessageReceived?.Invoke(message);
            }
        }
        catch (Exception exception) when (exception is IOException or SocketException or OperationCanceledException or ObjectDisposedException)
        {
            if (!cancellationToken.IsCancellationRequested && ReferenceEquals(_client, client))
            {
                _client = null;
                ConnectionLost?.Invoke(exception is OperationCanceledException
                    ? "No data from ESP32 for 20 seconds. Check power and Wi-Fi, then reconnect."
                    : exception.Message);
            }
        }
        finally { client.Dispose(); }
    }

    public async ValueTask DisposeAsync()
    {
        await DisconnectAsync().ConfigureAwait(false);
        _writeLock.Dispose();
    }
}
