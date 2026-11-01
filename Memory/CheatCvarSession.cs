using Lab.Overlay;
using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace Lab.Memory;

// Display/session snapshot logic from DESRU-Enhanced's CheatCvarSession.
// LAB starts observation on attach; it does not toggle or restore cheats.
internal sealed class CheatCvarSession
{
    private const string Magic = "LABCVR01";
    private readonly Process _game;
    private readonly IntPtr _owner;
    private readonly ProtectedCvarMonitor _monitor;
    private IReadOnlyList<CvarDifference> _baseline = Array.Empty<CvarDifference>();
    private readonly SemaphoreSlim _begin = new(1, 1);
    private IReadOnlyList<CvarDifference>? _displaySource;
    private IReadOnlyList<CvarDifference> _displayValues = Array.Empty<CvarDifference>();
    internal bool Active { get; private set; }

    internal CheatCvarSession(Process game, IntPtr owner, ProtectedCvarMonitor monitor)
    {
        _game = game; _owner = owner; _monitor = monitor;
        IntPtr storage = game.ReadPointer(owner + NativeSpeedometerCode.CvarSessionPointerOffset);
        if(storage == IntPtr.Zero) return;
        byte[] header = game.ReadBytes(storage, 16) ?? throw new InvalidOperationException("Cannot read cheat cvar session");
        if(Encoding.ASCII.GetString(header, 0, 8) != Magic) throw new NotSupportedException("Unknown cheat cvar session");
        Active = BitConverter.ToInt32(header, 8) == 1;
        int size = BitConverter.ToInt32(header, 12);
        if(size < 2 || size > 16 * 1024 * 1024) throw new InvalidOperationException("Invalid cheat cvar session size");
        _baseline = JsonSerializer.Deserialize<CvarDifference[]>(game.ReadBytes(storage + 16, size) ?? throw new InvalidOperationException("Cannot read initial cvar values"))
            ?? throw new InvalidOperationException("Invalid initial cvar values");
    }

    internal async Task BeginAsync()
    {
        await _begin.WaitAsync();
        try {
            if(Active) return;
            var baseline = await Task.Run(_monitor.Read);
            byte[] text = JsonSerializer.SerializeToUtf8Bytes(baseline);
            var data = new byte[16 + text.Length];
            Encoding.ASCII.GetBytes(Magic).CopyTo(data, 0);
            BitConverter.GetBytes(1).CopyTo(data, 8); BitConverter.GetBytes(text.Length).CopyTo(data, 12); text.CopyTo(data, 16);
            IntPtr storage = _game.AllocateMemory(data.Length);
            if(storage == IntPtr.Zero || !_game.WriteBytes(storage, data) || !_game.VirtualProtect(storage, data.Length, MemPageProtect.PAGE_READWRITE) ||
                !_game.WriteBytes(_owner + NativeSpeedometerCode.CvarSessionPointerOffset, BitConverter.GetBytes(storage.ToInt64())))
                throw new InvalidOperationException("Cannot preserve initial cvar values");
            _baseline = baseline; Active = true;
            _displaySource = null;
        } finally { _begin.Release(); }
    }

    internal static IReadOnlyList<CvarDifference> DisplayChanges(IReadOnlyList<CvarDifference> baseline, IReadOnlyList<CvarDifference> current)
    {
        // Initial nondefault values are preserved preferences, not cheat-session
        // changes. Keep these names out of the HUD even if changed again later.
        var preserved = new HashSet<string>(baseline.Select(c => c.Name), StringComparer.OrdinalIgnoreCase);
        return current.Where(c => !preserved.Contains(c.Name) && !ProtectedCvarMonitor.IsDefault(c.Value, c.Default, c.Flags)).ToArray();
    }

    internal IReadOnlyList<CvarDifference> DisplayChanges(IReadOnlyList<CvarDifference> current)
    {
        if(!Active) return Array.Empty<CvarDifference>();
        if(!ReferenceEquals(current, _displaySource)) {
            _displayValues = DisplayChanges(_baseline, current);
            _displaySource = current;
        }
        return _displayValues;
    }
}
