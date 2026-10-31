using System.Diagnostics;
using System.Security.Cryptography;
namespace Lab.Memory { internal static class HudChain {
 private const int PatchHead=0x661b8;
 private const string OriginalHash="02c7159964245e249bcffc12598651b871f6c73925c4e04355510587114cdab3";
        private static bool IsMeathookModule(ProcessModule module) {
            if(module.ModuleName.Equals("DESRU.Meathook.Live.dll", StringComparison.OrdinalIgnoreCase)) return true;
            return module.ModuleName.Equals("XINPUT1_3.dll", StringComparison.OrdinalIgnoreCase) &&
                Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(module.FileName))).Equals(OriginalHash, StringComparison.OrdinalIgnoreCase);
        }

        internal static IntPtr UnwrapHud(Process game, IntPtr slot, IntPtr current) {
            var module = game.Modules.Cast<ProcessModule>().FirstOrDefault(m => current.ToInt64() >= m.BaseAddress.ToInt64() && current.ToInt64() < m.BaseAddress.ToInt64() + m.ModuleMemorySize && IsMeathookModule(m));
            if(module == null || current.ToInt64() < module.BaseAddress.ToInt64() || current.ToInt64() >= module.BaseAddress.ToInt64() + module.ModuleMemorySize) return slot;
            IntPtr patch = game.ReadPointer(module.BaseAddress + PatchHead);
            for(int i = 0; patch != IntPtr.Zero && i < 4096; i++) {
                byte[] record = game.ReadBytes(patch, 40) ?? throw new InvalidOperationException("Cannot read Meathook HUD chain");
                if(new IntPtr(BitConverter.ToInt64(record, 32)) == slot && BitConverter.ToInt64(record, 24) == 8) {
                    IntPtr original = game.ReadPointer(new IntPtr(BitConverter.ToInt64(record)));
                    byte[] data = game.ReadBytes(module.BaseAddress + 0x52000, 0x17378) ?? throw new InvalidOperationException("Cannot read Meathook HUD bridge");
                    byte[] value = BitConverter.GetBytes(original.ToInt64());
                    var matches = Enumerable.Range(0, data.Length / 8).Where(n => data.AsSpan(n * 8, 8).SequenceEqual(value)).ToArray();
                    if(matches.Length != 1) throw new InvalidOperationException("Cannot uniquely reconnect the Meathook HUD bridge");
                    return module.BaseAddress + 0x52000 + matches[0] * 8;
                }
                patch = new IntPtr(BitConverter.ToInt64(record, 16));
            }
            throw new InvalidOperationException("Cannot reconnect the Meathook HUD chain");
        }

        internal static void SynchronizeHudBridge(Process game, IntPtr actualSlot, IntPtr bridgeSlot, IntPtr renderer) {
            if(actualSlot == bridgeSlot) return;
            IntPtr wrapper = game.ReadPointer(actualSlot);
            var module = game.Modules.Cast<ProcessModule>().FirstOrDefault(m => wrapper.ToInt64() >= m.BaseAddress.ToInt64() && wrapper.ToInt64() < m.BaseAddress.ToInt64() + m.ModuleMemorySize && IsMeathookModule(m));
            if(module == null) throw new NotSupportedException("Cannot update an unknown HUD bridge");
            IntPtr link = game.ReadPointer(module.BaseAddress + PatchHead);
            for(int i = 0; link != IntPtr.Zero && i < 4096; i++) {
                byte[] record = game.ReadBytes(link, 40) ?? throw new InvalidOperationException("Cannot update Meathook HUD ownership");
                if(new IntPtr(BitConverter.ToInt64(record, 32)) == actualSlot && BitConverter.ToInt64(record, 24) == 8) {
                    if(!game.WriteBytes(new IntPtr(BitConverter.ToInt64(record)), BitConverter.GetBytes(renderer.ToInt64()))) throw new InvalidOperationException("Cannot preserve LAB across Meathook switching");
                    return;
                }
                link = new IntPtr(BitConverter.ToInt64(record, 16));
            }
            throw new InvalidOperationException("Missing Meathook HUD bridge ownership");
        }

}}
