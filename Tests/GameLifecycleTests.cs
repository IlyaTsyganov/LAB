using System.Diagnostics;
using Lab.Runtime;

internal static class GameLifecycleTests
{
    private static void WaitFor(Func<bool> condition,string name)
    {
        var until=Stopwatch.StartNew();
        while(!condition()){
            if(until.Elapsed>TimeSpan.FromSeconds(8))throw new Exception(name);
            Thread.Sleep(20);
        }
    }
    internal static void Run(Action<bool,string> check)
    {
        check(GameStatus.Format(false,"5.1",true,false)=="Game not running"&&
            GameStatus.Format(true,"5.1",false,false)=="Running 5.1"&&
            GameStatus.Format(true,"5.1",true,false)=="Restart game"&&
            GameStatus.Format(true,"5.1",true,true)=="Running 5.1",
            "Only the three capitalized game statuses are exposed");
        var identity=new GameLaunchStamp(123,456,@"C:\Games\DOOMEternalx64vk.exe");
        check(identity.Matches(123,456,@"c:\games\DOOMEternalx64vk.exe")&&
            !identity.Matches(123,457,@"C:\Games\DOOMEternalx64vk.exe"),
            "A reused PID cannot inherit LAB launch ownership");
        string data=Path.Combine(Environment.GetEnvironmentVariable("LAB_TEST_DATA_ROOT")??Path.GetTempPath(),"lifecycle-"+Guid.NewGuid().ToString("N"));
        var store=new SettingsStore(data);
        using(var pending=new GameMonitor(store,true)){
            check(pending.TryBeginLaunch()&&pending.LaunchPending&&!pending.TryBeginLaunch(),"Launch pending is immediate and refuses a second click");
            pending.CancelLaunch();
            check(!pending.LaunchPending&&pending.TryBeginLaunch(),"A failed launch can be retried");
            pending.CancelLaunch();
        }
        string fixtures=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../Tests/output"));
        string folder=Path.Combine(fixtures,"monitor-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        Process? child=null;
        try{
            foreach(string file in Directory.GetFiles(AppContext.BaseDirectory))
                File.Copy(file,Path.Combine(folder,Path.GetFileName(file)));
            string fake=Path.Combine(folder,"DOOMEternalx64vk.exe");
            File.Copy(Path.Combine(AppContext.BaseDirectory,"Lab.Tests.exe"),fake);
            Process Start()=>Process.Start(new ProcessStartInfo(fake,"--monitor-child"){UseShellExecute=false,CreateNoWindow=true,WorkingDirectory=folder})!;
            store.Update(s=>{s.GamePath=fake;return true;});
            child=Start();
            using(var monitor=new GameMonitor(store,false,DateTime.UtcNow.AddSeconds(1))){
                WaitFor(()=>monitor.Connected&&monitor.RequiresRestart,"Preexisting game was not detected");
                check(monitor.Status=="Restart game"&&!monitor.CanApplyFeatures,"An external preexisting game cannot apply LAB features");
                check(File.ReadAllText(Path.Combine(data,"lighting.ini")).Contains("Enabled=0"),"Restart mode keeps lighting control disabled");
                check(!monitor.TryBeginLaunch(),"A detected game disables launch before native adapters initialize");
                child.Kill();child.WaitForExit();
                WaitFor(()=>monitor.Status=="Game not running"&&!monitor.Connected,"Closing game did not reset the UI status");
                check(!monitor.RequiresRestart&&monitor.Features is null,"Game shutdown clears restart and feature state");
            }
            child.Dispose();child=Start();
            GameLaunchHistory.Record(child,fake,store);
            using(var monitor=new GameMonitor(new SettingsStore(data),false,DateTime.UtcNow.AddSeconds(1))){
                WaitFor(()=>monitor.Connected,"Recorded LAB launch was not detected");
                check(monitor.Status=="Running unknown"&&!monitor.RequiresRestart&&monitor.CanApplyFeatures,
                    "LAB launch identity survives a fresh settings store and LAB restart");
                child.Kill();child.WaitForExit();
                WaitFor(()=>monitor.Status=="Game not running","Recorded game shutdown did not clear status");
            }
        }finally{
            if(child is not null){if(!child.HasExited){child.Kill();child.WaitForExit();}child.Dispose();}
            foreach(string file in Directory.GetFiles(folder))File.Delete(file);
            Directory.Delete(folder);
        }
    }
}
