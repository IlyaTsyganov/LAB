using Lab.Memory;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Lab.Runtime;

namespace Lab.Overlay {
    internal sealed class NativeSpeedometer : IDisposable {
        private readonly Process _game;
        private readonly IntPtr _state;
        private readonly int[] _sequences = new int[NativeSpeedometerCode.Commands.Length];
        private readonly Queue<string> _consoleMessages = new();
        private bool _tintAttached, _tintAttachFailed;
        private readonly LowHealthTintTarget _tintTarget;
        private readonly NativeGameLayout _layout;
        internal int GroundPlaneFromVelocity => _layout.GroundPlaneFromVelocity;
        internal bool TintAttached => _tintAttached;
        private readonly float _glyphWidth, _glyphHeight;
        internal const int ModuleSize11 = 492113920;
        internal bool CommandsRegistered => _game.ReadValue<int>(_state + 4) == NativeSpeedometerCode.Commands.Length;
        internal long RenderCount => _game.ReadValue<long>(_state + 8);
        internal IntPtr State => _state;
        internal NativeGameLayout Layout => _layout;
        internal float GlyphWidth => CurrentGlyph(_layout.GlyphWidth, _glyphWidth);
        internal float GlyphHeight => CurrentGlyph(_layout.GlyphHeight, _glyphHeight);
        private float CurrentGlyph(int offset, float fallback) {
            float value = _game.ReadValue<float>(_game.MainModule!.BaseAddress + offset, fallback);
            return float.IsFinite(value) && value > 0 && value <= 1000 ? value : fallback;
        }

        private NativeSpeedometer(Process game, IntPtr state, NativeGameLayout layout) {
            _game = game; _state = state;
            _layout = layout;
            _tintTarget = new LowHealthTintTarget(game);
            _glyphWidth = game.ReadValue<float>(game.MainModule!.BaseAddress + layout.GlyphWidth);
            _glyphHeight = game.ReadValue<float>(game.MainModule.BaseAddress + layout.GlyphHeight);
            if(!float.IsFinite(_glyphWidth) || _glyphWidth <= 0 || _glyphWidth > 1000 ||
                !float.IsFinite(_glyphHeight) || _glyphHeight <= 0 || _glyphHeight > 1000)
                throw new NativeGameNotReadyException("Game font metrics are not initialized yet");
            // Saved user settings are authoritative on reconnect; do not replay old console requests.
            for(int i = 0; i < _sequences.Length; i++) _sequences[i] = game.ReadValue<int>(state + NativeSpeedometerCode.MailboxOffset + i * NativeSpeedometerCode.MailboxStride);

        }

        internal static NativeSpeedometer Attach(Process game) {
            var layout = NativeGameLayout.Resolve(game.MainModule!.FileName);
            if(game.MainModule.ModuleMemorySize != layout.ImageSize) throw new NotSupportedException("On-disk and loaded game images differ");
            IntPtr image = game.MainModule.BaseAddress, slot = image + layout.HudSlot;
            var ready = RequireReady(game, image + layout.CommandGlobal, image + layout.GlyphWidth, image + layout.GlyphHeight);
            if(!game.ReadPointer(slot, true, out var original) || original == IntPtr.Zero) throw new NativeGameNotReadyException("The game HUD renderer is not initialized yet");
            slot = HudChain.UnwrapHud(game, slot, original);
            if(!game.ReadPointer(slot, true, out original)) throw new InvalidOperationException("Cannot read chained HUD renderer");
            IntPtr previousState = IntPtr.Zero;
            if(original != image + layout.Render) {
                // Reconnect after LAB restarted. Code/data remain alive for the lifetime of the game.
                var existing = game.ReadBytes(original, 20);
                if(existing != null && existing.AsSpan(0, 12).SequenceEqual(Convert.FromHexString("5356574883EC40488BF248BB"))) {
                    IntPtr state = new(BitConverter.ToInt64(existing, 12));
                    string magic = Encoding.ASCII.GetString(game.ReadBytes(state + 48, 8) ?? Array.Empty<byte>());
                    if(magic == NativeSpeedometerCode.Magic || magic == NativeSpeedometerCode.LegacyMagic) {
                        byte[] descriptions = CreateState();
                        for(int i = 0; i < NativeSpeedometerCode.Commands.Length; i++) game.WriteBytes(state + NativeSpeedometerCode.NamesOffset + i * 0x100, descriptions.AsSpan(NativeSpeedometerCode.NamesOffset + i * 0x100, 256).ToArray());
                        if(magic == NativeSpeedometerCode.Magic) return new NativeSpeedometer(game, state, layout);
                        // Install new code in a separate allocation; keep the old
                        // callbacks/state alive for registered commands and readers.
                        previousState = state;
                    }
                }
                if(previousState == IntPtr.Zero) throw new NotSupportedException("Another HUD renderer hook is installed; it was left unchanged");
            }
            foreach(int code in new[] { layout.Render, layout.Draw, layout.Width, layout.Height, layout.Print }) VerifyFileCode(game, code);
            IntPtr commandSystem = ready.System, addCommand = ready.AddCommand;
            IntPtr allocation = game.AllocateMemory(16384);
            if(allocation == IntPtr.Zero) throw new InvalidOperationException("Cannot allocate the HUD adapter");
            bool installed = false;
            try {
                IntPtr state = previousState != IntPtr.Zero ? previousState : allocation + 8192;
                byte[] data = CreateState();
                if(previousState == IntPtr.Zero && !game.WriteBytes(state, data)) throw new InvalidOperationException("Cannot initialize HUD state");
                byte[] code = NativeSpeedometerCode.Render(allocation.ToInt64(), state.ToInt64(), (image + layout.Render).ToInt64(),
                    (image + layout.Draw).ToInt64(), (image + layout.Width).ToInt64(), (image + layout.Height).ToInt64(), commandSystem.ToInt64(), addCommand.ToInt64(), (image + layout.Print).ToInt64());
                if(!game.WriteBytes(allocation, code)) throw new InvalidOperationException("Cannot write the HUD adapter");
                for(int i = 0; i < NativeSpeedometerCode.Commands.Length; i++)
                    if(!game.WriteBytes(allocation + NativeSpeedometerCode.CallbackStart + i * NativeSpeedometerCode.CallbackStride,
                        NativeSpeedometerCode.Command((state + NativeSpeedometerCode.MailboxOffset + i * NativeSpeedometerCode.MailboxStride).ToInt64(), i == 6 ? 4 : 2))) throw new InvalidOperationException("Cannot initialize console commands");
                if(!game.VirtualProtect(allocation, 8192, MemPageProtect.PAGE_EXECUTE_READ) ||
                    !game.VirtualProtect(state, 8192, MemPageProtect.PAGE_READWRITE) || !FlushInstructionCache(game.Handle, allocation, (UIntPtr) 8192))
                    throw new InvalidOperationException("Cannot protect HUD adapter memory");
                if(!game.ReadPointer(slot, true, out var check) || check != original) throw new InvalidOperationException("HUD renderer changed while attaching");
                if(!game.VirtualProtect(slot, 8, MemPageProtect.PAGE_READWRITE, out var previous)) throw new InvalidOperationException("Cannot update HUD slot");
                try { installed = game.WriteBytes(slot, BitConverter.GetBytes(allocation.ToInt64())); }
                finally { game.VirtualProtect(slot, 8, previous); }
                if(!installed) throw new InvalidOperationException("Cannot attach the HUD adapter");
                HudChain.SynchronizeHudBridge(game, image + layout.HudSlot, slot, allocation);
                if(previousState != IntPtr.Zero) {
                    game.WriteBytes(state + 48, Encoding.ASCII.GetBytes(NativeSpeedometerCode.Magic));
                }
                return new NativeSpeedometer(game, state, layout);
            } finally {
                if(!installed) game.FreeMemory(allocation);
            }
        }

        internal static (IntPtr System, IntPtr AddCommand) RequireReady(Process game, IntPtr commandGlobal, IntPtr glyphWidth, IntPtr glyphHeight) {
            // These globals are initially empty when LAB sees the process before
            // engine startup. Do not allocate/register a HUD until they are usable.
            if(!game.ReadPointer(commandGlobal, true, out var system) || system == IntPtr.Zero ||
                !game.ReadPointer(system, true, out var vtable) || vtable == IntPtr.Zero ||
                !game.ReadPointer(vtable + 24, true, out var addCommand) || addCommand == IntPtr.Zero ||
                !game.ReadPointer(system + 16, true, out var registry) || registry == IntPtr.Zero ||
                !game.ReadBytes(registry, 16, out var header) || BitConverter.ToInt64(header) == 0 ||
                BitConverter.ToInt32(header, 8) < 1 || BitConverter.ToInt32(header, 8) > 65536 ||
                BitConverter.ToInt32(header, 12) < BitConverter.ToInt32(header, 8))
                throw new NativeGameNotReadyException("The game command system is not initialized yet");
            if(!game.ReadValue<float>(glyphWidth, out var width) || !game.ReadValue<float>(glyphHeight, out var height) ||
                !float.IsFinite(width) || width <= 0 || width > 1000 || !float.IsFinite(height) || height <= 0 || height > 1000)
                throw new NativeGameNotReadyException("Game font metrics are not initialized yet");
            return (system, addCommand);
        }

        internal static byte[] CreateState() {
            var data = new byte[NativeSpeedometerCode.StateSize];
            BitConverter.GetBytes(384f).CopyTo(data, 16);
            BitConverter.GetBytes(48f).CopyTo(data, 20);
            BitConverter.GetBytes(1920f).CopyTo(data, 56);
            BitConverter.GetBytes(1080f).CopyTo(data, 60);
            BitConverter.GetBytes(1f).CopyTo(data, LowHealthTintCode.ScaleOffset);
            for(int i = 0; i < 4; i++) BitConverter.GetBytes(1f).CopyTo(data, 32 + i * 4);
            for(int i = 0; i < 8; i++) BitConverter.GetBytes(1f).CopyTo(data, 384 + i * 4);
            Encoding.ASCII.GetBytes(NativeSpeedometerCode.Magic).CopyTo(data, 48);
            Encoding.ASCII.GetBytes("%s\0").CopyTo(data, NativeSpeedometerCode.ConsoleFormatOffset);
            for(int i = 0; i < NativeSpeedometerCode.Commands.Length; i++) {
                Encoding.ASCII.GetBytes(NativeSpeedometerCode.Commands[i]).CopyTo(data, NativeSpeedometerCode.NamesOffset + i * 0x100);
                string help = i == 4 ? "<0..100>: low-health red tint opacity; 100 restores original opacity" :
                    i == 8 ? "<0|1>: vertical direction sign; 1=+up/-down, 0=magnitude" :
                    i == 7 ? "<0|1>: include vertical speed" :
                    i == 5 ? "<0|1>: include last-landing speed (preserved while hidden)" :
                    i == 6 ? "<R G B> [current|landing|vertical]: text color, integer channels 0..255" :
                    i == 0 ? "<0|1> [current|landing|vertical]: show/hide LAB numbers" :
                    i == 3 ? "<0.1..10> [current|landing|vertical]: independent text size (all if omitted)" : "<pixels> [current|landing|vertical]: position from the top-left corner (all if omitted)";
                Encoding.ASCII.GetBytes(help).CopyTo(data, NativeSpeedometerCode.NamesOffset + 64 + i * 0x100);
            }
            return data;
        }

        private static void VerifyFileCode(Process game, int rva) {
            using var file = File.OpenRead(game.MainModule!.FileName);
            using var pe = new System.Reflection.PortableExecutable.PEReader(file, System.Reflection.PortableExecutable.PEStreamOptions.LeaveOpen);
            var section = pe.PEHeaders.SectionHeaders.Single(s => rva >= s.VirtualAddress && rva + 16 <= s.VirtualAddress + s.SizeOfRawData);
            file.Position = rva - section.VirtualAddress + section.PointerToRawData;
            var expected = new byte[16];
            if(file.Read(expected, 0, expected.Length) != expected.Length ||
                !(game.ReadBytes(game.MainModule.BaseAddress + rva, expected.Length) ?? Array.Empty<byte>()).SequenceEqual(expected))
                throw new NotSupportedException("Game code differs from the resolved executable; another hook was left unchanged");
        }

        internal void Update(SpeedometerReading current, SpeedometerReading landing, SpeedometerReading vertical, double lowHpTintOpacity = 100, IntPtr player = default) {
            FlushConsoleMessage();
            lowHpTintOpacity = double.IsFinite(lowHpTintOpacity) ? Math.Clamp(lowHpTintOpacity, 0, 100) : 100;
            if(lowHpTintOpacity < 100 && !_tintAttached && !_tintAttachFailed) {
                try {
                    LowHealthTintHook.Attach(_game, _state);
                    LowHealthHudHook.Attach(_game, _state);
                    _tintAttached = true;
                }
                catch(Exception e) {
                    _tintAttachFailed = true;
                    Log.Warning(e, "Low-health tint control is unavailable; original tint and speedometers remain active");
                }
            }
            byte[] tint = new byte[8];
            if(_tintAttached) _tintTarget.Update(_state, player);
            BitConverter.GetBytes(_tintAttachFailed ? 0 : 240).CopyTo(tint, 0);
            BitConverter.GetBytes((float) (_tintAttachFailed ? 1 : lowHpTintOpacity / 100)).CopyTo(tint, 4);
            byte[] readings = CreateReadings(current, landing, vertical, GlyphWidth);
            if(!GameViewport.TryGetPhysicalViewport(_game.MainWindowHandle, out var viewport)) viewport = new Rectangle(0, 0, 1920, 1080);
            byte[] dimensions = new byte[8];
            BitConverter.GetBytes((float) viewport.Width).CopyTo(dimensions, 0);
            BitConverter.GetBytes((float) viewport.Height).CopyTo(dimensions, 4);
            if(!_game.WriteBytes(_state + LowHealthTintCode.LeaseOffset, tint) ||
                !_game.WriteBytes(_state + 20, BitConverter.GetBytes(GlyphHeight)) || !_game.WriteBytes(_state + 64, readings[..128]) || !_game.WriteBytes(_state + 512, readings[128..192]) ||
                !_game.WriteBytes(_state + 384, CreateColors(current, landing, vertical)) ||
                !_game.WriteBytes(_state + 56, dimensions) ||
                !_game.WriteBytes(_state, BitConverter.GetBytes(current.Enabled || landing.Enabled || vertical.Enabled ? 240 : 0)))
                throw new InvalidOperationException("Cannot update HUD values");
        }

        internal static byte[] CreateReadings(SpeedometerReading current, SpeedometerReading landing, SpeedometerReading vertical, float glyphWidth) {
            byte[] data = new byte[192];
            WriteReading(data, 0, current, glyphWidth);
            WriteReading(data, 64, landing, glyphWidth);
            WriteReading(data, 128, vertical, glyphWidth);
            return data;
        }

        internal static byte[] CreateColors(SpeedometerReading current, SpeedometerReading landing, SpeedometerReading vertical) {
            byte[] data = new byte[48];
            var colors = new[] { current.DisplayColor, landing.DisplayColor, vertical.DisplayColor };
            for(int i = 0; i < 3; i++) {
                BitConverter.GetBytes(colors[i].R / 255f).CopyTo(data, i * 16);
                BitConverter.GetBytes(colors[i].G / 255f).CopyTo(data, i * 16 + 4);
                BitConverter.GetBytes(colors[i].B / 255f).CopyTo(data, i * 16 + 8);
                BitConverter.GetBytes(1f).CopyTo(data, i * 16 + 12);
            }
            return data;
        }

        private static void WriteReading(byte[] data, int offset, SpeedometerReading reading, float glyphWidth) {
            string text = SpeedometerText.Format(reading);
            BitConverter.GetBytes(reading.Enabled ? 1 : 0).CopyTo(data, offset);
            BitConverter.GetBytes((float) Math.Max(0, reading.X)).CopyTo(data, offset + 4);
            BitConverter.GetBytes((float) Math.Max(0, reading.Y)).CopyTo(data, offset + 8);
            BitConverter.GetBytes((float) Math.Clamp(reading.Scale, 0.1, 10) * 0.5f).CopyTo(data, offset + 12);
            Encoding.ASCII.GetBytes(text[..Math.Min(text.Length, 31)]).CopyTo(data, offset + 16);
            BitConverter.GetBytes(text.Length * glyphWidth).CopyTo(data, offset + 48);
        }

        internal IEnumerable<(int Command, string[] Arguments)> ReadCommands() {
            for(int i = 0; i < _sequences.Length; i++) {
                var data = _game.ReadBytes(_state + NativeSpeedometerCode.MailboxOffset + i * NativeSpeedometerCode.MailboxStride, NativeSpeedometerCode.MailboxStride);
                if(data == null) continue;
                int sequence = BitConverter.ToInt32(data, 0), argc = BitConverter.ToInt32(data, 4);
                if(sequence == _sequences[i] ||
                    !_game.ReadValue<int>(_state + NativeSpeedometerCode.MailboxOffset + i * NativeSpeedometerCode.MailboxStride, out var confirmed) || confirmed != sequence) continue;
                _sequences[i] = sequence;
                if(argc < 1 || argc > (i == 6 ? 5 : 3)) continue;
                yield return (i, Enumerable.Range(0, argc - 1).Select(n => ReadArgument(data, 16 + n * 32)).ToArray());
            }
        }

        private static string ReadArgument(byte[] data, int offset) {
            int end = Array.IndexOf(data, (byte) 0, offset, 32);
            return Encoding.ASCII.GetString(data, offset, end >= offset ? end - offset : 31);
        }

        internal void QueueConsoleMessage(string message) {
            if(Encoding.ASCII.GetByteCount(message) >= NativeSpeedometerCode.ConsoleTextCapacity)
                throw new ArgumentException("Console response exceeds its shared buffer", nameof(message));
            // A bounded host queue preserves several different help requests until HUD acknowledges them.
            if(_consoleMessages.Count < 32) _consoleMessages.Enqueue(message);
        }

        private void FlushConsoleMessage() {
            if(_consoleMessages.Count == 0 || !_game.ReadValue<int>(_state + NativeSpeedometerCode.ConsoleReadyOffset, out var ready) || ready != 0) return;
            byte[] text = Encoding.ASCII.GetBytes(_consoleMessages.Peek() + "\0");
            if(!_game.WriteBytes(_state + NativeSpeedometerCode.ConsoleTextOffset, text) ||
                !_game.WriteBytes(_state + NativeSpeedometerCode.ConsoleReadyOffset, BitConverter.GetBytes(1)))
                throw new InvalidOperationException("Cannot publish console response");
            _consoleMessages.Dequeue();
        }

        public void Dispose() {

            try {
                if(!_game.HasExited) {
                    _game.WriteBytes(_state + LowHealthTintCode.LeaseOffset, new byte[4]);
                    _game.WriteBytes(_state, new byte[4]);
                }
            }
            catch(InvalidOperationException) { }
            catch(System.ComponentModel.Win32Exception) { }
            // Do not free code that a game thread/registered command could still be executing.
            // A reconnect reuses this 8 KiB block; the OS releases it when the game exits.
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool FlushInstructionCache(IntPtr process, IntPtr address, UIntPtr size);
    }
}
