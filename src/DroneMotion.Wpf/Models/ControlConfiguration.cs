using System.Globalization;

namespace DroneMotion.Wpf.Models;

// Read back from the board; never infer these values from editable UI fields.
public sealed record ControlConfiguration(string Build, double BaseDuty, int Axis, double[] Limits)
{
    public const string CsvHeader = "firmwareBuild,acceptedBase,acceptedAxis,rollAngleLimit,rollAngleILimit,pitchAngleLimit,pitchAngleILimit,rollRateLimit,rollRateILimit,pitchRateLimit,pitchRateILimit,motorMin,motorMax";
    public static string UnknownCsv => string.Join(',', Enumerable.Repeat("", 13));
    public string AxisName => Axis switch { 1 => "ROLL", 2 => "PITCH", _ => "BOTH" };
    public string Csv => string.Join(',', new[] { "\"" + Build.Replace("\"", "\"\"") + "\"", BaseDuty.ToString(CultureInfo.InvariantCulture), AxisName }
        .Concat(Limits.Select(v => v.ToString(CultureInfo.InvariantCulture))));
    public bool IncludesAxis(int axis) => Axis == 0 || Axis == axis + 1;

    public static bool TryParse(string[] fields, out ControlConfiguration? configuration)
    {
        configuration = null;
        if (fields.Length != 14 || string.IsNullOrWhiteSpace(fields[1]) ||
            !double.TryParse(fields[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var duty) ||
            !double.IsFinite(duty) || duty < 0 || duty > 180 ||
            !int.TryParse(fields[3], NumberStyles.None, CultureInfo.InvariantCulture, out var axis) || axis < 0 || axis > 2)
            return false;
        var limits = new double[10];
        for (var n = 0; n < limits.Length; n++)
        {
            if (!double.TryParse(fields[n + 4], NumberStyles.Float, CultureInfo.InvariantCulture, out limits[n]) ||
                !double.IsFinite(limits[n]) || limits[n] < 0 || (n < 8 && n % 2 == 0 && limits[n] == 0))
                return false;
        }
        if (limits[9] <= limits[8] || limits[9] > 255) return false;
        configuration = new(fields[1], duty, axis, limits);
        return true;
    }
}

public sealed record ControlConfigurationMessage(ControlConfiguration Configuration) : ProtocolMessage;
