using Lab.Runtime;

namespace Lab.Memory;

// Registry discovery and string reads run outside the 20 Hz HUD update. The
// game render callback only sees a published immutable page of text.
internal sealed class ProtectedCvarSampler : IDisposable
{
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _worker;
    private IReadOnlyList<CvarDifference> _snapshot = Array.Empty<CvarDifference>();
    internal IReadOnlyList<CvarDifference> Snapshot => Volatile.Read(ref _snapshot);

    internal ProtectedCvarSampler(ProtectedCvarMonitor monitor, CheatCvarSession session)
    {
        _worker = Task.Run(async () => {
            bool failed = false;
            try {
                while(!_stop.IsCancellationRequested) {
                    try {
                        await session.BeginAsync();
                        var current = session.DisplayChanges(monitor.Read());
                        if(!current.SequenceEqual(Snapshot)) Volatile.Write(ref _snapshot, current);
                        failed = false;
                    } catch(Exception e) when(e is IOException or InvalidOperationException or System.ComponentModel.Win32Exception) {
                        Volatile.Write(ref _snapshot, Array.Empty<CvarDifference>());
                        if(!failed) Log.Warning(e, "Cheat cvar monitor unavailable; other LAB features remain active");
                        failed = true;
                    }
                    await Task.Delay(500, _stop.Token);
                }
            } catch(OperationCanceledException) { }
        });
    }
    public void Dispose() { _stop.Cancel(); _worker.GetAwaiter().GetResult(); _stop.Dispose(); }
}
