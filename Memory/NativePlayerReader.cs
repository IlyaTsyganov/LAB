using System.Diagnostics;

namespace Lab.Memory;

// The same independently audited game-system/player/HUD chain used by the
// native reader. This avoids borrowing another patch's legacy telemetry globals.
internal sealed record NativePlayerLayout(int GameSystem, int Vtable, int MapPlayer, int Hud, int Menus, int Paused);
internal sealed class NativePlayerReader(Process game, IntPtr image, NativePlayerLayout layout)
{
    internal IntPtr Player
    {
        get
        {
            IntPtr system = game.ReadPointer(image + layout.GameSystem);
            if(system == IntPtr.Zero || !game.ReadValue<int>(system + 0x44, out int state) || state != 2) return IntPtr.Zero;
            IntPtr map = game.ReadPointer(system + 0x50);
            if(map == IntPtr.Zero) return IntPtr.Zero;
            IntPtr player = game.ReadPointer(map + layout.MapPlayer);
            if(player == IntPtr.Zero || game.ReadPointer(player) != image + layout.Vtable ||
                !game.ReadValue<byte>(player + 0xa4, out byte deleted) || deleted != 0 ||
                game.ReadPointer(system + 0x50) != map) return IntPtr.Zero;
            return player;
        }
    }

    internal bool Active
    {
        get
        {
            IntPtr player = Player;
            if(player == IntPtr.Zero || !game.ReadValue<byte>(player + layout.Hud + layout.Paused, out byte paused) || paused != 0 ||
                !game.ReadBytes(player + layout.Hud + layout.Menus, 16, out var header)) return false;
            IntPtr menus = new(BitConverter.ToInt64(header));
            int count = BitConverter.ToInt32(header, 8);
            if(count < 0 || count > 64 || (count > 0 && menus == IntPtr.Zero)) return false;
            for(int i = 0; i < count; i++) {
                IntPtr menu = game.ReadPointer(menus + i * 8);
                if(menu == IntPtr.Zero || !game.ReadBytes(menu, 0xc8, out var data)) return false;
                if(data[0xb1] != 0) continue;
                if(BitConverter.ToInt32(data, 0x90) >= 0 || BitConverter.ToInt32(data, 0x94) >= 0 ||
                    BitConverter.ToInt32(data, 0x80) > 0 || data[0xb2] != 0) return false;
            }
            return Player == player;
        }
    }
}
