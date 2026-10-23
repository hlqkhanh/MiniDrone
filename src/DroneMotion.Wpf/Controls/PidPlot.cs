using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using DroneMotion.Wpf.Models;

namespace DroneMotion.Wpf.Controls;

public sealed class PidPlot : FrameworkElement
{
    public static readonly DependencyProperty HistoryProperty=DependencyProperty.Register(nameof(History),typeof(PidHistory),typeof(PidPlot),new FrameworkPropertyMetadata(null,OnHistoryChanged));
    public static readonly DependencyProperty AxisProperty=DependencyProperty.Register(nameof(Axis),typeof(string),typeof(PidPlot),new FrameworkPropertyMetadata("ROLL",FrameworkPropertyMetadataOptions.AffectsRender));
    public PidHistory? History { get=>(PidHistory?)GetValue(HistoryProperty); set=>SetValue(HistoryProperty,value); }
    public string Axis { get=>(string)GetValue(AxisProperty); set=>SetValue(AxisProperty,value); }
    private readonly DispatcherTimer _timer;
    private static readonly Brush Cyan=new SolidColorBrush(Color.FromRgb(34,211,238));
    private static readonly Brush Amber=Brushes.Orange;
    private static readonly Brush Purple=Brushes.MediumPurple;
    private static readonly Brush Green=Brushes.LightGreen;
    private static readonly Brush TextBrush=new SolidColorBrush(Color.FromRgb(203,213,225));
    private static readonly Brush GridBrush=new SolidColorBrush(Color.FromRgb(51,65,85));
    public PidPlot() {
        _timer=new DispatcherTimer(TimeSpan.FromMilliseconds(250),DispatcherPriority.Background,(_,_)=>InvalidateVisual(),Dispatcher);
        _timer.Stop();
        Loaded+=(_,_)=>_timer.Start(); Unloaded+=(_,_)=>_timer.Stop();
    }
    private static void OnHistoryChanged(DependencyObject d,DependencyPropertyChangedEventArgs e) {
        var plot=(PidPlot)d;
        if(e.OldValue is PidHistory old) old.Changed-=plot.InvalidateVisual;
        if(e.NewValue is PidHistory current) current.Changed+=plot.InvalidateVisual;
        plot.InvalidateVisual();
    }
    protected override void OnRender(DrawingContext dc) {
        base.OnRender(dc);
        dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(15,23,42)),null,new Rect(RenderSize));
        if(ActualWidth<180 || ActualHeight<260) return;
        var frames=History?.Snapshot() ?? [];
        string state=History?.IsPaused==true ? "PAUSED" : frames.Length==0 ? "Waiting for firmware control telemetry" : History!.AgeSeconds>2 ? $"NO NEW DATA ({History.AgeSeconds:F1}s)" : "LIVE";
        Label(dc,$"{state}   |   {Axis}   |   last 30 s   |   firmware angles (independent of viewer Zero)",12,8);
        if(frames.Length==0) { Label(dc,"Connect ESP32. Plots show measured telemetry; no simulated data.",12,35); }
        int angle=Axis=="PITCH" ? 2 : 1, rate=Axis=="PITCH" ? 4 : 3, target=Axis=="PITCH" ? 6 : 5;
        int p=Axis=="PITCH" ? 10 : 7, rateTarget=Axis=="PITCH" ? 20 : 19, output=Axis=="PITCH" ? 22 : 21;
        var panels=new (string title,(string name,int column,Brush brush)[] lines,bool pwm)[] {
            ("ANGLE (degrees)",[("Measured",angle,Cyan),("Target",target,Amber)],false),
            ("BODY RATE (degrees/s)",[("Measured",rate,Cyan),("Target (C2)",rateTarget,Amber)],false),
            ("RATE PID (PWM duty units)",[("P",p,Cyan),("I",p+1,Green),("D",p+2,Purple),("Output limited (C2)",output,Brushes.White)],false),
            ("MOTOR OUTPUT (0–255 duty)",[("LF",13,Cyan),("RF",14,Amber),("LR",15,Green),("RR",16,Purple)],true)
        };
        double panelHeight=(ActualHeight-35)/4;
        double end=frames.Length==0 ? 30 : Math.Max(30,frames[^1].Seconds), start=end-30;
        for(int n=0;n<4;n++) {
            double top=35+n*panelHeight;
            var panel=panels[n];
            Label(dc,panel.title,12,top);
            double legendX=12;
            foreach(var line in panel.lines) {
                dc.DrawLine(new Pen(line.brush,2),new Point(legendX,top+25),new Point(legendX+12,top+25));
                Label(dc,line.name,legendX+16,top+17,10); legendX+=line.name.Length*6+42;
            }
            var area=new Rect(58,top+38,ActualWidth-76,Math.Max(12,panelHeight-61));
            double low=0,high=0;
            foreach(var frame in frames) foreach(var line in panel.lines) if(line.column<frame.Values.Length) {
                low=Math.Min(low,frame.Values[line.column]); high=Math.Max(high,frame.Values[line.column]);
            }
            if(panel.pwm) { low=0; high=255; }
            else { double margin=Math.Max(1,(high-low)*0.12); low-=margin; high+=margin; }
            for(int tick=0;tick<=4;tick++) {
                double y=area.Top+area.Height*tick/4;
                dc.DrawLine(new Pen(GridBrush,0.5),new Point(area.Left,y),new Point(area.Right,y));
                Label(dc,(high-(high-low)*tick/4).ToString("0.#",CultureInfo.InvariantCulture),4,y-6,10);
            }
            for(int tick=0;tick<=6;tick++) {
                double x=area.Left+area.Width*tick/6;
                dc.DrawLine(new Pen(GridBrush,0.5),new Point(x,area.Top),new Point(x,area.Bottom));
                Label(dc,$"{(start+tick*5):0}s",x-6,area.Bottom+2,10);
            }
            dc.PushClip(new RectangleGeometry(area));
            foreach(var line in panel.lines) {
                var geometry=new StreamGeometry();
                using(var context=geometry.Open()) {
                    double last=double.NegativeInfinity;
                    foreach(var frame in frames) {
                        if(frame.Seconds<start || line.column>=frame.Values.Length) continue;
                        var point=new Point(area.Left+(frame.Seconds-start)/30*area.Width,area.Bottom-(frame.Values[line.column]-low)/(high-low)*area.Height);
                        if(frame.Seconds-last>0.3) context.BeginFigure(point,false,false); else context.LineTo(point,true,false);
                        last=frame.Seconds;
                    }
                }
                geometry.Freeze(); dc.DrawGeometry(null,new Pen(line.brush,1.5),geometry);
            }
            dc.Pop();
        }
    }
    private void Label(DrawingContext dc,string text,double x,double y,double size=11) =>
        dc.DrawText(new FormattedText(text,CultureInfo.InvariantCulture,FlowDirection.LeftToRight,new Typeface("Segoe UI"),size,TextBrush,VisualTreeHelper.GetDpi(this).PixelsPerDip),new Point(x,y));
}
