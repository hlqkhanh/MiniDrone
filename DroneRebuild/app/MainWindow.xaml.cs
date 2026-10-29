using System.Globalization;
using System.IO;
using System.IO.Ports;
using System.Net.Sockets;
using System.Net;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using Microsoft.Win32;

namespace DroneRebuild;
public partial class MainWindow : Window
{
    private SerialPort? serial; private TcpClient? tcp; private Stream? stream;
    private CancellationTokenSource? cancellation;
    private readonly SemaphoreSlim writeLock = new(1, 1), commandLock = new(1, 1);
    private readonly Dictionary<uint, TaskCompletionSource<string>> pending = new();
    private readonly List<double[]> history = new();
    private readonly DispatcherTimer heartbeat = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private readonly DispatcherTimer display = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private StreamWriter? telemetryLog, eventLog;
    private uint id, epoch, operation; private string state = "DISCONNECTED"; private DateTime received;
    private bool calibrated, armedHere, runningHere, stopping, closing, replay, connecting;
    private int latestMode;
    private int boardAxis;
    private int telemetryAxis;
    private bool pitchSupported, outerPidSupported;
    private readonly string[][] axisSettings = [ ["0","0","0","0","80","1","20","0","0","0"], ["0","0","0","0","80","1","20","0","0","0"] ];
    private int editedAxis;
    private int connectionGeneration;
    private double[]? latest;
    private double plotSpan = 12_000_000, plotEnd;
    private bool plotFollowing = true;
    private Canvas? draggingPlot;
    private Point dragPosition;
    private readonly Dictionary<Canvas, (double low, double high)> plotY = new();
    private readonly Dictionary<Canvas, (double low, double high)> drawnY = new();
    private readonly AttitudeDisplay attitudeDisplay = new();
    private DateTime orientationReceived;

    public MainWindow()
    {
        InitializeComponent();
        heartbeat.Tick += async (_, _) => { if (runningHere && !stopping) await Act(async () => await Request("KEEPALIVE")); };
        display.Tick += (_, _) => { if (stream != null && !connecting && (DateTime.UtcNow - received).TotalSeconds > 1) Status.Text = received==DateTime.MinValue?"Đã mở kết nối · chưa có dữ liệu IMU. Xem Thông tin board và nhật ký.":"Dữ liệu đã cũ · chưa xác định trạng thái board. Gửi STOP để kết thúc phiên."; UpdateUi(); Draw(); };
        display.Start(); heartbeat.Start();
        PreviewKeyDown += async (_, e) => { if (e.Key == Key.Escape) { e.Handled = true; await Stop(); } };
        Loaded += async (_, _) => { UpdateUi(); Draw(); await LoadDroneModel(); };
    }
    internal async Task LoadDroneModel()
    {
        try {
            var model=await Task.Run(()=>DroneModelLoader.Load(System.IO.Path.Combine(AppContext.BaseDirectory,"Assets","drone.glb")));
            if(closing)return;
            DroneModel.Content=model;ModelLoadStatus.Visibility=Visibility.Collapsed;
        } catch(Exception ex) { ModelLoadStatus.Text="Không tải được mô hình: "+ex.Message; }
    }
    private void UpdateOrientation()
    {
        if(DroneRotation==null)return;
        bool fresh=stream!=null && state!="DISCONNECTED" && calibrated && attitudeDisplay.HasSample && (DateTime.UtcNow-orientationReceived).TotalSeconds<=1;
        ZeroYawButton.IsEnabled=fresh;
        if(!attitudeDisplay.HasSample) {
            DroneRotation.Quaternion=System.Windows.Media.Media3D.Quaternion.Identity;
            OrientationReadout.Text="Roll — · Pitch — · Yaw —";
            OrientationStatus.Text=replay?"CSV mô phỏng không có đủ góc IMU 3D.":"Chờ góc IMU 3D · cần firmware gửi T53.";
            return;
        }
        var q=attitudeDisplay.ModelQuaternion;
        DroneRotation.Quaternion=new System.Windows.Media.Media3D.Quaternion(q.X,q.Y,q.Z,q.W);
        OrientationReadout.Text=FormattableString.Invariant($"Roll {attitudeDisplay.Roll:F2}° · Pitch {attitudeDisplay.Pitch:F2}° · Yaw {attitudeDisplay.Yaw:F2}°");
        OrientationStatus.Text=replay?"Xem trước tư thế 3D · dữ liệu mô phỏng.":fresh?"Góc IMU trực tiếp · kéo để xoay camera, cuộn để zoom.":!calibrated?"IMU đang hiệu chuẩn · chờ cảm biến sẵn sàng.":"Dữ liệu IMU đã cũ · đang giữ tư thế cuối.";
    }
    private void ZeroYawClick(object sender,RoutedEventArgs e)
    {
        if(stream==null || state=="DISCONNECTED" || !calibrated || (DateTime.UtcNow-orientationReceived).TotalSeconds>1)return;
        if(attitudeDisplay.ZeroYaw()) { UpdateOrientation();Event("Đã hiệu chỉnh yaw hiển thị về 0°; roll/pitch giữ theo IMU."); }
    }
    private void Event(string s) { eventLog?.WriteLine($"{DateTime.UtcNow:O},{s}"); Events.AppendText(s + "\n"); if (Events.Text.Length > 20000) Events.Text = Events.Text[^12000..]; Events.ScrollToEnd(); }
    private async Task Act(Func<Task> action) { try { await action(); } catch (Exception ex) { Event("ERROR: " + ex.Message); Status.Text = ex.Message; if (stream != null && !stopping) await Stop(); } }
    private async Task Send(string command, uint requestId)
    {
        Event($"TX,{requestId},{command}");
        var bytes = Encoding.ASCII.GetBytes($"{requestId} {command}\n");
        var output=stream ?? throw new IOException("Chưa kết nối thiết bị.");
        var token=cancellation?.Token ?? CancellationToken.None;
        if(!await writeLock.WaitAsync(TimeSpan.FromSeconds(2),token))throw new TimeoutException("Đường gửi đang bận. Ngắt kết nối rồi thử lại.");
        try {
            using var timeout=CancellationTokenSource.CreateLinkedTokenSource(token);timeout.CancelAfter(1500);
            // SerialStream may ignore cancellation until its OS write completes.
            // WaitAsync still bounds the UI wait. Never FlushFileBuffers on each
            // command: serial drivers can block indefinitely in that operation.
            await output.WriteAsync(bytes.AsMemory(),timeout.Token).AsTask().WaitAsync(TimeSpan.FromMilliseconds(1500),token);
        }
        finally { writeLock.Release(); }
    }
    private async Task<string> Request(string command,int timeoutSeconds=2)
    {
        var token=cancellation?.Token ?? CancellationToken.None;
        if(!await commandLock.WaitAsync(TimeSpan.FromSeconds(3),token))throw new TimeoutException("Đang chờ lệnh trước. Hãy ngắt kết nối rồi thử lại.");
        uint n = ++id; var promise = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously); pending[n] = promise;
        try { await Send(command, n); var reply = await promise.Task.WaitAsync(TimeSpan.FromSeconds(timeoutSeconds),token); if (!reply.Contains(",OK,")) throw new IOException(reply); return reply; }
        finally { pending.Remove(n); commandLock.Release(); }
    }
    private async void ConnectClick(object sender, RoutedEventArgs e) => await Act(ConnectToBoard);
    private async Task ConnectToBoard()
    {
        if(connecting)return;connecting=true;UpdateUi();
        try {
            await Disconnect(); replay = false;pitchSupported=false;outerPidSupported=false;boardAxis=0; history.Clear();latest=null;ResetPlotView();AcceptedSummary.Text="Chưa nhận cấu hình từ board.";
            cancellation=new CancellationTokenSource();var token=cancellation.Token;
            var generation=connectionGeneration;
            if (Transport.SelectedIndex == 0) {
                var port=new SerialPort(Address.Text.Trim(),460800){DtrEnable=false,RtsEnable=false,WriteTimeout=1500};serial=port;
                await Task.Run(()=>port.Open()).WaitAsync(TimeSpan.FromSeconds(5),token);
                token.ThrowIfCancellationRequested();stream=port.BaseStream;
            } else {
                tcp=new TcpClient{NoDelay=true};using var timeout=CancellationTokenSource.CreateLinkedTokenSource(token);timeout.CancelAfter(5000);
                await tcp.ConnectAsync(Address.Text.Trim(),connectionPort,timeout.Token);stream=tcp.GetStream();
            }
            received=DateTime.MinValue;
            _ = ReadLoop(stream,cancellation.Token,generation);Status.Text="Đã mở kết nối · chờ board trả lời INFO (tối đa 8 giây)";
            await Request("INFO",8);
        } catch (TimeoutException) { await CloseTransport();throw new TimeoutException("Board chưa trả lời đúng hạn. Kiểm tra COM16/firmware, đóng Serial Monitor; với Wi-Fi cần firmware đã sửa TCP."); }
        catch { await CloseTransport();throw; }
        finally { connecting=false;UpdateUi(); }
    }
    private int connectionPort=3333;
    private async Task ReadLoop(Stream input,CancellationToken token,int generation)
    {
        try
        {
            using var reader = new StreamReader(input, Encoding.ASCII, false, 4096, leaveOpen:true);
            while (!token.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync(token).ConfigureAwait(false); if (line == null) throw new IOException("Connection closed");
                if (line.Length > 1000) throw new IOException("Oversized protocol line");
                await Dispatcher.InvokeAsync(() => { if(generation==connectionGeneration && !token.IsCancellationRequested)Receive(line); });
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { await Dispatcher.InvokeAsync(() => { if(generation!=connectionGeneration)return;state="DISCONNECTED";runningHere=armedHere=false;Status.Text="Mất kết nối: "+ex.Message;Event(Status.Text);foreach(var p in pending.Values)p.TrySetException(new IOException(Status.Text));if(!connecting)_=CloseTransport(); }); }
    }
    private void Receive(string line)
    {
        var f = line.Split(',');
        if (f.Length >= 5 && f[0] == "ACK" && uint.TryParse(f[1], out var n) && uint.TryParse(f[3], out var ep))
        { epoch=ep; Event(line); if (pending.TryGetValue(n, out var p)) p.TrySetResult(line); return; }
        if ((f.Length == 45 || f.Length==50 || f.Length==53 || f.Length==55) && f[0] == "T")
        {

            var v = new double[21];
            int[] indices = [2,13,14,15,17,18,19,20,21,22,23,24,25,26,27,28,44];
            for (int k=0;k<indices.Length;k++) { if (indices[k]>=f.Length) v[k]=0; else if(!double.TryParse(f[indices[k]], NumberStyles.Float, CultureInfo.InvariantCulture, out v[k]) || !double.IsFinite(v[k])) { Event("Rejected invalid telemetry");return; } }
            if(f.Length==55) {
                if(!double.TryParse(f[53],NumberStyles.Float,CultureInfo.InvariantCulture,out var aki) || !double.IsFinite(aki) || !double.TryParse(f[54],NumberStyles.Float,CultureInfo.InvariantCulture,out var akd) || !double.IsFinite(akd))return;
            }
            if(f.Length>=50) {
                if(!int.TryParse(f[45],out var axis) || axis<0 || axis>1)return;
                for(int k=0;k<4;k++)if(!double.TryParse(f[46+k],NumberStyles.Float,CultureInfo.InvariantCulture,out v[17+k]) || !double.IsFinite(v[17+k]))return;
                pitchSupported=true;boardAxis=axis;
            } else { pitchSupported=false;outerPidSupported=false;boardAxis=0;v[17]=v[1];v[18]=v[2];v[19]=v[20]=double.NaN; }
            if(f.Length>=53) {
                var angles = new double[3];
                for(int k=0;k<3;k++)if(!double.TryParse(f[50+k],NumberStyles.Float,CultureInfo.InvariantCulture,out angles[k]) || !double.IsFinite(angles[k]))return;
                attitudeDisplay.Update(angles[0],angles[1],angles[2]);
                orientationReceived=DateTime.UtcNow;
            } else { attitudeDisplay.Reset();orientationReceived=default; }
            outerPidSupported=f.Length==55;
            telemetryLog?.WriteLine(f.Length==55?line:f.Length==53?line+",,":f.Length==50?line+",,,,,":line+",0,"+f[13]+","+f[14]+",,,,,,,");
            if(latest!=null && (telemetryAxis!=boardAxis || v[0]<latest[0])) { history.Clear();ResetPlotView(); }
            telemetryAxis=boardAxis;
            received=DateTime.UtcNow;epoch=uint.Parse(f[4],CultureInfo.InvariantCulture);calibrated=f[34]=="1";latestMode=int.Parse(f[5],CultureInfo.InvariantCulture);
            if (state!=f[3]) Event($"STATE,{f[3]},epoch={epoch},reason={f[33]}");state=f[3];
            if(state!="RUNNING" && state!="ARMED") runningHere=armedHere=false;
            latest=v;history.Add(v); if(history.Count>1200)history.RemoveRange(0,history.Count-1200);
            Status.Text=$"Board: {StateText()} · {(calibrated?"IMU sẵn sàng":$"Hiệu chuẩn {f[6]}/500")} · {f[33]}";
            AcceptedSummary.Text=$"Board {(boardAxis==0?"Roll":"Pitch")}: PID {f[37]} / {f[38]} / {f[39]} · Outer {f[40]} / {(outerPidSupported?f[53]:"0")} / {(outerPidSupported?f[54]:"0")} · Base {f[41]} · Sign {f[42]} · Limit {f[43]}";
            Readout.Text=FormattableString.Invariant($"Roll {v[17]:F2}° | Pitch {v[19]:F2}° | Active {(boardAxis==0?"Roll":"Pitch")} | Rate {v[2]:F2} / target {v[3]:F2}°/s | P/I/D {v[4]:F2}/{v[5]:F2}/{v[6]:F2}\nOutput {v[8]:F2} | LF/RF/LR/RR {v[9]}/{v[10]}/{v[11]}/{v[12]} | PID limit {v[13]} | Mixer clip {v[14]} | dt {v[15]*1000:F2} ms | missing {f[29]} | dropped {f[31]}\nBoard accepted: Kp/Ki/Kd {f[37]}/{f[38]}/{f[39]}, AngleP {f[40]}, base {f[41]}, sign {f[42]}, limit {f[43]}");
        }
        else {
            if(f.Length>=7 && f[0]=="HEALTH") {
                if(uint.TryParse(f[1],out var epHealth))epoch=epHealth;
                if(f[2]!="1" || f[3]!="1")calibrated=false;
            }
            Event(line);
        }
    }
    private double Num(TextBox b) { if (!double.TryParse(b.Text, NumberStyles.Float,CultureInfo.InvariantCulture,out var n)||!double.IsFinite(n)) throw new ArgumentException("Invalid number: " + b.Text);return n; }
    private void Fresh() { if (stream==null || state=="DISCONNECTED" || replay || (DateTime.UtcNow-received).TotalSeconds>1) throw new InvalidOperationException("Need fresh board telemetry"); }
    private async void ApplyClick(object s,RoutedEventArgs e)=>await Act(async()=>{
        Fresh();
        if(AxisChoice.SelectedIndex==1 && !pitchSupported)throw new InvalidOperationException("Nạp firmware mới để dùng Pitch.");
        if(!outerPidSupported && (Num(AngleKi)!=0 || Num(AngleKd)!=0))throw new InvalidOperationException("Cần nạp firmware mới để chỉnh Ki/Kd vòng Outer.");
        var boxes=outerPidSupported?new[]{Kp,Ki,Kd,AngleKp,AngleKi,AngleKd,BaseDuty,Sign,Limit}:new[]{Kp,Ki,Kd,AngleKp,BaseDuty,Sign,Limit};
        var values=boxes.Select(Num).Select(n=>n.ToString("G9",CultureInfo.InvariantCulture)).ToArray();
        if(pitchSupported)await Request($"AXIS {AxisChoice.SelectedIndex}");
        Verified.IsChecked=false;
        await Request((outerPidSupported?"CONFIG_CASCADE ":"CONFIG ")+string.Join(' ',values));
    });
    private async void ArmClick(object s,RoutedEventArgs e)=>await Act(async()=>{ Fresh();if(!calibrated)throw new InvalidOperationException("Wait for calibration");if(TestTabs.SelectedIndex==0 && boardAxis!=AxisChoice.SelectedIndex)throw new InvalidOperationException("Áp dụng cấu hình đúng trục trước khi ARM.");var op=operation;var armEpoch=epoch;await Request($"VERIFY {(Verified.IsChecked==true?1:0)}");if(op!=operation)return;await Request($"PROPS_REMOVED {(PropsRemoved.IsChecked==true?1:0)}");if(op!=operation)return;await Request($"ARM {armEpoch}");if(op==operation)armedHere=true; });
    private async void StartClick(object s,RoutedEventArgs e)=>await Act(async()=>{ Fresh();if(!armedHere||Verified.IsChecked!=true)throw new InvalidOperationException("Confirm rig/mapping, then ARM");var op=operation;await Request(FormattableString.Invariant($"START {epoch} {Mode.SelectedIndex} {Num(Target):G9} 0"));if(op==operation){runningHere=true;armedHere=false;} });
    private async void BenchClick(object s,RoutedEventArgs e)=>await Act(async()=>{ Fresh();if(!armedHere||PropsRemoved.IsChecked!=true)throw new InvalidOperationException("Remove props, confirm, then ARM");var op=operation;await Request(FormattableString.Invariant($"BENCH {epoch} {Motor.SelectedIndex} {Num(BenchDuty):G9} 1000"));if(op==operation){runningHere=true;armedHere=false;} });
    private async void InfoClick(object s,RoutedEventArgs e)=>await Act(async()=>await Request("INFO"));
    private async void CalibrateClick(object s,RoutedEventArgs e)=>await Act(async()=>{ await Stop();await Request("CALIBRATE"); });
    private async void ZeroRollClick(object s,RoutedEventArgs e)=>await Act(async()=>{
        Fresh();
        if(!calibrated || armedHere || runningHere || state is "ARMED" or "RUNNING")throw new InvalidOperationException("Dừng motor, đặt khung ngang và giữ đứng yên trước khi lấy mốc.");
        if(AxisChoice.SelectedIndex==1 && !pitchSupported)throw new InvalidOperationException("Nạp firmware 0.2 để dùng Pitch.");
        await Request(AxisChoice.SelectedIndex==0?"ZERO_ROLL":"ZERO_PITCH");
        Verified.IsChecked=false;
        Event("Đã lấy mốc ngang trên board. Mốc mất khi reset hoặc hiệu chuẩn IMU lại.");
        await Request("INFO");
    });
    private async void StopClick(object s,RoutedEventArgs e)=>await Stop();
    private async void ConfirmationRemoved(object s,RoutedEventArgs e) { if(IsLoaded && stream!=null)await Stop(); }
    private async Task Stop()
    {
        ++operation;if(stopping)return;stopping=true;runningHere=armedHere=false;UpdateUi();
        try { if(stream!=null)await Send("STOP",++id); }
        catch(Exception ex){Event("STOP send failed: "+ex.Message);}finally{stopping=false;}
    }
    private async Task Disconnect()
    {
        await Stop();await CloseTransport();
    }
    private async Task CloseTransport()
    {
        ++operation;++connectionGeneration;
        var oldStream=stream;var oldSerial=serial;var oldTcp=tcp;var oldCancellation=cancellation;
        attitudeDisplay.Reset();orientationReceived=default;
        stream=null;serial=null;tcp=null;cancellation=null;oldCancellation?.Cancel();
        foreach(var p in pending.Values)p.TrySetCanceled();runningHere=armedHere=false;state="DISCONNECTED";
        Status.Text="Chưa kết nối · motor chưa được ra lệnh";UpdateUi();
        try { await Task.Run(()=>{try{oldStream?.Dispose();}finally{oldSerial?.Dispose();oldTcp?.Dispose();}}).WaitAsync(TimeSpan.FromSeconds(2)); }
        catch(Exception ex){Event("CLOSE: "+ex.Message);}
        finally { oldCancellation?.Dispose(); }
    }
    private async void DisconnectClick(object s,RoutedEventArgs e)=>await Act(Disconnect);
    private void LogClick(object s,RoutedEventArgs e)
    {
        try {
            if(telemetryLog!=null){telemetryLog.Dispose();eventLog?.Dispose();telemetryLog=eventLog=null;LogButton.Content="Bắt đầu ghi log";return;}
            var folder=System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),"DroneRebuildLogs",DateTime.Now.ToString("yyyyMMdd-HHmmss-fff"));Directory.CreateDirectory(folder);
            telemetryLog=new StreamWriter(System.IO.Path.Combine(folder,"telemetry.csv")){AutoFlush=true};eventLog=new StreamWriter(System.IO.Path.Combine(folder,"events.log")){AutoFlush=true};
            telemetryLog.WriteLine("type,seq,micros,state,epoch,mode,calCount,ax,ay,az,gx,gy,gz,axisAngle,axisRate,rateTarget,error,p,i,d,raw,output,LF,RF,LR,RR,pidLimited,mixerLimited,dt,missing,late,dropTelemetry,dropReplies,reason,calibrated,verified,propsRemoved,kp,ki,kd,angleKp,base,motorSign,outputLimit,command,axis,roll,rollRate,pitch,pitchRate,imuRoll,imuPitch,imuYaw,angleKi,angleKd");
            LogButton.Content="■  Dừng ghi log";Event("SESSION,"+folder);Event("SESSION_NOTE,"+SessionNote.Text);if(stream!=null)_=Act(async()=>await Request("INFO"));
        }catch(Exception ex){Status.Text=ex.Message;}
    }
    private async void ReplayClick(object s,RoutedEventArgs e)=>await Act(async()=>
    {
        if(stream!=null)throw new InvalidOperationException("Disconnect before simulation replay");
        var dialog=new OpenFileDialog{Filter="Simulation CSV|*.csv"};if(dialog.ShowDialog()!=true)return;
        await Task.CompletedTask;LoadReplay(dialog.FileName);
    });
    internal void LoadReplay(string path)
    {
        var lines=File.ReadAllLines(path);history.Clear();ResetPlotView();attitudeDisplay.Reset();orientationReceived=default;
        if(lines.Length<2 || !lines[0].StartsWith("t,angle,rate,target,"))throw new InvalidDataException("Select native_roll.csv from native simulation");
        foreach(var line in lines.Skip(1)){var f=line.Split(',');if(f.Length<16)continue;var a=new double[21];int[] ix=[0,1,2,3,4,5,6,7,8,9,10,11,12,13,14];for(int k=0;k<ix.Length;k++)a[k]=double.Parse(f[ix[k]],CultureInfo.InvariantCulture);a[0]*=1e6;a[17]=a[1];a[18]=a[2];a[19]=a[20]=double.NaN;history.Add(a);}
        replay=true;latestMode=1;Status.Text="MÔ PHỎNG · phát lại dữ liệu giả định, không kết nối phần cứng";latest=history.LastOrDefault();
        Mode.SelectedIndex=1;Readout.Text=latest==null?"No simulation samples":FormattableString.Invariant($"SYNTHETIC DATA · {history.Count} samples · final Roll {latest[1]:F3}° · rate {latest[2]:F3}°/s\nHardware gains above remain unchanged. Replay does not validate the real drone.");Draw();
    }
    internal void SetPreviewScenario(string scenario)
    {
        if(scenario=="logs")ViewTabs.SelectedIndex=2;
        if(scenario=="zoom") { ZoomPlot(.2,.5); }
        if(scenario=="3d") {
            ViewTabs.SelectedIndex=1;
            attitudeDisplay.Update(23,-17,72);attitudeDisplay.ZeroYaw();attitudeDisplay.Update(23,-17,102);
            UpdateOrientation();
        }
        if(scenario=="bench")TestTabs.SelectedIndex=1;
        if(scenario=="empty") { history.Clear();latest=null;ResetPlotView();replay=false;Status.Text="Chưa kết nối · chưa có dữ liệu từ board"; }
        if(scenario=="pitch") {
            replay=false;pitchSupported=true;boardAxis=telemetryAxis=1;
            AxisChoice.SelectedIndex=1;
            foreach(var row in history) { row[19]=row[1];row[20]=row[2];row[17]=.1;row[18]=0; }
            latest=history.LastOrDefault();Status.Text="XEM TRƯỚC GIAO DIỆN PITCH · dữ liệu giả định, không kết nối drone";
        }
        UpdateUi();Draw();
    }
    // Diagnostic mode: loopback fake board only. Never connects to an ESP32
    // or sends ARM/START. Exercises the actual WPF handshake/read/cleanup code.
    internal async Task<string> CheckConnections()
    {
        var report=new StringBuilder();
        history.Clear();
        for(int n=0;n<1200;n++) { var row=new double[21];row[0]=n*10000;row[17]=Math.Sin(n*.01);history.Add(row); }
        ResetPlotView();ZoomPlot(.5,.5);
        if(plotFollowing || plotSpan!=6_000_000 || Math.Abs(plotEnd-8_990_000)>1)throw new IOException("Plot anchored zoom failed");
        double frozenEnd=plotEnd;var extra=new double[21];extra[0]=12_000_000;history.Add(extra);Draw();
        if(plotEnd!=frozenEnd)throw new IOException("Plot moved while inspecting history");
        ZoomPlot(1e-9,.5);if(plotSpan!=100_000)throw new IOException("Plot minimum zoom failed");
        ZoomPlot(1e9,.5);if(plotSpan!=12_000_000)throw new IOException("Plot maximum zoom failed");
        plotEnd=-1;ClampPlotEnd();if(plotEnd!=12_000_000)throw new IOException("Plot pan bounds failed");
        plotY[AnglePlot]=(-1,1);ResetPlotView();
        if(!plotFollowing || plotSpan!=12_000_000 || plotY.Count!=0)throw new IOException("Plot follow/reset failed");
        history.Clear();Draw();
        report.AppendLine("PASS plot anchored zoom, frozen inspection, zoom/pan bounds, follow/reset, empty history");
        var orientation=new AttitudeDisplay();
        if(orientation.ZeroYaw())throw new IOException("Yaw zero accepted without data");
        orientation.Update(23,-17,170);orientation.ZeroYaw();
        if(orientation.Roll!=23 || orientation.Pitch!=-17 || orientation.Yaw!=0)throw new IOException("Yaw zero changed tilt");
        orientation.Update(-31,12,-170);
        if(orientation.Roll!=-31 || orientation.Pitch!=12 || orientation.Yaw!=20)throw new IOException("Yaw wrap/IMU tilt failed");
        if(orientation.Update(double.NaN,0,0))throw new IOException("Invalid orientation accepted");
        var q=orientation.ModelQuaternion;
        if(Math.Abs(q.Length()-1)>1e-6)throw new IOException("Invalid model quaternion");
        orientation.ZeroYaw();orientation.ZeroYaw();
        if(orientation.Yaw!=0 || orientation.Roll!=-31 || orientation.Pitch!=12)throw new IOException("Repeated yaw zero changed tilt");
        orientation.Reset();orientation.Update(5,8,42);
        if(orientation.Yaw!=42)throw new IOException("Yaw reference survived reset");
        await LoadDroneModel();
        if(DroneModel.Content==null)throw new IOException("GLB model failed to load");
        report.AppendLine("PASS GLB load, yaw-only zero, repeated zero, wrap, live tilt, invalid samples, reconnect reset");
        var probe=new CommandStreamProbe(false);stream=probe;cancellation=new CancellationTokenSource();
        await Send("INFO",42);
        if(probe.Written!="42 INFO\n")throw new IOException("Command framing failed");
        await CloseTransport();report.AppendLine("PASS command write: exact newline framing, no serial flush");
        probe=new CommandStreamProbe(true);stream=probe;cancellation=new CancellationTokenSource();
        bool bounded=false;var writeStarted=DateTime.UtcNow;
        try{await Send("INFO",43);}catch(TimeoutException){bounded=true;}
        if(!bounded || (DateTime.UtcNow-writeStarted).TotalSeconds>3 || writeLock.CurrentCount!=1)throw new IOException("Uncancellable driver write blocked UI or leaked lock");
        await CloseTransport();report.AppendLine("PASS stalled driver write: UI wait bounded, write lock released");
        foreach(var scenario in new[]{"normal","delayed-info","closed-peer","wrong-ack"}) {
            var listener=new TcpListener(IPAddress.Loopback,0);listener.Start();
            connectionPort=((IPEndPoint)listener.LocalEndpoint).Port;
            using var cancel=new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var server=Task.Run(async()=>{
                using var peer=await listener.AcceptTcpClientAsync(cancel.Token);
                using var reader=new StreamReader(peer.GetStream(),Encoding.ASCII,false,4096,true);
                using var writer=new StreamWriter(peer.GetStream(),Encoding.ASCII,4096,true){AutoFlush=true,NewLine="\n"};
                var command=await reader.ReadLineAsync(cancel.Token);
                if(command==null || !command.EndsWith(" INFO",StringComparison.Ordinal))throw new IOException("Expected INFO request");
                if(scenario=="closed-peer")return;
                var n=command.Split(' ')[0];
                if(scenario=="delayed-info")await Task.Delay(2500,cancel.Token);
                await writer.WriteLineAsync("INFO,DroneRebuild-TEST,LOOPBACK");
                await writer.WriteLineAsync($"ACK,{(scenario=="wrong-ack"?"99999":n)},OK,0,INFO");
                var f=Enumerable.Repeat("0",45).ToArray();f[0]="T";f[1]="4";f[2]="20000";f[3]="DISARMED";f[6]="500";f[9]="1";f[28]="0.005";f[33]="BOOT";f[34]="1";f[41]="80";f[42]="1";f[43]="20";
                // Deliberately split a telemetry line across TCP packets.
                var text=string.Join(',',f)+"\n";
                await writer.WriteAsync(text[..30]);await writer.FlushAsync();await Task.Delay(10,cancel.Token);await writer.WriteAsync(text[30..]);await writer.FlushAsync();
                while(!cancel.IsCancellationRequested && await reader.ReadLineAsync(cancel.Token)!=null){}
            },cancel.Token);
            Transport.SelectedIndex=1;Address.Text="127.0.0.1";
            Exception? failure=null;var started=DateTime.UtcNow;
            try { await ConnectToBoard();await Task.Delay(100); }
            catch(Exception ex){failure=ex;}
            if(scenario is "normal" or "delayed-info") {
                if(failure!=null || latest==null || state!="DISARMED" || !calibrated || connecting)throw new IOException($"{scenario}: handshake failed",failure);
                report.AppendLine($"PASS {scenario}: matching ACK, fragmented telemetry, UI ready");
                var extended=Enumerable.Repeat("0",50).ToArray();extended[0]="T";extended[2]="30000";extended[3]="DISARMED";extended[6]="500";extended[13]="-3";extended[14]="2";extended[28]="0.005";extended[33]="STOP";extended[34]="1";extended[41]="80";extended[42]="1";extended[43]="20";extended[45]="1";extended[46]="1.25";extended[47]="-0.5";extended[48]="-3";extended[49]="2";
                Receive(string.Join(',',extended));UpdateUi();
                if(!pitchSupported || boardAxis!=1 || latest[17]!=1.25 || latest[19]!=-3 || latest[1]!=-3 || history.Count!=1 || !PitchValue.Text.Contains("-3.00"))throw new IOException("Pitch telemetry axis/readout mapping failed");
                var full=extended.Concat(new[]{"23","-17","170"}).ToArray();Receive(string.Join(',',full));UpdateUi();
                if(!attitudeDisplay.HasSample || attitudeDisplay.Roll!=23 || attitudeDisplay.Pitch!=-17 || !ZeroYawButton.IsEnabled)throw new IOException("T53 orientation missing");
                ZeroYawClick(this,new RoutedEventArgs());
                if(attitudeDisplay.Yaw!=0 || attitudeDisplay.Roll!=23 || attitudeDisplay.Pitch!=-17)throw new IOException("T53 zero changed IMU tilt");
                full[52]="-170";Receive(string.Join(',',full));
                if(attitudeDisplay.Yaw!=20)throw new IOException("T53 yaw reference not preserved");
                var cascadeFrame=full.Concat(new[]{"0.3","0.04"}).ToArray();Receive(string.Join(',',cascadeFrame));UpdateUi();
                if(!outerPidSupported || !AcceptedSummary.Text.Contains("0.3 / 0.04"))throw new IOException("Outer PID T55 readback failed");
                int beforeCascadeInvalid=history.Count;cascadeFrame[53]="NaN";Receive(string.Join(',',cascadeFrame));
                if(history.Count!=beforeCascadeInvalid)throw new IOException("Invalid outer PID readback accepted");
                AxisChoice.SelectedIndex=1;Kp.Text="0.4";AngleKi.Text="0.3";AngleKd.Text="0.04";
                AxisChoice.SelectedIndex=0;AngleKi.Text="0.1";AngleKd.Text="0.02";AxisChoice.SelectedIndex=1;
                if(AngleKi.Text!="0.3" || AngleKd.Text!="0.04")throw new IOException("Pitch Outer gains not preserved across axis switch");
                AxisChoice.SelectedIndex=0;
                if(AngleKi.Text!="0.1" || AngleKd.Text!="0.02")throw new IOException("Roll Outer gains not preserved across axis switch");
                AxisChoice.SelectedIndex=1;
                report.AppendLine("PASS T55 Outer PID readback, nonfinite rejection, independent Roll/Pitch gain editing");
                full[50]="NaN";int beforeInvalid=history.Count;Receive(string.Join(',',full));
                if(history.Count!=beforeInvalid || attitudeDisplay.Roll!=23)throw new IOException("Invalid T53 accepted");
                Receive(string.Join(',',extended));UpdateUi();
                if(attitudeDisplay.HasSample || ZeroYawButton.IsEnabled)throw new IOException("Legacy telemetry retained 3D pose");
                report.AppendLine("PASS T53 raw IMU angles, yaw button, wrap, nonfinite rejection, T50 fallback");
                int count=history.Count;extended[48]="NaN";Receive(string.Join(',',extended));
                if(history.Count!=count)throw new IOException("Nonfinite Pitch was accepted");
                report.AppendLine("PASS Pitch T50: active-axis mapping, separate angles, history reset, nonfinite rejection");
            } else {
                if(failure==null || connecting || stream!=null || pending.Count!=0 || !ConnectButton.IsEnabled)throw new IOException($"{scenario}: connection did not clean up correctly");
                if((DateTime.UtcNow-started).TotalSeconds>12)throw new IOException("Handshake exceeded bound");
                report.AppendLine($"PASS {scenario}: bounded failure, resources cleared, retry enabled");
            }
            await Disconnect();cancel.Cancel();listener.Stop();
            try{await server.WaitAsync(TimeSpan.FromSeconds(2));}catch(OperationCanceledException){}
        }
        connectionPort=3333;return report.ToString();
    }
    private string StateText() => state switch { "RUNNING"=>"Đang chạy", "ARMED"=>"Đã ARM", "FAULT"=>"Lỗi", "DISARMED"=>"Đã dừng", _=>"Chưa kết nối" };
    private void UpdateUi()
    {
        if(!IsInitialized || ConnectButton==null)return;
        UpdateOrientation();
        bool connected=stream!=null, fresh=connected && state!="DISCONNECTED" && (DateTime.UtcNow-received).TotalSeconds<=1;
        bool busy= armedHere || runningHere || state is "ARMED" or "RUNNING";
        bool bench=TestTabs.SelectedIndex==1;
        ConnectionBadge.Text=replay?"MÔ PHỎNG":connecting?"ĐANG KẾT NỐI":!connected?"CHƯA KẾT NỐI":!fresh?"CHỜ DỮ LIỆU":StateText().ToUpperInvariant();
        ConnectionBadge.Foreground=replay?Brushes.DarkOrange:runningHere?Brushes.SeaGreen:Brushes.SlateGray;
        ConnectButton.IsEnabled=!connected && !connecting;DisconnectButton.IsEnabled=connected || connecting;
        Transport.IsEnabled=Address.IsEnabled=!connected && !connecting;
        ApplyButton.IsEnabled=fresh && !busy;
        ZeroRollButton.IsEnabled=fresh && calibrated && !busy && !connecting && !stopping;
        ArmButton.IsEnabled=fresh && calibrated && !busy && (bench?PropsRemoved.IsChecked==true:Verified.IsChecked==true && boardAxis==AxisChoice.SelectedIndex);
        StartButton.IsEnabled=fresh && armedHere && Verified.IsChecked==true && !bench && !stopping;
        BenchButton.IsEnabled=fresh && armedHere && PropsRemoved.IsChecked==true && bench && !stopping;
        StartButton.Visibility=bench?Visibility.Collapsed:Visibility.Visible;BenchButton.Visibility=bench?Visibility.Visible:Visibility.Collapsed;
        foreach(var control in new Control[]{Kp,Ki,Kd,AngleKp,AngleKi,AngleKd,BaseDuty,Sign,Limit,Mode,Target,Motor,BenchDuty,AxisChoice})control.IsEnabled=!busy;
        AngleKp.IsEnabled=AngleKi.IsEnabled=AngleKd.IsEnabled=!busy && Mode.SelectedIndex==1;
        RunHint.Text=replay?"Đang xem dữ liệu mô phỏng; chưa thể ARM.":!fresh?"Kết nối và đợi dữ liệu mới từ board.":!calibrated?"Giữ board đứng yên để hoàn tất hiệu chuẩn.":runningHere?"Đang chạy · Esc hoặc DỪNG MOTOR để dừng.":armedHere?(bench?"Đã ARM · bấm Chạy motor trong 15 giây.":"Đã ARM · bấm Bắt đầu Roll trong 15 giây."):bench?"Tháo cánh, đánh dấu xác nhận, rồi ARM.":Verified.IsChecked!=true?"Hoàn tất kiểm tra IMU, chiều lực và đế trước khi xác nhận.":"Áp dụng cấu hình, chờ board xác nhận, rồi ARM.";
        if(latest!=null) {
            RollValue.Text=latest[17].ToString("F2",CultureInfo.InvariantCulture)+"°";
            PitchValue.Text=double.IsFinite(latest[19])?latest[19].ToString("F2",CultureInfo.InvariantCulture)+"°":"—";
            RateValue.Text=latest[2].ToString("F2",CultureInfo.InvariantCulture);
            RateTargetValue.Text=FormattableString.Invariant($"Mục tiêu: {latest[3]:F2} °/s");
            OutputValue.Text=latest[8].ToString("F2",CultureInfo.InvariantCulture);
            LimitValue.Text=latest[13]!=0?"PID chạm giới hạn":latest[14]!=0?"Mixer bị cắt lệnh":"Trong giới hạn đã báo";
        } else { PitchValue.Text=RollValue.Text=RateValue.Text=OutputValue.Text="—";RateTargetValue.Text="Mục tiêu: — °/s";LimitValue.Text="Chưa có dữ liệu"; }
        var active=boardAxis==0?"Roll":"Pitch";
        RateTitle.Text=$"TỐC ĐỘ {active.ToUpperInvariant()}";
        RatePlotTitle.Text=$"Tốc độ {active} · °/s";
        AngleLegend.Text=(pitchSupported && !replay?"Xanh: Roll · Xanh lá: Pitch":"Xanh: Roll")+(latestMode==1?$" · Cam: mục tiêu {active}":" · RATE không giữ góc");
        ZeroRollButton.Content=$"Khung ngang · Lấy mốc {(AxisChoice.SelectedIndex==0?"Roll":"Pitch")} 0°";
        StartButton.Content=$"2. Bắt đầu {(AxisChoice.SelectedIndex==0?"Roll":"Pitch")}";
    }
    private void TransportChanged(object s,SelectionChangedEventArgs e)
    {
        if(Address==null || ConnectionHint==null)return;
        if(Transport.SelectedIndex==1) { if(Address.Text.StartsWith("COM",StringComparison.OrdinalIgnoreCase))Address.Text="192.168.4.1";ConnectionHint.Text="Wi-Fi ESP32 · TCP 3333"; }
        else { if(Address.Text.Contains('.'))Address.Text="COM6";ConnectionHint.Text="USB · 460800 baud"; }
    }
    private void ModeChanged(object s,SelectionChangedEventArgs e) { if(TargetLabel==null)return;TargetLabel.Text=Mode.SelectedIndex==1?"Mục tiêu · độ (°)":"Mục tiêu · °/s";UpdateUi(); }
    private void AxisChanged(object s,SelectionChangedEventArgs e) {
        if(!IsLoaded || e.Source!=AxisChoice)return;
        if(armedHere || runningHere || state is "ARMED" or "RUNNING") { AxisChoice.SelectedIndex=editedAxis;return; }
        var boxes=new[]{Kp,Ki,Kd,AngleKp,BaseDuty,Sign,Limit,Target,AngleKi,AngleKd};
        axisSettings[editedAxis]=boxes.Select(b=>b.Text).ToArray();
        editedAxis=AxisChoice.SelectedIndex;
        for(int k=0;k<boxes.Length;k++)boxes[k].Text=axisSettings[editedAxis][k];
        Verified.IsChecked=false;
        Status.Text="Đã chọn "+(editedAxis==0?"Roll":"Pitch")+" · kiểm tra đế và chiều lực, rồi áp dụng cấu hình lên board.";
        UpdateUi();
    }
    private async void TestTabChanged(object s,SelectionChangedEventArgs e) { if(!IsLoaded || e.Source!=TestTabs)return;if(armedHere || runningHere)await Stop();UpdateUi(); }
    private void ResetPlotView()
    {
        plotFollowing=true;plotSpan=12_000_000;plotY.Clear();drawnY.Clear();
    }
    private void ClampPlotEnd()
    {
        if(history.Count==0)return;
        plotEnd=Math.Clamp(plotEnd,Math.Min(history[^1][0],history[0][0]+plotSpan),history[^1][0]);
    }
    private void ZoomPlot(double factor,double anchor)
    {
        if(history.Count<2)return;
        double end=plotFollowing?history[^1][0]:plotEnd;
        double next=Math.Clamp(plotSpan*factor,100_000,Math.Max(12_000_000,history[^1][0]-history[0][0]));
        double time=end-plotSpan+anchor*plotSpan;
        plotEnd=time+(1-anchor)*next;plotSpan=next;plotFollowing=false;ClampPlotEnd();Draw();
    }
    private void PlotZoomInClick(object s,RoutedEventArgs e)=>ZoomPlot(.8,.5);
    private void PlotZoomOutClick(object s,RoutedEventArgs e)=>ZoomPlot(1.25,.5);
    private void PlotFollowClick(object s,RoutedEventArgs e) { ResetPlotView();Draw(); }
    private void PlotWheel(object s,MouseWheelEventArgs e)
    {
        if(s is not Canvas canvas || history.Count<2)return;
        double factor=Math.Pow(.8,e.Delta/120.0);
        if(Keyboard.Modifiers.HasFlag(ModifierKeys.Control) && drawnY.TryGetValue(canvas,out var range)) {
            double fraction=Math.Clamp((e.GetPosition(canvas).Y-8)/Math.Max(1,canvas.ActualHeight-32),0,1);
            double anchor=range.high-fraction*(range.high-range.low);
            double span=Math.Clamp((range.high-range.low)*factor,.001,1e9);
            plotY[canvas]=(anchor-(1-fraction)*span,anchor+fraction*span);Draw();
        } else ZoomPlot(factor,Math.Clamp((e.GetPosition(canvas).X-43)/Math.Max(1,canvas.ActualWidth-51),0,1));
        e.Handled=true;
    }
    private void PlotMouseDown(object s,MouseButtonEventArgs e)
    {
        if(s is not Canvas canvas || history.Count<2)return;
        if(e.ClickCount==2) { ResetPlotView();Draw();e.Handled=true;return; }
        plotEnd=plotFollowing?history[^1][0]:plotEnd;plotFollowing=false;
        draggingPlot=canvas;dragPosition=e.GetPosition(canvas);canvas.CaptureMouse();canvas.Cursor=Cursors.SizeAll;e.Handled=true;
    }
    private void PlotMouseMove(object s,MouseEventArgs e)
    {
        if(s is not Canvas canvas || draggingPlot!=canvas || e.LeftButton!=MouseButtonState.Pressed)return;
        var position=e.GetPosition(canvas);
        plotEnd-=(position.X-dragPosition.X)/Math.Max(1,canvas.ActualWidth-51)*plotSpan;ClampPlotEnd();
        if(drawnY.TryGetValue(canvas,out var range)) {
            double shift=(position.Y-dragPosition.Y)/Math.Max(1,canvas.ActualHeight-32)*(range.high-range.low);
            plotY[canvas]=(range.low+shift,range.high+shift);
        }
        dragPosition=position;Draw();e.Handled=true;
    }
    private void PlotMouseUp(object s,MouseButtonEventArgs e) { if(s is Canvas canvas)canvas.ReleaseMouseCapture(); }
    private void PlotLostCapture(object s,MouseEventArgs e) { if(draggingPlot!=null)draggingPlot.Cursor=Cursors.Hand;draggingPlot=null; }
    private void PlotResize(object s,SizeChangedEventArgs e)=>Draw();
    private void Draw()
    {
        if(MotorPlot==null)return;
        if(!plotFollowing)ClampPlotEnd();
        if(PlotViewStatus!=null)PlotViewStatus.Text=(plotFollowing?"Trực tiếp":"Đang xem lại")+$" · {plotSpan/1e6:0.##} s · Cuộn: zoom · Kéo: xem lại";
        var angles=new List<(int index,Brush color)>{(17,Brushes.DodgerBlue)};
        if(pitchSupported && !replay)angles.Add((19,Brushes.SeaGreen));
        if(latestMode==1)angles.Add((16,Brushes.DarkOrange));
        Plot(AnglePlot,angles.ToArray());
        Plot(RatePlot,[(2,Brushes.DodgerBlue),(3,Brushes.DarkOrange)]);
        Plot(OutputPlot,[(8,Brushes.MediumPurple)]);
        Plot(MotorPlot,[(9,Brushes.SeaGreen),(10,Brushes.Gray),(11,Brushes.DarkCyan),(12,Brushes.SlateGray)]);
    }
    private void Plot(Canvas canvas,(int index,Brush color)[] series)
    {
        canvas.Children.Clear();double w=canvas.ActualWidth,h=canvas.ActualHeight;if(w<30||h<30)return;
        if(history.Count<2) { var hint=new TextBlock{Text="Chưa có dữ liệu",Foreground=Brushes.SlateGray,FontSize=12};Canvas.SetLeft(hint,16);Canvas.SetTop(hint,Math.Max(12,h/2-10));canvas.Children.Add(hint);return; }
        double end=plotFollowing?history[^1][0]:plotEnd;
        double t0=Math.Max(history[0][0],end-plotSpan),span=Math.Max(1,end-t0);
        // Include adjacent samples so lines interpolate at the viewport edges.
        int first=history.FindIndex(v=>v[0]>=t0);if(first<0)first=history.Count-1;
        first=Math.Max(0,first-1);
        int last=history.FindLastIndex(v=>v[0]<=end);last=Math.Min(history.Count-1,Math.Max(first,last)+1);
        var rows=history.GetRange(first,last-first+1).ToArray();
        var values=series.SelectMany(s=>rows.Select(v=>v[s.index])).Where(double.IsFinite).ToArray();
        if(values.Length==0)return;
        double low=values.Min(),high=values.Max();if(high-low<1){high+=.5;low-=.5;}double pad=(high-low)*.1;low-=pad;high+=pad;
        if(plotY.TryGetValue(canvas,out var manual)) { low=manual.low;high=manual.high; }
        drawnY[canvas]=(low,high);
        const double left=43,top=8;double pw=Math.Max(1,w-left-8),ph=Math.Max(1,h-32);
        for(int k=0;k<=2;k++){double y=top+ph*k/2;var grid=new Line{X1=left,X2=w-8,Y1=y,Y2=y,Stroke=new SolidColorBrush(Color.FromRgb(233,237,244)),StrokeThickness=1};canvas.Children.Add(grid);var label=new TextBlock{Text=(high-(high-low)*k/2).ToString("F1",CultureInfo.InvariantCulture),FontSize=10,Foreground=Brushes.SlateGray};Canvas.SetTop(label,y-7);canvas.Children.Add(label);}
        var layer=new Canvas{Width=pw,Height=ph,ClipToBounds=true,IsHitTestVisible=false};Canvas.SetLeft(layer,left);Canvas.SetTop(layer,top);canvas.Children.Add(layer);
        foreach(var s in series) {
            var line=new Polyline{Stroke=s.color,StrokeThickness=1.7};
            foreach(var v in rows) {
                if(!double.IsFinite(v[s.index]))continue;
                line.Points.Add(new Point(pw*(v[0]-t0)/span,ph*(1-(v[s.index]-low)/(high-low))));
            }
            layer.Children.Add(line);
        }
        var time=new TextBlock{Text=FormattableString.Invariant($"{t0/1e6:0.##} – {end/1e6:0.##} s"),FontSize=10,Foreground=Brushes.SlateGray};Canvas.SetLeft(time,left);Canvas.SetTop(time,h-17);canvas.Children.Add(time);
    }
    private async void ClosingWindow(object? s,System.ComponentModel.CancelEventArgs e)
    {
        if(closing)return;e.Cancel=true;closing=true;await Disconnect();telemetryLog?.Dispose();eventLog?.Dispose();display.Stop();heartbeat.Stop();Close();
    }
}
