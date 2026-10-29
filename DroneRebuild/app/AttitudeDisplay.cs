using System.Numerics;

namespace DroneRebuild;

// Display reference only; never sends a level/calibration command to the board.
public sealed class AttitudeDisplay
{
    private double rawYaw, yawOffset;
    public bool HasSample { get; private set; }
    public double Roll { get; private set; }
    public double Pitch { get; private set; }
    public double Yaw => Math.IEEERemainder(rawYaw - yawOffset, 360);

    public bool Update(double roll, double pitch, double yaw)
    {
        if (!double.IsFinite(roll) || !double.IsFinite(pitch) || !double.IsFinite(yaw)) return false;
        Roll = roll; Pitch = pitch; rawYaw = yaw; HasSample = true;
        return true;
    }

    public bool ZeroYaw()
    {
        if (!HasSample) return false;
        yawOffset = rawYaw;
        return true;
    }

    public void Reset()
    {
        Roll = Pitch = rawYaw = yawOffset = 0;
        HasSample = false;
    }

    public Quaternion ModelQuaternion
    {
        get
        {
            const double k = Math.PI / 360;
            var cr = Math.Cos(Roll * k); var sr = Math.Sin(Roll * k);
            var cp = Math.Cos(Pitch * k); var sp = Math.Sin(Pitch * k);
            var cy = Math.Cos(Yaw * k); var sy = Math.Sin(Yaw * k);
            // Body ZYX to the old GLB's Y-up space: X→+Z, Y→+X, Z→+Y.
            return Quaternion.Normalize(new Quaternion(
                (float)(cr * sp * cy + sr * cp * sy),
                (float)(cr * cp * sy - sr * sp * cy),
                (float)(sr * cp * cy - cr * sp * sy),
                (float)(cr * cp * cy + sr * sp * sy)));
        }
    }
}
