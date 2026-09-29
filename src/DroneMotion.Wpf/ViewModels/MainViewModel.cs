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

    public MainViewModel()
    {
        _uiContext = SynchronizationContext.Current ?? new SynchronizationContext();
        _serial.MessageReceived += OnMessageReceived;
        _serial.ConnectionLost += OnConnectionLost;

        RefreshPortsCommand = new RelayCommand(RefreshPorts);
        ConnectCommand = new AsyncRelayCommand(ConnectAsync, () => !IsConnected && !string.IsNullOrWhiteSpace(SelectedPort), ShowError);
        DisconnectCommand = new AsyncRelayCommand(DisconnectAsync, () => IsConnected, ShowError);
        CalibrateCommand = new AsyncRelayCommand(CalibrateAsync, () => IsConnected, ShowError);
        ZeroCommand = new RelayCommand(ZeroOrientation, () => HasOrientation);

        RefreshPorts();
    }

    public ObservableCollection<string> AvailablePorts { get; } = new();

    public IReadOnlyList<int> BaudRates { get; } = [115200, 57600, 38400, 19200, 9600];

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
        DeviceStatus = AvailablePorts.Count == 0 ? "No COM ports found" : DeviceStatus;
    }

    private async Task ConnectAsync()
    {
        if (SelectedPort is null)
        {
            return;
        }

        ConnectionStatus = $"Opening {SelectedPort}...";
        await _serial.ConnectAsync(SelectedPort, SelectedBaudRate);
        IsConnected = true;
        ConnectionStatus = $"Connected to {SelectedPort}";
        DeviceStatus = "Waiting for Arduino...";

        // Opening a Nano's serial port normally resets it. Wait for the
        // bootloader before sending the lightweight connection check.
        await Task.Delay(2000);
        if (IsConnected)
        {
            await _serial.SendCommandAsync("PING");
        }
    }

    private async Task DisconnectAsync()
    {
        await _serial.DisconnectAsync();
        IsConnected = false;
        ConnectionStatus = "Disconnected";
        DeviceStatus = "Waiting for connection";
    }

    private async Task CalibrateAsync()
    {
        await _serial.SendCommandAsync("CAL");
        DeviceStatus = "Calibration requested — keep the sensor still";
    }

    private void ZeroOrientation()
    {
        if (_orientation.Zero())
        {
            DeviceStatus = "Orientation zeroed";
        }
    }

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
        _serial.MessageReceived -= OnMessageReceived;
        _serial.ConnectionLost -= OnConnectionLost;
        await _serial.DisposeAsync();
    }
}
