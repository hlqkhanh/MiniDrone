using System.Globalization;
using System.IO;
using System.IO.Ports;
using System.Net.Sockets;
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
    private double[]? latest;
    public MainWindow()
    {
        InitializeComponent();
        heartbeat.Tick += async (_, _) => { if (runningHere && !stopping) await Act(async () => await Request("KEEPALIVE")); };
        display.Tick += (_, _) => { if (stream != null && (DateTime.UtcNow - received).TotalSeconds > 1) Status.Text = "Dữ liệu đã cũ · chưa xác định trạng thái board. Gửi STOP để kết thúc phiên."; UpdateUi(); Draw(); };
        display.Start(); heartbeat.Start();
        PreviewKeyDown += async (_, e) => { if (e.Key == Key.Escape) { e.Handled = true; await Stop(); } };
        Loaded += (_, _) => { UpdateUi(); Draw(); };
    }
    private void Event(string s) { eventLog?.WriteLine($"{DateTime.UtcNow:O},{s}"); Events.AppendText(s + "\n"); if (Events.Text.Length > 20000) Events.Text = Events.Text[^12000..]; Events.ScrollToEnd(); }
    private async Task Act(Func<Task> action) { try { await action(); } catch (Exception ex) { Event("ERROR: " + ex.Message); Status.Text = ex.Message; if (stream != null && !stopping) await Stop(); } }
    private async Task Send(string command, uint requestId)
    {
        Event($"TX,{requestId},{command}");
        var bytes = Encoding.ASCII.GetBytes($"{requestId} {command}\n");
        await writeLock.WaitAsync();
        try { if (stream == null) throw new IOException("Not connected"); using var timeout = new CancellationTokenSource(1500); await stream.WriteAsync(bytes, timeout.Token); await stream.FlushAsync(timeout.Token); }
        finally { writeLock.Release(); }
    }
    private async Task<string> Request(string command)
    {
        await commandLock.WaitAsync();
        uint n = ++id; var promise = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously); pending[n] = promise;
        try { await Send(command, n); var reply = await promise.Task.WaitAsync(TimeSpan.FromSeconds(2)); if (!reply.Contains(",OK,")) throw new IOException(reply); return reply; }
        finally { pending.Remove(n); commandLock.Release(); }
    }
    private async void ConnectClick(object sender, RoutedEventArgs e) => await Act(async () =>
    {
        if(connecting)return;connecting=true;UpdateUi();
        try {
            await Disconnect(); replay = false; history.Clear();latest=null;AcceptedSummary.Text="Chưa nhận cấu hình từ board.";
            if (Transport.SelectedIndex == 0) { serial = new SerialPort(Address.Text.Trim(), 460800) { DtrEnable = false, RtsEnable = false }; serial.Open(); stream = serial.BaseStream; }
            else { tcp = new TcpClient { NoDelay = true }; using var timeout = new CancellationTokenSource(5000); await tcp.ConnectAsync(Address.Text.Trim(), 3333, timeout.Token); stream = tcp.GetStream(); }
            cancellation = new CancellationTokenSource();received=DateTime.MinValue;
            _ = ReadLoop(stream, cancellation.Token); Status.Text = "Đã kết nối · đang chờ dữ liệu board"; await Request("INFO");
        } catch { await Disconnect();throw; }
        finally { connecting=false;UpdateUi(); }
    });
    private async Task ReadLoop(Stream input, CancellationToken token)
    {
        try
        {
            using var reader = new StreamReader(input, Encoding.ASCII, false, 4096, leaveOpen:true);
            while (!token.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync(token); if (line == null) throw new IOException("Connection closed");
                if (line.Length > 1000) throw new IOException("Oversized protocol line");
                await Dispatcher.InvokeAsync(() => Receive(line));
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { await Dispatcher.InvokeAsync(() => { state = "DISCONNECTED"; runningHere = armedHere = false; Status.Text = "Disconnected: " + ex.Message; Event(Status.Text); }); }
    }
    private void Receive(string line)
    {
        var f = line.Split(',');
        if (f.Length >= 5 && f[0] == "ACK" && uint.TryParse(f[1], out var n) && uint.TryParse(f[3], out var ep))
        { epoch=ep; Event(line); if (pending.TryGetValue(n, out var p)) p.TrySetResult(line); return; }
        if (f.Length == 45 && f[0] == "T")
        {
            telemetryLog?.WriteLine(line);
            var v = new double[17];
            int[] indices = [2,13,14,15,17,18,19,20,21,22,23,24,25,26,27,28,44];
            for (int k=0;k<indices.Length;k++) { if (indices[k]>=f.Length) v[k]=0; else if(!double.TryParse(f[indices[k]], NumberStyles.Float, CultureInfo.InvariantCulture, out v[k]) || !double.IsFinite(v[k])) { Event("Rejected invalid telemetry");return; } }
            received=DateTime.UtcNow;epoch=uint.Parse(f[4],CultureInfo.InvariantCulture);calibrated=f[34]=="1";latestMode=int.Parse(f[5],CultureInfo.InvariantCulture);
            if (state!=f[3]) Event($"STATE,{f[3]},epoch={epoch},reason={f[33]}");state=f[3];
            if(state!="RUNNING" && state!="ARMED") runningHere=armedHere=false;
            latest=v;history.Add(v); if(history.Count>1200)history.RemoveRange(0,history.Count-1200);
            Status.Text=$"Board: {StateText()} · {(calibrated?"IMU sẵn sàng":$"Hiệu chuẩn {f[6]}/500")} · {f[33]}";
            AcceptedSummary.Text=$"Board đã nhận: PID {f[37]} / {f[38]} / {f[39]} · P góc {f[40]} · Base {f[41]} · Sign {f[42]} · Limit {f[43]}";
            Readout.Text=FormattableString.Invariant($"Roll {v[1]:F2}° | Rate {v[2]:F2} / target {v[3]:F2}°/s | P/I/D {v[4]:F2}/{v[5]:F2}/{v[6]:F2}\nOutput {v[8]:F2} | LF/RF/LR/RR {v[9]}/{v[10]}/{v[11]}/{v[12]} | PID limit {v[13]} | Mixer clip {v[14]} | dt {v[15]*1000:F2} ms | missing {f[29]} | dropped {f[31]}\nBoard accepted: Kp/Ki/Kd {f[37]}/{f[38]}/{f[39]}, AngleP {f[40]}, base {f[41]}, sign {f[42]}, limit {f[43]}");
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
    private async void ApplyClick(object s,RoutedEventArgs e)=>await Act(async()=>{ Fresh();var values=new[]{Kp,Ki,Kd,AngleKp,BaseDuty,Sign,Limit}.Select(Num).Select(n=>n.ToString("G9",CultureInfo.InvariantCulture));await Request("CONFIG "+string.Join(' ',values)); });
    private async void ArmClick(object s,RoutedEventArgs e)=>await Act(async()=>{ Fresh();if(!calibrated)throw new InvalidOperationException("Wait for calibration");var op=operation;var armEpoch=epoch;await Request($"VERIFY {(Verified.IsChecked==true?1:0)}");if(op!=operation)return;await Request($"PROPS_REMOVED {(PropsRemoved.IsChecked==true?1:0)}");if(op!=operation)return;await Request($"ARM {armEpoch}");if(op==operation)armedHere=true; });
    private async void StartClick(object s,RoutedEventArgs e)=>await Act(async()=>{ Fresh();if(!armedHere||Verified.IsChecked!=true)throw new InvalidOperationException("Confirm rig/mapping, then ARM");var op=operation;await Request(FormattableString.Invariant($"START {epoch} {Mode.SelectedIndex} {Num(Target):G9} {Num(Duration):G9}"));if(op==operation){runningHere=true;armedHere=false;} });
    private async void BenchClick(object s,RoutedEventArgs e)=>await Act(async()=>{ Fresh();if(!armedHere||PropsRemoved.IsChecked!=true)throw new InvalidOperationException("Remove props, confirm, then ARM");var op=operation;await Request(FormattableString.Invariant($"BENCH {epoch} {Motor.SelectedIndex} {Num(BenchDuty):G9} 1000"));if(op==operation){runningHere=true;armedHere=false;} });
    private async void InfoClick(object s,RoutedEventArgs e)=>await Act(async()=>await Request("INFO"));
    private async void CalibrateClick(object s,RoutedEventArgs e)=>await Act(async()=>{ await Stop();await Request("CALIBRATE"); });
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
        await Stop();cancellation?.Cancel();stream?.Dispose();serial?.Dispose();tcp?.Dispose();stream=null;serial=null;tcp=null;
        foreach(var p in pending.Values)p.TrySetCanceled();runningHere=armedHere=false;state="DISCONNECTED";Status.Text="Chưa kết nối · motor chưa được ra lệnh";UpdateUi();
    }
    private async void DisconnectClick(object s,RoutedEventArgs e)=>await Act(Disconnect);
    private void LogClick(object s,RoutedEventArgs e)
    {
        try {
            if(telemetryLog!=null){telemetryLog.Dispose();eventLog?.Dispose();telemetryLog=eventLog=null;LogButton.Content="Bắt đầu ghi log";return;}
            var folder=System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),"DroneRebuildLogs",DateTime.Now.ToString("yyyyMMdd-HHmmss-fff"));Directory.CreateDirectory(folder);
            telemetryLog=new StreamWriter(System.IO.Path.Combine(folder,"telemetry.csv")){AutoFlush=true};eventLog=new StreamWriter(System.IO.Path.Combine(folder,"events.log")){AutoFlush=true};
            telemetryLog.WriteLine("type,seq,micros,state,epoch,mode,calCount,ax,ay,az,gx,gy,gz,roll,rollRate,rateTarget,error,p,i,d,raw,output,LF,RF,LR,RR,pidLimited,mixerLimited,dt,missing,late,dropTelemetry,dropReplies,reason,calibrated,verified,propsRemoved,kp,ki,kd,angleKp,base,motorSign,outputLimit,command");
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
        var lines=File.ReadAllLines(path);history.Clear();
        if(lines.Length<2 || !lines[0].StartsWith("t,angle,rate,target,"))throw new InvalidDataException("Select native_roll.csv from native simulation");
        foreach(var line in lines.Skip(1)){var f=line.Split(',');if(f.Length<16)continue;var a=new double[17];int[] ix=[0,1,2,3,4,5,6,7,8,9,10,11,12,13,14];for(int k=0;k<ix.Length;k++)a[k]=double.Parse(f[ix[k]],CultureInfo.InvariantCulture);a[0]*=1e6;history.Add(a);}
        replay=true;latestMode=1;Status.Text="MÔ PHỎNG · phát lại dữ liệu giả định, không kết nối phần cứng";latest=history.LastOrDefault();
        Mode.SelectedIndex=1;Readout.Text=latest==null?"No simulation samples":FormattableString.Invariant($"SYNTHETIC DATA · {history.Count} samples · final Roll {latest[1]:F3}° · rate {latest[2]:F3}°/s\nHardware gains above remain unchanged. Replay does not validate the real drone.");Draw();
    }
    internal void SetPreviewScenario(string scenario)
    {
        if(scenario=="bench")TestTabs.SelectedIndex=1;
        if(scenario=="empty") { history.Clear();latest=null;replay=false;Status.Text="Chưa kết nối · chưa có dữ liệu từ board"; }
        UpdateUi();Draw();
    }
    private string StateText() => state switch { "RUNNING"=>"Đang chạy", "ARMED"=>"Đã ARM", "FAULT"=>"Lỗi", "DISARMED"=>"Đã dừng", _=>"Chưa kết nối" };
    private void UpdateUi()
    {
        if(!IsInitialized || ConnectButton==null)return;
        bool connected=stream!=null, fresh=connected && state!="DISCONNECTED" && (DateTime.UtcNow-received).TotalSeconds<=1;
        bool busy=armedHere || runningHere || state is "ARMED" or "RUNNING";
        bool bench=TestTabs.SelectedIndex==1;
        ConnectionBadge.Text=replay?"MÔ PHỎNG":connecting?"ĐANG KẾT NỐI":!connected?"CHƯA KẾT NỐI":!fresh?"CHỜ DỮ LIỆU":StateText().ToUpperInvariant();
        ConnectionBadge.Foreground=replay?Brushes.DarkOrange:runningHere?Brushes.SeaGreen:Brushes.SlateGray;
        ConnectButton.IsEnabled=!connected && !connecting;DisconnectButton.IsEnabled=connected && !connecting;
        Transport.IsEnabled=Address.IsEnabled=!connected && !connecting;
        ApplyButton.IsEnabled=fresh && !busy;
        ArmButton.IsEnabled=fresh && calibrated && !busy && (bench?PropsRemoved.IsChecked==true:Verified.IsChecked==true);
        StartButton.IsEnabled=fresh && armedHere && Verified.IsChecked==true && !bench && !stopping;
        BenchButton.IsEnabled=fresh && armedHere && PropsRemoved.IsChecked==true && bench && !stopping;
        StartButton.Visibility=bench?Visibility.Collapsed:Visibility.Visible;BenchButton.Visibility=bench?Visibility.Visible:Visibility.Collapsed;
        foreach(var control in new Control[]{Kp,Ki,Kd,AngleKp,BaseDuty,Sign,Limit,Mode,Target,Duration,Motor,BenchDuty})control.IsEnabled=!busy;
        AngleKp.IsEnabled=!busy && Mode.SelectedIndex==1;
        RunHint.Text=replay?"Đang xem dữ liệu mô phỏng; chưa thể ARM.":!fresh?"Kết nối và đợi dữ liệu mới từ board.":!calibrated?"Giữ board đứng yên để hoàn tất hiệu chuẩn.":runningHere?"Đang chạy · Esc hoặc DỪNG MOTOR để dừng.":armedHere?(bench?"Đã ARM · bấm Chạy motor trong 15 giây.":"Đã ARM · bấm Bắt đầu Roll trong 15 giây."):bench?"Tháo cánh, đánh dấu xác nhận, rồi ARM.":Verified.IsChecked!=true?"Hoàn tất kiểm tra IMU, chiều lực và đế trước khi xác nhận.":"Áp dụng cấu hình, chờ board xác nhận, rồi ARM.";
        if(latest!=null) {
            RollValue.Text=latest[1].ToString("F2",CultureInfo.InvariantCulture)+"°";
            RateValue.Text=latest[2].ToString("F2",CultureInfo.InvariantCulture);
            RateTargetValue.Text=FormattableString.Invariant($"Mục tiêu: {latest[3]:F2} °/s");
            OutputValue.Text=latest[8].ToString("F2",CultureInfo.InvariantCulture);
            LimitValue.Text=latest[13]!=0?"PID chạm giới hạn":latest[14]!=0?"Mixer bị cắt lệnh":"Trong giới hạn đã báo";
        } else { RollValue.Text=RateValue.Text=OutputValue.Text="—";RateTargetValue.Text="Mục tiêu: — °/s";LimitValue.Text="Chưa có dữ liệu"; }
        AngleLegend.Text=latestMode==1?"Xanh: đo · Cam: mục tiêu góc":"Xanh: góc đo · RATE không giữ góc";
    }
    private void TransportChanged(object s,SelectionChangedEventArgs e)
    {
        if(Address==null || ConnectionHint==null)return;
        if(Transport.SelectedIndex==1) { if(Address.Text.StartsWith("COM",StringComparison.OrdinalIgnoreCase))Address.Text="192.168.4.1";ConnectionHint.Text="Wi-Fi ESP32 · TCP 3333"; }
        else { if(Address.Text.Contains('.'))Address.Text="COM6";ConnectionHint.Text="USB · 460800 baud"; }
    }
    private void ModeChanged(object s,SelectionChangedEventArgs e) { if(TargetLabel==null)return;TargetLabel.Text=Mode.SelectedIndex==1?"Mục tiêu · độ (°)":"Mục tiêu · °/s";UpdateUi(); }
    private async void TestTabChanged(object s,SelectionChangedEventArgs e) { if(!IsLoaded || e.Source!=TestTabs)return;if(armedHere || runningHere)await Stop();UpdateUi(); }
    private void PlotResize(object s,SizeChangedEventArgs e)=>Draw();
    private void Draw()
    {
        if(MotorPlot==null)return;
        Plot(AnglePlot,latestMode==1?[(1,Brushes.DodgerBlue),(16,Brushes.DarkOrange)]:[(1,Brushes.DodgerBlue)]);
        Plot(RatePlot,[(2,Brushes.DodgerBlue),(3,Brushes.DarkOrange)]);
        Plot(OutputPlot,[(8,Brushes.MediumPurple)]);
        Plot(MotorPlot,[(9,Brushes.SeaGreen),(10,Brushes.Gray),(11,Brushes.DarkCyan),(12,Brushes.SlateGray)]);
    }
    private void Plot(Canvas canvas,(int index,Brush color)[] series)
    {
        canvas.Children.Clear();double w=canvas.ActualWidth,h=canvas.ActualHeight;if(w<30||h<30)return;
        if(history.Count<2) { var hint=new TextBlock{Text="Chưa có dữ liệu",Foreground=Brushes.SlateGray,FontSize=12};Canvas.SetLeft(hint,16);Canvas.SetTop(hint,Math.Max(12,h/2-10));canvas.Children.Add(hint);return; }
        var rows=history.Where(v=>v[0]>=history[^1][0]-12000000).ToArray();if(rows.Length<2)return;
        double low=series.Min(s=>rows.Min(v=>v[s.index])),high=series.Max(s=>rows.Max(v=>v[s.index]));if(high-low<1){high+=.5;low-=.5;}double pad=(high-low)*.1;low-=pad;high+=pad;
        double t0=rows[0][0],span=Math.Max(1,rows[^1][0]-t0);
        const double left=43,top=8;double pw=Math.Max(1,w-left-8),ph=Math.Max(1,h-32);
        for(int k=0;k<=2;k++){double y=top+ph*k/2;var grid=new Line{X1=left,X2=w-8,Y1=y,Y2=y,Stroke=new SolidColorBrush(Color.FromRgb(233,237,244)),StrokeThickness=1};canvas.Children.Add(grid);var label=new TextBlock{Text=(high-(high-low)*k/2).ToString("F1",CultureInfo.InvariantCulture),FontSize=10,Foreground=Brushes.SlateGray};Canvas.SetTop(label,y-7);canvas.Children.Add(label);}
        foreach(var s in series){var line=new Polyline{Stroke=s.color,StrokeThickness=1.7};foreach(var v in rows)line.Points.Add(new Point(left+pw*(v[0]-t0)/span,top+ph*(1-(v[s.index]-low)/(high-low))));canvas.Children.Add(line);}
        var time=new TextBlock{Text=$"{span/1e6:F1} giây gần nhất",FontSize=10,Foreground=Brushes.SlateGray};Canvas.SetLeft(time,left);Canvas.SetTop(time,h-17);canvas.Children.Add(time);
    }
    private async void ClosingWindow(object? s,System.ComponentModel.CancelEventArgs e)
    {
        if(closing)return;e.Cancel=true;closing=true;await Disconnect();telemetryLog?.Dispose();eventLog?.Dispose();display.Stop();heartbeat.Stop();Close();
    }
}
