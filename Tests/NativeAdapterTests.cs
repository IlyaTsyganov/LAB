using Lab.Memory;
using Lab.Overlay;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
internal static class NativeAdapterTests
{
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]private delegate void Hud(IntPtr hud,IntPtr gui);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]private delegate float Dimension(IntPtr gui);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]private delegate void Draw(IntPtr gui,float x,float y,IntPtr text,IntPtr color,byte force,float scale);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]private delegate void Add(IntPtr self,IntPtr name,IntPtr callback,IntPtr help,IntPtr complete,uint flags);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]private delegate void Command(IntPtr args);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]private delegate void Print(int channel,int severity,IntPtr format,IntPtr args);
    internal static void Run(Action<bool,string> check)
    {
        using var p=Process.GetCurrentProcess();IntPtr memory=p.AllocateMemory(16384),args=Marshal.AllocHGlobal(128);IntPtr state=memory+8192;
        int originalCalls=0;var draws=new List<(float X,float Y,string Text,float Scale,float R)>();var commands=new List<(string Name,IntPtr Callback,uint Flags)>();var messages=new List<string>();
        Hud original=(_,_)=>originalCalls++;Dimension width=_=>1920,height=_=>1080;
        Draw draw=(_,x,y,text,color,force,scale)=>draws.Add((x,y,Marshal.PtrToStringAnsi(text)!,scale,BitConverter.Int32BitsToSingle(Marshal.ReadInt32(color))));
        Add add=(_,name,callback,_,_,flags)=>commands.Add((Marshal.PtrToStringAnsi(name)!,callback,flags));
        Print print=(_,_,format,values)=>{check(Marshal.PtrToStringAnsi(format)=="%s","Console uses fixed format string");messages.Add(Marshal.PtrToStringAnsi(Marshal.ReadIntPtr(values))!);};
        long Ptr(Delegate d)=>Marshal.GetFunctionPointerForDelegate(d).ToInt64();
        try{
            byte[] data=NativeSpeedometer.CreateState();BitConverter.GetBytes(2).CopyTo(data,0);BitConverter.GetBytes(2).CopyTo(data,LowHealthTintCode.LeaseOffset);
            var meters=new[]{new SpeedometerReading(true,160,860,12.5f,Scale:1,TextColor:Color.Red),new SpeedometerReading(true,720,860,34.5f,Scale:2,TextColor:Color.Lime),new SpeedometerReading(true,1280,860,-56.5f,Scale:3,TextColor:Color.Blue,ShowSign:true)};
            var records=NativeSpeedometer.CreateReadings(meters[0],meters[1],meters[2],10);for(int i=0;i<3;i++)records.AsSpan(i*64,64).CopyTo(data.AsSpan(NativeSpeedometerCode.ReadingOffsets[i],64));NativeSpeedometer.CreateColors(meters[0],meters[1],meters[2]).CopyTo(data,384);p.WriteBytes(state,data);
            p.WriteBytes(memory,NativeSpeedometerCode.Render(memory.ToInt64(),state.ToInt64(),Ptr(original),Ptr(draw),Ptr(width),Ptr(height),123,Ptr(add),Ptr(print)));
            for(int i=0;i<NativeSpeedometerCode.Commands.Length;i++)p.WriteBytes(memory+NativeSpeedometerCode.CallbackStart+i*NativeSpeedometerCode.CallbackStride,NativeSpeedometerCode.Command((state+NativeSpeedometerCode.MailboxOffset+i*NativeSpeedometerCode.MailboxStride).ToInt64(),i==6?4:2));
            p.VirtualProtect(memory,8192,MemPageProtect.PAGE_EXECUTE_READ);var render=Marshal.GetDelegateForFunctionPointer<Hud>(memory);render((IntPtr)1,(IntPtr)2);
            check(originalCalls==1&&draws.Count==3,"Executed renderer draws three meters and preserves HUD");check(draws.Select(x=>x.Text).SequenceEqual(new[]{"12.50","34.50","-56.50"}),"Renderer receives numeric values only");check(draws.Select(x=>x.Scale).SequenceEqual(new[]{.5f,1f,1.5f}),"Executed draw ABI preserves three independent scales");check(draws[0].X==160&&draws[1].X==720&&draws[2].X==1280,"Three independent positions reach renderer");check(draws[0].R==1&&draws[1].R==0&&draws[2].R==0,"Three color records remain separate");check(commands.Count==9&&commands.Select(x=>x.Name).SequenceEqual(NativeSpeedometerCode.Commands)&&commands.All(x=>x.Flags==2),"Nine lab commands registered without cheats");
            render((IntPtr)1,(IntPtr)2);check(commands.Count==9,"Commands register once");render((IntPtr)1,(IntPtr)2);check(draws.Count==6&&originalCalls==3,"Expired heartbeat stops drawing and preserves HUD");
            commands.Clear();Marshal.WriteInt32(state,4,3);render((IntPtr)1,(IntPtr)2);check(commands.Count==1&&commands[0].Name=="lab_speedometer_verticalsign"&&Marshal.ReadInt32(state,4)==9,"Legacy HUD migration registers only the new sign command");render((IntPtr)1,(IntPtr)2);check(commands.Count==1,"Migrated commands register once");
            // Restore registration for testing each native callback.
            commands.Clear();Marshal.WriteInt32(state,4,0);render((IntPtr)1,(IntPtr)2);
            for(int index=0;index<NativeSpeedometerCode.Commands.Length;index++){
                var strings=(index==6?new[]{"255","128","0","vertical"}:new[]{"1.7","vertical"}).Select(Marshal.StringToHGlobalAnsi).ToArray();try{Marshal.WriteInt32(args,strings.Length+1);for(int n=0;n<strings.Length;n++)Marshal.WriteIntPtr(args,16+n*8,strings[n]);var callback=Marshal.GetDelegateForFunctionPointer<Command>(commands[index].Callback);callback(args);var mailbox=p.ReadBytes(state+NativeSpeedometerCode.MailboxOffset+index*NativeSpeedometerCode.MailboxStride,160);check(BitConverter.ToInt32(mailbox)==1&&Enumerable.Range(0,strings.Length).All(n=>Encoding.ASCII.GetString(mailbox,16+n*32,Array.IndexOf(mailbox,(byte)0,16+n*32,32)-(16+n*32))==Marshal.PtrToStringAnsi(strings[n])),"Command callback arguments stay in own mailbox");Marshal.WriteInt32(args,1);callback(args);check(p.ReadValue<byte>(state+NativeSpeedometerCode.MailboxOffset+index*NativeSpeedometerCode.MailboxStride+16)==0,"Query clears previous values");}finally{foreach(var s in strings)Marshal.FreeHGlobal(s);}}
            p.WriteBytes(state+NativeSpeedometerCode.ConsoleTextOffset,Encoding.ASCII.GetBytes("Help: literal %n and 100%\n\0"));Marshal.WriteInt32(state,NativeSpeedometerCode.ConsoleReadyOffset,1);render((IntPtr)1,(IntPtr)2);render((IntPtr)1,(IntPtr)2);check(messages.SequenceEqual(new[]{"Help: literal %n and 100%\n"}),"Hidden-meter console help prints once safely");
        }finally{p.FreeMemory(memory);Marshal.FreeHGlobal(args);GC.KeepAlive(original);GC.KeepAlive(draw);GC.KeepAlive(width);GC.KeepAlive(height);GC.KeepAlive(add);GC.KeepAlive(print);}
    }
}
