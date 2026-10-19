using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using DroneMotion.Wpf.Models;
using DroneMotion.Wpf.Services;

namespace DroneMotion.Tests;

public sealed class TcpConnectionServiceTests
{
    [Fact]
    public async Task StreamsQuaternionAndSendsCalibrationAndPingOverSameConnection()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            await using var connection = new TcpConnectionService();
            var messages = new ConcurrentQueue<ProtocolMessage>();
            var received = new TaskCompletionSource<QuaternionMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
            connection.MessageReceived += message =>
            {
                messages.Enqueue(message);
                if (message is QuaternionMessage q) received.TrySetResult(q);
            };
            await connection.ConnectAsync("127.0.0.1", ((IPEndPoint)listener.LocalEndpoint).Port);
            using var peer = await listener.AcceptTcpClientAsync().WaitAsync(TimeSpan.FromSeconds(3));
            var stream = peer.GetStream();
            await stream.WriteAsync(Encoding.ASCII.GetBytes("HELLO,1,100\nSTATUS,READY\nQ,42,10000,1,0."));
            await stream.WriteAsync(Encoding.ASCII.GetBytes("0,0,0\n"));
            var sample = await received.Task.WaitAsync(TimeSpan.FromSeconds(3));
            Assert.Equal(42u, sample.Sequence);
            Assert.Equal(1f, sample.Orientation.W);
            Assert.Contains(messages, message => message is StatusMessage { Status: "READY" });

            await connection.SendCommandAsync("CAL");
            await connection.SendCommandAsync("PING");
            using var reader = new StreamReader(stream, Encoding.ASCII, false, 1024, true);
            Assert.Equal("CAL", await reader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(3)));
            Assert.Equal("PING", await reader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(3)));
        }
        finally { listener.Stop(); }
    }

    [Fact]
    public async Task PeerDisconnectIsReportedAndReconnectDiscardsIncompleteOldLine()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            await using var connection = new TcpConnectionService();
            var disconnected = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            connection.ConnectionLost += reason => disconnected.TrySetResult(reason);
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            await connection.ConnectAsync("127.0.0.1", port);
            using (var first = await listener.AcceptTcpClientAsync().WaitAsync(TimeSpan.FromSeconds(3)))
                await first.GetStream().WriteAsync(Encoding.ASCII.GetBytes("Q,1,100,1,"));
            await disconnected.Task.WaitAsync(TimeSpan.FromSeconds(3));
            Assert.False(connection.IsConnected);

            var hello = new TaskCompletionSource<HelloMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
            connection.MessageReceived += message => { if (message is HelloMessage h) hello.TrySetResult(h); };
            await connection.ConnectAsync("127.0.0.1", port);
            using var second = await listener.AcceptTcpClientAsync().WaitAsync(TimeSpan.FromSeconds(3));
            await second.GetStream().WriteAsync(Encoding.ASCII.GetBytes("HELLO,1,100\n"));
            Assert.Equal(100, (await hello.Task.WaitAsync(TimeSpan.FromSeconds(3))).SampleRateHz);
            Assert.True(connection.IsConnected);
        }
        finally { listener.Stop(); }
    }

    [Fact]
    public async Task DisconnectCancelsIdleReadWithoutReportingUnexpectedLoss()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            await using var connection = new TcpConnectionService();
            var losses = 0;
            connection.ConnectionLost += _ => Interlocked.Increment(ref losses);
            await connection.ConnectAsync("127.0.0.1", ((IPEndPoint)listener.LocalEndpoint).Port);
            using var peer = await listener.AcceptTcpClientAsync().WaitAsync(TimeSpan.FromSeconds(3));
            await connection.DisconnectAsync().WaitAsync(TimeSpan.FromSeconds(3));
            Assert.False(connection.IsConnected);
            Assert.Equal(0, losses);
        }
        finally { listener.Stop(); }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(65536)]
    public async Task RejectsInvalidPortBeforeConnecting(int port)
    {
        await using var connection = new TcpConnectionService();
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => connection.ConnectAsync("127.0.0.1", port));
        Assert.False(connection.IsConnected);
    }
}
