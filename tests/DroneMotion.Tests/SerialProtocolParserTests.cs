using DroneMotion.Wpf.Models;
using DroneMotion.Wpf.Services;

namespace DroneMotion.Tests;

public sealed class SerialProtocolParserTests
{
    [Fact]
    public void Feed_ParsesFragmentedQuaternionLine()
    {
        var parser = new SerialProtocolParser();

        Assert.Empty(parser.Feed("Q,42,10000,1.0,0."));
        var messages = parser.Feed("0,0.0,0.0\r\n");

        var message = Assert.IsType<QuaternionMessage>(Assert.Single(messages));
        Assert.Equal((uint)42, message.Sequence);
        Assert.Equal((uint)10000, message.TimestampMicros);
        Assert.Equal(1.0f, message.Orientation.W, 5);
    }

    [Fact]
    public void Feed_ParsesMultipleMessagesInOneChunk()
    {
        var parser = new SerialProtocolParser();

        var messages = parser.Feed("HELLO,1,100\nSTATUS,CALIBRATING\nSTATUS,READY\nPONG\n");

        Assert.Collection(messages,
            message => Assert.IsType<HelloMessage>(message),
            message => Assert.Equal("CALIBRATING", Assert.IsType<StatusMessage>(message).Status),
            message => Assert.Equal("READY", Assert.IsType<StatusMessage>(message).Status),
            message => Assert.IsType<PongMessage>(message));
    }

    [Theory]
    [InlineData("noise\n")]
    [InlineData("Q,1,2,NaN,0,0,0\n")]
    [InlineData("Q,1,2,0,0,0,0\n")]
    [InlineData("Q,not-a-number,2,1,0,0,0\n")]
    public void Feed_IgnoresMalformedOrInvalidMessages(string input)
    {
        var parser = new SerialProtocolParser();

        Assert.Empty(parser.Feed(input));
    }

    [Fact]
    public void Feed_NormalizesValidQuaternion()
    {
        var parser = new SerialProtocolParser();

        var message = Assert.IsType<QuaternionMessage>(
            Assert.Single(parser.Feed("Q,1,2,2,0,0,0\n")));

        Assert.Equal(1.0f, message.Orientation.Length(), 5);
    }
}

