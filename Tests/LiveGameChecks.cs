using System.Diagnostics;
using System.Text;
using Lab.Memory;
using Lab.Overlay;
using Lab.Runtime;

// Explicit opt-in, read-only acceptance checks against LAB's existing adapters.
internal static class LiveGameChecks
{
    internal static int Run(bool active)
    {
        var store=new SettingsStore();var settings=store.Snapshot();
        using var game=Process.GetProcessesByName("DOOMEternalx64vk").Single(p=>p.MainModule!.FileName.Equals(settings.GamePath,StringComparison.OrdinalIgnoreCase));
        var layout=NativeGameLayout.Resolve(settings.GamePath);var profile=GameProfile.Resolve(settings.GamePath);var reader=new GameReader(game,profile);
        IntPtr image=game.MainModule!.BaseAddress;
        byte[] Read(IntPtr address,int size)=>game.ReadBytes(address,size)??throw new IOException("Cannot read existing LAB adapter");
        var entry=Read(image+layout.Metrics!.Function,16);
        if(!entry.AsSpan(0,6).SequenceEqual(Convert.FromHexString("FF2500000000")))throw new Exception("LAB metrics adapter is not installed");
        IntPtr metrics=new(BitConverter.ToInt64(entry,6));
        if(Encoding.ASCII.GetString(Read(metrics+0x700,8))!=PerformanceMetrics.Magic)throw new Exception("Unknown metrics adapter");
        long lease=game.ReadValue<long>(metrics+4096);string indicator=Encoding.ASCII.GetString(Read(metrics+4096+64,64)).Split('\0')[0];
        if(active?(lease<=Environment.TickCount64||indicator!=LabIndicator.Format(settings)):lease>Environment.TickCount64)throw new Exception("Unexpected metrics lease or indicator: "+indicator);
        if(active){
            IntPtr hudSlot=image+layout.HudSlot;hudSlot=HudChain.UnwrapHud(game,hudSlot,game.ReadPointer(hudSlot));
            IntPtr render=game.ReadPointer(hudSlot);var head=Read(render,20);
            if(!head.AsSpan(0,12).SequenceEqual(Convert.FromHexString("5356574883EC40488BF248BB")))throw new Exception("Unknown LAB HUD renderer");
            IntPtr hudState=new(BitConverter.ToInt64(head,12));
            if(Encoding.ASCII.GetString(Read(hudState+48,8))!=NativeSpeedometerCode.Magic||game.ReadValue<int>(hudState+4)!=NativeSpeedometerCode.Commands.Length)throw new Exception("New LAB console command registration is not ready");
            string lastName=Encoding.ASCII.GetString(Read(hudState+NativeSpeedometerCode.NamesOffset+8*256,64)).Split('\0')[0];
            if(lastName!="lab_speedometer_verticalsign")throw new Exception("Vertical sign command is missing");
            Console.WriteLine($"HUD adapter verified: {NativeSpeedometerCode.Commands.Length} commands; {lastName}");
        }
        var modbot=layout.ModbotInput??throw new Exception(layout.ModbotInputFailure);
        IntPtr callback=game.ReadPointer(image+modbot.MenuVtable+modbot.ShowSlot*8),state=callback+4096;
        if(Encoding.ASCII.GetString(Read(state,8))!=NativeModbotInputCode.Magic)throw new Exception("LAB Modbot adapter is not installed");
        int enabled=game.ReadValue<int>(state+NativeModbotInputCode.Enabled);long inputLease=game.ReadValue<long>(state+NativeModbotInputCode.Lease);
        bool expectedInput=active&&settings.ModbotFix&&reader.MaxHz is >0 and <1000;
        if((enabled!=0)!=expectedInput||(active&&inputLease<=Environment.TickCount64))throw new Exception("Unexpected Modbot activation/lease");
        Console.WriteLine($"PASS: live game {profile.Name}, PID {game.Id}; metrics {(active?indicator:"stopped")}; driver {reader.DriverName}; internal FPS limit {reader.MaxHz}; Modbot {(enabled!=0?"active":"standby/off")}");
        Console.WriteLine(FeatureStatus.Describe(settings,profile.Name,reader.DriverName,GameLauncher.LightingActive(game.Id),true,reader.MaxHz,null,true));
        return 0;
    }
}
