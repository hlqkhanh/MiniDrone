using System.Globalization;
using System.Numerics;
using System.Text;
using DroneMotion.Wpf.Models;

namespace DroneMotion.Wpf.Services;

public sealed class SerialProtocolParser
{
    private const int MaximumBufferedCharacters = 4096;
    private readonly StringBuilder _buffer = new();

    public void Reset() => _buffer.Clear();

    public IReadOnlyList<ProtocolMessage> Feed(string chunk)
    {
        if (string.IsNullOrEmpty(chunk))
        {
            return Array.Empty<ProtocolMessage>();
        }

        _buffer.Append(chunk);
        if (_buffer.Length > MaximumBufferedCharacters && !ContainsNewline(_buffer))
        {
            _buffer.Clear();
            return Array.Empty<ProtocolMessage>();
        }

        var messages = new List<ProtocolMessage>();
        while (TryTakeLine(out var line))
        {
            if (TryParseLine(line, out var message))
            {
                messages.Add(message);
            }
        }

        return messages;
    }

    private bool TryTakeLine(out string line)
    {
        for (var index = 0; index < _buffer.Length; index++)
        {
            if (_buffer[index] != '\n')
            {
                continue;
            }

            line = _buffer.ToString(0, index).TrimEnd('\r').Trim();
            _buffer.Remove(0, index + 1);
            return true;
        }

        line = string.Empty;
        return false;
    }

    private static bool TryParseLine(string line, out ProtocolMessage message)
    {
        message = null!;
        if (string.IsNullOrWhiteSpace(line))
        {
            return false;
        }

        var fields = line.Split(',', StringSplitOptions.TrimEntries);
        switch (fields[0])
        {
            case "CAL" when fields.Length == 4 &&
                int.TryParse(fields[1], NumberStyles.None, CultureInfo.InvariantCulture, out var accepted) &&
                int.TryParse(fields[2], NumberStyles.None, CultureInfo.InvariantCulture, out var required) &&
                required > 0 && accepted >= 0 && accepted <= required &&
                fields[3] is "COLLECTING" or "MOTION" or "ACCEL_NOT_1G" or "GYRO_OUT_OF_RANGE" or "COMPLETE":
                message = new CalibrationMessage(accepted, required, fields[3]);
                return true;
            case "PID" when fields.Length == 5 &&
                PidSettings.TryCreate(fields[1], fields[2], fields[3], fields[4], out var settings):
                message = new PidMessage(settings!);
                return true;
            case "C" when fields.Length == 20:
            case "C2" when fields.Length == 26:
                var values = new double[fields.Length-1];
                for (var n = 0; n < values.Length; n++)
                    if (!double.TryParse(fields[n + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out values[n]) ||
                        !double.IsFinite(values[n])) return false;
                message = new ControlTelemetryMessage(string.Join(',', fields.Skip(1)), values);
                return true;
            case "MOTOR_TEST":
                message = new ErrorMessage("WRONG_FIRMWARE_MOTOR_TEST_USE_MAIN_SKETCH");
                return true;
            case "HELLO" when fields.Length == 3 &&
                                   int.TryParse(fields[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var version) &&
                                   int.TryParse(fields[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var rate):
                message = new HelloMessage(version, rate);
                return true;

            case "STATUS" when fields.Length == 2:
                message = new StatusMessage(fields[1]);
                return true;

            case "ERROR" when fields.Length >= 2:
                message = new ErrorMessage(string.Join(',', fields.Skip(1)));
                return true;

            case "PONG" when fields.Length == 1:
                message = new PongMessage();
                return true;

            case "Q" when fields.Length == 7:
                return TryParseQuaternion(fields, out message);

            default:
                return false;
        }
    }

    private static bool TryParseQuaternion(string[] fields, out ProtocolMessage message)
    {
        message = null!;
        if (!uint.TryParse(fields[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var sequence) ||
            !uint.TryParse(fields[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var micros) ||
            !TryParseFinite(fields[3], out var w) ||
            !TryParseFinite(fields[4], out var x) ||
            !TryParseFinite(fields[5], out var y) ||
            !TryParseFinite(fields[6], out var z))
        {
            return false;
        }

        var quaternion = new Quaternion(x, y, z, w);
        var lengthSquared = quaternion.LengthSquared();
        if (!float.IsFinite(lengthSquared) || lengthSquared < 1e-8f)
        {
            return false;
        }

        message = new QuaternionMessage(sequence, micros, Quaternion.Normalize(quaternion));
        return true;
    }

    private static bool TryParseFinite(string value, out float parsed)
    {
        return float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed) &&
               float.IsFinite(parsed);
    }

    private static bool ContainsNewline(StringBuilder builder)
    {
        for (var index = 0; index < builder.Length; index++)
        {
            if (builder[index] == '\n')
            {
                return true;
            }
        }

        return false;
    }
}
