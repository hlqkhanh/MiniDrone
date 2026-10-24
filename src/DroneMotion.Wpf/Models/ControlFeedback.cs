using System.Globalization;

namespace DroneMotion.Wpf.Models;

public static class ControlFeedback
{
    public static string CsvHeader => "micros,roll,pitch,rollRate,pitchRate,rollTarget,pitchTarget,rollP,rollI,rollD,pitchP,pitchI,pitchD,LF,RF,LR,RR,saturated,active,rollRateTarget,pitchRateTarget,rollOutput,pitchOutput,rollKp,rollKi,rollKd,pitchKp,pitchKi,pitchKd,angleRollKp,angleRollKi,angleRollKd,anglePitchKp,anglePitchKi,anglePitchKd," + ControlConfiguration.CsvHeader;

    public static string CsvRow(ControlTelemetryMessage sample,
        IReadOnlyDictionary<string, PidSettings> gains, ControlConfiguration? configuration)
    {
        var confirmedGains = new[] { "ROLL", "PITCH", "ANGLE_ROLL", "ANGLE_PITCH" }
            .Select(axis => gains.TryGetValue(axis, out var g)
                ? FormattableString.Invariant($"{g.Kp:G9},{g.Ki:G9},{g.Kd:G9}") : ",,");
        return $"{TelemetryCsv(sample)},{string.Join(',', confirmedGains)},{configuration?.Csv ?? ControlConfiguration.UnknownCsv}";
    }

    public static string TelemetryCsv(ControlTelemetryMessage sample) => sample.Values.Length == 19
        ? sample.Csv + ",,,,"
        : string.Join(',', sample.Csv.Split(',').Take(23));

    private static string Number(double value) => value.ToString("F2", CultureInfo.InvariantCulture);

    // Three-decimal telemetry can round a value onto the limit. This is an
    // indicator of being at the boundary, not proof that the PID was clipped.
    public static string LimitState(double value, double limit) =>
        Math.Abs(value) >= limit - 0.0005 ? "AT LIMIT" : "within limit";

    public static string Describe(ControlTelemetryMessage sample, ControlConfiguration? configuration)
    {
        var v = sample.Values;
        var extended = v.Length >= 23;
        var lines = new List<string>();
        for (var axis = 0; axis < 2; axis++)
        {
            var p = 7 + axis * 3;
            lines.Add(axis == 0 ? "ROLL" : "PITCH");
            lines.Add($"Angle / target: {Number(v[1 + axis])} / {Number(v[5 + axis])} deg");
            lines.Add($"Angle error (target - angle): {Number(v[5 + axis] - v[1 + axis])} deg");
            lines.Add($"Measured rate: {Number(v[3 + axis])} deg/s");
            lines.Add(extended
                ? $"Rate target / error: {Number(v[19 + axis])} / {Number(v[19 + axis] - v[3 + axis])} deg/s"
                : "Rate target / error: unavailable (requires C2)");
            lines.Add($"Inner P / I / D: {Number(v[p])} / {Number(v[p + 1])} / {Number(v[p + 2])} duty");
            lines.Add(extended ? $"Limited output: {Number(v[21 + axis])} duty" : "Limited output: unavailable (requires C2)");
            if (configuration is null) lines.Add("Angle / rate limits: unknown (no board CONFIG)");
            else if (v[18] != 1 || !configuration.IncludesAxis(axis)) lines.Add("Limit indicators: inactive axis / stopped");
            else if (!extended) lines.Add("Limit indicators: unavailable (requires C2)");
            else lines.Add($"Angle: {LimitState(v[19 + axis], configuration.Limits[axis * 2])}; rate: {LimitState(v[21 + axis], configuration.Limits[4 + axis * 2])}");
        }
        lines.Add($"PWM LF / RF / LR / RR: {v[13]} / {v[14]} / {v[15]} / {v[16]}");
        lines.Add(v[17] == 1 ? "Mixer: CLIPPED motor command" : "Mixer: no clipping reported");
        return string.Join('\n', lines);
    }

    public static string DescribeConfiguration(ControlConfiguration? c) => c is null
        ? "Board limits unknown; update firmware for CONFIG readback."
        : $"Build: {c.Build}\nLast accepted START: base {c.BaseDuty}, {c.AxisName} (base 0 = none since boot)\n" +
          $"Angle output / I (deg/s): R +/-{Number(c.Limits[0])} / {Number(c.Limits[1])}; P +/-{Number(c.Limits[2])} / {Number(c.Limits[3])}\n" +
          $"Rate output / I (duty): R +/-{Number(c.Limits[4])} / {Number(c.Limits[5])}; P +/-{Number(c.Limits[6])} / {Number(c.Limits[7])}\n" +
          $"Motor duty range: {c.Limits[8]} to {c.Limits[9]}";

    public static string Freshness(long timestamp, bool active, TimeSpan age) => timestamp == 0
        ? "Control telemetry: waiting / unknown"
        : $"Control telemetry: {(age.TotalSeconds > 2 ? "STALE; current board state unknown" : active ? "RUNNING" : "STOPPED")} | age {age.TotalSeconds:F1} s";
}
