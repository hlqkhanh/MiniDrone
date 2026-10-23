using System.Globalization;

namespace DroneMotion.Wpf.Models;

public sealed record PidSettings(string Axis, double Kp, double Ki, double Kd)
{
    public static bool TryCreate(string axis, string kp, string ki, string kd, out PidSettings? settings)
    {
        settings = null;
        if (axis is not ("ROLL" or "PITCH" or "ANGLE_ROLL" or "ANGLE_PITCH") || !Parse(kp, 5, out var p) ||
            !Parse(ki, 2, out var i) || !Parse(kd, 0.2, out var d)) return false;
        settings = new(axis, p, i, d);
        return true;
    }
    private static bool Parse(string text, double max, out double value) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) &&
        double.IsFinite(value) && value >= 0 && value <= max;
    public string Command => FormattableString.Invariant($"PID SET {Axis} {Kp:G9} {Ki:G9} {Kd:G9}");
}
