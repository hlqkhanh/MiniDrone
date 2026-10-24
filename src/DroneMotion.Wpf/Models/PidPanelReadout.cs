using System.Globalization;

namespace DroneMotion.Wpf.Models;

public sealed record PidPanelReadout(string[] Values, string Detail, bool Warning = false)
{
    public string CompactDetail { get; init; } = "";
    private static string Number(double value) => value.ToString("0.00", CultureInfo.InvariantCulture);
    private static string Limit(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);

    // Array order matches the four charts and each chart's colored series.
    // Read from the displayed frame, including when the history is paused.
    public static PidPanelReadout[] Create(PidFrame? frame, string axis)
    {
        if (frame is null)
            return [new(["—", "—", "Error —"], "Waiting for an angle sample"),
                new(["—", "—", "Error —"], "Waiting for a rate sample"),
                new(["—", "—", "—", "—"], "Waiting for a PID sample"),
                new(["—", "—", "—", "—"], "Waiting for motor telemetry")];

        var v = frame.Values;
        var selected = axis == "PITCH" ? 1 : 0;
        var p = 7 + selected * 3;
        var extended = v.Length >= 23;
        var c = frame.Configuration;
        var active = v[18] == 1;
        var selectedActive = active && c is not null && c.IncludesAxis(selected);
        var angleLimit = c?.Limits[selected * 2];
        var rateLimit = c?.Limits[4 + selected * 2];
        var atAngleLimit = selectedActive && extended &&
            ControlFeedback.LimitState(v[19 + selected], angleLimit!.Value) == "AT LIMIT";
        var atRateLimit = selectedActive && extended &&
            ControlFeedback.LimitState(v[21 + selected], rateLimit!.Value) == "AT LIMIT";

        string Gains(string name) => frame.Gains?.TryGetValue(name, out var g) == true
            ? FormattableString.Invariant($"{g.Kp:G4} / {g.Ki:G4} / {g.Kd:G4}")
            : "not received";
        string Boundary(bool atLimit) => !active ? "STOPPED"
            : c is null ? "axis / limits unknown (no CONFIG)"
            : !c.IncludesAxis(selected) ? "axis inactive"
            : !extended ? "not reported (requires C2)"
            : atLimit ? "AT LIMIT" : "within limit";

        var angleDetail = $"Outer Kp / Ki / Kd: {Gains(selected == 0 ? "ANGLE_ROLL" : "ANGLE_PITCH")}";
        if (c is not null)
            angleDetail += $"  |  Output ±{Limit(angleLimit!.Value)} °/s; I ±{Limit(c.Limits[selected * 2 + 1])} °/s";
        else angleDetail += "  |  Limits unknown (no CONFIG)";

        var rateDetail = $"Outer rate command: {Boundary(atAngleLimit)}";
        if (angleLimit is double limit) rateDetail += $"  |  Command limit ±{Limit(limit)} °/s";
        var pidDetail = $"Inner Kp / Ki / Kd: {Gains(axis == "PITCH" ? "PITCH" : "ROLL")}";
        if (c is not null)
            pidDetail += $"  |  Output ±{Limit(rateLimit!.Value)}; I ±{Limit(c.Limits[5 + selected * 2])} duty";
        else pidDetail += "  |  Limits unknown (no CONFIG)";
        pidDetail += $"  |  {Boundary(atRateLimit)}";

        var motorDetail = c is null ? "Last START base / control axis / motor limits: unknown (no CONFIG)"
            : $"Last START: base {Limit(c.BaseDuty)}, {c.AxisName}  |  Motor range {Limit(c.Limits[8])}–{Limit(c.Limits[9])} duty";
        motorDetail += v[17] == 1 ? "  |  MIXER CLIPPED" : "  |  Mixer: no clipping reported";

        return [
            new([Number(v[1 + selected]) + "°", Number(v[5 + selected]) + "°",
                "Error " + Number(v[5 + selected] - v[1 + selected]) + "°"], angleDetail)
                { CompactDetail = $"Outer {Gains(selected == 0 ? "ANGLE_ROLL" : "ANGLE_PITCH")}" },
            new([Number(v[3 + selected]) + " °/s",
                extended ? Number(v[19 + selected]) + " °/s" : "not reported (C2)",
                extended ? "Error " + Number(v[19 + selected] - v[3 + selected]) + " °/s" : "Error unavailable"],
                rateDetail, atAngleLimit)
                { CompactDetail = (angleLimit is double a ? $"±{Limit(a)} °/s" : "Limit —") +
                    (atAngleLimit ? " · AT LIMIT" : active && c is not null && !c.IncludesAxis(selected) ? " · INACTIVE" : "") },
            new([Number(v[p]), Number(v[p + 1]), Number(v[p + 2]),
                extended ? Number(v[21 + selected]) : "not reported (C2)"], pidDetail, atRateLimit)
                { CompactDetail = $"Inner {Gains(axis == "PITCH" ? "PITCH" : "ROLL")}" +
                    (rateLimit is double r ? $" · ±{Limit(r)}" : "") + (atRateLimit ? " · AT LIMIT" : "") },
            new([Number(v[13]), Number(v[14]), Number(v[15]), Number(v[16])], motorDetail, v[17] == 1)
                { CompactDetail = (c is null ? "Base —" : $"Base {Limit(c.BaseDuty)} · {c.AxisName} · {Limit(c.Limits[8])}–{Limit(c.Limits[9])}") +
                    (v[17] == 1 ? " · CLIPPED" : "") }
        ];
    }
}
