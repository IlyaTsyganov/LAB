using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Lab.Memory;
using Lab.Overlay;

internal static class ProtectedCvarTests
{
    internal static void Run(Action<bool,string> check)
    {
        var layout = new NativeCvarLayout(16,64,0,8,12,16,24,32,512,1024,2048,4096,8192);
        check(ProtectedCvarMonitor.IsProtected(512,layout) && !ProtectedCvarMonitor.IsProtected(0,layout), "Only cheat-protected cvars enter the HUD");
        foreach(int excluded in new[] {1024,2048,4096,8192})
            check(!ProtectedCvarMonitor.IsProtected(512|excluded,layout), "NOCHEAT/ROM/INIT/shipping-disabled flags remain excluded");
        check(ProtectedCvarMonitor.IsMenuSetting("G_GameDifficulty") && !ProtectedCvarMonitor.IsMenuSetting("g_playerIncomingDamageScale_DIFFICULTY_HARD"),
            "Menu difficulty is excluded without hiding difficulty-related cheat controls");
        foreach(string name in new[]{"s_volume","s_volumeMusic","s_volumeSFX","s_volumeVO","r_gamma","g_reticleMode","g_setting_photomode"})
            check(CvarDisplayPolicy.IsPreference(name), "Actual 5.1 menu preference is excluded: " + name);
        foreach(string name in new[]{"g_infiniteAmmo","timescale","s_volumesDebug","ui_settings_game_lockDifficulty","s_volumeViewDistance"})
            check(!CvarDisplayPolicy.IsPreference(name), "Exact menu names do not hide unrelated cheats: " + name);
        check(CvarDisplayPolicy.IsAutomaticState("r_umbraAccurateOcclusionThreshold") &&
            !CvarDisplayPolicy.IsAutomaticState("r_umbraDebug"), "Automatic renderer values are excluded without broadly hiding renderer cheats");
        string[] observedFalseRows = [
            "demonBounty_Enabled", "g_hints_disable", "g_reticleMode", "g_settings_dashStyle",
            "g_setting_notification_menu", "g_setting_photomode", "g_setting_sentinel_armor", "g_weaponAutoEquipNew",
            "g_weaponAutoSwitchOnEmpty", "menu_commandBar_holdTime", "rs_forceResolution", "r_filmGrainRatio",
            "r_gamma", "r_hdrDisplayBrightness", "r_hdrDisplayMidPoint", "r_hdrDisplayMinStops",
            "r_hdrDisplaySaturation", "r_particleFadeQualityMultiplier", "r_particlesLightAtlasQuality", "r_shadowRestrictGeomCachesToSun",
            "r_umbraAccurateOcclusionThreshold", "swf_anchorProfile", "swf_colorProfile", "s_panningRule",
            "s_volume", "s_volumeMusic", "s_volumeSFX", "s_volumeVO",
            "view_skipShakes",
        ];
        foreach(string name in observedFalseRows)
            check(CvarDisplayPolicy.IsPreference(name)||CvarDisplayPolicy.IsAutomaticState(name), "Every actual false row observed on 5.1 is excluded: " + name);
        check(CheatCvarSession.DisplayChanges([], [new("g_infiniteAmmo","1","0",0x40009),new("timescale","0.5","1",0x4000c)]).Count==2,
            "Real cheat names and runtime flags remain displayable after their values change");
        var retailLayout = layout with {Cheat=8,NoCheat=16,ReadOnly=32768,InitOnly=16384,ShippingDisabled=4194304};
        check(ProtectedCvarMonitor.IsProtected(0xc000c,retailLayout) && ProtectedCvarMonitor.IsProtected(0x40009,retailLayout),
            "5.1 flags alone cannot distinguish modified menu values from actual cheats");
        check(ProtectedCvarMonitor.IsDefault("true","1",1) && ProtectedCvarMonitor.IsDefault("false","0f",1), "Boolean cvar defaults compare by value");
        check(ProtectedCvarMonitor.IsDefault("1.000f","1",4) && ProtectedCvarMonitor.IsDefault("001","1",2), "Numeric formatting does not produce false cheat warnings");
        check(!ProtectedCvarMonitor.IsDefault("16777217","16777216",2) && !ProtectedCvarMonitor.IsDefault("2147483647","2147483646",2) && ProtectedCvarMonitor.IsDefault("1.0","1",2), "Integer cvars compare exactly beyond float precision while tolerating numeric formatting");
        check(!ProtectedCvarMonitor.IsDefault("nan","0",4) && !ProtectedCvarMonitor.IsDefault("A","a",0), "Invalid numbers and changed strings remain differences");

        CvarDifference[] preferences = [new("s_volume", "0.6", "1", 516), new("g_gameskill", "3", "2", 514)];
        CvarDifference[] current = [new("S_VOLUME", "0.2", "1", 516), new("g_gameskill", "4", "2", 514),
            new("g_cheat_changed", "2", "1", 514), new("g_cheat_reset", "1.000", "1", 516)];
        check(CheatCvarSession.DisplayChanges(preferences, current).Select(c=>c.Name).SequenceEqual(new[]{"g_cheat_changed"}),
            "DESRU session filter excludes initial volume/difficulty preferences even when changed again, retaining only new nondefault cvars");
        check(NativeSpeedometerCode.CvarSessionPointerOffset >= NativeSpeedometerCode.NamesOffset + NativeSpeedometerCode.Commands.Length * 256 &&
            NativeSpeedometerCode.CvarSessionPointerOffset + 8 <= NativeSpeedometerCode.StateSize,
            "Persistent cvar session pointer does not overlap HUD readings, console mailboxes or command names");

        using var game = Process.GetCurrentProcess(); IntPtr memory = game.AllocateMemory(65536);
        IntPtr sessionState = game.AllocateMemory(NativeSpeedometerCode.StateSize);
        try {
            IntPtr system=memory+128,list=memory+256,array=memory+512;
            void Pointer(IntPtr at,IntPtr value)=>Marshal.WriteIntPtr(at,value);
            void Text(IntPtr at,string value)=>game.WriteBytes(at,Encoding.UTF8.GetBytes(value+"\0"));
            Pointer(memory+16,system);Pointer(system+8,list);Pointer(list,array);Marshal.WriteInt32(list,8,8);Marshal.WriteInt32(list,12,16);
            void Entry(int i,string name,string value,string reset,int flags) {
                IntPtr obj=memory+2048+i*128,data=memory+4096+i*128,strings=memory+8192+i*1024;
                Pointer(array+i*8,obj);Pointer(obj,data);Pointer(data,strings+512);Pointer(data+16,strings);Pointer(data+24,strings+256);Marshal.WriteInt32(data,32,flags);
                Text(strings,name);Text(strings+256,reset);Text(strings+512,value);
            }
            Entry(0,"g_changed","3","1",514);Entry(1,"g_bool","true","1",513);Entry(2,"g_float","1.000f","1",516);
            Entry(3,"g_string","100% ready","ready",512);Entry(4,"g_plain","2","1",2);Entry(5,"g_readonly","2","1",512|2048);
            Entry(6,"bad;command","2","1",512);Entry(7,"g_already_changed","9","0",514);
            var monitor=new ProtectedCvarMonitor(game,layout,memory);var changed=monitor.Read();
            check(changed.Select(c=>c.Name).SequenceEqual(new[]{"g_already_changed","g_changed","g_string"}), "Registry scan uses factory resetString and includes pre-existing changes");
            var session = new CheatCvarSession(game, sessionState, monitor);
            check(!session.Active && session.DisplayChanges(changed).Count == 0, "Uninitialized DESRU-style session displays no unclassified preferences");
            session.BeginAsync().GetAwaiter().GetResult();
            check(session.Active && session.DisplayChanges(changed).Count == 0, "Initial protected cvar differences become preserved session preferences");
            check(changed.Single(c=>c.Name=="g_string").Value=="100% ready", "Read-only cvar scan preserves printf characters as data");
            Text(memory+8192+512,"1");
            check(!monitor.Read().Any(c=>c.Name=="g_changed"), "Returning to the factory default removes the row immediately");
            Marshal.WriteInt32(memory+4096+7*128,32,512|1024);
            check(!monitor.Read().Any(c=>c.Name=="g_already_changed"), "Changed protection flags are rechecked on each scan");
            Entry(8,"g_new","2","1",514);Marshal.WriteInt32(list,8,9);
            check(monitor.Read().Any(c=>c.Name=="g_new"), "Newly registered cvars are discovered without restarting LAB");
            check(session.DisplayChanges(monitor.Read()).Select(c=>c.Name).SequenceEqual(new[]{"g_new"}), "Only a cvar changed after the baseline reaches the displayed list");
            var resumed = new CheatCvarSession(game, sessionState, monitor);
            resumed.BeginAsync().GetAwaiter().GetResult();
            check(resumed.Active && resumed.DisplayChanges(monitor.Read()).Select(c=>c.Name).SequenceEqual(new[]{"g_new"}),
                "Reattaching uses the original DESRU-style baseline rather than hiding active cheat changes in a new snapshot");
            Entry(9,"g_large","16777217","16777216",514);Marshal.WriteInt32(list,8,10);
            check(monitor.Read().Any(c=>c.Name=="g_large"), "Registry scan keeps an adjacent large integer change visible");
            // Reproduce the actual 5.1 failure: menu preferences arrive after
            // the original baseline, then can be changed again through menus.
            Entry(10,"s_volume","0.09","1",516);
            Entry(11,"r_gamma","1.5","1.2",516);
            Entry(12,"g_gameDifficulty","3","1",514);Marshal.WriteInt32(list,8,13);
            var late = session.DisplayChanges(monitor.Read());
            check(late.Select(c=>c.Name).SequenceEqual(new[]{"g_large","g_new"}),
                "Late-loaded menu preferences never become new cheat rows, even when absent from the baseline");
            Text(memory+8192+10*1024+512,"0.5");Text(memory+8192+12*1024+512,"4");
            check(session.DisplayChanges(monitor.Read()).Select(c=>c.Name).SequenceEqual(new[]{"g_large","g_new"}),
                "Changing volume or campaign difficulty later keeps menu values hidden and actual cheats visible");
            Marshal.WriteInt32(list,8,65537);
            bool rejected=false;try { monitor.Read(); } catch(InvalidOperationException) { rejected=true; }
            check(rejected,"Corrupt registry bounds are rejected before allocation or traversal");
            Marshal.WriteInt32(list,8,13);
            var watch=Stopwatch.StartNew();for(int i=0;i<100;i++) monitor.Read();watch.Stop();
            Console.WriteLine($"Cvar fixture: cached 10-entry registry averages {watch.Elapsed.TotalMilliseconds/100:F3} ms/read.");
        } finally {
            IntPtr storage = game.ReadPointer(sessionState + NativeSpeedometerCode.CvarSessionPointerOffset);
            if(storage != IntPtr.Zero) game.FreeMemory(storage);
            game.FreeMemory(sessionState); game.FreeMemory(memory);
        }

        var many=Enumerable.Range(0,200).Select(i=>new CvarDifference("g_test"+i,i.ToString(),"0",512)).ToArray();
        int capacity=CvarHudPage.Capacity(1080,48,.5f,false),pageSize=capacity-1,pages=(many.Length+pageSize-1)/pageSize;
        var displayed=new HashSet<string>();
        for(int page=0;page<pages;page++) {
            var lines=CvarHudPage.Lines(many,1080,48,.5f,false,page*CvarHudPage.PageDuration);
            check(lines[0].StartsWith($"Cheat cvars: page {page+1}/{pages}") && lines.Length<=capacity,"Overflow HUD pages identify their page and fit the available rows");
            foreach(string line in lines.Skip(1)) displayed.Add(line.Split(" = ")[0]);
        }
        check(displayed.SetEquals(many.Select(c=>c.Name)),"Automatic paging displays every changed cvar without a silent hard cap");
        check(CvarHudPage.Capacity(540,48,.5f,false)<capacity,"Small viewports reduce the number of rows per page");
        check(CvarHudPage.Lines([new("g_text","line1\nline2","",512)],1080,48,.5f,false,0).Single()=="g_text = line1 line2","Cvar line breaks cannot inject unrelated HUD rows");
    }
}
