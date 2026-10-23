using System.Diagnostics;

namespace DroneMotion.Wpf.Models;

public sealed record PidFrame(double Seconds, double[] Values);

// Accessed on the WPF dispatcher. Copy each packet so plots retain real samples.
public sealed class PidHistory
{
    public const int Capacity=600;
    private readonly Queue<PidFrame> _frames=new();
    private uint? _previous;
    private double _seconds;
    private long _received;
    public bool IsPaused { get; set; }
    public event Action? Changed;
    public double AgeSeconds => _received==0 ? double.PositiveInfinity : Stopwatch.GetElapsedTime(_received).TotalSeconds;
    public PidFrame[] Snapshot() => _frames.ToArray();
    public void Clear() { _frames.Clear(); _previous=null; _seconds=0; _received=0; Changed?.Invoke(); }
    public bool Add(ControlTelemetryMessage packet)
    {
        var v=packet.Values;
        if (v.Length is not (19 or 25) || v.Any(n=>!double.IsFinite(n)) || v[0]<0 || v[0]>uint.MaxValue) return false;
        var timestamp=(uint)v[0];
        if (_previous is uint previous) {
            var delta=unchecked(timestamp-previous);
            if (delta==0) return false;
            if (delta>30_000_000) { _frames.Clear(); _seconds=0; }
            else _seconds+=delta/1_000_000.0;
        }
        _previous=timestamp; _received=Stopwatch.GetTimestamp();
        if (!IsPaused) {
            _frames.Enqueue(new(_seconds,(double[])v.Clone()));
            while(_frames.Count>Capacity) _frames.Dequeue();
            while(_frames.Count>1 && _seconds-_frames.Peek().Seconds>30) _frames.Dequeue();
            Changed?.Invoke();
        }
        return true;
    }
}
