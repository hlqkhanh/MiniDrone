using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows.Input;
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
    private string _wifiHost = "";
    private int _wifiPort = 3333;
    private readonly OrientationService _orientation = new();
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
    private int _motorPercent = 20;
    private int _motorDuration = 1000;
    private string _motorStatus = "Requires updated ESP32 firmware";
    private int _motorRequestVersion;

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
        CalibrateCommand = new AsyncRelayCommand(CalibrateAsync, () => IsConnected, ShowError);
        ZeroCommand = new RelayCommand(ZeroOrientation, () => HasOrientation);
        RunMotorCommand = new AsyncRelayCommand(RunMotorAsync,
            () => IsConnected && _motorsAvailable && PropellersRemoved &&
                  MotorTargets.Contains(SelectedMotor) && MotorPercent is >= 1 and <= 35 &&
                  MotorDuration is >= 1 and <= 2000, ShowError);
        StopMotorsCommand = new AsyncRelayCommand(StopMotorsAsync,
            () => IsConnected, ShowError);

        RefreshPorts();
    }

    public ObservableCollection<string> AvailablePorts { get; } = new();

    public IReadOnlyList<int> BaudRates { get; } = [115200, 57600, 38400, 19200, 9600];

    public bool CanEditConnection => !IsConnected && !_isConnecting;
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
    public IReadOnlyList<string> MotorTargets { get; } = ["LF", "RF", "LR", "RR", "ALL"];
    public string SelectedMotor
    {
        get => _selectedMotor;
        set { if (SetProperty(ref _selectedMotor, value)) CommandManager.InvalidateRequerySuggested(); }
    }
    public bool PropellersRemoved
    {
        get => _propellersRemoved;
        set { if (SetProperty(ref _propellersRemoved, value)) CommandManager.InvalidateRequerySuggested(); }
    }
    public int MotorPercent
    {
        get => _motorPercent;
        set { if (SetProperty(ref _motorPercent, value)) CommandManager.InvalidateRequerySuggested(); }
    }
    public int MotorDuration
    {
        get => _motorDuration;
        set { if (SetProperty(ref _motorDuration, value)) CommandManager.InvalidateRequerySuggested(); }
    }
    public string MotorStatus { get => _motorStatus; private set => SetProperty(ref _motorStatus, value); }

    private async Task RunMotorAsync()
    {
        // Validate again at execution; each click arms exactly one bounded test.
        if (!IsConnected || !_motorsAvailable || !PropellersRemoved ||
            !MotorTargets.Contains(SelectedMotor) || MotorPercent is < 1 or > 35 ||
            MotorDuration is < 1 or > 2000) return;
        var command = $"MOTOR {SelectedMotor} {MotorPercent} {MotorDuration}";
        var version = ++_motorRequestVersion;
        MotorStatus = "Requesting motor test...";
        await SendCommandAsync("MOTOR ARM");
        if (version != _motorRequestVersion || !IsConnected) return;
        await SendCommandAsync(command);
    }

    private Task StopMotorsAsync()
    {
        ++_motorRequestVersion;
        return SendCommandAsync("MOTOR STOP");
    }

    private void ResetMotorControls()
    {
        _motorsAvailable = false;
        ++_motorRequestVersion;
        PropellersRemoved = false;
        MotorStatus = "Requires updated ESP32 firmware";
        CommandManager.InvalidateRequerySuggested();
    }

    public void OnRenderFrame()
    {
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
            DeviceStatus = "Waiting for sensor data...";
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
        await SendCommandAsync("CAL");
        DeviceStatus = "Calibration requested — keep the sensor still";
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
        if (message is QuaternionMessage quaternion)
        {
            if (_orientation.Update(quaternion.Orientation))
            {
                Interlocked.Increment(ref _samplesInWindow);
            }
            return;
        }

        PostToUi(() =>
        {
            if (message is StatusMessage motor && motor.Status.StartsWith("MOTOR_", StringComparison.Ordinal))
            {
                MotorStatus = motor.Status switch
                {
                    "MOTOR_AVAILABLE" => "Ready — max 35% PWM / 2 seconds",
                    "MOTOR_UNAVAILABLE" => "Motor control unavailable on this firmware",
                    "MOTOR_ARMED" => "Motor test armed",
                    "MOTOR_RUNNING" => "Motor test running",
                    "MOTOR_STOPPED" => "Motors stopped",
                    _ => motor.Status
                };
                if (motor.Status is "MOTOR_AVAILABLE" or "MOTOR_UNAVAILABLE")
                    _motorsAvailable = motor.Status == "MOTOR_AVAILABLE";
                CommandManager.InvalidateRequerySuggested();
                return;
            }
            if (message is ErrorMessage motorError && motorError.Code.StartsWith("MOTOR_", StringComparison.Ordinal))
            {
                MotorStatus = motorError.Code;
                return;
            }
            DeviceStatus = message switch
            {
                HelloMessage hello => $"Firmware protocol {hello.ProtocolVersion}, target {hello.SampleRateHz} Hz",
                StatusMessage status => HumanizeStatus(status.Status),
                ErrorMessage error => $"Device error: {error.Code}",
                PongMessage => "Device responded to ping",
                _ => DeviceStatus
            };
        });
    }

    private void OnConnectionLost(string reason)
    {
        PostToUi(() =>
        {
            IsConnected = false;
            ResetMotorControls();
            ConnectionStatus = "Disconnected";
            DeviceStatus = $"Connection lost: {reason}";
        });
    }

    private void ShowError(Exception exception)
    {
        ConnectionStatus = IsConnected ? ConnectionStatus : "Disconnected";
        DeviceStatus = exception.Message;
    }

    private void PostToUi(Action action) => _uiContext.Post(_ => action(), null);

    private static string HumanizeStatus(string status) => status.ToUpperInvariant() switch
    {
        "CALIBRATING" => "Calibrating — keep the sensor still",
        "READY" => "Sensor ready",
        _ => status
    };

    public async ValueTask DisposeAsync()
    {
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
