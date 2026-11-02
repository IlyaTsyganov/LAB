using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Lab.Memory;
using Lab.Overlay;
using Lab.Runtime;

internal static class MetricsTests
{
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate void Row(IntPtr self,IntPtr cursor,int margin,float scale,IntPtr color,IntPtr format,long a,long b,long d);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate long Clock();
    internal static void Run(Action<bool,string> check)
    {
        var settings = new LabSettings{SpeedometersEnabled=true,TintOpacity=50};
        check(LabIndicator.Format(settings)=="LAB (MR)","Only M and R appear in the indicator");
        settings.SpeedometersEnabled=settings.LightingFix=settings.ModbotFix=settings.HideDriverName=false;settings.TintOpacity=100;
        check(LabIndicator.Format(settings)=="LAB","No empty parentheses when all features are off");
        settings.ModbotFix=true;settings.TintOpacity=99.9;
        check(LabIndicator.Format(settings)=="LAB (MR)","R includes any opacity other than 100 percent");
        settings.SpeedometersEnabled=true;settings.Current.Enabled=settings.Landing.Enabled=settings.Vertical.Enabled=false;
        check(!LabIndicator.Format(settings).Contains('S'),"Speedometer preferences never add an indicator letter");
        // A standby/unavailable native feature must not silently change selected letters.
        settings=new LabSettings{SpeedometersEnabled=true};settings.TintOpacity=100;
        var availability=FeatureStatus.Describe(settings,"1.1","AMD proprietary driver",false,true,1000,null,true);
        check(LabIndicator.Format(settings)=="LAB (M)"&&availability.Lighting.Contains("NVIDIA")&&availability.Modbot.Contains("1000"),"AMD and a 1000 FPS limit keep only selected M and explain availability");
        availability=FeatureStatus.Describe(settings,"1.1","NVIDIA",true,true,999,null,true);
        check(availability.Lighting=="Active"&&availability.Modbot=="Active","Supported active lighting and Modbot are reported accurately");
        settings.LightingFix=settings.ModbotFix=false;
        availability=FeatureStatus.Describe(settings,"1.1","AMD",false,true,1000,null,true);
        check(LabIndicator.Format(settings)=="LAB"&&availability.Lighting=="Off"&&availability.Modbot=="Off","Disabling preferences removes letters and overrides native standby status");
        settings.LightingFix=settings.ModbotFix=true;settings.TintOpacity=50;
        availability=FeatureStatus.Describe(settings,"6.66 Rev 3.2","NVIDIA",false,false,-1,"Input layout not ready",false);
        check(availability.Lighting.Contains("1.0, 1.1 or 2.0")&&availability.Modbot=="Unavailable"&&availability.Tint.Contains("Unavailable")&&LabIndicator.Format(settings)=="LAB (MR)","Unavailable adapters retain configured letters and report their actual failure");
        using var process=Process.GetCurrentProcess();IntPtr memory=process.AllocateMemory(8192),state=memory+4096;
        IntPtr formats=Marshal.AllocHGlobal(256),cursor=Marshal.AllocHGlobal(8),mode=Marshal.AllocHGlobal(8),thunks=process.AllocateMemory(4096);
        var rows=new List<(string Text,float Scale,long B,long D)>();
        Row original=(_,_,_,scale,_,format,a,b,d)=>{
            string fmt=Marshal.PtrToStringAnsi(format)!,text=fmt=="%s"?Marshal.PtrToStringUTF8(new IntPtr(a))!:fmt;
            lock(rows)rows.Add((text,scale,b,d));
        };Clock clock=()=>100;
        try {
            process.WriteBytes(formats,new byte[256]);process.WriteBytes(formats,Encoding.ASCII.GetBytes("%i FPS\0\0%.2fms\0"));
            process.WriteBytes(formats+64,Encoding.ASCII.GetBytes("Driver %s\0"));process.WriteBytes(formats+96,Encoding.ASCII.GetBytes("DESRU (ABC)\0"));
            process.WriteBytes(formats+128,Encoding.ASCII.GetBytes("%s\0"));process.WriteBytes(formats+160,Encoding.ASCII.GetBytes("AMD Proprietary Driver\0"));
            var data=new byte[4096];BitConverter.GetBytes(1000L).CopyTo(data,0);BitConverter.GetBytes(mode.ToInt64()).CopyTo(data,16);BitConverter.GetBytes(1.25f).CopyTo(data,24);Encoding.ASCII.GetBytes("LAB (MR)\0").CopyTo(data,64);Encoding.ASCII.GetBytes("%s\0").CopyTo(data,128);Encoding.ASCII.GetBytes("%i FPS\0").CopyTo(data,160);process.WriteBytes(state,data);Marshal.WriteInt32(mode,2);
            var hardwareCalls = new Row[2];
            for(int index = 0; index < 2; index++) {
                var stub = new NativeSpeedometerCode.Code();
                stub.Emit("4883EC48");
                for(int arg = 0; arg < 5; arg++) {
                    stub.Emit("488B8424"); stub.Bytes.AddRange(BitConverter.GetBytes(0x70 + arg * 8));
                    stub.Emit("48894424" + (0x20 + arg * 8).ToString("X2"));
                }
                stub.Call(memory.ToInt64());
                stub.Emit("4883C448C3");
                process.WriteBytes(thunks + index * 128, stub.Finish());
            }
            PerformanceMetrics.StoreHardwareNames(data,["AMD Ryzen 9 9950X", "Intel Core Ultra 9", "AMD Radeon RX 7900 XTX", "NVIDIA GeForce RTX 5090", "Intel Arc B580", "Other vendor GPU 100%"]);
            process.WriteBytes(state,data);
            process.VirtualProtect(thunks,4096,MemPageProtect.PAGE_EXECUTE_READ);
            for(int index = 0; index < 2; index++) hardwareCalls[index] = Marshal.GetDelegateForFunctionPointer<Row>(thunks + index * 128);
            process.WriteBytes(memory,MetricsCode.Create(state.ToInt64(),Marshal.GetFunctionPointerForDelegate(original).ToInt64(),Marshal.GetFunctionPointerForDelegate(clock).ToInt64(),formats.ToInt64(),(formats+64).ToInt64(),0,long.MaxValue));
            process.VirtualProtect(memory,4096,MemPageProtect.PAGE_EXECUTE_READ);var row=Marshal.GetDelegateForFunctionPointer<Row>(memory);
            void Send(int at,long arg=0,float scale=1.25f)=>row(IntPtr.Zero,cursor,10,scale,IntPtr.Zero,formats+at,arg,123456789,987654321);
            Send(0,144,1.75f);Send(8);Send(96);Send(128,(formats+160).ToInt64());Send(64);
            check(rows.Select(x=>x.Text).SequenceEqual(new[]{"%i FPS","LAB (MR)"}),"Standalone metrics contain only FPS and LAB");
            check(rows[0].Scale==1.75f&&rows[1].Scale==1.25f,"LAB uses the original small metrics font");
            // A state block retained from the older preview must not render its
            // cvar or player-cheat text in either standalone or DESRU mode.
            IntPtr legacyText=process.AllocateMemory(16+2*1024);
            try {
                process.WriteBytes(legacyText,new byte[16+2*1024]);Marshal.WriteInt32(legacyText,2);
                process.WriteBytes(legacyText+16,Encoding.UTF8.GetBytes("g_speed = 2\0"));
                process.WriteBytes(legacyText+16+1024,Encoding.UTF8.GetBytes("noclip\0"));
                Marshal.WriteIntPtr(state,40,legacyText);
                rows.Clear();Send(0);Send(8);
                check(rows.Select(x=>x.Text).SequenceEqual(new[]{"%i FPS","LAB (MR)"}),"Standalone LAB never renders old cvar or player-cheat text");
                rows.Clear();Marshal.WriteInt32(state,8,1);process.WriteBytes(formats+64,[0]);
                Send(0);Send(96);Send(128,(formats+160).ToInt64());Send(64);
                check(rows.Select(x=>x.Text).SequenceEqual(new[]{"%i FPS","DESRU (ABC)","LAB (MR)","AMD Proprietary Driver"}),"DESRU coexistence never adds LAB cvar rows and preserves driver order");
            } finally { Marshal.WriteIntPtr(state,40,IntPtr.Zero);process.FreeMemory(legacyText); }
            rows.Clear();Marshal.WriteInt32(state,8,1);process.WriteBytes(formats+64,[0]);
            Send(0,144,1.75f);Send(96);Send(128,(formats+160).ToInt64());Send(64);
            check(rows.Select(x=>x.Text).SequenceEqual(new[]{"%i FPS","DESRU (ABC)","LAB (MR)","AMD Proprietary Driver"}),"DESRU text, LAB and AMD driver keep their required order");
            check(rows.All(x=>x.B==123456789&&x.D==987654321),"Original printf stack arguments survive native calls");
            rows.Clear();Marshal.WriteInt32(state,12,1);Send(0);Send(96);Send(128,(formats+160).ToInt64());Send(64);
            check(rows.Select(x=>x.Text).SequenceEqual(new[]{"%i FPS","DESRU (ABC)","LAB (MR)"}),"H removes the AMD row while retaining LAB");
            rows.Clear();process.WriteBytes(formats+200,Encoding.ASCII.GetBytes("AMD Ryzen 9\0"));Send(128,(formats+200).ToInt64());check(rows.Single().Text=="AMD Ryzen 9","H leaves AMD CPU and unrelated DESRU text intact");
            foreach(string model in new[]{"AMD Ryzen 9 9950X", "Intel Core Ultra 9", "AMD Radeon RX 7900 XTX", "NVIDIA GeForce RTX 5090", "Intel Arc B580", "Other vendor GPU 100%"}) {
                process.WriteBytes(formats+200,Encoding.ASCII.GetBytes(model+"\0"));
                foreach(var hardware in hardwareCalls) {
                    rows.Clear();Marshal.WriteInt32(state,12,1);
                    hardware(IntPtr.Zero,cursor,10,1.25f,IntPtr.Zero,formats+128,(formats+200).ToInt64(),123456789,987654321);
                    check(rows.Count==0,"H hides the audited hardware row independently of vendor: "+model);
                    Marshal.WriteInt32(state,12,0);
                    hardware(IntPtr.Zero,cursor,10,1.25f,IntPtr.Zero,formats+128,(formats+200).ToInt64(),123456789,987654321);
                    check(rows.Single().Text==model&&rows[0].B==123456789&&rows[0].D==987654321,"Disabling H restores exact hardware text and printf arguments");
                }
            }
            Marshal.WriteInt32(state,12,1);rows.Clear();
            hardwareCalls[0](IntPtr.Zero,cursor,10,1.25f,IntPtr.Zero,formats+96,0,123456789,987654321);
            check(rows.Single().Text=="DESRU (ABC)","A custom DESRU format at a reused hardware call is preserved");
            rows.Clear();process.WriteBytes(formats+8,Encoding.ASCII.GetBytes("vel: %f\0"));
            process.WriteBytes(formats+200,Encoding.ASCII.GetBytes("arbitrary DESRU scroll text\0"));
            hardwareCalls[0](IntPtr.Zero,cursor,10,1.25f,IntPtr.Zero,formats+128,(formats+200).ToInt64(),123456789,987654321);
            check(rows.Single().Text=="arbitrary DESRU scroll text","DESRU-owned CPU/GPU argument buffers retain trainer and arbitrary scroll text");
            rows.Clear();process.WriteBytes(formats+200,Encoding.ASCII.GetBytes("amd RYZEN 9 9950x\0"));
            hardwareCalls[0](IntPtr.Zero,cursor,10,1.25f,IntPtr.Zero,formats+128,(formats+200).ToInt64(),123456789,987654321);
            check(rows.Count==0,"Actual hardware is hidden even when DESRU rewrites the frame-time row, with case-independent matching");
            process.WriteBytes(formats+8,Encoding.ASCII.GetBytes("%.2fms\0\0"));
            rows.Clear();Marshal.WriteInt64(state,0,0);
            hardwareCalls[1](IntPtr.Zero,cursor,10,1.25f,IntPtr.Zero,formats+128,(formats+200).ToInt64(),123456789,987654321);
            check(rows.Count==1,"Closing LAB restores hardware rows even with H selected");Marshal.WriteInt64(state,0,1000);
            // Reproduce the retail 6.x order: CPU is a third string call before
            // the Vulkan block; GPU and AMD driver use later shared %s calls.
            // Include literal/replaced driver formats and the final callback.
            foreach(bool hide in new[]{false,true}) for(int driverStyle=0;driverStyle<4;driverStyle++) {
                rows.Clear();Marshal.WriteInt32(state,8,1);Marshal.WriteInt32(state,12,hide?1:0);
                process.WriteBytes(formats+8,Encoding.ASCII.GetBytes("6.66 (MFL)\0"));
                process.WriteBytes(formats+64,Encoding.ASCII.GetBytes(driverStyle==3?"AMD Proprietary Driver\0":"\0"));
                process.WriteBytes(formats+160,Encoding.ASCII.GetBytes("AMD Proprietary Driver\0"));
                Send(0);Send(8);
                process.WriteBytes(formats+200,Encoding.ASCII.GetBytes("AMD Ryzen 9 9950X\0"));
                Send(128,(formats+200).ToInt64()); // Not one of the two trailing stubs.
                if(driverStyle==0)hardwareCalls[0](IntPtr.Zero,cursor,10,.9f,IntPtr.Zero,formats+128,(formats+160).ToInt64(),777,888);
                if(driverStyle==1)Send(128,(formats+160).ToInt64());
                if(driverStyle==2)Send(160);
                process.WriteBytes(formats+200,Encoding.ASCII.GetBytes("NVIDIA GeForce RTX 5090\0"));
                hardwareCalls[1](IntPtr.Zero,cursor,10,1.25f,IntPtr.Zero,formats+128,(formats+200).ToInt64(),123456789,987654321);
                Send(64);
                string[] expected=hide?["%i FPS","6.66 (MFL)","LAB (MR)"]:
                    ["%i FPS","6.66 (MFL)","AMD Ryzen 9 9950X","NVIDIA GeForce RTX 5090","LAB (MR)","AMD Proprietary Driver"];
                check(rows.Select(r=>r.Text).SequenceEqual(expected),$"Retail three-string order, AMD driver style {driverStyle}, Hide={hide}: indicator precedes the deferred driver");
                if(!hide&&driverStyle==0)check(rows.Last().Scale==.9f&&rows.Last().B==777&&rows.Last().D==888,"Deferred AMD row retains original font and all printf arguments");
            }
            process.WriteBytes(formats+8,Encoding.ASCII.GetBytes("%.2fms\0\0"));
            process.WriteBytes(formats+64,[0]);
            rows.Clear();process.WriteBytes(formats+160,Encoding.ASCII.GetBytes("AMD proprietary driver\0"));Send(0);Send(128,(formats+160).ToInt64());Send(64);check(rows.Select(x=>x.Text).SequenceEqual(new[]{"%i FPS","LAB (MR)"}),"AMD driver matching ignores letter case");
            rows.Clear();Marshal.WriteInt32(state,8,0);process.WriteBytes(formats,Encoding.ASCII.GetBytes("%i FPS stale DESRU\0"));Send(0);Send(8);check(rows.Select(x=>x.Text).SequenceEqual(new[]{"%i FPS","LAB (MR)"}),"Standalone FPS drops stale DESRU suffixes after DESRU closes");process.WriteBytes(formats,Encoding.ASCII.GetBytes("%i FPS\0\0%.2fms\0"));Marshal.WriteInt32(state,8,1);process.WriteBytes(formats+160,Encoding.ASCII.GetBytes("AMD Proprietary Driver\0"));
            rows.Clear();Marshal.WriteInt32(mode,1);Send(0);check(rows.Count==2&&rows[1].Text=="LAB (MR)","Minimal DESRU metrics still include LAB");
            rows.Clear();Marshal.WriteInt64(state,0,0);Send(8);Send(128,(formats+160).ToInt64());
            check(rows.Select(x=>x.Text).SequenceEqual(new[]{"%.2fms","AMD Proprietary Driver"}),"Closing LAB immediately restores original formatter behavior");
            rows.Clear();Marshal.WriteInt64(state,0,99);Send(96);check(rows.Single().Text=="DESRU (ABC)","An expired lease also disables LAB after a crash");
            rows.Clear();Marshal.WriteInt32(mode,2);Marshal.WriteInt32(state,8,1);Marshal.WriteInt32(state,12,0);Marshal.WriteInt64(state,0,1000);
            process.WriteBytes(state+64,Encoding.ASCII.GetBytes(PerformanceMetrics.RestartText+"\0"));
            Send(0);Send(8);Send(96);Send(128,(formats+160).ToInt64());Send(64);
            check(rows.Select(x=>x.Text).SequenceEqual(new[]{"%i FPS","%.2fms","DESRU (ABC)","RESTART GAME(LAB)","AMD Proprietary Driver"}),"Restart notice replaces feature flags and retains original/DESRU metrics and AMD row");
            rows.Clear();Marshal.WriteInt32(mode,1);Send(0);check(rows.Select(x=>x.Text).SequenceEqual(new[]{"%i FPS","RESTART GAME(LAB)"}),"Restart notice is visible with minimal FPS metrics");
            Marshal.WriteInt32(mode,0);
            using(var notice=new PerformanceMetrics(process,state,mode)){
                notice.UpdateRestartNotice();
                check(Marshal.ReadInt32(mode)==1&&Marshal.ReadInt32(state,8)==1&&Marshal.ReadInt32(state,12)==0&&Marshal.PtrToStringAnsi(state+64)==PerformanceMetrics.RestartText,"Notice-only controller enables only the notice and preserves the driver row");
            }
            check(Marshal.ReadInt32(mode)==0&&Marshal.ReadInt64(state)==0,"Closing the restart notice restores the original metrics setting");
        } finally { process.FreeMemory(memory);process.FreeMemory(thunks);Marshal.FreeHGlobal(formats);Marshal.FreeHGlobal(cursor);Marshal.FreeHGlobal(mode);GC.KeepAlive(original);GC.KeepAlive(clock); }
        foreach(string path in (Environment.GetEnvironmentVariable("LAB_LAYOUT_TEST_PATHS")??"").Split('|',StringSplitOptions.RemoveEmptyEntries)) {
            var layout=NativeGameLayout.Resolve(path);check(layout.Metrics is not null,"Metrics resolve from the real executable: "+path+": "+layout.MetricsFailure);
            var profile=GameProfile.Resolve(path);check(profile.Metrics!=0,"Managed metrics profile recognizes the exact installed executable");
            Console.WriteLine($"Metrics layout verified: {Path.GetDirectoryName(path)}; small font {layout.Metrics!.SmallScale}");
        }
    }
}
