using System.Diagnostics;
using System.Runtime.InteropServices;
using Lab.Memory;
using Lab.Overlay;

internal static class PlayerCheatTests
{
    internal static void Run(Action<bool,string> check)
    {
        byte[] shiftOne = Convert.FromHexString("0FB68100200000D1E883E001C3CC");
        byte[] shiftThree = Convert.FromHexString("0FB68100200000C1E80383E001C3");
        check(NativePlayerCheatFlag.Decode(shiftOne) == new NativePlayerCheatFlag(0x2000,2), "Reflected player bool getter decodes a one-bit shift");
        check(NativePlayerCheatFlag.Decode(shiftThree) == new NativePlayerCheatFlag(0x2000,8), "Reflected player bool getter decodes a packed bit mask");
        foreach(string invalid in new[] {
            "0FB681FFFFFFFFC1E80383E001C3", "0FB68100001000C1E80383E001C3",
            "0FB68100200000C1E80883E001C3", "0FB68100200000C1E80383E003C3",
            "0FB68900200000C1E80383E001C3", "0FB68100200000C1E803" }) {
            bool rejected=false;try { NativePlayerCheatFlag.Decode(Convert.FromHexString(invalid)); } catch(NotSupportedException) { rejected=true; }
            check(rejected, "Unsupported player getter code is rejected before reading a guessed offset");
        }

        using var process=Process.GetCurrentProcess();IntPtr player=process.AllocateMemory(4096);
        try {
            var layout=new NativePlayerCheatLayout(new(128,2),new(128,4),new(129,16));
            var monitor=new PlayerCheatMonitor(process,layout);
            IReadOnlyList<CvarDifference> cvars=[new("g_infiniteAmmo","1","0",9)];
            check(monitor.Read(IntPtr.Zero)==default && monitor.Read(player)==default, "Missing or clean player state yields no active cheat modes");
            check(ReferenceEquals(monitor.Read(player).AddTo(cvars),cvars), "Inactive player modes preserve the original cvar rows");
            Marshal.WriteByte(player,128,2);
            var rows=monitor.Read(player).AddTo(cvars);
            check(rows.Select(r=>r.Name).SequenceEqual(new[]{"noclip","g_infiniteAmmo"}) && rows[0].Value=="ON", "An already active noclip bit appears before cvars without a console argument or session change");
            Marshal.WriteByte(player,128,6);Marshal.WriteByte(player,129,16);
            rows=monitor.Read(player).AddTo(cvars);
            check(rows.Select(r=>r.Name).SequenceEqual(new[]{"noclip","notarget","god","g_infiniteAmmo"}) && rows.Take(3).All(r=>r.Value=="ON"), "All three live player bits appear in a stable order alongside changed cvars");
            check(CvarHudPage.Lines(rows,1080,16,1.25f,false,0).Take(3).SequenceEqual(new[]{"noclip = ON","notarget = ON","god = ON"}), "Player mode rows use the same HUD text pipeline below the LAB indicator");
            Marshal.WriteByte(player,128,4);Marshal.WriteByte(player,129,0);
            check(monitor.Read(player).AddTo(cvars).Select(r=>r.Name).SequenceEqual(new[]{"notarget","g_infiniteAmmo"}), "Turning noclip and god bits off removes their rows on the next read");
            Marshal.WriteByte(player,128,128);
            check(monitor.Read(player)==default, "Unrelated bits in the same player byte do not activate cheat rows");
            check(monitor.Read(new IntPtr(1))==default, "An unreadable player pointer does not retain stale active cheat modes");
        } finally { process.FreeMemory(player); }

        foreach(string path in (Environment.GetEnvironmentVariable("LAB_LAYOUT_TEST_PATHS")??"").Split('|',StringSplitOptions.RemoveEmptyEntries)) {
            var game=NativeGameLayout.Resolve(path);
            check(game.PlayerCheats is not null,"Player cheat bits resolve from the real executable: "+path+": "+game.PlayerCheatsFailure);
            var flags=game.PlayerCheats!;
            check(new[]{flags.Noclip,flags.Notarget,flags.God}.All(f=>f.Offset>0&&f.Offset<1024*1024&&f.Mask!=0), "Actual player cheat offsets and masks are bounded: "+path);
            Console.WriteLine($"Player cheat layout verified: {Path.GetDirectoryName(path)}; {flags}");
        }
    }
}
