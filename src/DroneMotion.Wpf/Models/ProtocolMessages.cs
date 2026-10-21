using System.Numerics;

namespace DroneMotion.Wpf.Models;

public abstract record ProtocolMessage;

public sealed record HelloMessage(int ProtocolVersion, int SampleRateHz) : ProtocolMessage;

public sealed record StatusMessage(string Status) : ProtocolMessage;

public sealed record ErrorMessage(string Code) : ProtocolMessage;

public sealed record PongMessage : ProtocolMessage;
public sealed record PidMessage(PidSettings Settings) : ProtocolMessage;
public sealed record ControlTelemetryMessage(string Csv, double[] Values) : ProtocolMessage;

public sealed record QuaternionMessage(
    uint Sequence,
    uint TimestampMicros,
    Quaternion Orientation) : ProtocolMessage;

