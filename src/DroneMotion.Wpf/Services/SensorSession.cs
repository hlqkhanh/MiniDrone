using DroneMotion.Wpf.Models;

namespace DroneMotion.Wpf.Services;

// Transport/router messages must not erase sensor readiness or calibration progress.
public sealed class SensorSession
{
    public bool IsCalibrating { get; private set; }
    public string Status { get; private set; } = "Waiting for sensor status";
    public string? SensorName { get; private set; }

    public void Reset()
    {
        IsCalibrating = false;
        SensorName = null;
        Status = "Waiting for sensor status";
    }

    public bool Update(ProtocolMessage message)
    {
        switch (message)
        {
            case CalibrationMessage progress:
                IsCalibrating = progress.Reason != "COMPLETE";
                var detail = progress.Reason switch
                {
                    "MOTION" => "motion detected; place board on a stable surface",
                    "ACCEL_NOT_1G" => "acceleration outside 1 g range; check vibration and sensor",
                    "GYRO_OUT_OF_RANGE" => "gyro too high; stop movement and check sensor",
                    "COMPLETE" => "complete; waiting for orientation",
                    _ => "keep board still on a stable surface"
                };
                Status = $"Calibration {progress.Accepted * 100 / progress.Required}% — {detail}";
                return true;
            case StatusMessage { Status: "CALIBRATING" }:
                // Repeated INFO/CAL acknowledgements preserve the latest progress.
                if (!IsCalibrating) Status = "Calibrating — keep board still on a stable surface";
                IsCalibrating = true;
                return true;
            case StatusMessage { Status: "READY" }:
                IsCalibrating = false;
                Status = $"{SensorName ?? "Sensor"} ready";
                return true;
            case StatusMessage { Status: "SENSOR_RECOVERING_KEEP_STILL" }:
                IsCalibrating = false;
                Status = "Recovering sensor — keep board still; connection remains active";
                return true;
            case StatusMessage sensor when sensor.Status is "SENSOR_MPU6500" or "SENSOR_MPU9250" or "SENSOR_MPU6050":
                SensorName = sensor.Status[7..];
                return false;
            case ErrorMessage error when error.Code.StartsWith("MPU_", StringComparison.Ordinal) ||
                                         error.Code.StartsWith("SENSOR_", StringComparison.Ordinal) ||
                                         error.Code.StartsWith("CALIBRATION_", StringComparison.Ordinal):
                IsCalibrating = false;
                Status = error.Code switch
                {
                    "CALIBRATION_TIMEOUT_KEEP_STILL" or "SENSOR_RECOVERY_FAILED_CALIBRATION_KEEP_STILL" =>
                        "Calibration timed out — place board on a stable surface; automatic retry pending",
                    "MPU_NOT_FOUND" or "SENSOR_RECOVERY_FAILED_MPU_NOT_FOUND" =>
                        "Sensor not responding — check 3.3 V power and SPI connections",
                    "MPU_NO_FRESH_DATA_RECOVERING" =>
                        "Sensor identified but no fresh samples — reinitializing sensor",
                    "MPU_ID_UNSTABLE" or "SENSOR_RECOVERY_FAILED_MPU_ID_UNSTABLE" =>
                        "Sensor identity changes between SPI reads — check power and contacts",
                    _ => $"Sensor: {error.Code}"
                };
                return true;
            default: return false;
        }
    }
}
