using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Windows.Threading;
using DroneMotion.Wpf.ViewModels;

namespace DroneMotion.Tests;

public sealed class BalanceWorkflowTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task WpfAppliesAcknowledgedGainsAndArmsStartsStopsOverTcp(int scenario)
    {
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
            dispatcher.BeginInvoke(new Action(async () =>
            {
                try { await ExerciseWorkflow(scenario % 2, scenario == 2); completed.TrySetResult(); }
                catch (Exception e) { completed.TrySetException(e); }
                finally { dispatcher.BeginInvokeShutdown(DispatcherPriority.Normal); }
            }));
            Dispatcher.Run();
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();
        await completed.Task.WaitAsync(TimeSpan.FromSeconds(20));
    }

    private static async Task Until(Func<bool> condition)
    {
        for (int n = 0; n < 300; n++) { if (condition()) return; await Task.Delay(10); }
        Assert.True(condition(), "Workflow did not reach expected state");
    }

    private static async Task ExerciseWorkflow(int wifiMode, bool cascade)
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var commands = new List<string>();
        int suppressGainReplies = 0;
        int closing = 0;
        var finishCalibration = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var server = Task.Run(async () =>
        {
            using var client = await listener.AcceptTcpClientAsync();
            using var reader = new StreamReader(client.GetStream(), Encoding.ASCII);
            using var writer = new StreamWriter(client.GetStream(), Encoding.ASCII) { AutoFlush = true, NewLine = "\n" };
            while (await reader.ReadLineAsync() is { } line)
            {
                lock (commands) commands.Add(line);
                if (line == "INFO")
                {
                    await writer.WriteLineAsync("HELLO,1,200\nSTATUS,MOTOR_AVAILABLE\nSTATUS,MOTOR_ADVANCED_V2\nSTATUS,CONTROL_V1\nPID,ROLL,0,0,0\nPID,PITCH,0,0,0");
                    if (cascade) await writer.WriteLineAsync("STATUS,CONTROL_CASCADE_V2\nPID,ANGLE_ROLL,2,0,0\nPID,ANGLE_PITCH,2,0,0");
                    await writer.WriteLineAsync("STATUS,SENSOR_MPU6500\nSTATUS,CALIBRATING\nCAL,250,500,COLLECTING\nERROR,WIFI_CONNECT_TIMEOUT_CHECK_PORTAL\nPONG");
                    await finishCalibration.Task.WaitAsync(TimeSpan.FromSeconds(3));
                    await writer.WriteLineAsync("CAL,500,500,COMPLETE\nSTATUS,READY\nQ,1,10000,1,0,0,0");
                }
                else if (line.StartsWith("PID SET "))
                {
                    if (Volatile.Read(ref suppressGainReplies) == 1) continue;
                    var parts = line.Split(' ');
                    await writer.WriteLineAsync($"PID,{parts[2]},{parts[3]},{parts[4]},{parts[5]}");
                }
                else if (line.StartsWith("CONTROL SETPOINT ")) await writer.WriteLineAsync("STATUS,CONTROL_SETPOINT_OK");
                else if (line == "CONTROL ARM") await writer.WriteLineAsync("STATUS,CONTROL_ARMED");
                else if (line.StartsWith("CONTROL START ")) await writer.WriteLineAsync("STATUS,CONTROL_RUNNING\nC2,10000,1,2,3,4,0,0,1,2,3,4,5,6,40,41,42,43,0,1,10,11,12,13");
                else if (line == "MOTOR STOP") await writer.WriteLineAsync("STATUS,CONTROL_STOPPED");
            }
        });
        await using (var vm = new MainViewModel())
        {
            Assert.Equal("192.168.4.1", vm.WifiHost);
            vm.WifiMode=vm.WifiModes[1];
            Assert.Equal("",vm.WifiHost);
            vm.WifiHost="192.168.1.55";
            vm.WifiMode=vm.WifiModes[0];
            Assert.Equal("192.168.4.1",vm.WifiHost);
            vm.WifiMode=vm.WifiModes[1];
            Assert.Equal("192.168.1.55",vm.WifiHost);
            vm.WifiMode=vm.WifiModes[wifiMode];
            vm.WifiHost = "127.0.0.1"; vm.WifiPort = ((IPEndPoint)listener.LocalEndpoint).Port;
            vm.ConnectCommand.Execute(null);
            await Until(() => vm.ControlEditable);
            await Until(() => !vm.CalibrateCommand.CanExecute(null));
            vm.OnRenderFrame();
            Assert.Contains("Calibration 50%", vm.TelemetryStatus);
            Assert.DoesNotContain("verify main firmware", vm.TelemetryStatus);
            Assert.False(vm.ArmControlCommand.CanExecute(null));
            finishCalibration.TrySetResult();
            await Until(() => vm.DeviceStatus == "MPU6500 ready");
            await Until(() => { vm.OnRenderFrame(); return vm.TelemetryStatus == "Receiving live orientation"; });
            Assert.True(vm.CalibrateCommand.CanExecute(null));
            vm.WifiMode=vm.WifiModes[1-wifiMode];
            Assert.Equal(vm.WifiModes[wifiMode],vm.WifiMode);
            vm.RollKp = "0.2"; vm.RollKi = "0.01"; vm.RollKd = "0.003";
            vm.PitchKp = "0.15";
            if (cascade) {
                await Until(() => vm.CascadeEditable);
                vm.AngleRollKi = "0.1";
                vm.AnglePitchKd = "0.007";
            }
            vm.ApplyPidCommand.Execute(null);
            await Until(() => vm.ControlEditable && vm.AppliedPid.Contains("PITCH: P=0.15") &&
                (!cascade || vm.AppliedPid.Contains("ANGLE_PITCH: P=2 I=0 D=0.007")));
            vm.RigConfirmed = true;
            vm.ArmControlCommand.Execute(null);
            await Until(() => vm.StartControlCommand.CanExecute(null));
            Assert.False(vm.ApplyPidCommand.CanExecute(null));
            vm.StartControlCommand.Execute(null);
            await Until(() => vm.ControlStatus == "CONTROL_RUNNING");
            await Until(() => vm.PidHistory.Snapshot().Length == 1);
            Assert.Equal(12, vm.PidHistory.Snapshot()[0].Values[21]);
            vm.PausePlots = true; Assert.True(vm.PidHistory.IsPaused);
            vm.PausePlots = false;
            vm.ClearPlotsCommand.Execute(null);
            Assert.Empty(vm.PidHistory.Snapshot());
            Assert.False(vm.RunMotorCommand.CanExecute(null));
            vm.StopMotorsCommand.Execute(null);
            await Until(() => vm.ControlEditable);
            lock (commands)
            {
                Assert.Contains("PID SET ROLL 0.2 0.01 0.003", commands);
                Assert.Contains("PID SET PITCH 0.15 0 0", commands);
                if (cascade) {
                    Assert.Contains("PID SET ANGLE_ROLL 2 0.1 0", commands);
                    Assert.Contains("PID SET ANGLE_PITCH 2 0 0.007", commands);
                } else Assert.DoesNotContain(commands, c => c.StartsWith("PID SET ANGLE_"));
                Assert.True(commands.IndexOf("CONTROL SETPOINT 0 0") < commands.IndexOf("CONTROL ARM"));
                Assert.Contains("CONTROL START 40 1", commands);
                Assert.Contains("MOTOR STOP", commands);
            }
            // Stop cancels an in-flight Apply. The second axis must not be sent later.
            Volatile.Write(ref suppressGainReplies, 1);
            vm.RollKp = "0.25";
            vm.ApplyPidCommand.Execute(null);
            await Until(() => { lock (commands) return commands.Contains("PID SET ROLL 0.25 0.01 0.003"); });
            Assert.False(vm.ArmControlCommand.CanExecute(null));
            vm.StopMotorsCommand.Execute(null);
            await Until(() => vm.ControlEditable);
            lock (commands) Assert.Equal(1, commands.Count(c => c == "PID SET PITCH 0.15 0 0"));
            Volatile.Write(ref closing,1);
        }
        try { await server.WaitAsync(TimeSpan.FromSeconds(3)); }
        catch (IOException) when (Volatile.Read(ref closing)==1) { /* Client disposal can reset a socket with an unread STOP acknowledgement. */ }
    }
}
