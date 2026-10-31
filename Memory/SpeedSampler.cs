using System.Diagnostics;
using System.Runtime.InteropServices;
namespace Lab.Memory;
internal sealed record SpeedSnapshot(float? Horizontal,float? Landing,float? Vertical)
{internal static readonly SpeedSnapshot Empty=new(null,null,null);}
internal sealed class SpeedSampler:IDisposable
{
    private sealed record Context(IntPtr Address,int ContactOffset,bool Active);
    private readonly Process _game;private readonly Thread _thread;private readonly CancellationTokenSource _stop=new();
    private Context _context=new(IntPtr.Zero,0,false);private SpeedSnapshot _snapshot=SpeedSnapshot.Empty;
    internal SpeedSnapshot Snapshot=>Volatile.Read(ref _snapshot);
    internal SpeedSampler(Process game){_game=game;_thread=new Thread(Run){IsBackground=true,Name="LAB velocity reader"};_thread.Start();}
    internal void SetContext(IntPtr address,int contactOffset,bool active)=>Volatile.Write(ref _context,new(address,contactOffset,active&&address!=IntPtr.Zero));
    internal static float? VerticalMagnitude(float z)=>float.IsFinite(z)?Math.Abs(z):null;
    private void Run()
    {
        var tracker=new HorizontalSpeedTracker(8,8);var clock=Stopwatch.StartNew();IntPtr last=IntPtr.Zero;bool timer=false;
        try{while(!_stop.IsCancellationRequested){var c=Volatile.Read(ref _context);if(!c.Active){tracker.Reset();last=IntPtr.Zero;Volatile.Write(ref _snapshot,SpeedSnapshot.Empty);if(timer){timeEndPeriod(1);timer=false;}_stop.Token.WaitHandle.WaitOne(20);continue;}if(!timer)timer=timeBeginPeriod(1)==0;if(c.Address!=last)tracker.Reset();last=c.Address;float? vertical=null;
            int length=c.ContactOffset>0?Math.Max(12,c.ContactOffset+1):12;if(_game.ReadBytes(c.Address,length,out var b)&&float.IsFinite(BitConverter.ToSingle(b,8))&&(c.ContactOffset<=0||b[c.ContactOffset]<=1)){tracker.Update(BitConverter.ToSingle(b),BitConverter.ToSingle(b,4),c.ContactOffset>0?b[c.ContactOffset]!=0:null,clock.Elapsed.TotalMilliseconds);vertical=VerticalMagnitude(BitConverter.ToSingle(b,8));}else tracker.Reset();
            Volatile.Write(ref _snapshot,new(tracker.Current,tracker.LastLanding,vertical));Thread.Sleep(1);
        }}catch(Exception e){Runtime.Log.Warning(e,"Velocity sampling stopped");}finally{if(timer)timeEndPeriod(1);Volatile.Write(ref _snapshot,SpeedSnapshot.Empty);}
    }
    public void Dispose(){_stop.Cancel();_thread.Join();_stop.Dispose();}
    [DllImport("winmm.dll")]private static extern uint timeBeginPeriod(uint n);
    [DllImport("winmm.dll")]private static extern uint timeEndPeriod(uint n);
}
