using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Lab.Memory;
using Lab.Runtime;

namespace Lab.Overlay;

internal sealed record MetricsLayout(int Function, int FpsFormat, int LastFormat, int FirstReturn, int LastReturn, float SmallScale);

internal static class LabIndicator
{
    internal static string Format(LabSettings s)
    {
        string flags = (s.SpeedometersEnabled && (s.Current.Enabled || s.Landing.Enabled || s.Vertical.Enabled) ? "S" : "") +
            (s.LightingFix ? "L" : "") + (s.ModbotFix ? "M" : "") + (s.HideDriverName ? "H" : "") + (s.TintOpacity != 100 ? "R" : "");
        return flags.Length == 0 ? "LAB" : $"LAB ({flags})";
    }
}

// Filter the game's metrics formatter on its own rendering thread. DESRU's format
// strings are read after DESRU updates them; LAB never writes those strings.
internal sealed class PerformanceMetrics : IDisposable
{
    internal const string Magic = "LABMET01";
    internal const string RestartText = "RESTART GAME(LAB)";
    internal static readonly byte[] Prologue = Convert.FromHexString("4C8BDC5355565741564881EC70080000");
    private readonly Process _game;
    private readonly IntPtr _state, _mode;
    private int? _savedMode,_appliedMode;
    private bool _desru;
    private long _nextDetection;
    internal PerformanceMetrics(Process game, IntPtr state, IntPtr mode) { _game = game; _state = state; _mode = mode; }

    internal static PerformanceMetrics Attach(Process game, NativeGameLayout layout, int metricsOffset)
    {
        var m = layout.Metrics ?? throw new NotSupportedException(layout.MetricsFailure ?? "Performance metrics are unavailable");
        IntPtr image = game.MainModule!.BaseAddress, site = image + m.Function;
        byte[] before = game.ReadBytes(site, 16) ?? throw new IOException("Cannot read metrics formatter");
        if(before.AsSpan(0, 6).SequenceEqual(Convert.FromHexString("FF2500000000"))) {
            IntPtr existing = new(BitConverter.ToInt64(before, 6));
            if(Encoding.ASCII.GetString(game.ReadBytes(existing + 0x700, 8) ?? []) != Magic)
                throw new NotSupportedException("Another metrics formatter hook is installed");
            return new(game, existing + 4096, image + metricsOffset);
        }
        if(!before.SequenceEqual(Prologue)) throw new NotSupportedException("The metrics formatter was changed by another program");
        IntPtr memory = game.AllocateMemory(8192);
        if(memory == IntPtr.Zero) throw new IOException("Cannot allocate metrics adapter");
        bool referenced = false;
        try {
            IntPtr state = memory + 4096, trampoline = memory + 0x800;
            var data = new byte[4096];
            BitConverter.GetBytes((image + metricsOffset).ToInt64()).CopyTo(data, 16);
            BitConverter.GetBytes(m.SmallScale).CopyTo(data, 24);
            Encoding.ASCII.GetBytes("%s\0").CopyTo(data, 128);
            Encoding.ASCII.GetBytes("%i FPS\0").CopyTo(data, 160);
            byte[] code = MetricsCode.Create(state.ToInt64(), trampoline.ToInt64(),
                NativeModbotInputGuard.RemoteApi(game, "kernel32.dll", "GetTickCount64").ToInt64(),
                (image + m.FpsFormat).ToInt64(), (image + m.LastFormat).ToInt64(),
                (image + m.FirstReturn).ToInt64(), (image + m.LastReturn).ToInt64());
            if(!game.WriteBytes(state, data) || !game.WriteBytes(memory, code) ||
                !game.WriteBytes(memory + 0x700, Encoding.ASCII.GetBytes(Magic)) ||
                !game.WriteBytes(trampoline, Prologue.Concat(Jump((site + 16).ToInt64())).ToArray()) ||
                !game.VirtualProtect(memory, 4096, MemPageProtect.PAGE_EXECUTE_READ) ||
                !FlushInstructionCache(game.Handle, memory, (UIntPtr)4096)) throw new IOException("Cannot initialize metrics adapter");
            if(!(game.ReadBytes(site, 16) ?? []).SequenceEqual(before)) throw new IOException("Metrics formatter changed during attach");
            if(!game.VirtualProtect(site, 16, MemPageProtect.PAGE_EXECUTE_READWRITE, out var protection)) throw new IOException("Cannot protect metrics formatter");
            try {
                // Keep the allocation even if a partial write makes ownership uncertain.
                referenced = true;
                if(!game.WriteBytes(site, Jump(memory.ToInt64()).Concat(new byte[] { 0x90, 0x90 }).ToArray())) throw new IOException("Cannot attach metrics adapter");
                FlushInstructionCache(game.Handle, site, (UIntPtr)16);
            } finally { game.VirtualProtect(site, 16, protection); }
            return new(game, state, image + metricsOffset);
        } finally { if(!referenced) game.FreeMemory(memory); }
    }

    private static byte[] Jump(long target) => Convert.FromHexString("FF2500000000").Concat(BitConverter.GetBytes(target)).ToArray();

    internal void Update(LabSettings settings)
    {
        if(Environment.TickCount64 >= _nextDetection) {
            _nextDetection = Environment.TickCount64 + 500;
            bool detected = false;
            foreach(string name in new[] { "DESRU", "DESpeedrunUtil" })
                foreach(var process in Process.GetProcessesByName(name)) { using(process) { if(!process.HasExited) detected = true; } }
            if(detected && !_desru) RestoreMode();
            _desru = detected;
        }
        if(!_desru && _game.ReadValue<int>(_mode, out int mode) && mode is >= 0 and <= 6) {
            _savedMode ??= mode;
            if(mode != 2) { if(!_game.WriteBytes(_mode, BitConverter.GetBytes(2))) throw new IOException("Cannot enable LAB performance metrics"); _appliedMode=2; }
        }
        byte[] flags = new byte[8];
        BitConverter.GetBytes(_desru ? 1 : 0).CopyTo(flags, 0);
        BitConverter.GetBytes(settings.HideDriverName ? 1 : 0).CopyTo(flags, 4);
        if(!_game.WriteBytes(_state + 8, flags) ||
            !_game.WriteBytes(_state + 64, Encoding.ASCII.GetBytes(LabIndicator.Format(settings) + "\0")) ||
            !_game.WriteBytes(_state, BitConverter.GetBytes(Environment.TickCount64 + 3000))) throw new IOException("Cannot update LAB indicator");
    }

    internal void UpdateRestartNotice()
    {
        // Only insert the requested notice. The existing coexistence path retains
        // original metrics text and the driver row; no normal LAB settings apply.
        if(_game.ReadValue<int>(_mode,out int mode)&&mode==0){
            _savedMode??=mode;_appliedMode=1;
            if(!_game.WriteBytes(_mode,BitConverter.GetBytes(1)))throw new IOException("Cannot display restart notice");
        }
        if(!_game.WriteBytes(_state+8,BitConverter.GetBytes(1).Concat(new byte[4]).ToArray())||
            !_game.WriteBytes(_state+64,Encoding.ASCII.GetBytes(RestartText+"\0"))||
            !_game.WriteBytes(_state,BitConverter.GetBytes(Environment.TickCount64+3000)))throw new IOException("Cannot update restart notice");
    }

    private void RestoreMode()
    {
        if(_savedMode is int mode && _game.ReadValue<int>(_mode, out int current) && current == _appliedMode)
            _game.WriteBytes(_mode, BitConverter.GetBytes(mode));
        _savedMode = _appliedMode = null;
    }
    public void Dispose()
    {
        try { if(!_game.HasExited) { _game.WriteBytes(_state, new byte[8]); RestoreMode(); } }
        catch(Exception e) when(e is InvalidOperationException or System.ComponentModel.Win32Exception) { }
        // Retain executable memory while a game thread may still be in the adapter.
        // A zero/expired lease passes every original call through without filtering.
    }
    [DllImport("kernel32.dll")] private static extern bool FlushInstructionCache(IntPtr process, IntPtr address, UIntPtr size);
}

internal static class MetricsCode
{
    internal static byte[] Create(long state, long original, long clock, long fps, long last, long firstReturn, long lastReturn)
    {
        var c = new NativeSpeedometerCode.Code();
        // Three saves and 0x90 bytes align every call. Copy eight stack arguments,
        // including doubles and pointers, without interpreting printf varargs.
        c.Emit("535657 4881EC90000000 48894C2470 4889542478 4C89842480000000 4C898C2488000000 F30F119C2488000000");
        c.Emit("488DB424D0000000 488D7C2420 B908000000 F348A5 48BB"); c.Pointer(state);
        c.Emit("488B8424A8000000 49BA"); c.Pointer(firstReturn); c.Emit("4C39D0"); c.Jump("0F82", "forward");
        c.Emit("49BA"); c.Pointer(lastReturn); c.Emit("4C39D0"); c.Jump("0F87", "forward");
        c.Call(clock); c.Emit("483B03"); c.Jump("0F83", "forward");
        c.Emit("488B742428 48B8"); c.Pointer(fps); c.Emit("4839C6"); c.Jump("0F84", "fps");
        c.Emit("837B0800"); c.Jump("0F85", "desru");
        c.Emit("48B8"); c.Pointer(fps + 8); c.Emit("4839C6"); c.Jump("0F84", "marker");
        c.Jump("E9", "done");
        c.Label("fps"); c.Emit("48C7432000000000 837B0800"); c.Jump("0F85", "fpsOriginal");
        c.Emit("488D83A0000000 4889442428"); c.Label("fpsOriginal"); CallOriginal();
        c.Emit("488B4310 833802"); c.Jump("0F8C", "marker"); c.Jump("E9", "done");
        c.Label("desru");
        c.Emit("48B8"); c.Pointer(last); c.Emit("4839C6"); c.Jump("0F84", "last");
        // DESRU's empty format rows must not consume vertical space.
        c.Emit("803E00"); c.Jump("0F84", "done");
        c.Emit("66813E2573"); c.Jump("0F85", "forward");
        c.Emit("807E0200"); c.Jump("0F85", "forward");
        c.Emit("488B442430 4885C0"); c.Jump("0F84", "forward");
        c.Emit("803800"); c.Jump("0F84", "done");
        // Match the driver label, not an AMD CPU or another DESRU text row.
        foreach(var part in new[] { (0,"amd "), (4,"prop"), (8,"riet"), (12,"ary ") }) {
            c.Emit(part.Item1 == 0 ? "448B10" : "448B50" + part.Item1.ToString("X2"));
            c.Emit("4181CA20202020 4181FA"); c.Bytes.AddRange(Encoding.ASCII.GetBytes(part.Item2)); c.Jump("0F85", "forward");
        }
        c.Emit("48894320"); c.Jump("E9", "done");
        c.Label("last"); c.Emit("803E00"); c.Jump("0F84", "afterLast"); CallOriginal();
        c.Label("afterLast"); Marker();
        c.Emit("837B0C00"); c.Jump("0F85", "done");
        c.Emit("488B4320 4885C0"); c.Jump("0F84", "done");
        c.Emit("4889442430 488D8380000000 4889442428"); CallOriginal(); c.Jump("E9", "done");
        c.Label("marker"); Marker(); c.Jump("E9", "done");
        c.Label("forward"); CallOriginal();
        c.Label("done"); c.Emit("4881C490000000 5F5E5B C3");
        byte[] bytes = c.Finish();
        if(bytes.Length >= 0x700) throw new InvalidOperationException("Metrics adapter exceeds its code region");
        return bytes;

        void CallOriginal() {
            c.Emit("488B4C2470 488B542478 4C8B842480000000 4C8B8C2488000000 66410F6ED9"); c.Call(original);
        }
        void Marker() {
            c.Emit("488D4340 4889442428 8B4318 89842488000000 48C744243000000000"); CallOriginal();
        }
    }
}
