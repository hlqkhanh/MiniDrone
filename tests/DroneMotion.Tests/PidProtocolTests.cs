using System.Globalization;
using DroneMotion.Wpf.Models;
using DroneMotion.Wpf.Services;

namespace DroneMotion.Tests;

public sealed class PidProtocolTests
{
    [Theory]
    [InlineData("ANGLE_ROLL")]
    [InlineData("ANGLE_PITCH")]
    public void OuterLoopGainsRoundTrip(string axis)
    {
        Assert.True(PidSettings.TryCreate(axis, "2", "0.5", "0.007", out var settings));
        Assert.Equal($"PID SET {axis} 2 0.5 0.007", settings!.Command);
        var parsed = Assert.IsType<PidMessage>(Assert.Single(new SerialProtocolParser().Feed($"PID,{axis},2,0.5,0.007\n")));
        Assert.Equal(settings, parsed.Settings);
        Assert.False(PidSettings.TryCreate(axis, "2", "NaN", "0", out _));
    }
    [Theory]
    [InlineData("NaN", "0", "0")]
    [InlineData("-1", "0", "0")]
    [InlineData("1", "3", "0")]
    [InlineData("1", "0", "0.3")]
    [InlineData("1,2", "0", "0")]
    public void InvalidGainsCannotProduceCommands(string p, string i, string d) =>
        Assert.False(PidSettings.TryCreate("ROLL", p, i, d, out _));

    [Fact]
    public void WireFormatIsIndependentOfWindowsCulture()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("vi-VN");
            Assert.True(PidSettings.TryCreate("ROLL", "0.12", "0.04", "0.003", out var settings));
            Assert.Equal("PID SET ROLL 0.12 0.04 0.003", settings!.Command);
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }
    [Fact]
    public void FragmentedAcknowledgementAndControlTelemetryAreParsed()
    {
        var parser = new SerialProtocolParser();
        Assert.Empty(parser.Feed("PID,ROLL,0.12,"));
        var messages = parser.Feed("0.04,0.003\nC,10000,1,2,3,4,0,0,1,2,3,4,5,6,40,41,42,43,0,1\n");
        Assert.Equal(2, messages.Count);
        Assert.Equal(0.003, Assert.IsType<PidMessage>(messages[0]).Settings.Kd);
        var control = Assert.IsType<ControlTelemetryMessage>(messages[1]);
        Assert.Equal(40, control.Values[13]); Assert.Equal(1, control.Values[18]);
        Assert.Empty(parser.Feed("PID,ROLL,NaN,0,0\nC,1,2,3\n"));
    }
}
