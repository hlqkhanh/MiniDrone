using System.Numerics;

namespace DroneMotion.Wpf.Models;

public abstract record ProtocolMessage;

public sealed record HelloMessage(int ProtocolVersion, int SampleRateHz) : ProtocolMessage;

public sealed record StatusMessage(string Status) : ProtocolMessage;

public sealed record ErrorMessage(string Code) : ProtocolMessage;

public sealed record PongMessage : ProtocolMessage;
public sealed record CalibrationMessage(int Accepted, int Required, string Reason) : ProtocolMessage;
public sealed record PidMessage(PidSettings Settings) : ProtocolMessage;
public sealed record ControlTelemetryMessage(string Csv, double[] Values) : ProtocolMessage;
public sealed record ControlTimingMessage(uint IntervalMicros, uint LatenessMicros,
    uint MaxServiceMicros, uint PreviousSampleWorkMicros) : ProtocolMessage
{
    public string Description => FormattableString.Invariant(
        $"Timing: dt {IntervalMicros / 1000.0:F3} ms · late {LatenessMicros / 1000.0:F3} ms\nIO max {MaxServiceMicros / 1000.0:F3} ms · previous sample {PreviousSampleWorkMicros / 1000.0:F3} ms");
}

public sealed record QuaternionMessage(
    uint Sequence,
    uint TimestampMicros,
    Quaternion Orientation) : ProtocolMessage;

