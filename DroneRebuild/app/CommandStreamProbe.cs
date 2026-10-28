using System.IO;
using System.Text;
namespace DroneRebuild;

// In-memory regression probe, used only by --connection-test.
internal sealed class CommandStreamProbe(bool stalled) : Stream
{
    private readonly TaskCompletionSource stalledWrite=new(TaskCreationOptions.RunContinuationsAsynchronously);
    public string Written { get; private set; }="";
    public override bool CanRead=>false;
    public override bool CanSeek=>false;
    public override bool CanWrite=>true;
    public override long Length=>throw new NotSupportedException();
    public override long Position { get=>throw new NotSupportedException();set=>throw new NotSupportedException(); }
    public override int Read(byte[] b,int o,int n)=>throw new NotSupportedException();
    public override long Seek(long n,SeekOrigin origin)=>throw new NotSupportedException();
    public override void SetLength(long n)=>throw new NotSupportedException();
    public override void Write(byte[] b,int o,int n)=>Written+=Encoding.ASCII.GetString(b,o,n);
    public override ValueTask WriteAsync(ReadOnlyMemory<byte> b,CancellationToken token=default)
    {
        Written+=Encoding.ASCII.GetString(b.Span);
        return stalled?new ValueTask(stalledWrite.Task):ValueTask.CompletedTask;
    }
    public override void Flush()=>throw new InvalidOperationException("Serial commands must not FlushFileBuffers");
    public override Task FlushAsync(CancellationToken token)=>throw new InvalidOperationException("Serial commands must not FlushFileBuffers");
    protected override void Dispose(bool disposing){if(disposing)stalledWrite.TrySetCanceled();base.Dispose(disposing);}
}
