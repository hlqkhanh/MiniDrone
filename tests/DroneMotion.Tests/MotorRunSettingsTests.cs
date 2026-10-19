using DroneMotion.Wpf.Models;

namespace DroneMotion.Tests;

public sealed class MotorRunSettingsTests
{
    [Fact]
    public void Custom_MapsIndependentPercentagesToPhysicalDutyRange()
    {
        Assert.True(MotorRunSettings.TryCreate("CUSTOM", 20, 0, 100, 50, 0.4,
            "60000", false, out var settings));
        Assert.Equal("MOTOR SET 0 255 128 1 60000", settings!.Command);
    }

    [Theory]
    [InlineData("LF", "MOTOR SET 255 0 0 0 4294967295")]
    [InlineData("RF", "MOTOR SET 0 255 0 0 4294967295")]
    [InlineData("LR", "MOTOR SET 0 0 255 0 4294967295")]
    [InlineData("RR", "MOTOR SET 0 0 0 255 4294967295")]
    [InlineData("ALL", "MOTOR SET 255 255 255 255 4294967295")]
    public void Targets_SupportFullDutyAndLongDurations(string target, string command)
    {
        Assert.True(MotorRunSettings.TryCreate(target, 100, 0, 0, 0, 0,
            uint.MaxValue.ToString(), false, out var settings));
        Assert.Equal(command, settings!.Command);
    }

    [Theory]
    [InlineData("")]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("1.5")]
    [InlineData("4294967296")]
    [InlineData("abc")]
    public void TimedMode_RejectsInvalidDurationRatherThanUsingOldValue(string duration)
    {
        Assert.False(MotorRunSettings.TryCreate("ALL", 20, 20, 20, 20, 20,
            duration, false, out _));
    }

    [Fact]
    public void ContinuousMode_ExplicitlyEncodesNoDeadline()
    {
        Assert.True(MotorRunSettings.TryCreate("ALL", 20, 20, 20, 20, 20,
            "", true, out var settings));
        Assert.Equal("MOTOR SET 51 51 51 51 0", settings!.Command);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(100.01)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void InvalidDuty_IsRejected(double percent)
    {
        Assert.False(MotorRunSettings.TryCreate("CUSTOM", 20, percent, 0, 0, 0,
            "1000", false, out _));
    }
}
