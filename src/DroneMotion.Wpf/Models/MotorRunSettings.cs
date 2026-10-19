using System.Globalization;

namespace DroneMotion.Wpf.Models;

public sealed record MotorRunSettings(int LF, int RF, int LR, int RR, uint DurationMs)
{
    public string Command => FormattableString.Invariant($"MOTOR SET {LF} {RF} {LR} {RR} {DurationMs}");

    public static bool TryCreate(string target, double common, double lf, double rf,
        double lr, double rr, string duration, bool continuous, out MotorRunSettings? settings)
    {
        settings = null;
        uint milliseconds = 0;
        if (!continuous && (!uint.TryParse(duration, NumberStyles.None,
                CultureInfo.InvariantCulture, out milliseconds) || milliseconds == 0)) return false;
        if (!Valid(common) || !Valid(lf) || !Valid(rf) || !Valid(lr) || !Valid(rr)) return false;
        if (target != "CUSTOM")
        {
            lf = target is "LF" or "ALL" ? common : 0;
            rf = target is "RF" or "ALL" ? common : 0;
            lr = target is "LR" or "ALL" ? common : 0;
            rr = target is "RR" or "ALL" ? common : 0;
            if (target is not ("LF" or "RF" or "LR" or "RR" or "ALL")) return false;
        }
        settings = new(Duty(lf), Duty(rf), Duty(lr), Duty(rr), milliseconds);
        return true;
    }

    private static bool Valid(double percent) => double.IsFinite(percent) && percent is >= 0 and <= 100;
    private static int Duty(double percent) => (int)Math.Round(percent * 255 / 100, MidpointRounding.AwayFromZero);
}
