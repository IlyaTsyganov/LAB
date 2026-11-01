using System.Diagnostics;
using System.Buffers.Binary;
using Lab.Game;
using Lab.Runtime;
using Lab.Memory;
using Lab.Overlay;

internal static class PortPolicyTests
{
    internal static void Run(Action<bool,string> check)
    {
        foreach(string path in (Environment.GetEnvironmentVariable("LAB_LAYOUT_TEST_PATHS")??"").Split('|',StringSplitOptions.RemoveEmptyEntries)) {
            var profile=GameProfile.Resolve(path);
            var layout=NativeGameLayout.Resolve(path);
            check(profile.Metrics!=0&&profile.MaxHz!=0&&profile.ModuleSize==layout.ImageSize,
                "The exact installed executable has coherent metrics, tick and image-size profiles: "+profile.Name);
            check(layout.Tint is not null&&layout.ModbotInput is not null&&layout.Cvars is not null&&layout.PlayerCheats is not null,
                "Tint, Modbot, cvar and player-cheat layouts resolve: "+profile.Name);
            if(profile.Name is "2.0" or "2.1" or "3.0" or "3.1" or "4.0" or "4.1" or "5.0" or "6.66")
                check(profile.NativePlayer is not null,"Newly ported builds use their audited native player chain: "+profile.Name);
            bool early=profile.Name is "1.0 (Release)" or "1.1" or "2.0";
            check(LightingFixPolicy.CanUse(path,0x10de)==early&&!LightingFixPolicy.CanUse(path,0x1002),
                "Installed patch lighting eligibility agrees with the native NVIDIA guard: "+profile.Name);
        }
        check(!LightingFixPolicy.CanUse("",0x10de)&&!LightingFixPolicy.CanUse("missing-game.exe",0x10de),
            "An unset or missing executable keeps lighting disabled without breaking startup");
        string root=Path.Combine(Path.GetTempPath(),"lab-policy-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try {
            string exe=Path.Combine(root,"DOOMEternalx64vk.exe");
            foreach(var item in new[]{(0x5e794131,0x1e706000,0x336210,"1.0"),(0x5ec7deca,0x1d551000,0x339b10,"1.1"),(0x5eeb0267,0x1d396000,0x303dc0,"2.0")}) {
                File.WriteAllBytes(exe,Image(item.Item1,item.Item2,item.Item3));
                check(GameExecutableInfo.Read(exe)?.Version==item.Item4&&LightingFixPolicy.CanUse(exe,0x10de),
                    "Audited early executable identity permits the NVIDIA correction: "+item.Item4);
                foreach(uint vendor in new uint[]{0,0x1002,0x8086})
                    check(!LightingFixPolicy.CanUse(exe,vendor),"AMD, Intel and unknown hardware cannot opt into the NVIDIA correction");
            }
            File.WriteAllBytes(exe,[1,2,3]);
            var settings=new LabSettings {GamePath=exe,LightingFix=true};
            var before=Environment.GetEnvironmentVariable("VK_INSTANCE_LAYERS");
            var info=GameLauncher.CreateStartInfo(settings,root);
            check(info.Environment["LAB_LAUNCHED"]=="1"&&info.Environment["SteamAppId"]=="782330"&&
                info.Environment["LAB_LIGHTING_FIX"]=="0"&&info.Environment["LAB_DATA_DIRECTORY"]==root,
                "The direct child carries LAB ownership and Steam identity even without eligible lighting");
            check(settings.LightingFix&&Environment.GetEnvironmentVariable("VK_INSTANCE_LAYERS")==before&&
                (info.Environment.TryGetValue("VK_INSTANCE_LAYERS",out var childLayers)?childLayers:null)==before,
                "Unsupported lighting preserves saved preferences and the parent Vulkan environment");
            var store=new SettingsStore(root);
            store.Update(s=>{s.GamePath=exe;s.LightingFix=true;return true;});
            GameLauncher.WriteLightingControl(store);
            check(File.ReadAllText(Path.Combine(root,"lighting.ini")).Contains("Enabled=0"),
                "Unsupported executables cannot activate the lighting control file");
            using var owner=Process.GetCurrentProcess();
            GameLaunchHistory.Record(owner,owner.MainModule!.FileName,store);
            check(GameLauncher.WasLaunchedByLab(owner,store),"Exact launch records establish LAB ownership");
            GameLaunchHistory.Record(owner,exe,store);
            check(!GameLauncher.WasLaunchedByLab(owner,store),"An unrelated executable cannot inherit a launch record");
        } finally { Directory.Delete(root,true); }
    }

    // Minimal PE data fixtures: these files are never loaded or executed.
    private static byte[] Image(int timestamp,int imageSize,int fingerprint)
    {
        var data=new byte[0x40200];
        void I16(int at,ushort value)=>BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(at),value);
        void I32(int at,int value)=>BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(at),value);
        I16(0,0x5a4d);I32(0x3c,0x80);I32(0x80,0x4550);
        I16(0x84,0x8664);I16(0x86,1);I32(0x88,timestamp);I16(0x94,0xf0);I16(0x96,0x22);
        I16(0x98,0x20b);I32(0x98+32,0x1000);I32(0x98+36,0x200);
        I32(0x98+56,imageSize);I32(0x98+60,0x200);I32(0x98+108,16);
        I32(0x188+8,0x40000);I32(0x188+12,0x300000);I32(0x188+16,0x40000);I32(0x188+20,0x200);
        Convert.FromHexString("48895C2408574883EC20").CopyTo(data,fingerprint-0x300000+0x200);
        return data;
    }
}
