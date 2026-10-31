using System.Diagnostics;
using System.Text;
using Lab.Memory;
using Lab.Overlay;
using Lab.Runtime;

// Explicit opt-in, read-only acceptance checks against LAB's existing adapters.
internal static class LiveGameChecks
{
    internal static int Run(bool active,bool restart=false)
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
        string expected=restart?PerformanceMetrics.RestartText:LabIndicator.Format(settings);
        if(active?(lease<=Environment.TickCount64||indicator!=expected):lease>Environment.TickCount64)throw new Exception("Unexpected metrics lease or indicator: "+indicator);
        if(active){
            IntPtr hudSlot=image+layout.HudSlot;hudSlot=HudChain.UnwrapHud(game,hudSlot,game.ReadPointer(hudSlot));
            IntPtr render=game.ReadPointer(hudSlot);var head=Read(render,20);
            bool ours=head.AsSpan(0,12).SequenceEqual(Convert.FromHexString("5356574883EC40488BF248BB"));
            if(!ours&&!restart)throw new Exception("Unknown LAB HUD renderer");
            if(ours){
                IntPtr hudState=new(BitConverter.ToInt64(head,12));
                if(restart){
                    if(game.ReadValue<int>(hudState)>0||game.ReadValue<int>(hudState+LowHealthTintCode.LeaseOffset)>0)
                        throw new Exception("Normal HUD/tint features are active in restart mode");
                }else{
                    if(Encoding.ASCII.GetString(Read(hudState+48,8))!=NativeSpeedometerCode.Magic||game.ReadValue<int>(hudState+4)!=NativeSpeedometerCode.Commands.Length)throw new Exception("New LAB console command registration is not ready");
                    string lastName=Encoding.ASCII.GetString(Read(hudState+NativeSpeedometerCode.NamesOffset+8*256,64)).Split('\0')[0];
                    if(lastName!="lab_speedometer_verticalsign")throw new Exception("Vertical sign command is missing");
                }
            }
        }
        var modbot=layout.ModbotInput??throw new Exception(layout.ModbotInputFailure);
        IntPtr callback=game.ReadPointer(image+modbot.MenuVtable+modbot.ShowSlot*8),state=callback+4096;
        bool guard=(game.ReadBytes(state,8) is byte[] magic)&&Encoding.ASCII.GetString(magic)==NativeModbotInputCode.Magic;
        if(!guard&&!restart)throw new Exception("LAB Modbot adapter is not installed");
        int enabled=guard?game.ReadValue<int>(state+NativeModbotInputCode.Enabled):0;
        long inputLease=guard?game.ReadValue<long>(state+NativeModbotInputCode.Lease):0;
        bool expectedInput=active&&!restart&&settings.ModbotFix&&reader.MaxHz is >0 and <1000;
        if((enabled!=0)!=expectedInput||(active&&!restart&&inputLease<=Environment.TickCount64))throw new Exception("Unexpected Modbot activation/lease");
        if(restart&&(!File.ReadAllText(Path.Combine(store.DirectoryPath,"lighting.ini")).Contains("Enabled=0")||game.ReadValue<int>(metrics+4096+12)!=0))
            throw new Exception("Normal lighting or driver filtering is active in restart mode");
        Console.WriteLine($"PASS: game {profile.Name}, PID {game.Id}; {(active?indicator:"stopped")}; normal features {(restart?"disabled":"available")}");
        return 0;
    }
}
