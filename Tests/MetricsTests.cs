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
        check(LabIndicator.Format(settings)=="LAB (SLMHR)","Indicator order is SLMHR");
        settings.SpeedometersEnabled=settings.LightingFix=settings.ModbotFix=settings.HideDriverName=false;settings.TintOpacity=100;
        check(LabIndicator.Format(settings)=="LAB","No empty parentheses when all features are off");
        settings.ModbotFix=true;settings.TintOpacity=99.9;
        check(LabIndicator.Format(settings)=="LAB (MR)","R includes any opacity other than 100 percent");
        settings.SpeedometersEnabled=true;settings.Current.Enabled=settings.Landing.Enabled=settings.Vertical.Enabled=false;
        check(!LabIndicator.Format(settings).Contains('S'),"S requires at least one enabled meter");
        // A standby/unavailable native feature must not silently change selected letters.
        settings=new LabSettings{SpeedometersEnabled=true};settings.TintOpacity=100;
        var availability=FeatureStatus.Describe(settings,"1.1","AMD proprietary driver",false,true,1000,null,true);
        check(LabIndicator.Format(settings)=="LAB (SLMH)"&&availability.Lighting.Contains("NVIDIA")&&availability.Modbot.Contains("1000"),"AMD and a 1000 FPS limit retain selected L/M while explaining availability");
        availability=FeatureStatus.Describe(settings,"1.1","NVIDIA",true,true,999,null,true);
        check(availability.Lighting=="Active"&&availability.Modbot=="Active","Supported active lighting and Modbot are reported accurately");
        settings.LightingFix=settings.ModbotFix=false;
        availability=FeatureStatus.Describe(settings,"1.1","AMD",false,true,1000,null,true);
        check(LabIndicator.Format(settings)=="LAB (SH)"&&availability.Lighting=="Off"&&availability.Modbot=="Off","Disabling preferences removes letters and overrides native standby status");
        settings.LightingFix=settings.ModbotFix=true;settings.TintOpacity=50;
        availability=FeatureStatus.Describe(settings,"6.66 Rev 3.2","NVIDIA",false,false,-1,"Input layout not ready",false);
        check(availability.Lighting.Contains("1.0, 1.1 or 2.0")&&availability.Modbot=="Unavailable"&&availability.Tint.Contains("Unavailable")&&LabIndicator.Format(settings)=="LAB (SLMHR)","Unavailable adapters retain configured letters and report their actual failure");
        using var process=Process.GetCurrentProcess();IntPtr memory=process.AllocateMemory(8192),state=memory+4096;
        IntPtr formats=Marshal.AllocHGlobal(256),cursor=Marshal.AllocHGlobal(8),mode=Marshal.AllocHGlobal(8);
        var rows=new List<(string Text,float Scale,long B,long D)>();
        Row original=(_,_,_,scale,_,format,a,b,d)=>{string fmt=Marshal.PtrToStringAnsi(format)!;rows.Add((fmt=="%s"?Marshal.PtrToStringAnsi(new IntPtr(a))!:fmt,scale,b,d));};Clock clock=()=>100;
        try {
            process.WriteBytes(formats,new byte[256]);process.WriteBytes(formats,Encoding.ASCII.GetBytes("%i FPS\0\0%.2fms\0"));
            process.WriteBytes(formats+64,Encoding.ASCII.GetBytes("Driver %s\0"));process.WriteBytes(formats+96,Encoding.ASCII.GetBytes("DESRU (ABC)\0"));
            process.WriteBytes(formats+128,Encoding.ASCII.GetBytes("%s\0"));process.WriteBytes(formats+160,Encoding.ASCII.GetBytes("AMD Proprietary Driver\0"));
            var data=new byte[4096];BitConverter.GetBytes(1000L).CopyTo(data,0);BitConverter.GetBytes(mode.ToInt64()).CopyTo(data,16);BitConverter.GetBytes(1.25f).CopyTo(data,24);Encoding.ASCII.GetBytes("LAB (SLMHR)\0").CopyTo(data,64);Encoding.ASCII.GetBytes("%s\0").CopyTo(data,128);Encoding.ASCII.GetBytes("%i FPS\0").CopyTo(data,160);process.WriteBytes(state,data);Marshal.WriteInt32(mode,2);
            process.WriteBytes(memory,MetricsCode.Create(state.ToInt64(),Marshal.GetFunctionPointerForDelegate(original).ToInt64(),Marshal.GetFunctionPointerForDelegate(clock).ToInt64(),formats.ToInt64(),(formats+64).ToInt64(),0,long.MaxValue));
            process.VirtualProtect(memory,4096,MemPageProtect.PAGE_EXECUTE_READ);var row=Marshal.GetDelegateForFunctionPointer<Row>(memory);
            void Send(int at,long arg=0,float scale=1.25f)=>row(IntPtr.Zero,cursor,10,scale,IntPtr.Zero,formats+at,arg,123456789,987654321);
            Send(0,144,1.75f);Send(8);Send(96);Send(128,(formats+160).ToInt64());Send(64);
            check(rows.Select(x=>x.Text).SequenceEqual(new[]{"%i FPS","LAB (SLMHR)"}),"Standalone metrics contain only FPS and LAB");
            check(rows[0].Scale==1.75f&&rows[1].Scale==1.25f,"LAB uses the original small metrics font");
            rows.Clear();Marshal.WriteInt32(state,8,1);process.WriteBytes(formats+64,[0]);
            Send(0,144,1.75f);Send(96);Send(128,(formats+160).ToInt64());Send(64);
            check(rows.Select(x=>x.Text).SequenceEqual(new[]{"%i FPS","DESRU (ABC)","LAB (SLMHR)","AMD Proprietary Driver"}),"DESRU text, LAB and AMD driver keep their required order");
            check(rows.All(x=>x.B==123456789&&x.D==987654321),"Original printf stack arguments survive native calls");
            rows.Clear();Marshal.WriteInt32(state,12,1);Send(0);Send(96);Send(128,(formats+160).ToInt64());Send(64);
            check(rows.Select(x=>x.Text).SequenceEqual(new[]{"%i FPS","DESRU (ABC)","LAB (SLMHR)"}),"H removes the AMD row while retaining LAB");
            rows.Clear();process.WriteBytes(formats+200,Encoding.ASCII.GetBytes("AMD Ryzen 9\0"));Send(128,(formats+200).ToInt64());check(rows.Single().Text=="AMD Ryzen 9","H leaves AMD CPU and unrelated DESRU text intact");
            rows.Clear();process.WriteBytes(formats+160,Encoding.ASCII.GetBytes("AMD proprietary driver\0"));Send(0);Send(128,(formats+160).ToInt64());Send(64);check(rows.Select(x=>x.Text).SequenceEqual(new[]{"%i FPS","LAB (SLMHR)"}),"AMD driver matching ignores letter case");
            rows.Clear();Marshal.WriteInt32(state,8,0);process.WriteBytes(formats,Encoding.ASCII.GetBytes("%i FPS stale DESRU\0"));Send(0);Send(8);check(rows.Select(x=>x.Text).SequenceEqual(new[]{"%i FPS","LAB (SLMHR)"}),"Standalone FPS drops stale DESRU suffixes after DESRU closes");process.WriteBytes(formats,Encoding.ASCII.GetBytes("%i FPS\0\0%.2fms\0"));Marshal.WriteInt32(state,8,1);process.WriteBytes(formats+160,Encoding.ASCII.GetBytes("AMD Proprietary Driver\0"));
            rows.Clear();Marshal.WriteInt32(mode,1);Send(0);check(rows.Count==2&&rows[1].Text=="LAB (SLMHR)","Minimal DESRU metrics still include LAB");
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
        } finally { process.FreeMemory(memory);Marshal.FreeHGlobal(formats);Marshal.FreeHGlobal(cursor);Marshal.FreeHGlobal(mode);GC.KeepAlive(original);GC.KeepAlive(clock); }
        foreach(string path in (Environment.GetEnvironmentVariable("LAB_LAYOUT_TEST_PATHS")??"").Split('|',StringSplitOptions.RemoveEmptyEntries)) {
            var layout=NativeGameLayout.Resolve(path);check(layout.Metrics is not null,"Metrics resolve from the real executable: "+path+": "+layout.MetricsFailure);
            Console.WriteLine($"Metrics layout verified: {Path.GetDirectoryName(path)}; small font {layout.Metrics!.SmallScale}");
        }
    }
}
