using DroneMotion.Wpf.Models;

namespace DroneMotion.Tests;

public sealed class PidHistoryTests
{
    [Theory]
    [InlineData(19)]
    [InlineData(23)]
    [InlineData(25)]
    public void SupportsLegacyAndBothExtendedTelemetryLayouts(int length)
    {
        var history = new PidHistory();
        var packet = Packet(10000, length);
        packet.Values[1] = 3;
        Assert.True(history.Add(packet));
        Assert.Equal(length, Assert.Single(history.Snapshot()).Values.Length);
        Assert.Equal(3, history.Snapshot()[0].Values[1]);
        history.Clear();
        Assert.Empty(history.Snapshot());
    }
    private static ControlTelemetryMessage Packet(uint micros, int length=25)
    {
        var values=new double[length];
        values[0]=micros;
        return new(string.Join(',',values),values);
    }

    [Fact]
    public void StoresCopiesAndKeepsThirtySecondBound()
    {
        var history=new PidHistory();
        var packet=Packet(0);
        Assert.True(history.Add(packet));
        packet.Values[1]=99;
        Assert.NotEqual(99,history.Snapshot()[0].Values[1]);
        for(uint n=1;n<=700;n++) Assert.True(history.Add(Packet(n*50_000)));
        var frames=history.Snapshot();
        Assert.InRange(frames.Length,599,600);
        Assert.True(frames[^1].Seconds-frames[0].Seconds<=30.000001);
    }

    [Fact]
    public void HandlesTimestampWrapPauseAndSessionGap()
    {
        var history=new PidHistory();
        history.Add(Packet(uint.MaxValue-10_000));
        history.Add(Packet(40_000));
        Assert.Equal(0.050001,history.Snapshot()[1].Seconds,6);
        history.IsPaused=true;
        history.Add(Packet(90_000));
        Assert.Equal(2,history.Snapshot().Length);
        history.IsPaused=false;
        history.Add(Packet(140_000));
        Assert.Equal(0.150001,history.Snapshot()[^1].Seconds,6);
        history.Add(Packet(40_000_000));
        Assert.Single(history.Snapshot());
        Assert.Equal(0,history.Snapshot()[0].Seconds);
    }

    [Fact]
    public void RejectsMalformedAndDuplicatePackets()
    {
        var history=new PidHistory();
        Assert.False(history.Add(Packet(1,20)));
        var invalid=Packet(1); invalid.Values[2]=double.NaN;
        Assert.False(history.Add(invalid));
        Assert.True(history.Add(Packet(2)));
        Assert.False(history.Add(Packet(2)));
    }
}
