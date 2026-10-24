using DroneMotion.Wpf.Models;

namespace DroneMotion.Tests;

public sealed class PidPanelReadoutTests
{
    private static ControlConfiguration Configuration => new("test-build", 90, 1,
        [45, 10, 30, 8, 40, 20, 35, 15, 0, 220]);

    private static double[] Values =>
        [10000, 5, -3, -3, 2, 0, 1, -7, 0.1, 0.2, 4, 0.3, 0.4, 97, 83, 97, 83, 0, 1, -10, 8, -6.7, 4.7];

    [Fact]
    public void ReadoutsFollowSelectedAxisAndKeepMotorValuesGlobal()
    {
        var gains = new Dictionary<string, PidSettings>
        {
            ["ROLL"] = new("ROLL", 1, 0.01, 0),
            ["ANGLE_ROLL"] = new("ANGLE_ROLL", 2, 0, 0),
            ["PITCH"] = new("PITCH", 0.7, 0, 0),
            ["ANGLE_PITCH"] = new("ANGLE_PITCH", 2.5, 0, 0)
        };
        var frame = new PidFrame(0, Values, Configuration, gains);
        var roll = PidPanelReadout.Create(frame, "ROLL");
        Assert.Equal(["5.00°", "0.00°", "Error -5.00°"], roll[0].Values);
        Assert.Contains("2 / 0 / 0", roll[0].Detail);
        Assert.Equal(["-3.00 °/s", "-10.00 °/s", "Error -7.00 °/s"], roll[1].Values);
        Assert.Equal(["-7.00", "0.10", "0.20", "-6.70"], roll[2].Values);
        Assert.Contains("1 / 0.01 / 0", roll[2].Detail);
        Assert.Equal(["97.00", "83.00", "97.00", "83.00"], roll[3].Values);
        Assert.Contains("base 90, ROLL", roll[3].Detail);
        Assert.Contains("0–220", roll[3].Detail);
        var pitch = PidPanelReadout.Create(frame, "PITCH");
        Assert.Equal(["-3.00°", "1.00°", "Error 4.00°"], pitch[0].Values);
        Assert.Equal("Error 6.00 °/s", pitch[1].Values[2]);
        Assert.Contains("axis inactive", pitch[1].Detail);
        Assert.Contains("±35", pitch[2].Detail);
        Assert.Contains("0.7 / 0 / 0", pitch[2].Detail);
        Assert.Equal(roll[3], pitch[3] with { Values = roll[3].Values });
    }

    [Fact]
    public void LimitAndClippingWarningsBelongToTheirOwnPanel()
    {
        var v = Values;
        v[19] = -45;
        v[21] = -40;
        var frame = new PidFrame(0, v, Configuration);
        var panels = PidPanelReadout.Create(frame, "ROLL");
        Assert.True(panels[1].Warning);
        Assert.True(panels[2].Warning);
        Assert.False(panels[3].Warning);
        Assert.Contains("AT LIMIT", panels[1].Detail);
        v[17] = 1;
        Assert.True(PidPanelReadout.Create(frame, "ROLL")[3].Warning);
        v[18] = 0;
        panels = PidPanelReadout.Create(frame, "ROLL");
        Assert.False(panels[1].Warning);
        Assert.False(panels[2].Warning);
        Assert.Contains("STOPPED", panels[2].Detail);
    }

    [Fact]
    public void PauseRetainsGainsAndLimitsOfTheDisplayedSample()
    {
        var history = new PidHistory();
        var configuration = Configuration;
        var gains = new Dictionary<string, PidSettings> { ["ROLL"] = new("ROLL", 1, 0, 0) };
        history.Add(new("", Values), configuration, gains);
        var shown = Assert.Single(history.Snapshot());
        history.IsPaused = true;
        configuration.Limits[4] = 80;
        gains["ROLL"] = new("ROLL", 2, 0, 0);
        var next = Values;
        next[0] = 60000;
        next[1] = 20;
        Assert.True(history.Add(new("", next), configuration with { BaseDuty = 120 }, gains));
        Assert.Same(shown, Assert.Single(history.Snapshot()));
        var readouts = PidPanelReadout.Create(shown, "ROLL");
        Assert.Equal("5.00°", readouts[0].Values[0]);
        Assert.Contains("±40", readouts[2].Detail);
        Assert.Contains("1 / 0 / 0", readouts[2].Detail);
        Assert.Contains("base 90", readouts[3].Detail);
        history.IsPaused = false;
        next[0] = 110000;
        history.Add(new("", next), configuration with { BaseDuty = 120 }, gains);
        readouts = PidPanelReadout.Create(history.Snapshot()[^1], "ROLL");
        Assert.Contains("±80", readouts[2].Detail);
        Assert.Contains("2 / 0 / 0", readouts[2].Detail);
        Assert.Contains("base 120", readouts[3].Detail);
    }

    [Fact]
    public void MissingMetadataAndLegacyFieldsAreNeverGuessed()
    {
        var panels = PidPanelReadout.Create(new(0, Values[..19]), "ROLL");
        Assert.Equal("not reported (C2)", panels[1].Values[1]);
        Assert.Equal("not reported (C2)", panels[2].Values[3]);
        Assert.Contains("not received", panels[2].Detail);
        Assert.Contains("unknown", panels[3].Detail);
        Assert.All(panels, p => Assert.False(p.Warning));
        Assert.All(PidPanelReadout.Create(null, "PITCH"), p => Assert.Contains("Waiting", p.Detail));
    }
}
