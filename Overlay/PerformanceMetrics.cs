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
        string flags = (s.ModbotFix ? "M" : "") + (s.TintOpacity != 100 ? "R" : "");
        return flags.Length == 0 ? "LAB" : $"LAB ({flags})";
    }
}

// Filter the game's metrics formatter on its own rendering thread. DESRU's format
// strings are read after DESRU updates them; LAB never writes those strings.
internal sealed class PerformanceMetrics : IDisposable
{
    internal const string Magic = "LABMET06";
    internal const int HardwareCountOffset=192, HardwareNamesOffset=320, HardwareNameSize=256, HardwareNameLimit=14;
    internal static void StoreHardwareNames(byte[] data,IReadOnlyList<string> names)
    {
        string[] accepted=names.Where(n=>!string.IsNullOrEmpty(n)&&Encoding.UTF8.GetByteCount(n)<HardwareNameSize).Distinct(StringComparer.OrdinalIgnoreCase).Take(HardwareNameLimit).ToArray();
        BitConverter.GetBytes(accepted.Length).CopyTo(data,HardwareCountOffset);
        for(int i=0;i<accepted.Length;i++)Encoding.UTF8.GetBytes(accepted[i]+"\0").CopyTo(data,HardwareNamesOffset+i*HardwareNameSize);
    }
    internal const float CvarFontScale = .75f;
    internal const int CvarBankOffset = 40, CvarAcknowledgedOffset = 48;
    internal const int CvarRecordSize = 1024;
    internal const string RestartText = "RESTART GAME(LAB)";
    internal static readonly byte[] Prologue = Convert.FromHexString("4C8BDC5355565741564881EC70080000");
    private readonly Process _game;
    private readonly IntPtr _state, _mode;
    private int? _savedMode,_appliedMode;
    private bool _desru;
    private long _nextDetection;
    private string[] _publishedLines = [];
    private readonly List<IntPtr> _cvarBanks = [];
    internal int CvarBankCount => _cvarBanks.Count;
    private float _smallScale = 1;
    internal PerformanceMetrics(Process game, IntPtr state, IntPtr mode) { _game = game; _state = state; _mode = mode; }

    internal static PerformanceMetrics Attach(Process game, NativeGameLayout layout, int metricsOffset)
    {
        var m = layout.Metrics ?? throw new NotSupportedException(layout.MetricsFailure ?? "Performance metrics are unavailable");
        IntPtr image = game.MainModule!.BaseAddress, site = image + m.Function;
        byte[] before = game.ReadBytes(site, 16) ?? throw new IOException("Cannot read metrics formatter");
        if(before.AsSpan(0, 6).SequenceEqual(Convert.FromHexString("FF2500000000"))) {
            IntPtr existing = new(BitConverter.ToInt64(before, 6));
            string magic = Encoding.ASCII.GetString(game.ReadBytes(existing + 0x700, 8) ?? []);
            if(magic is "LABMET01" or "LABMET02" or "LABMET03" or "LABMET04" or "LABMET05") throw new NotSupportedException("Restart DOOM Eternal to load the updated LAB metrics HUD.");
            if(magic != Magic)
                throw new NotSupportedException("Another metrics formatter hook is installed");
            return new(game, existing + 4096, image + metricsOffset) { _smallScale = m.SmallScale };
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
            BitConverter.GetBytes(m.SmallScale * CvarFontScale).CopyTo(data, 28);
            Encoding.ASCII.GetBytes("%s\0").CopyTo(data, 128);
            Encoding.ASCII.GetBytes("%i FPS\0").CopyTo(data, 160);
            StoreHardwareNames(data,Game.HardwareNames.Current);
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
            return new(game, state, image + metricsOffset) { _smallScale = m.SmallScale };
        } finally { if(!referenced) game.FreeMemory(memory); }
    }

    private static byte[] Jump(long target) => Convert.FromHexString("FF2500000000").Concat(BitConverter.GetBytes(target)).ToArray();

    internal void Update(LabSettings settings, IReadOnlyList<CvarDifference>? cvars = null, float glyphHeight = 48)
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
        PublishCvars(cvars ?? [], glyphHeight);
        byte[] flags = new byte[8];
        BitConverter.GetBytes(_desru ? 1 : 0).CopyTo(flags, 0);
        BitConverter.GetBytes(settings.HideDriverName ? 1 : 0).CopyTo(flags, 4);
        if(!_game.WriteBytes(_state + 8, flags) ||
            !_game.WriteBytes(_state + 28, BitConverter.GetBytes(_smallScale * CvarFontScale)) ||
            !_game.WriteBytes(_state + 64, Encoding.ASCII.GetBytes(LabIndicator.Format(settings) + "\0")) ||
            !_game.WriteBytes(_state, BitConverter.GetBytes(Environment.TickCount64 + 3000))) throw new IOException("Cannot update LAB indicator");
    }

    // Only immutable text banks are published. The engine acknowledges a bank
    // after its final row; earlier banks are then safe to release. The current
    // bank remains allocated while the engine may still be inside this hook.
    private void PublishCvars(IReadOnlyList<CvarDifference> differences, float glyphHeight)
    {
        int viewportHeight = GameViewport.TryGetPhysicalViewport(_game.MainWindowHandle, out var viewport) ? viewport.Height : 1080;
        string[] lines = CvarHudPage.Lines(differences, viewportHeight, glyphHeight, _smallScale * CvarFontScale, _desru, Environment.TickCount64);
        IntPtr completed = _game.ReadPointer(_state + CvarAcknowledgedOffset);
        int acknowledged = _cvarBanks.IndexOf(completed);
        if(acknowledged > 0) {
            for(int i = 0; i < acknowledged; i++) _game.FreeMemory(_cvarBanks[i]);
            _cvarBanks.RemoveRange(0, acknowledged);
        }
        // A minimized/paused game may stop drawing indefinitely. Coalesce new
        // values/pages in managed memory until the latest bank is consumed;
        // never grow an unbounded queue of remote allocations while it sleeps.
        bool canPublish = _cvarBanks.Count == 0 || completed == _cvarBanks[^1];
        if(canPublish && (!_publishedLines.SequenceEqual(lines, StringComparer.Ordinal) || _cvarBanks.Count == 0)) {
            var data = new byte[16 + lines.Length * CvarRecordSize];
            BitConverter.GetBytes(lines.Length).CopyTo(data, 0);
            for(int i = 0; i < lines.Length; i++) {
                byte[] bytes = Encoding.UTF8.GetBytes(lines[i]);
                if(bytes.Length >= CvarRecordSize) throw new InvalidOperationException("Cvar HUD text exceeds its bounded record.");
                bytes.CopyTo(data, 16 + i * CvarRecordSize);
            }
            IntPtr bank = _game.AllocateMemory(data.Length);
            if(bank == IntPtr.Zero) throw new IOException("Cannot allocate cvar HUD text.");
            if(!_game.WriteBytes(bank, data)) { _game.FreeMemory(bank); throw new IOException("Cannot write cvar HUD text."); }
            // Retain even if publishing partially fails: ownership is uncertain.
            _cvarBanks.Add(bank);
            if(!_game.WriteBytes(_state + CvarBankOffset, BitConverter.GetBytes(bank.ToInt64()))) throw new IOException("Cannot publish cvar HUD text.");
            _publishedLines = lines;
        }
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
            !_game.WriteBytes(_state+CvarBankOffset,new byte[8])||
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
        int markerCount = 0;
        // Three saves and 0x90 bytes align every call. Copy eight stack arguments,
        // including doubles and pointers, without interpreting printf varargs.
        c.Emit("535657 4881EC90000000 48894C2470 4889542478 4C89842480000000 4C898C2488000000 F30F119C2488000000");
        c.Emit("488DB424D0000000 488D7C2420 B908000000 F348A5 48BB"); c.Pointer(state);
        c.Emit("488B8424A8000000 49BA"); c.Pointer(firstReturn); c.Emit("4C39D0"); c.Jump("0F82", "forward");
        c.Emit("49BA"); c.Pointer(lastReturn); c.Emit("4C39D0"); c.Jump("0F87", "forward");
        c.Call(clock); c.Emit("483B03"); c.Jump("0F83", "forward");
        // Examine every string in the verified metrics caller range: on 6.x
        // CPU precedes Vulkan, while GPU and the driver label follow it.
        c.Emit("837B0800"); c.Jump("0F84","regular");
        c.Emit("488B742428 4889F7 66813E2573"); c.Jump("0F85","candidate");
        c.Emit("807E0200"); c.Jump("0F85","candidate");
        c.Emit("488B7C2430");
        c.Label("candidate"); c.Emit("4885FF"); c.Jump("0F84","regular");
        foreach(var part in "amd proprietary driver".Select((letter,index)=>(letter,index))) {
            c.Emit(part.index==0?"8A07":"8A47"+part.index.ToString("X2"));
            c.Emit("0C20 3C"+((byte)part.letter).ToString("X2"));c.Jump("0F85","hardwareTest");
        }
        // Save the entire row, including its original format, color, printf
        // arguments and font size, then replay it below LAB at the final call.
        c.Emit("48897B20 8B842488000000 898310010000 488D742420 488DBBD0000000 B908000000 F348A5");
        c.Jump("E9","suppressed");
        c.Label("hardwareTest");c.Emit("837B0C00");c.Jump("0F84","regular");
        c.Emit("8B8BC0000000 85C9"); c.Jump("0F8E","regular");
        c.Emit("83F90E"); c.Jump("0F87","regular");
        c.Emit("488DB340010000");
        c.Label("hardwareName"); c.Emit("4989FA 4989F3 41B800010000");
        c.Label("hardwareByte"); c.Emit("418A02 418A13 38D0"); c.Jump("0F84","hardwareEqual");
        c.Emit("0C20 80CA20 38D0"); c.Jump("0F85","hardwareNext");
        c.Emit("3C61"); c.Jump("0F82","hardwareNext"); c.Emit("3C7A"); c.Jump("0F87","hardwareNext");
        c.Label("hardwareEqual"); c.Emit("84C0"); c.Jump("0F84","suppressed");
        c.Emit("49FFC2 49FFC3 41FFC8"); c.Jump("0F85","hardwareByte");
        c.Label("hardwareNext"); c.Emit("4881C600010000 FFC9"); c.Jump("0F85","hardwareName");
        c.Jump("E9","regular");
        c.Label("suppressed");c.Emit("488B442428 49BA");c.Pointer(last);c.Emit("4C39D0");c.Jump("0F84","afterLast");
        c.Emit("488B8424A8000000 49BA");c.Pointer(lastReturn);c.Emit("4C39D0");c.Jump("0F84","afterLast");
        c.Jump("E9","done");
        c.Label("regular");
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
        c.Jump("E9", "forward");
        c.Label("last"); c.Emit("803E00"); c.Jump("0F84", "afterLast"); CallOriginal();
        c.Label("afterLast"); Marker();
        c.Emit("837B0C00"); c.Jump("0F85", "done");
        c.Emit("488B4320 4885C0"); c.Jump("0F84", "done");
        c.Emit("488DB3D0000000 488D7C2420 B908000000 F348A5 8B8310010000 89842488000000"); CallOriginal(); c.Jump("E9", "done");
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
            string suffix = (markerCount++).ToString();
            // A nonblocking reader guard serializes bank consumption, including
            // unusual concurrent/reentrant metrics callbacks. An old reader can
            // never outlive a newer completion acknowledgment.
            c.Emit("31C0 BA01000000 F00FB15338"); c.Jump("0F85", "cvarsDone" + suffix);
            c.Emit("488B7328 4885F6"); c.Jump("0F84", "cvarsUnlock" + suffix);
            c.Emit("8B06 85C0"); c.Jump("0F8E", "cvarsAck" + suffix);
            c.Emit("3D00000100"); c.Jump("0F87", "cvarsAck" + suffix);
            c.Emit("8B531C 89942488000000"); // cvar-only font scale; preserve EAX row count
            c.Emit("89442460 488D7E10"); c.Label("cvarRow" + suffix);
            // User text is always a %s argument, never a printf format string.
            c.Emit("488D8380000000 4889442428 48897C2430"); CallOriginal();
            c.Emit("4881C700040000 FF4C2460"); c.Jump("0F85", "cvarRow" + suffix);
            c.Label("cvarsAck" + suffix); c.Emit("48897330");
            c.Label("cvarsUnlock" + suffix); c.Emit("C7433800000000");
            c.Label("cvarsDone" + suffix);
            c.Emit("8B4318 89842488000000"); // restore metrics/indicator font
        }
    }
}
