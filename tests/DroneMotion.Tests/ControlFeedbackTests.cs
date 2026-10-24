using System.Globalization;
using DroneMotion.Wpf.Models;
using DroneMotion.Wpf.Services;

namespace DroneMotion.Tests;

public sealed class ControlFeedbackTests
{
    private const string Configuration = "CONFIG,test-build,90,1,45,10,45,10,40,20,40,20,0,220";

    [Fact]
    public void ConfigurationUsesFirmwareAxisAndReportedLimits()
    {
        var parser = new SerialProtocolParser();
        Assert.Empty(parser.Feed(Configuration[..20]));
        var c = Assert.IsType<ControlConfigurationMessage>(Assert.Single(parser.Feed(Configuration[20..] + "\n"))).Configuration;
        Assert.Equal("ROLL", c.AxisName);
        Assert.True(c.IncludesAxis(0));
        Assert.False(c.IncludesAxis(1));
        Assert.True((c with { Axis = 0 }).IncludesAxis(1));
        Assert.Equal("PITCH", (c with { Axis = 2 }).AxisName);
        Assert.Equal(90, c.BaseDuty);
        Assert.Equal(45, c.Limits[0]);
        Assert.Equal(13, c.Csv.Split(',').Length);
        Assert.Equal(13, ControlConfiguration.UnknownCsv.Split(',').Length);
        Assert.Equal(13, ControlConfiguration.CsvHeader.Split(',').Length);
    }

    [Theory]
    [InlineData("CONFIG,x,90,3,45,10,45,10,40,20,40,20,0,220")]
    [InlineData("CONFIG,x,NaN,1,45,10,45,10,40,20,40,20,0,220")]
    [InlineData("CONFIG,x,90,1,0,10,45,10,40,20,40,20,0,220")]
    [InlineData("CONFIG,x,90,1,45,10,45,10,40,20,40,20,220,0")]
    [InlineData("CONFIG,x,90,1,45,10,45,10,40,20,40,Infinity,0,220")]
    public void MalformedConfigurationIsIgnored(string line) =>
        Assert.Empty(new SerialProtocolParser().Feed(line + "\n"));

    [Fact]
    public void ErrorsUseTargetMinusMeasurementAndLimitsApplyOnlyToActiveAxis()
    {
        var parser = new SerialProtocolParser();
        var configuration = Assert.IsType<ControlConfigurationMessage>(Assert.Single(parser.Feed(Configuration + "\n"))).Configuration;
        var sample = Assert.IsType<ControlTelemetryMessage>(Assert.Single(parser.Feed(
            "C2,10000,10,2,-5,4,0,0,100,0,0,0,0,0,50,130,50,130,0,1,-45,0,-40,0\n")));
        var text = ControlFeedback.Describe(sample, configuration);
        Assert.Contains("Angle error (target - angle): -10.00 deg", text);
        Assert.Contains("Rate target / error: -45.00 / -40.00 deg/s", text);
        Assert.Contains("Limited output: -40.00 duty", text);
        Assert.Contains("Angle: AT LIMIT; rate: AT LIMIT", text);
        Assert.Contains("inactive axis / stopped", text);
        Assert.Contains("no clipping reported", text);
        Assert.Contains("unknown (no board CONFIG)", ControlFeedback.Describe(sample, null));
        Assert.Equal("within limit", ControlFeedback.LimitState(39.99, 40));
        sample.Values[18] = 0;
        Assert.DoesNotContain("AT LIMIT", ControlFeedback.Describe(sample, configuration));
    }

    [Theory]
    [InlineData(19)]
    [InlineData(23)]
    [InlineData(25)]
    public void LegacyAndExtendedCsvKeepTheSameTelemetryColumns(int length)
    {
        var values = Enumerable.Range(0, length).Select(n => (double)n).ToArray();
        var sample = new ControlTelemetryMessage(string.Join(',', values.Select(v => v.ToString(CultureInfo.InvariantCulture))), values);
        var fields = ControlFeedback.TelemetryCsv(sample).Split(',');
        Assert.Equal(23, fields.Length);
        if (length == 19)
        {
            Assert.Equal("", fields[21]);
            Assert.Contains("unavailable (requires C2)", ControlFeedback.Describe(sample, null));
        }
        else Assert.Equal("22", fields[22]);
    }

    [Fact]
    public void StaleOrMissingTelemetryNeverClaimsCurrentRunningState()
    {
        Assert.Contains("waiting / unknown", ControlFeedback.Freshness(0, true, TimeSpan.Zero));
        Assert.Contains("RUNNING", ControlFeedback.Freshness(1, true, TimeSpan.FromMilliseconds(50)));
        Assert.Contains("STOPPED", ControlFeedback.Freshness(1, false, TimeSpan.FromMilliseconds(50)));
        var stale = ControlFeedback.Freshness(1, true, TimeSpan.FromSeconds(3));
        Assert.Contains("STALE", stale);
        Assert.DoesNotContain("RUNNING", stale);
    }

    [Theory]
    [InlineData(19, false)]
    [InlineData(23, true)]
    [InlineData(25, true)]
    public void CsvMetadataUsesBoardConfigurationAndConfirmedGains(int length, bool configured)
    {
        var previousCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("vi-VN");
            var values = new double[length];
            var sample = new ControlTelemetryMessage(string.Join(',', values), values);
            var c = Assert.IsType<ControlConfigurationMessage>(Assert.Single(new SerialProtocolParser().Feed(Configuration + "\n"))).Configuration;
            var gains = new Dictionary<string, PidSettings> { ["ROLL"] = new("ROLL", 1.7, 0.01, 0) };
            var csv = ControlFeedback.CsvRow(sample, gains, configured ? c with { Build = "test,\"build\"" } : null);
            using var parser = new Microsoft.VisualBasic.FileIO.TextFieldParser(new StringReader(csv));
            parser.SetDelimiters(",");
            var fields = parser.ReadFields()!;
            Assert.Equal(ControlFeedback.CsvHeader.Split(',').Length, fields.Length);
            Assert.Equal(48, fields.Length);
            Assert.Equal("1.7", fields[23]);
            Assert.Equal("0.01", fields[24]);
            Assert.Equal("", fields[26]); // Unacknowledged Pitch gains stay blank.
            Assert.Equal(configured ? "test,\"build\"" : "", fields[35]);
            Assert.Equal(configured ? "90" : "", fields[36]);
            Assert.Equal(configured ? "ROLL" : "", fields[37]);
            Assert.Equal(configured ? "220" : "", fields[47]);
        }
        finally { CultureInfo.CurrentCulture = previousCulture; }
    }
}
