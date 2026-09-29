using System.Numerics;
using DroneMotion.Wpf.Services;

namespace DroneMotion.Tests;

public sealed class OrientationServiceTests
{
    [Fact]
    public void Update_RejectsInvalidQuaternion()
    {
        var service = new OrientationService();

        Assert.False(service.Update(new Quaternion(float.NaN, 0, 0, 1)));
        Assert.False(service.Update(new Quaternion(0, 0, 0, 0)));
        Assert.False(service.HasSample);
    }

    [Fact]
    public void Zero_MakesCurrentOrientationIdentity()
    {
        var service = new OrientationService();
        var input = Quaternion.CreateFromYawPitchRoll(0.7f, -0.2f, 0.4f);

        Assert.True(service.Update(input));
        Assert.True(service.Zero());
        Assert.True(service.TryGetDisplayOrientation(out var display, out var euler));

        Assert.Equal(0.0f, display.X, 5);
        Assert.Equal(0.0f, display.Y, 5);
        Assert.Equal(0.0f, display.Z, 5);
        Assert.Equal(1.0f, Math.Abs(display.W), 5);
        Assert.Equal(0.0, euler.Roll, 4);
        Assert.Equal(0.0, euler.Pitch, 4);
        Assert.Equal(0.0, euler.Yaw, 4);
    }

    [Fact]
    public void SensorZRotation_MapsToWpfYRotation()
    {
        var service = new OrientationService();
        var sensorRotation = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.PI / 2.0f);

        Assert.True(service.Update(sensorRotation));
        Assert.True(service.TryGetDisplayOrientation(out var display, out _));

        Assert.Equal(0.0f, display.X, 5);
        Assert.Equal(MathF.Sqrt(0.5f), Math.Abs(display.Y), 5);
        Assert.Equal(0.0f, display.Z, 5);
        Assert.Equal(MathF.Sqrt(0.5f), Math.Abs(display.W), 5);
    }

    [Fact]
    public void SensorYRotation_IsReportedAsPitchForNodding()
    {
        var service = new OrientationService();
        var nodUp = Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 4.0f);

        Assert.True(service.Update(nodUp));
        Assert.True(service.TryGetDisplayOrientation(out var display, out var angles));

        Assert.Equal(MathF.Sin(MathF.PI / 8.0f), Math.Abs(display.X), 5);
        Assert.Equal(0.0, angles.Roll, 3);
        Assert.Equal(45.0, angles.Pitch, 3);
        Assert.Equal(0.0, angles.Yaw, 3);
    }

    [Fact]
    public void SensorXRotation_IsReportedAsRollForBanking()
    {
        var service = new OrientationService();
        var bank = Quaternion.CreateFromAxisAngle(Vector3.UnitX, MathF.PI / 4.0f);

        Assert.True(service.Update(bank));
        Assert.True(service.TryGetDisplayOrientation(out _, out var angles));

        Assert.Equal(-45.0, angles.Roll, 3);
        Assert.Equal(0.0, angles.Pitch, 3);
        Assert.Equal(0.0, angles.Yaw, 3);
    }

    [Fact]
    public void ToEulerDegrees_ReturnsExpectedRoll()
    {
        var rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitX, MathF.PI / 2.0f);

        var angles = OrientationService.ToEulerDegrees(rotation);

        Assert.Equal(90.0, angles.Roll, 3);
        Assert.Equal(0.0, angles.Pitch, 3);
        Assert.Equal(0.0, angles.Yaw, 3);
    }
}
