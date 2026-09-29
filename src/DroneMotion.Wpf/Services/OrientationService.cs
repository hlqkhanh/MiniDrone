using System.Numerics;

namespace DroneMotion.Wpf.Services;

public readonly record struct EulerAngles(double Roll, double Pitch, double Yaw);

public sealed class OrientationService
{
    // GY-521 mounting used by the viewer:
    // sensor +Y -> model +X (right)
    // sensor -X -> model -Z (forward)
    // sensor +Z -> model +Y (up)
    // The extra -90 degree rotation around sensor Z corrects the X/Y swap
    // observed when the physical board is mounted with its narrow end forward.
    private static readonly Quaternion SensorToWpfBasis = Quaternion.Normalize(
        Quaternion.CreateFromAxisAngle(Vector3.UnitX, MathF.PI / 2.0f) *
        Quaternion.CreateFromAxisAngle(Vector3.UnitZ, -MathF.PI / 2.0f));

    private readonly object _sync = new();
    private Quaternion _current = Quaternion.Identity;
    private Quaternion _zero = Quaternion.Identity;
    private bool _hasSample;

    public bool HasSample
    {
        get
        {
            lock (_sync)
            {
                return _hasSample;
            }
        }
    }

    public bool Update(Quaternion sensorOrientation)
    {
        if (!IsValid(sensorOrientation))
        {
            return false;
        }

        sensorOrientation = Quaternion.Normalize(sensorOrientation);
        var mapped = SensorToWpfBasis * sensorOrientation * Quaternion.Inverse(SensorToWpfBasis);
        mapped = Quaternion.Normalize(mapped);

        lock (_sync)
        {
            _current = mapped;
            _hasSample = true;
        }

        return true;
    }

    public bool Zero()
    {
        lock (_sync)
        {
            if (!_hasSample)
            {
                return false;
            }

            _zero = _current;
            return true;
        }
    }

    public bool TryGetDisplayOrientation(out Quaternion orientation, out EulerAngles euler)
    {
        lock (_sync)
        {
            if (!_hasSample)
            {
                orientation = Quaternion.Identity;
                euler = default;
                return false;
            }

            orientation = Quaternion.Normalize(Quaternion.Inverse(_zero) * _current);
        }

        // Convert the relative WPF rotation back to sensor axes before naming
        // the angles. With this mounting, sensor Y is the right axis (Pitch),
        // sensor -X is the forward axis (Roll), and sensor Z is up (Yaw).
        var sensorRelative = Quaternion.Normalize(
            Quaternion.Inverse(SensorToWpfBasis) * orientation * SensorToWpfBasis);
        var sensorAngles = ToEulerDegrees(sensorRelative);
        euler = new EulerAngles(
            Roll: -sensorAngles.Roll,
            Pitch: sensorAngles.Pitch,
            Yaw: sensorAngles.Yaw);
        return true;
    }

    public static EulerAngles ToEulerDegrees(Quaternion q)
    {
        q = Quaternion.Normalize(q);

        var sinRollCosPitch = 2.0 * (q.W * q.X + q.Y * q.Z);
        var cosRollCosPitch = 1.0 - 2.0 * (q.X * q.X + q.Y * q.Y);
        var roll = Math.Atan2(sinRollCosPitch, cosRollCosPitch);

        var sinPitch = 2.0 * (q.W * q.Y - q.Z * q.X);
        var pitch = Math.Abs(sinPitch) >= 1.0
            ? Math.CopySign(Math.PI / 2.0, sinPitch)
            : Math.Asin(sinPitch);

        var sinYawCosPitch = 2.0 * (q.W * q.Z + q.X * q.Y);
        var cosYawCosPitch = 1.0 - 2.0 * (q.Y * q.Y + q.Z * q.Z);
        var yaw = Math.Atan2(sinYawCosPitch, cosYawCosPitch);

        const double radiansToDegrees = 180.0 / Math.PI;
        return new EulerAngles(roll * radiansToDegrees, pitch * radiansToDegrees, yaw * radiansToDegrees);
    }

    private static bool IsValid(Quaternion quaternion)
    {
        return float.IsFinite(quaternion.X) &&
               float.IsFinite(quaternion.Y) &&
               float.IsFinite(quaternion.Z) &&
               float.IsFinite(quaternion.W) &&
               quaternion.LengthSquared() >= 1e-8f;
    }
}
