using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Lab.Runtime;
using Lab.Memory;
using Lab.Overlay;
internal static class Program
{
    private static readonly string Output=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../Tests/output"));
    private static int _checks;
    private static void Check(bool condition,string description){if(!condition)throw new Exception(description);_checks++;}
    private static int Main(string[] args)
    {
        try{if(args.Contains("--verify-live")||args.Contains("--verify-stopped"))return LiveGameChecks.Run(args.Contains("--verify-live"));Settings();Speeds();Driver();MetricsTests.Run(Check);NativeAdapterTests.Run(Check);ModbotInputTests.Run(Check);LowHealthTintTests.Run(Check);Console.WriteLine($"PASS: {_checks} checks, including executed x64 metrics, HUD, tint and Modbot fixtures.");return 0;}
        catch(Exception e){Console.Error.WriteLine(e);return 1;}
    }
    private static void Settings()
    {
        string folder=Path.GetFullPath(Path.Combine(Environment.GetEnvironmentVariable("LAB_TEST_DATA_ROOT")??Path.Combine(Path.GetTempPath(),"lab-tests"),"settings-"+Guid.NewGuid().ToString("N")));var store=new SettingsStore(folder);
        var initial=store.Snapshot();Check(!initial.SpeedometersEnabled&&initial.Current.Enabled&&initial.Landing.Enabled&&initial.Vertical.Enabled&&initial.VerticalShowSign&&initial.Current.Size==3&&initial.Landing.Size==3&&initial.Vertical.Size==3&&initial.TintOpacity==100,"First-run defaults match release 1.0");
        Check(LabSettings.CreateDefaults(p=>p==LabSettings.DefaultGamePath).GamePath==LabSettings.DefaultGamePath&&LabSettings.CreateDefaults(_=>false).GamePath=="","Steam executable is selected only when the exact default file exists");
        Check(initial.GamePath==(File.Exists(LabSettings.DefaultGamePath)?LabSettings.DefaultGamePath:""),"First-run game discovery uses the installed Steam location");
        string marker="log-location-"+Guid.NewGuid().ToString("N");Log.Information(marker);Check(Log.DirectoryPath==Path.Combine(AppContext.BaseDirectory,"logs")&&File.ReadAllText(Log.FilePath).Contains(marker)&&!File.Exists(Path.Combine(folder,"lab.log")),"Logs stay beside the program independently of the settings directory");
        foreach(var pair in new[]{("current",1.2),("landing",2.3),("vertical",3.4)})Check(store.Update(s=>ConsoleControls.Apply(s,3,[pair.Item2.ToString(System.Globalization.CultureInfo.InvariantCulture),pair.Item1])),"Independent size accepted");
        var s=new SettingsStore(folder).Snapshot();Check(s.Current.Size==1.2&&s.Landing.Size==2.3&&s.Vertical.Size==3.4,"Three different sizes survive restart");
        foreach(string invalid in new[]{"NaN","Infinity","0","10.1","-1"})Check(!ConsoleControls.Apply(s,3,[invalid,"vertical"]),"Invalid size rejected");
        Check(s.Vertical.Size==3.4&&s.Landing.Size==2.3,"Rejected values preserve sizes");
        Check(ConsoleControls.Apply(s,3,[".8"])&&s.Current.Size==.8&&s.Landing.Size==.8&&s.Vertical.Size==.8,"Untargeted size changes all three");
        Check(!ConsoleControls.Apply(s,3,["1","unknown"]),"Unknown target rejected");
        Check(ConsoleControls.Apply(s,6,["12","34","56","vertical"])&&s.Vertical.Color=="#0c2238"&&s.Current.Color!="#0c2238","RGB targets a single meter");
        Check(!ConsoleControls.Apply(s,6,["256","0","0"]),"Invalid RGB rejected");
        Check(ConsoleControls.Apply(s,4,["37.5"])&&s.TintOpacity==37.5&&!ConsoleControls.Apply(s,4,["101"]),"Tint range and fraction preserved");
        Check(ConsoleControls.Apply(s,7,["0"])&&!s.Vertical.Enabled&&s.Landing.Enabled,"Vertical visibility independent");
        Check(ConsoleControls.Apply(s,0,["0"])&&!s.SpeedometersEnabled&&s.Landing.Enabled,"Overall hide preserves individual preferences");
        Check(Enumerable.Range(0,NativeSpeedometerCode.Commands.Length).All(i=>Encoding.ASCII.GetByteCount("Invalid value. "+ConsoleControls.Help(s,i))<NativeSpeedometerCode.ConsoleTextCapacity),"All command responses fit native mailbox");
        using var json=JsonDocument.Parse("2.7");Check(store.Apply("landing.size",json.RootElement),"UI size path accepted");Check(new SettingsStore(folder).Snapshot().Landing.Size==2.7,"UI size saved to disk");
        Check(NativeSpeedometerCode.Commands.Length==9&&NativeSpeedometerCode.Commands.All(x=>x.StartsWith("lab_")),"Only nine selected lab_ commands");
        Check(new SettingsStore(folder).Snapshot().VerticalShowSign,"Default vertical speed includes its sign");
        using var sign=JsonDocument.Parse("true");Check(store.Apply("verticalShowSign",sign.RootElement)&&new SettingsStore(folder).Snapshot().VerticalShowSign,"UI vertical sign is saved across restart");
        Check(store.Update(v=>ConsoleControls.Apply(v,8,["0"]))&&!new SettingsStore(folder).Snapshot().VerticalShowSign,"Console sign command updates the same persisted setting");
        Check(store.Update(v=>{v.GamePath="";v.SpeedometersEnabled=true;v.VerticalShowSign=false;v.TintOpacity=42;v.Current.Size=2;return true;})&&new SettingsStore(folder).Snapshot() is {GamePath:"",SpeedometersEnabled:true,VerticalShowSign:false,TintOpacity:42,Current.Size:2},"User changes survive restart instead of being overwritten by defaults");
        Check(ConsoleControls.Apply(s,8,["1"])&&s.VerticalShowSign&&ConsoleControls.Help(s,8).Contains("is: 1"),"Console sign query reports its current state");
        Check(!ConsoleControls.Apply(s,8,["2"])&&!ConsoleControls.Apply(s,8,["1","vertical"])&&s.VerticalShowSign,"Invalid sign arguments preserve the selected mode");
        Check(NativeSpeedometerCode.MailboxOffset+NativeSpeedometerCode.Commands.Length*NativeSpeedometerCode.MailboxStride<=NativeSpeedometerCode.NamesOffset&&NativeSpeedometerCode.NamesOffset+NativeSpeedometerCode.Commands.Length*256<=NativeSpeedometerCode.StateSize&&NativeSpeedometerCode.CallbackStart+NativeSpeedometerCode.Commands.Length*NativeSpeedometerCode.CallbackStride<=8192,"Nine native mailboxes, names and callbacks fit without overlap");
        string lighting=GameLauncher.PrepareLighting(store);Directory.CreateDirectory(Output);File.WriteAllText(Path.Combine(Output,"lighting-path.txt"),lighting);Console.WriteLine("Lighting manifest prepared");
        GameLauncher.WriteLightingControl(store,false);Check(File.ReadAllText(Path.Combine(store.DirectoryPath,"lighting.ini")).Contains("Enabled=0")&&store.Snapshot().LightingFix,"Shutdown disables live lighting without changing saved preferences");
    }
    private static void Speeds()
    {
        var t=new HorizontalSpeedTracker(8,8);t.Update(3,4,true,0);Check(t.Current==5&&t.LastLanding is null,"Initial ground is not landing");
        t.Update(6,8,false,1);t.Update(9,12,true,10);Check(t.LastLanding==15,"Horizontal landing recorded after minimum airtime");t.Update(12,16,true,11);Check(t.LastLanding==20,"Landing retains adjacent physics impact peak");t.Update(30,40,true,30);Check(t.LastLanding==20,"Later walking does not replace landing");
        Check(SpeedSampler.VerticalSpeed(-12.5f,false)==12.5f&&SpeedSampler.VerticalSpeed(12.5f,false)==12.5f&&SpeedSampler.VerticalSpeed(float.NaN,true) is null&&SpeedSampler.VerticalSpeed(null,true) is null,"Vertical speed is unsigned magnitude");
        Check(SpeedometerText.Format(new(true,0,0,SpeedSampler.VerticalSpeed(-12.5f,false)))=="12.50","Vertical display has two decimals and no sign");
        Check(SpeedSampler.VerticalSpeed(-12.5f,true)==-12.5f&&SpeedSampler.VerticalSpeed(12.5f,true)==12.5f,"Signed vertical readings retain direction");
        Check(SpeedometerText.Format(new(true,0,0,12.5f,ShowSign:true))=="+12.50"&&SpeedometerText.Format(new(true,0,0,-12.5f,ShowSign:true))=="-12.50","Signed display shows both ascent and descent");
        Check(SpeedometerText.Format(new(true,0,0,-0.0001f,ShowSign:true))=="0.00"&&SpeedometerText.Format(new(true,0,0,-0f,ShowSign:true))=="0.00","Rounded zero does not flicker a direction sign");
        Check(SpeedometerText.Format(new(true,0,0,null,ShowSign:true))=="--","Missing signed reading retains the placeholder");
        Check(SpeedometerText.Format(new(true,0,0,float.MaxValue,ShowSign:true)).Length<=12,"Signed scientific notation fits the native text buffer");
        var r=NativeSpeedometer.CreateReadings(new(true,10,20,5,Scale:1.2),new(true,30,40,7,Scale:2.3),new(true,50,60,9,Scale:3.4),10);
        Check(Math.Abs(BitConverter.ToSingle(r,12)-.6f)<.00001&&Math.Abs(BitConverter.ToSingle(r,76)-1.15f)<.00001&&Math.Abs(BitConverter.ToSingle(r,140)-1.7f)<.00001,"Independent sizes reach three native draw records");
    }
    private static void Driver()
    {
        using var p=Process.GetCurrentProcess();IntPtr memory=p.AllocateMemory(512);try{
            IntPtr inline=memory+16,buffer=memory+64;string text="AMD Proprietary Driver";var bytes=Encoding.UTF8.GetBytes(text+"\0");Marshal.WriteIntPtr(memory,buffer);Marshal.WriteInt32(memory,8,text.Length);Marshal.WriteInt32(memory,12,64);p.WriteBytes(buffer,bytes);
            using(var control=new DriverLineControl(p,inline)){Check(control.Update(true)&&Marshal.ReadByte(buffer)==0,"AMD line hidden");Check(Marshal.ReadInt32(memory,8)==text.Length,"Driver string metadata untouched");control.Update(false);Check(p.ReadBytes(buffer,bytes.Length).SequenceEqual(bytes),"Driver line restored exactly");}
            p.WriteBytes(buffer,Encoding.UTF8.GetBytes("NVIDIA Display Driver\0"));Marshal.WriteInt32(memory,8,21);using(var control=new DriverLineControl(p,inline)){Check(!control.Update(true)&&Marshal.ReadByte(buffer)==(byte)'N',"Other driver strings untouched");}
        }finally{p.FreeMemory(memory);}
    }
}
