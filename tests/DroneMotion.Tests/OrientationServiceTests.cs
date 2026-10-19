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
        service.Update(Quaternion.Identity);
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
    public void FirstSampleAndReconnect_StartWithUsbFacingCamera()
    {
        var service = new OrientationService();
        Assert.False(service.Zero());
        Assert.False(service.TryGetDisplayOrientation(out _, out _));
        var initial = Quaternion.CreateFromYawPitchRoll(1.3f, -0.4f, 0.6f);
        service.Update(initial);
        Assert.True(service.TryGetDisplayOrientation(out var display, out var angles));
        Assert.True(Vector3.Distance(Vector3.UnitZ, Vector3.Transform(Vector3.UnitZ, display)) < 1e-5f);
        Assert.True(Vector3.Distance(Vector3.UnitY, Vector3.Transform(Vector3.UnitY, display)) < 1e-5f);
        Assert.Equal(0, angles.Roll, 4);
        Assert.Equal(0, angles.Pitch, 4);
        Assert.Equal(0, angles.Yaw, 4);

        service.Update(initial * Quaternion.CreateFromAxisAngle(Vector3.UnitX, 0.5f));
        service.Reset();
        Assert.False(service.HasSample);
        Assert.False(service.Update(new Quaternion(float.NaN, 0, 0, 1)));
        Assert.False(service.HasSample);
        service.Update(Quaternion.CreateFromYawPitchRoll(-2.1f, 0.3f, -0.8f));
        Assert.True(service.TryGetDisplayOrientation(out display, out _));
        Assert.Equal(1.0f, Math.Abs(display.W), 5);
    }

    [Theory]
    [InlineData(45.0f)]
    [InlineData(-45.0f)]
    public void SensorZRotation_TurnsModelNoseInTheSameDirection(float degrees)
    {
        var service = new OrientationService();
        service.Update(Quaternion.Identity);
        var radians = degrees * MathF.PI / 180.0f;
        var sensorRotation = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, radians);

        Assert.True(service.Update(sensorRotation));
        Assert.True(service.TryGetDisplayOrientation(out var display, out _));

        var nose = Vector3.Transform(Vector3.UnitZ, display);
        Assert.Equal(MathF.Sin(radians), nose.X, 5);
        Assert.Equal(0.0f, nose.Y, 5);
        Assert.Equal(MathF.Cos(radians), nose.Z, 5);
    }

    [Theory]
    [InlineData(45.0f)]
    [InlineData(-45.0f)]
    public void SensorXRotation_LowersUsbEndWithoutBanking(float degrees)
    {
        var service = new OrientationService();
        service.Update(Quaternion.Identity);
        var radians = degrees * MathF.PI / 180.0f;
        var nodUp = Quaternion.CreateFromAxisAngle(Vector3.UnitX, radians);

        Assert.True(service.Update(nodUp));
        Assert.True(service.TryGetDisplayOrientation(out var display, out var angles));

        var nose = Vector3.Transform(Vector3.UnitZ, display);
        Assert.Equal(0.0f, nose.X, 5);
        Assert.Equal(-MathF.Sin(radians), nose.Y, 5);
        Assert.Equal(MathF.Cos(radians), nose.Z, 5);
        var rightEdge = Vector3.Transform(-Vector3.UnitX, display);
        Assert.Equal(0.0f, rightEdge.Y, 5);
        Assert.Equal(0.0, angles.Roll, 3);
        Assert.Equal(-degrees, angles.Pitch, 3);
        Assert.Equal(0.0, angles.Yaw, 3);
    }

    [Theory]
    [InlineData(45.0f)]
    [InlineData(-45.0f)]
    public void SensorYRotation_LiftsRightSideWithoutPitching(float degrees)
    {
        var service = new OrientationService();
        service.Update(Quaternion.Identity);
        var radians = degrees * MathF.PI / 180.0f;
        var bank = Quaternion.CreateFromAxisAngle(Vector3.UnitY, radians);

        Assert.True(service.Update(bank));
        Assert.True(service.TryGetDisplayOrientation(out var display, out var angles));

        var rightEdge = Vector3.Transform(-Vector3.UnitX, display);
        Assert.Equal(-MathF.Cos(radians), rightEdge.X, 5);
        Assert.Equal(MathF.Sin(radians), rightEdge.Y, 5);
        Assert.Equal(0.0f, rightEdge.Z, 5);
        var nose = Vector3.Transform(Vector3.UnitZ, display);
        Assert.Equal(0.0f, nose.Y, 5);
        Assert.Equal(-degrees, angles.Roll, 3);
        Assert.Equal(0.0, angles.Pitch, 3);
        Assert.Equal(0.0, angles.Yaw, 3);
    }

    [Fact]
    public void Zero_FromTiltedPose_PreservesDirectionsOfCombinedRelativeMotion()
    {
        var service = new OrientationService();
        var initial = Quaternion.CreateFromYawPitchRoll(0.3f, -0.4f, 0.2f);
        var motion = Quaternion.CreateFromYawPitchRoll(-0.25f, 0.15f, 0.35f);
        Assert.True(service.Update(initial));
        Assert.True(service.Zero());
        Assert.True(service.Update(initial * motion));
        Assert.True(service.TryGetDisplayOrientation(out var relative, out _));

        // The USB end (+model Z) corresponds to -sensor Y.
        var sensorForward = Vector3.Transform(-Vector3.UnitY, motion);
        var expectedForward = new Vector3(sensorForward.X, sensorForward.Z, -sensorForward.Y);
        var actualForward = Vector3.Transform(Vector3.UnitZ, relative);
        Assert.True(Vector3.Distance(expectedForward, actualForward) < 1e-5f);
        Assert.Equal(1.0f, relative.Length(), 5);
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
