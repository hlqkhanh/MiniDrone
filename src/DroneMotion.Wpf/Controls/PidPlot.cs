using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
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
    private readonly TextBlock _tooltip = new() { MaxWidth = 520, TextWrapping = TextWrapping.Wrap, Foreground = Brushes.Black };
    private PidFrame? _shownFrame;
    private PidPanelReadout[] _shownReadouts = PidPanelReadout.Create(null, "ROLL");
    private double _panelHeight;
    private const double HeaderHeight = 23;
    public PidPlot() {
        _timer=new DispatcherTimer(TimeSpan.FromMilliseconds(250),DispatcherPriority.Background,(_,_)=>InvalidateVisual(),Dispatcher);
        _timer.Stop();
        Loaded+=(_,_)=>_timer.Start(); Unloaded+=(_,_)=>_timer.Stop();
        ToolTip = _tooltip;
        ToolTipService.SetInitialShowDelay(this, 300);
        ToolTipService.SetShowDuration(this, 30000);
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
        if(ActualWidth<180 || ActualHeight<240) return;
        var frames=History?.Snapshot() ?? [];
        var latest = frames.LastOrDefault();
        var readouts = PidPanelReadout.Create(latest, Axis);
        var paused = History?.IsPaused == true;
        var stale = latest is not null && History!.AgeSeconds > 2;
        string state = paused ? "PAUSED" : latest is null ? "WAITING" : stale ? "STALE" : "LIVE";
        var status = latest is null ? $"{state} · {Axis}" :
            $"{state} · {Axis} · sample {(latest.Values[18] == 1 ? "RUNNING" : "STOPPED")} · {latest.AgeSeconds:F1} s";
        TrimmedLabel(dc, status, 12, 3, ActualWidth - 24, 10.5, paused || stale ? Amber : TextBrush);
        _shownFrame = latest;
        _shownReadouts = readouts;
        int angle=Axis=="PITCH" ? 2 : 1, rate=Axis=="PITCH" ? 4 : 3, target=Axis=="PITCH" ? 6 : 5;
        int p=Axis=="PITCH" ? 10 : 7, rateTarget=Axis=="PITCH" ? 20 : 19, output=Axis=="PITCH" ? 22 : 21;
        var panels=new (string title,(string name,int column,Brush brush)[] lines,bool pwm)[] {
            ("ANGLE (°)",[("Measured",angle,Cyan),("Target",target,Amber)],false),
            ("BODY RATE (°/s)",[("Measured",rate,Cyan),("Target",rateTarget,Amber)],false),
            ("RATE PID (duty)",[("P",p,Cyan),("I",p+1,Green),("D",p+2,Purple),("Output",output,Brushes.White)],false),
            ("MOTORS (duty)",[("LF",13,Cyan),("RF",14,Amber),("LR",15,Green),("RR",16,Purple)],true)
        };
        double panelHeight=(ActualHeight-HeaderHeight)/4;
        _panelHeight = panelHeight;
        double end=frames.Length==0 ? 30 : Math.Max(30,frames[^1].Seconds), start=end-30;
        for(int n=0;n<4;n++) {
            double top=HeaderHeight+n*panelHeight;
            var panel=panels[n];
            var readout = readouts[n];
            dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromRgb(17,28,47)),
                new Pen(readout.Warning ? Amber : GridBrush, 0.8),
                new Rect(6, top, ActualWidth - 12, panelHeight - 4), 4, 4);
            TrimmedLabel(dc,panel.title,14,top+4,140,11,TextBrush);
            var compact = readout.Warning ? (n == 3 ? "MIXER CLIPPED" : "AT LIMIT") : readout.CompactDetail;
            TrimmedLabel(dc,compact,158,top+5,ActualWidth-174,10,readout.Warning ? Amber : TextBrush,TextAlignment.Right);
            const double left = 56;
            var chartWidth = ActualWidth - left - 16;
            double slotWidth=chartWidth/readout.Values.Length, legendY=top+22;
            for (var i = 0; i < readout.Values.Length; i++) {
                var series = i < panel.lines.Length;
                var color = series ? panel.lines[i].brush : TextBrush;
                var value = readout.Values[i].Contains("not reported") ? "—" : readout.Values[i].Replace("unavailable", "—");
                if (panel.pwm && value.EndsWith(".00",StringComparison.Ordinal)) value=value[..^3];
                var caption = series ? $"{panel.lines[i].name} {value}" : value;
                double legendX=left+i*slotWidth;
                var marker=series ? 12 : 0;
                if (series) dc.DrawLine(new Pen(color,2),new Point(legendX,legendY+7),new Point(legendX+7,legendY+7));
                TrimmedLabel(dc,caption,legendX+marker,legendY,slotWidth-marker-5,10.5,color);
            }
            // Shared offsets keep all four plot areas aligned at every window size.
            var area=new Rect(left,top+43,chartWidth,Math.Max(1,panelHeight-62));
            double low=0,high=0;
            foreach(var frame in frames) foreach(var line in panel.lines) if(line.column<frame.Values.Length) {
                low=Math.Min(low,frame.Values[line.column]); high=Math.Max(high,frame.Values[line.column]);
            }
            if(panel.pwm) {
                low=0;
                // Retain sufficient scale for every visible configuration;
                // legacy frames have only the native 8-bit scale to go by.
                high=frames.Select(f=>f.Configuration?.Limits[9] ?? 255).DefaultIfEmpty(255).Max();
            }
            else { double margin=Math.Max(1,(high-low)*0.12); low-=margin; high+=margin; }
            int verticalTicks=area.Height<80 ? 2 : 4;
            for(int tick=0;tick<=verticalTicks;tick++) {
                double y=area.Top+area.Height*tick/verticalTicks;
                dc.DrawLine(new Pen(GridBrush,0.5),new Point(area.Left,y),new Point(area.Right,y));
                double value=high-(high-low)*tick/verticalTicks;
                string label=value.ToString(Math.Abs(value)>=10000 ? "0.#e0" : "0.#",CultureInfo.InvariantCulture);
                TrimmedLabel(dc,label,8,y-6,left-15,9,TextBrush,TextAlignment.Right);
            }
            for(int tick=0;tick<=6;tick++) {
                double x=area.Left+area.Width*tick/6;
                dc.DrawLine(new Pen(GridBrush,0.5),new Point(x,area.Top),new Point(x,area.Bottom));
                // One common time scale is enough for the vertically aligned charts.
                if(n==3) TrimmedLabel(dc,$"{(start+tick*5):0}s",x-12,area.Bottom+2,32,9,TextBrush);
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
    private FormattedText MakeText(string text, double size, Brush brush) =>
        new(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"),
            size, brush, VisualTreeHelper.GetDpi(this).PixelsPerDip);

    private void TrimmedLabel(DrawingContext dc,string text,double x,double y,double width,double size,Brush brush,
        TextAlignment alignment=TextAlignment.Left)
    {
        if(string.IsNullOrEmpty(text) || width<=0) return;
        var formatted = MakeText(text,size,brush);
        formatted.MaxTextWidth = width;
        formatted.MaxLineCount = 1;
        formatted.Trimming = TextTrimming.CharacterEllipsis;
        formatted.TextAlignment = alignment;
        dc.DrawText(formatted,new Point(x,y));
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var y=e.GetPosition(this).Y;
        var index=_panelHeight>0 ? (int)Math.Floor((y-HeaderHeight)/_panelHeight) : -1;
        var build=_shownFrame?.Configuration?.Build ?? "not received";
        var state=_shownFrame is null ? "Waiting for control telemetry."
            : $"Displayed sample: {(_shownFrame.Values[18]==1 ? "RUNNING" : "STOPPED")}; age {_shownFrame.AgeSeconds:F1} s.";
        _tooltip.Text=(index is >=0 and <4 ? _shownReadouts[index].Detail+"\n" : "") +
            $"{state}\nFirmware build: {build}\n30 s history · firmware angles use the controller level reference, independently of viewer Zero.\nPause freezes displayed samples; motors and CSV continue.";
    }
}
