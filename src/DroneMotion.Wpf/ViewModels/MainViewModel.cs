using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Diagnostics;
using System.Windows.Input;
using System.Windows.Threading;
using System.Windows.Media.Media3D;
using DroneMotion.Wpf.Models;
using DroneMotion.Wpf.Services;
using NumericsQuaternion = System.Numerics.Quaternion;
using MediaQuaternion = System.Windows.Media.Media3D.Quaternion;

namespace DroneMotion.Wpf.ViewModels;

public sealed class MainViewModel : ObservableObject, IAsyncDisposable
{
    private readonly SerialConnectionService _serial = new();
    private readonly TcpConnectionService _tcp = new();
    private bool _useWifi = true;
    private bool _isConnecting;
    private string _wifiHost = "192.168.4.1";
    private string _wifiMode = "Direct ESP32 Wi-Fi";
    private string _directHost = "192.168.4.1", _routerHost = "";
    private int _wifiPort = 3333;
    private readonly OrientationService _orientation = new();
    private readonly SensorSession _sensorSession = new();
    private readonly SynchronizationContext _uiContext;
    private readonly Stopwatch _rateTimer = Stopwatch.StartNew();
    private string? _selectedPort;
    private int _selectedBaudRate = 115200;
    private string _connectionStatus = "Disconnected";
    private string _deviceStatus = "Waiting for connection";
    private MediaQuaternion _displayQuaternion = MediaQuaternion.Identity;
    private double _roll;
    private double _pitch;
    private double _yaw;
    private double _sampleRate;
    private double _framesPerSecond;
    private long _samplesInWindow;
    private int _framesInWindow;
    private bool _isConnected;
    private bool _hasOrientation;
    private bool _motorsAvailable;
    private bool _propellersRemoved;
    private string _selectedMotor = "LF";
    private double _motorPercent = 20;
    private string _motorDuration = "1000";
    private double _motorLF = 20, _motorRF = 20, _motorLR = 20, _motorRR = 20;
    private bool _continuousMotors, _advancedMotors, _motorRunning, _heartbeatBusy;
    private readonly DispatcherTimer _motorHeartbeat;
    private long _lastOrientationTimestamp;
    private string _telemetryStatus = "No orientation data";
    private string _motorStatus = "Requires updated ESP32 firmware";
    private int _motorRequestVersion;
    private bool _controlAvailable, _controlRunning, _controlArmed, _rigConfirmed, _stopPending;
    private string _rollKp = "0", _rollKi = "0", _rollKd = "0";
    private string _pitchKp = "0", _pitchKi = "0", _pitchKd = "0";
    private string _baseDuty = "40", _rollTarget = "0", _pitchTarget = "0";
    private string _controlStatus = "Requires CONTROL_V1 firmware", _controlData = "No control data";
    private string _appliedPid = "Board gains not received", _controlAxis = "ROLL";
    private StreamWriter? _controlLog;
    private string _logStatus = "CSV logging off";

    public MainViewModel()
    {
        _uiContext = SynchronizationContext.Current ?? new SynchronizationContext();
        _serial.MessageReceived += OnMessageReceived;
        _serial.ConnectionLost += OnConnectionLost;
        _tcp.MessageReceived += OnMessageReceived;
        _tcp.ConnectionLost += OnConnectionLost;

        RefreshPortsCommand = new RelayCommand(RefreshPorts);
        ConnectCommand = new AsyncRelayCommand(ConnectAsync,
            () => !IsConnected && !_isConnecting && (UseWifi
                ? !string.IsNullOrWhiteSpace(WifiHost) && WifiPort is >= 1 and <= 65535
                : !string.IsNullOrWhiteSpace(SelectedPort)), ShowError);
        DisconnectCommand = new AsyncRelayCommand(DisconnectAsync, () => IsConnected, ShowError);
        CalibrateCommand = new AsyncRelayCommand(CalibrateAsync, () => IsConnected && !_sensorSession.IsCalibrating, ShowError);
        ZeroCommand = new RelayCommand(ZeroOrientation, () => HasOrientation);
        RunMotorCommand = new AsyncRelayCommand(RunMotorAsync,
            () => IsConnected && !_sensorSession.IsCalibrating && !_stopPending && !_controlBusy && !_controlRunning && !_controlArmed && _motorsAvailable && _advancedMotors && PropellersRemoved && TryMotorSettings(out _), ShowError);
        StopMotorsCommand = new AsyncRelayCommand(StopMotorsAsync,
            () => IsConnected, ShowError);
        CopyMotorPwmCommand = new RelayCommand(() =>
        {
            MotorLF = MotorRF = MotorLR = MotorRR = MotorPercent;
            SelectedMotor = "CUSTOM";
        });
        ApplyPidCommand = new AsyncRelayCommand(ApplyPidAsync,
            () => ControlEditable && TryPids(out _, out _), ShowError);
        ArmControlCommand = new AsyncRelayCommand(ArmControlAsync,
            () => ControlEditable && !_sensorSession.IsCalibrating && RigConfirmed && SelectedGainsReady() && ValidControlInputs(out _, out _, out _), ShowError);
        StartControlCommand = new AsyncRelayCommand(StartControlAsync,
            () => IsConnected && !_stopPending && !_controlBusy && _controlArmed && RigConfirmed && ValidControlInputs(out _, out _, out _), ShowError);
        ZeroControlCommand = new AsyncRelayCommand(async () =>
        {
            if (!ControlEditable) return;
            SetControlBusy(true);
            try { await RequestControlAsync("CONTROL ZERO", m => m is StatusMessage s && s.Status == "CONTROL_ZEROED"); }
            finally { SetControlBusy(false); }
        }, () => ControlEditable, ShowError);
        ToggleControlLogCommand = new RelayCommand(ToggleControlLog);
        _motorHeartbeat = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _motorHeartbeat.Tick += MotorHeartbeatOnTick;
        _motorHeartbeat.Start();

        RefreshPorts();
    }

    public ObservableCollection<string> AvailablePorts { get; } = new();

    public IReadOnlyList<int> BaudRates { get; } = [115200, 57600, 38400, 19200, 9600];

    public bool CanEditConnection => !IsConnected && !_isConnecting;
    public IReadOnlyList<string> WifiModes { get; } = ["Direct ESP32 Wi-Fi", "Router / hotspot"];
    public string WifiMode
    {
        get => _wifiMode;
        set
        {
            if (!CanEditConnection || !WifiModes.Contains(value) || value == _wifiMode) return;
            if (_wifiMode == WifiModes[0]) _directHost = WifiHost; else _routerHost = WifiHost;
            SetProperty(ref _wifiMode, value);
            WifiHost = value == WifiModes[0] ? _directHost : _routerHost;
            OnPropertyChanged(nameof(WifiInstructions));
        }
    }
    public string WifiInstructions => WifiMode == WifiModes[0]
        ? "Connect laptop to DroneMotion_Setup-XXXX (password: drone12345). Keep connected without Internet. Then Connect to 192.168.4.1:3333."
        : "Connect laptop and ESP32 to the same router or 2.4 GHz hotspot. Enter the ESP32 IP from the setup page or Serial Monitor.";
    public bool UseWifi
    {
        get => _useWifi;
        set
        {
            if (!CanEditConnection) return;
            if (SetProperty(ref _useWifi, value)) CommandManager.InvalidateRequerySuggested();
        }
    }
    public string WifiHost
    {
        get => _wifiHost;
        set { if (SetProperty(ref _wifiHost, value)) CommandManager.InvalidateRequerySuggested(); }
    }
    public int WifiPort
    {
        get => _wifiPort;
        set { if (SetProperty(ref _wifiPort, value)) CommandManager.InvalidateRequerySuggested(); }
    }

    public string? SelectedPort
    {
        get => _selectedPort;
        set
        {
            if (SetProperty(ref _selectedPort, value))
            {
                CommandManager.InvalidateRequerySuggested();
            }
        }
    }

    public int SelectedBaudRate
    {
        get => _selectedBaudRate;
        set => SetProperty(ref _selectedBaudRate, value);
    }

    public string ConnectionStatus
    {
        get => _connectionStatus;
        private set => SetProperty(ref _connectionStatus, value);
    }

    public string DeviceStatus
    {
        get => _deviceStatus;
        private set => SetProperty(ref _deviceStatus, value);
    }

    public bool IsConnected
    {
        get => _isConnected;
        private set
        {
            if (SetProperty(ref _isConnected, value))
            {
                OnPropertyChanged(nameof(CanEditConnection));
                CommandManager.InvalidateRequerySuggested();
            }
        }
    }

    public bool HasOrientation
    {
        get => _hasOrientation;
        private set
        {
            if (SetProperty(ref _hasOrientation, value))
            {
                CommandManager.InvalidateRequerySuggested();
            }
        }
    }

    public MediaQuaternion DisplayQuaternion
    {
        get => _displayQuaternion;
        private set => SetProperty(ref _displayQuaternion, value);
    }

    public double Roll { get => _roll; private set => SetProperty(ref _roll, value); }
    public double Pitch { get => _pitch; private set => SetProperty(ref _pitch, value); }
    public double Yaw { get => _yaw; private set => SetProperty(ref _yaw, value); }
    public double SampleRate { get => _sampleRate; private set => SetProperty(ref _sampleRate, value); }
    public double FramesPerSecond { get => _framesPerSecond; private set => SetProperty(ref _framesPerSecond, value); }

    public ICommand RefreshPortsCommand { get; }
    public ICommand ConnectCommand { get; }
    public ICommand DisconnectCommand { get; }
    public ICommand CalibrateCommand { get; }
    public ICommand ZeroCommand { get; }
    public ICommand RunMotorCommand { get; }
    public ICommand StopMotorsCommand { get; }
    public ICommand CopyMotorPwmCommand { get; }
    public ICommand ApplyPidCommand { get; }
    public ICommand ArmControlCommand { get; }
    public ICommand StartControlCommand { get; }
    public ICommand ZeroControlCommand { get; }
    public ICommand ToggleControlLogCommand { get; }
    public bool ControlEditable => IsConnected && _controlAvailable && !_stopPending && !_controlBusy && !_controlRunning && !_controlArmed && !_motorRunning;
    public bool RigConfirmed
    {
        get => _rigConfirmed; set
        {
            if (SetProperty(ref _rigConfirmed, value))
            {
                if (!value && (_controlRunning || _controlArmed)) StopMotorsCommand.Execute(null);
                CommandManager.InvalidateRequerySuggested();
            }
        }
    }
    public string RollKp { get => _rollKp; set { SetProperty(ref _rollKp, value); CommandManager.InvalidateRequerySuggested(); } }
    public string RollKi { get => _rollKi; set { SetProperty(ref _rollKi, value); CommandManager.InvalidateRequerySuggested(); } }
    public string RollKd { get => _rollKd; set { SetProperty(ref _rollKd, value); CommandManager.InvalidateRequerySuggested(); } }
    public string PitchKp { get => _pitchKp; set { SetProperty(ref _pitchKp, value); CommandManager.InvalidateRequerySuggested(); } }
    public string PitchKi { get => _pitchKi; set { SetProperty(ref _pitchKi, value); CommandManager.InvalidateRequerySuggested(); } }
    public string PitchKd { get => _pitchKd; set { SetProperty(ref _pitchKd, value); CommandManager.InvalidateRequerySuggested(); } }
    public string BaseDuty { get => _baseDuty; set { SetProperty(ref _baseDuty, value); CommandManager.InvalidateRequerySuggested(); } }
    public string RollTarget { get => _rollTarget; set { SetProperty(ref _rollTarget, value); CommandManager.InvalidateRequerySuggested(); } }
    public string PitchTarget { get => _pitchTarget; set { SetProperty(ref _pitchTarget, value); CommandManager.InvalidateRequerySuggested(); } }
    public IReadOnlyList<string> ControlAxes { get; } = ["ROLL", "PITCH", "BOTH"];
    public string ControlAxis { get => _controlAxis; set { SetProperty(ref _controlAxis, value); CommandManager.InvalidateRequerySuggested(); } }
    public string ControlStatus { get => _controlStatus; private set => SetProperty(ref _controlStatus, value); }
    public string ControlData { get => _controlData; private set => SetProperty(ref _controlData, value); }
    public string AppliedPid { get => _appliedPid; private set => SetProperty(ref _appliedPid, value); }
    public string LogStatus { get => _logStatus; private set => SetProperty(ref _logStatus, value); }
    private readonly Dictionary<string, PidSettings> _boardPids = new();
    private bool _controlBusy;
    private TaskCompletionSource<ProtocolMessage>? _controlReply;
    private Func<ProtocolMessage, bool>? _controlReplyMatches;
    private async Task RequestControlAsync(string command, Func<ProtocolMessage, bool> matches)
    {
        var reply = new TaskCompletionSource<ProtocolMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        _controlReply = reply; _controlReplyMatches = matches;
        try
        {
            await SendCommandAsync(command);
            await reply.Task.WaitAsync(TimeSpan.FromSeconds(3));
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            try { if (IsConnected) await StopMotorsAsync(); } catch { /* Firmware watchdog remains active. */ }
            throw;
        }
        finally { if (_controlReply == reply) { _controlReply = null; _controlReplyMatches = null; } }
    }
    private void SetControlBusy(bool busy)
    {
        _controlBusy = busy; OnPropertyChanged(nameof(ControlEditable)); CommandManager.InvalidateRequerySuggested();
    }
    private bool TryPids(out PidSettings? roll, out PidSettings? pitch)
    {
        var r = PidSettings.TryCreate("ROLL", RollKp, RollKi, RollKd, out roll);
        var p = PidSettings.TryCreate("PITCH", PitchKp, PitchKi, PitchKd, out pitch);
        return r && p;
    }
    private bool SelectedGainsReady() =>
        (ControlAxis == "PITCH" || _boardPids.TryGetValue("ROLL", out var roll) && roll.Kp > 0) &&
        (ControlAxis == "ROLL" || _boardPids.TryGetValue("PITCH", out var pitch) && pitch.Kp > 0);
    private bool ValidControlInputs(out double duty, out double roll, out double pitch)
    {
        var a = double.TryParse(BaseDuty, NumberStyles.Float, CultureInfo.InvariantCulture, out duty);
        var b = double.TryParse(RollTarget, NumberStyles.Float, CultureInfo.InvariantCulture, out roll);
        var c = double.TryParse(PitchTarget, NumberStyles.Float, CultureInfo.InvariantCulture, out pitch);
        return a && b && c && double.IsFinite(duty) && duty is >= 1 and <= 180 &&
            double.IsFinite(roll) && Math.Abs(roll) <= 10 && double.IsFinite(pitch) && Math.Abs(pitch) <= 10;
    }
    private async Task ApplyPidAsync()
    {
        if (!ControlEditable || !TryPids(out var roll, out var pitch)) return;
        var version = ++_motorRequestVersion;
        SetControlBusy(true);
        try
        {
            ControlStatus = "Sending gains; waiting for board acknowledgement";
            await RequestControlAsync(roll!.Command, m => m is PidMessage p && SameGains(p.Settings, roll));
            if (version == _motorRequestVersion && IsConnected)
                await RequestControlAsync(pitch!.Command, m => m is PidMessage p && SameGains(p.Settings, pitch));
        }
        finally { SetControlBusy(false); }
    }
    private static bool SameGains(PidSettings actual, PidSettings expected) => actual.Axis == expected.Axis &&
        Math.Abs(actual.Kp - expected.Kp) <= 0.000001 && Math.Abs(actual.Ki - expected.Ki) <= 0.000001 && Math.Abs(actual.Kd - expected.Kd) <= 0.000001;
    private async Task StartControlAsync()
    {
        if (!IsConnected || _stopPending || _controlBusy || !_controlArmed || !RigConfirmed || !ValidControlInputs(out var duty, out _, out _)) return;
        SetControlBusy(true);
        try
        {
            await RequestControlAsync(FormattableString.Invariant($"CONTROL START {duty:G9} {(ControlAxis == "ROLL" ? 1 : ControlAxis == "PITCH" ? 2 : 0)}"),
                m => m is StatusMessage s && s.Status == "CONTROL_RUNNING");
        }
        finally { SetControlBusy(false); }
    }
    private async Task ArmControlAsync()
    {
        if (!ControlEditable || _sensorSession.IsCalibrating || !RigConfirmed || !SelectedGainsReady() || !ValidControlInputs(out _, out var roll, out var pitch)) return;
        var version = ++_motorRequestVersion;
        SetControlBusy(true);
        try
        {
            await RequestControlAsync(FormattableString.Invariant($"CONTROL SETPOINT {roll:G9} {pitch:G9}"),
                m => m is StatusMessage s && s.Status == "CONTROL_SETPOINT_OK");
            if (version != _motorRequestVersion || !IsConnected || !RigConfirmed) return;
            await RequestControlAsync("CONTROL ARM", m => m is StatusMessage s && s.Status == "CONTROL_ARMED");
        }
        finally { SetControlBusy(false); }
    }
    private void ToggleControlLog()
    {
        if (_controlLog is not null) { _controlLog.Dispose(); _controlLog = null; LogStatus = "CSV logging off"; return; }
        try
        {
            var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "DroneMotionLogs");
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, $"pid-{DateTime.Now:yyyyMMdd-HHmmss-fff}.csv");
            _controlLog = new StreamWriter(path) { AutoFlush = true };
            _controlLog.WriteLine("micros,roll,pitch,rollRate,pitchRate,rollTarget,pitchTarget,rollP,rollI,rollD,pitchP,pitchI,pitchD,LF,RF,LR,RR,saturated,active,rollKp,rollKi,rollKd,pitchKp,pitchKi,pitchKd");
            LogStatus = path;
        }
        catch (Exception e) { ShowError(e); }
    }
    private string CsvGains(string axis) => _boardPids.TryGetValue(axis, out var g)
        ? FormattableString.Invariant($"{g.Kp:G9},{g.Ki:G9},{g.Kd:G9}") : ",,";
    public IReadOnlyList<string> MotorTargets { get; } = ["LF", "RF", "LR", "RR", "ALL", "CUSTOM"];
    public string SelectedMotor
    {
        get => _selectedMotor;
        set { if (SetProperty(ref _selectedMotor, value)) MotorSettingsChanged(); }
    }
    public bool PropellersRemoved
    {
        get => _propellersRemoved;
        set
        {
            if (SetProperty(ref _propellersRemoved, value))
            {
                if (!value)
                {
                    ++_motorRequestVersion;
                    if (IsConnected) StopMotorsCommand.Execute(null);
                }
                CommandManager.InvalidateRequerySuggested();
            }
        }
    }
    public double MotorPercent
    {
        get => _motorPercent;
        set { if (SetProperty(ref _motorPercent, value)) MotorSettingsChanged(); }
    }
    public string MotorDuration
    {
        get => _motorDuration;
        set { if (SetProperty(ref _motorDuration, value)) MotorSettingsChanged(); }
    }
    public double MotorLF { get => _motorLF; set { if (SetProperty(ref _motorLF, value)) CustomMotorChanged(); } }
    public double MotorRF { get => _motorRF; set { if (SetProperty(ref _motorRF, value)) CustomMotorChanged(); } }
    public double MotorLR { get => _motorLR; set { if (SetProperty(ref _motorLR, value)) CustomMotorChanged(); } }
    public double MotorRR { get => _motorRR; set { if (SetProperty(ref _motorRR, value)) CustomMotorChanged(); } }
    public bool ContinuousMotors
    {
        get => _continuousMotors;
        set { if (SetProperty(ref _continuousMotors, value)) MotorSettingsChanged(); }
    }
    public string TelemetryStatus { get => _telemetryStatus; private set => SetProperty(ref _telemetryStatus, value); }
    public string MotorPreview => TryMotorSettings(out var settings)
        ? $"Output (0–255): LF {settings!.LF}, RF {settings.RF}, LR {settings.LR}, RR {settings.RR}. " +
          (settings.DurationMs == 0 ? "Continuous until STOP or link timeout." : $"Run {settings.DurationMs} ms.")
        : "Enter PWM 0–100% and duration 1–4294967295 ms, or select continuous.";
    private bool TryMotorSettings(out MotorRunSettings? settings) => MotorRunSettings.TryCreate(
        SelectedMotor, MotorPercent, MotorLF, MotorRF, MotorLR, MotorRR, MotorDuration, ContinuousMotors, out settings);
    private void CustomMotorChanged() { SelectedMotor = "CUSTOM"; MotorSettingsChanged(); }
    private void MotorSettingsChanged()
    {
        OnPropertyChanged(nameof(MotorPreview));
        CommandManager.InvalidateRequerySuggested();
    }
    public string MotorStatus { get => _motorStatus; private set => SetProperty(ref _motorStatus, value); }

    private async Task RunMotorAsync()
    {
        // Validate again at execution; each click arms exactly one bounded test.
        if (!IsConnected || _sensorSession.IsCalibrating || _stopPending || _controlBusy || _controlRunning || _controlArmed || !_motorsAvailable || !_advancedMotors || !PropellersRemoved ||
            !TryMotorSettings(out var settings)) return;
        var command = settings!.Command;
        var version = ++_motorRequestVersion;
        MotorStatus = "Requesting motor test...";
        await SendCommandAsync("MOTOR ARM");
        if (version != _motorRequestVersion || !IsConnected || !PropellersRemoved || _controlArmed || _controlRunning) return;
        await SendCommandAsync(command);
    }

    private Task StopMotorsAsync()
    {
        _stopPending = true;
        _controlReply?.TrySetCanceled();
        ++_motorRequestVersion;
        _motorRunning = false;
        _controlRunning = _controlArmed = false;
        OnPropertyChanged(nameof(ControlEditable));
        return SendCommandAsync("MOTOR STOP");
    }

    private async void MotorHeartbeatOnTick(object? sender, EventArgs e)
    {
        if (!IsConnected || (!_motorRunning && !_controlRunning) || _heartbeatBusy) return;
        _heartbeatBusy = true;
        try { await SendCommandAsync("MOTOR KEEPALIVE"); }
        catch (Exception exception)
        {
            _motorRunning = false;
            _controlRunning = _controlArmed = false;
            MotorStatus = $"Heartbeat failed; board will stop: {exception.Message}";
        }
        finally { _heartbeatBusy = false; }
    }

    private void ResetMotorControls()
    {
        _stopPending = false;
        _controlAvailable = _controlRunning = _controlArmed = false;
        _boardPids.Clear(); AppliedPid = "Board gains not received";
        RigConfirmed = false; ControlStatus = "Requires CONTROL_V1 firmware";
        OnPropertyChanged(nameof(ControlEditable));
        _motorsAvailable = false;
        _advancedMotors = _motorRunning = false;
        ++_motorRequestVersion;
        PropellersRemoved = false;
        MotorStatus = "Requires updated ESP32 firmware";
        CommandManager.InvalidateRequerySuggested();
    }

    public void OnRenderFrame()
    {
        var timestamp = Interlocked.Read(ref _lastOrientationTimestamp);
        TelemetryStatus = !IsConnected ? "Disconnected" : timestamp == 0
            ? _sensorSession.Status
            : _sensorSession.IsCalibrating ? _sensorSession.Status
            : Stopwatch.GetElapsedTime(timestamp).TotalSeconds > 2
                ? $"Orientation paused — {_sensorSession.Status}"
                : "Receiving live orientation";
        _framesInWindow++;
        if (_orientation.TryGetDisplayOrientation(out NumericsQuaternion orientation, out var euler))
        {
            DisplayQuaternion = new MediaQuaternion(orientation.X, orientation.Y, orientation.Z, orientation.W);
            Roll = euler.Roll;
            Pitch = euler.Pitch;
            Yaw = euler.Yaw;
            HasOrientation = true;
        }

        var elapsed = _rateTimer.Elapsed.TotalSeconds;
        if (elapsed < 1.0)
        {
            return;
        }

        SampleRate = Interlocked.Exchange(ref _samplesInWindow, 0) / elapsed;
        FramesPerSecond = _framesInWindow / elapsed;
        _framesInWindow = 0;
        _rateTimer.Restart();
    }

    private void RefreshPorts()
    {
        var previous = SelectedPort;
        var ports = SerialConnectionService.GetAvailablePorts();
        AvailablePorts.Clear();
        foreach (var port in ports)
        {
            AvailablePorts.Add(port);
        }

        SelectedPort = previous is not null && AvailablePorts.Contains(previous)
            ? previous
            : AvailablePorts.FirstOrDefault();
        if (!UseWifi && AvailablePorts.Count == 0) DeviceStatus = "No COM ports found";
    }

    private async Task ConnectAsync()
    {
        _isConnecting = true;
        OnPropertyChanged(nameof(CanEditConnection));
        try
        {
            var endpoint = UseWifi ? $"{WifiHost}:{WifiPort}" : SelectedPort;
            ResetMotorControls();
            _sensorSession.Reset();
            Interlocked.Exchange(ref _lastOrientationTimestamp, 0);
            _orientation.Reset();
            DisplayQuaternion = MediaQuaternion.Identity;
            Roll = Pitch = Yaw = 0;
            HasOrientation = false;
            ConnectionStatus = $"Opening {endpoint}...";
            if (UseWifi) await _tcp.ConnectAsync(WifiHost, WifiPort);
            else if (SelectedPort is not null) await _serial.ConnectAsync(SelectedPort, SelectedBaudRate);
            else return;
            IsConnected = UseWifi ? _tcp.IsConnected : _serial.IsConnected;
            ConnectionStatus = IsConnected ? $"Connected to {endpoint}" : "Disconnected";
            if (!IsConnected) return;
            DeviceStatus = _sensorSession.Status;
            if (!UseWifi) await Task.Delay(2000); // USB may reset the board.
            if (IsConnected) { await SendCommandAsync("PING"); await SendCommandAsync("INFO"); }
        }
        finally
        {
            _isConnecting = false;
            OnPropertyChanged(nameof(CanEditConnection));
            CommandManager.InvalidateRequerySuggested();
        }
    }

    private async Task DisconnectAsync()
    {
        try { if (IsConnected) await StopMotorsAsync(); }
        catch { /* The board's timer still bounds the run if transport is gone. */ }
        ResetMotorControls();
        await _serial.DisconnectAsync();
        await _tcp.DisconnectAsync();
        IsConnected = false;
        ConnectionStatus = "Disconnected";
        DeviceStatus = "Waiting for connection";
    }

    private async Task CalibrateAsync()
    {
        await StopMotorsAsync();
        _sensorSession.Update(new StatusMessage("CALIBRATING"));
        await SendCommandAsync("CAL");
        DeviceStatus = _sensorSession.Status;
    }

    private void ZeroOrientation()
    {
        if (_orientation.Zero())
        {
            DeviceStatus = "Orientation zeroed";
        }
    }

    private Task SendCommandAsync(string command) => UseWifi
        ? _tcp.SendCommandAsync(command)
        : _serial.SendCommandAsync(command);

    private void OnMessageReceived(ProtocolMessage message)
    {
        if (message is HelloMessage)
        {
            // Reset in receive order, before a Q in the same TCP/serial chunk.
            _orientation.Reset();
            Interlocked.Exchange(ref _lastOrientationTimestamp, 0);
        }
        if (message is QuaternionMessage quaternion)
        {
            if (_orientation.Update(quaternion.Orientation))
            {
                Interlocked.Exchange(ref _lastOrientationTimestamp, Stopwatch.GetTimestamp());
                Interlocked.Increment(ref _samplesInWindow);
            }
            return;
        }

        PostToUi(() =>
        {
            if (message is HelloMessage)
            {
                _controlReply?.TrySetException(new IOException("Firmware restarted or a new session began"));
                ResetMotorControls();
                _sensorSession.Reset();
                HasOrientation = false;
            }
            if (_sensorSession.Update(message))
            {
                DeviceStatus = _sensorSession.Status;
                CommandManager.InvalidateRequerySuggested();
                return;
            }
            // Router failures do not invalidate a working direct AP connection.
            if (message is ErrorMessage wifiError && wifiError.Code.StartsWith("WIFI_", StringComparison.Ordinal))
            {
                if (UseWifi && WifiMode == WifiModes[0]) return;
                DeviceStatus = $"Router Wi-Fi: {wifiError.Code}";
                return;
            }
            if (message is StatusMessage sensorInfo &&
                (sensorInfo.Status.StartsWith("SPI_", StringComparison.Ordinal) ||
                 sensorInfo.Status.StartsWith("SENSOR_", StringComparison.Ordinal) ||
                 sensorInfo.Status.StartsWith("WHO_AM_I_", StringComparison.Ordinal) ||
                 sensorInfo.Status.StartsWith("WIFI_", StringComparison.Ordinal))) return;
            if (message is ErrorMessage errorReply && errorReply.Code.StartsWith("CONTROL_", StringComparison.Ordinal))
                _controlReply?.TrySetException(new InvalidOperationException(errorReply.Code));
            else if (_controlReplyMatches?.Invoke(message) == true) _controlReply?.TrySetResult(message);
            if (message is PidMessage pid)
            {
                _boardPids[pid.Settings.Axis] = pid.Settings;
                AppliedPid = string.Join(" | ", _boardPids.Values.Select(g => FormattableString.Invariant($"{g.Axis}: P={g.Kp:G6} I={g.Ki:G6} D={g.Kd:G6}")));
                ControlStatus = "Board gains acknowledged (RAM; reset restores defaults)";
                CommandManager.InvalidateRequerySuggested();
                return;
            }
            if (message is ControlTelemetryMessage control)
            {
                var v = control.Values;
                ControlData = FormattableString.Invariant($"FW angle R {v[1]:F2}° P {v[2]:F2}° | rate {v[3]:F1}/{v[4]:F1}°/s\nTarget {v[5]:F1}/{v[6]:F1}°\nRoll P/I/D {v[7]:F2}/{v[8]:F2}/{v[9]:F2}\nPitch P/I/D {v[10]:F2}/{v[11]:F2}/{v[12]:F2}\nPWM {v[13]}/{v[14]}/{v[15]}/{v[16]} | saturation {v[17]}");
                try { _controlLog?.WriteLine($"{control.Csv},{CsvGains("ROLL")},{CsvGains("PITCH")}"); } catch (Exception e) { _controlLog?.Dispose(); _controlLog = null; LogStatus = e.Message; }
                return;
            }
            if (message is StatusMessage cs && cs.Status.StartsWith("CONTROL_", StringComparison.Ordinal))
            {
                if (cs.Status == "CONTROL_STOPPED") _stopPending = false;
                if (cs.Status == "CONTROL_V1") _controlAvailable = true;
                else if (cs.Status == "CONTROL_RUNNING") { _controlRunning = true; _controlArmed = false; }
                else if (cs.Status == "CONTROL_ARMED") _controlArmed = true;
                else if (cs.Status is not ("CONTROL_ZEROED" or "CONTROL_SETPOINT_OK")) _controlRunning = _controlArmed = false;
                ControlStatus = cs.Status;
                OnPropertyChanged(nameof(ControlEditable)); CommandManager.InvalidateRequerySuggested(); return;
            }
            if (message is ErrorMessage ce && ce.Code.StartsWith("CONTROL_", StringComparison.Ordinal))
            {
                if (ce.Code == "CONTROL_ARM_FAILED") _controlArmed = false;
                ControlStatus = ce.Code;
                CommandManager.InvalidateRequerySuggested(); return;
            }
            if (message is StatusMessage motor && motor.Status.StartsWith("MOTOR_", StringComparison.Ordinal))
            {
                MotorStatus = motor.Status switch
                {
                    "MOTOR_AVAILABLE" => "Motor outputs found; advanced controls require V2 firmware",
                    "MOTOR_ADVANCED_V2" => "Ready — independent PWM 0–100%, timed or continuous",
                    "MOTOR_UNAVAILABLE" => "Motor control unavailable on this firmware",
                    "MOTOR_ARMED" => "Motor test armed",
                    "MOTOR_RUNNING" => "Motor test running",
                    "MOTOR_STOPPED" => "Motors stopped",
                    _ => motor.Status
                };
                if (motor.Status is "MOTOR_AVAILABLE" or "MOTOR_UNAVAILABLE")
                    _motorsAvailable = motor.Status == "MOTOR_AVAILABLE";
                if (motor.Status == "MOTOR_ADVANCED_V2") _advancedMotors = true;
                if (motor.Status == "MOTOR_RUNNING") _motorRunning = true;
                if (motor.Status is "MOTOR_STOPPED" or "MOTOR_UNAVAILABLE" or "MOTOR_ARMED") _motorRunning = false;
                if (motor.Status is "MOTOR_STOPPED" or "MOTOR_UNAVAILABLE") { _controlRunning = _controlArmed = false; _stopPending = false; }
                OnPropertyChanged(nameof(ControlEditable));
                CommandManager.InvalidateRequerySuggested();
                return;
            }
            if (message is ErrorMessage motorError && motorError.Code.StartsWith("MOTOR_", StringComparison.Ordinal))
            {
                MotorStatus = motorError.Code;
                _motorRunning = false;
                if (motorError.Code is "MOTOR_UNAVAILABLE" or "MOTOR_PWM_FAILED")
                {
                    _motorsAvailable = false;
                    CommandManager.InvalidateRequerySuggested();
                }
                return;
            }
            DeviceStatus = message switch
            {
                HelloMessage hello => $"Firmware protocol {hello.ProtocolVersion}, target {hello.SampleRateHz} Hz",
                StatusMessage status => HumanizeStatus(status.Status),
                ErrorMessage error => $"Device error: {error.Code}",
                PongMessage => DeviceStatus,
                _ => DeviceStatus
            };
        });
    }

    private void OnConnectionLost(string reason)
    {
        PostToUi(() =>
        {
            _controlReply?.TrySetException(new IOException(reason));
            IsConnected = false;
            ResetMotorControls();
            ConnectionStatus = "Disconnected";
            DeviceStatus = $"Connection lost: {reason}";
        });
    }

    private void ShowError(Exception exception)
    {
        if (exception is OperationCanceledException) return;
        ConnectionStatus = IsConnected ? ConnectionStatus : "Disconnected";
        DeviceStatus = exception.Message;
    }

    private void PostToUi(Action action) => _uiContext.Post(_ => action(), null);

    private static string HumanizeStatus(string status) => status.ToUpperInvariant() switch
    {
        "CALIBRATING" => "Calibrating — keep the sensor still",
        "READY" => "Sensor ready",
        "SENSOR_RECOVERING_KEEP_STILL" => "Recovering sensor — motors stopped; keep board still",
        _ => status
    };

    public async ValueTask DisposeAsync()
    {
        _controlLog?.Dispose(); _controlLog = null;
        _motorHeartbeat.Stop();
        _motorHeartbeat.Tick -= MotorHeartbeatOnTick;
        try { if (IsConnected) await StopMotorsAsync(); }
        catch { /* Hardware timeout remains active. */ }
        _serial.MessageReceived -= OnMessageReceived;
        _serial.ConnectionLost -= OnConnectionLost;
        _tcp.MessageReceived -= OnMessageReceived;
        _tcp.ConnectionLost -= OnConnectionLost;
        await _tcp.DisposeAsync();
        await _serial.DisposeAsync();
    }
}
