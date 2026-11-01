using System.Diagnostics;
using System.Runtime.InteropServices;
using Lab.Memory;

internal static class NativePlayerReaderTests
{
    internal static void Run(Action<bool,string> check)
    {
        using var game = Process.GetCurrentProcess();
        IntPtr memory = game.AllocateMemory(16384);
        try {
            game.WriteBytes(memory, new byte[16384]);
            var layout = new NativePlayerLayout(0x100, 0x200, 0x100, 0x200, 0x100, 0x180);
            IntPtr system = memory + 0x1000, map = memory + 0x2000, player = memory + 0x3000;
            var reader = new NativePlayerReader(game, memory, layout);
            check(reader.Player == IntPtr.Zero && !reader.Active, "Missing native player chain fails closed");
            Marshal.WriteIntPtr(memory, layout.GameSystem, system);
            Marshal.WriteInt32(system, 0x44, 2); Marshal.WriteIntPtr(system, 0x50, map);
            Marshal.WriteIntPtr(map, layout.MapPlayer, player); Marshal.WriteIntPtr(player, memory + layout.Vtable);
            check(reader.Player == player && reader.Active, "Verified native player chain supports the 6.66 telemetry path");
            Marshal.WriteByte(player, 0xa4, 1);
            check(reader.Player == IntPtr.Zero && !reader.Active, "Deleted player cannot expose cheats or speedometer data");
            Marshal.WriteByte(player, 0xa4, 0); Marshal.WriteIntPtr(player, memory + layout.Vtable + 8);
            check(reader.Player == IntPtr.Zero, "Wrong player vtable is rejected");
            Marshal.WriteIntPtr(player, memory + layout.Vtable); Marshal.WriteByte(player, layout.Hud + layout.Paused, 1);
            check(!reader.Active, "Paused HUD suppresses gameplay telemetry");
            Marshal.WriteByte(player, layout.Hud + layout.Paused, 0);
            IntPtr menus = memory + 0x800, menu = memory + 0x500;
            Marshal.WriteIntPtr(player, layout.Hud + layout.Menus, menus);
            Marshal.WriteInt32(player, layout.Hud + layout.Menus + 8, 1); Marshal.WriteIntPtr(menus, menu);
            check(!reader.Active, "Visible menu suppresses gameplay telemetry");
            Marshal.WriteByte(menu, 0xb1, 1);
            check(reader.Active, "Explicitly hidden menus do not suppress gameplay telemetry");
            Marshal.WriteInt32(player, layout.Hud + layout.Menus + 8, 65);
            check(!reader.Active, "Invalid HUD menu bounds fail closed");
            Marshal.WriteInt32(system, 0x44, 1);
            check(reader.Player == IntPtr.Zero, "Loading game-system state rejects the old map");
        } finally { game.FreeMemory(memory); }
    }
}
