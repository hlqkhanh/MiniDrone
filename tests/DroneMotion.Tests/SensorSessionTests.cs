using DroneMotion.Wpf.Models;
using DroneMotion.Wpf.Services;

namespace DroneMotion.Tests;

public sealed class SensorSessionTests
{
    [Fact]
    public void FragmentedProgressSurvivesRouterAndInfoMessagesThenBecomesReady()
    {
        var parser = new SerialProtocolParser();
        var session = new SensorSession();
        Assert.Empty(parser.Feed("STATUS,SENSOR_MPU"));
        foreach (var message in parser.Feed("6500\nSTATUS,CALIBRATING\nCAL,250,500,COLLECTING\n")) session.Update(message);
        Assert.True(session.IsCalibrating);
        Assert.Contains("50%", session.Status);
        var progress = session.Status;
        foreach (var message in parser.Feed("ERROR,WIFI_CONNECT_TIMEOUT_CHECK_PORTAL\nPONG\nSTATUS,SPI_SCK18_MOSI23_MISO19_CS5\nSTATUS,CALIBRATING\n"))
            session.Update(message);
        Assert.Equal(progress, session.Status);
        foreach (var message in parser.Feed("CAL,500,500,COMPLETE\nSTATUS,READY\nQ,0,10000,1,0,0,0\n")) session.Update(message);
        Assert.False(session.IsCalibrating);
        Assert.Equal("MPU6500 ready", session.Status);
    }

    [Theory]
    [InlineData("CAL,501,500,COLLECTING\n")]
    [InlineData("CAL,0,0,COLLECTING\n")]
    [InlineData("CAL,-1,500,MOTION\n")]
    [InlineData("CAL,0,500,INVALID\n")]
    public void InvalidProgressIsIgnored(string line) => Assert.Empty(new SerialProtocolParser().Feed(line));

    [Fact]
    public void MotionTimeoutAndRetryRemainDistinctFromMissingSensor()
    {
        var session = new SensorSession();
        session.Update(new CalibrationMessage(1, 500, "MOTION"));
        Assert.Contains("motion detected", session.Status);
        session.Update(new ErrorMessage("CALIBRATION_TIMEOUT_KEEP_STILL"));
        Assert.False(session.IsCalibrating);
        Assert.Contains("timed out", session.Status);
        session.Update(new StatusMessage("CALIBRATING"));
        Assert.True(session.IsCalibrating);
        session.Update(new ErrorMessage("MPU_READ_RECOVERING"));
        Assert.False(session.IsCalibrating);
        session.Reset();
        Assert.Null(session.SensorName);
    }
}
