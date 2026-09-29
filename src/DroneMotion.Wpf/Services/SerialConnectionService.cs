using System.IO.Ports;
using System.Text;
using System.IO;
using DroneMotion.Wpf.Models;

namespace DroneMotion.Wpf.Services;

public sealed class SerialConnectionService : IAsyncDisposable
{
    private readonly SerialProtocolParser _parser = new();
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private SerialPort? _port;
    private CancellationTokenSource? _readCancellation;
    private Task? _readTask;

    public event Action<ProtocolMessage>? MessageReceived;
    public event Action<string>? ConnectionLost;

    public bool IsConnected => _port?.IsOpen == true;

    public static string[] GetAvailablePorts() =>
        SerialPort.GetPortNames().OrderBy(PortSortKey).ToArray();

    public Task ConnectAsync(string portName, int baudRate, CancellationToken cancellationToken = default)
    {
        if (IsConnected)
        {
            throw new InvalidOperationException("A serial connection is already open.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        var port = new SerialPort(portName, baudRate, Parity.None, 8, StopBits.One)
        {
            Encoding = Encoding.ASCII,
            NewLine = "\n",
            ReadTimeout = 1000,
            WriteTimeout = 1000,
            DtrEnable = true,
            RtsEnable = false
        };

        port.Open();
        _parser.Reset();
        _port = port;
        _readCancellation = new CancellationTokenSource();
        _readTask = Task.Run(() => ReadLoop(port, _readCancellation.Token));
        return Task.CompletedTask;
    }

    public async Task SendCommandAsync(string command, CancellationToken cancellationToken = default)
    {
        var port = _port;
        if (port?.IsOpen != true)
        {
            throw new InvalidOperationException("Serial port is not connected.");
        }

        await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!port.IsOpen)
                {
                    throw new InvalidOperationException("Serial port is no longer open.");
                }

                port.Write(command.Trim() + "\n");
            }, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async Task DisconnectAsync()
    {
        var cancellation = _readCancellation;
        var port = _port;
        var readTask = _readTask;

        _readCancellation = null;
        _readTask = null;
        _port = null;

        cancellation?.Cancel();
        if (port is not null)
        {
            try
            {
                port.Close();
            }
            catch
            {
                // The USB device may already be gone.
            }
        }

        if (readTask is not null)
        {
            try
            {
                await readTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
            catch
            {
                // Read-loop failures are reported through ConnectionLost.
            }
        }

        port?.Dispose();
        cancellation?.Dispose();
    }

    private void ReadLoop(SerialPort port, CancellationToken cancellationToken)
    {
        var bytes = new byte[512];
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                int count;
                try
                {
                    // SerialPort.Read is more reliable than BaseStream.ReadAsync
                    // with CH340 Nano clones. The timeout lets us observe cancel.
                    count = port.Read(bytes, 0, bytes.Length);
                }
                catch (TimeoutException)
                {
                    continue;
                }

                if (count <= 0)
                {
                    continue;
                }

                var chunk = Encoding.ASCII.GetString(bytes, 0, count);
                foreach (var message in _parser.Feed(chunk))
                {
                    MessageReceived?.Invoke(message);
                }
            }
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or UnauthorizedAccessException)
        {
            if (!cancellationToken.IsCancellationRequested)
            {
                // Mark the failed port closed before notifying the UI so a new
                // connection can be opened immediately after a USB disconnect.
                if (ReferenceEquals(_port, port))
                {
                    _port = null;
                }
                try
                {
                    port.Close();
                }
                catch
                {
                }
                ConnectionLost?.Invoke(exception.Message);
                port.Dispose();
            }
        }
    }

    private static (string Prefix, int Number, string Original) PortSortKey(string value)
    {
        var split = value.Length;
        while (split > 0 && char.IsDigit(value[split - 1]))
        {
            split--;
        }

        return (value[..split], int.TryParse(value[split..], out var number) ? number : int.MaxValue, value);
    }

    public async ValueTask DisposeAsync()
    {
        await DisconnectAsync().ConfigureAwait(false);
        _writeLock.Dispose();
    }
}
