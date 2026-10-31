using System.Diagnostics;
using System.Runtime.InteropServices;
using Lab.Memory;
using Lab.Overlay;
namespace Lab.Runtime;
internal sealed class GameSession:IDisposable
{
    private readonly Process _game;private readonly GameReader _reader;private readonly SpeedSampler _sampler;private readonly DriverLineControl _driver;private readonly NativeSpeedometer _hud;
    private NativeModbotInputGuard? _modbot;private bool _modbotAttempted;private string _modbotStatus="Waiting";
    internal string Version {get;}
    internal int Pid=>_game.Id;
    internal bool Exited=>_game.HasExited;
    internal GameSession(Process game)
    {
        _game=game;var p=GameProfile.Resolve(game.MainModule!.FileName);Version=p.Name;_reader=new(game,p);_hud=NativeSpeedometer.Attach(game);_sampler=new(game);_driver=new(game,_reader.DriverInlineBuffer);
    }
    internal (bool Changed,string Status) Tick(SettingsStore store)
    {
        bool changed=false;foreach(var command in _hud.ReadCommands()){if(command.Arguments.Length==0)_hud.QueueConsoleMessage(ConsoleControls.Help(store.Snapshot(),command.Command));else{bool ok=store.Update(s=>ConsoleControls.Apply(s,command.Command,command.Arguments));changed|=ok;if(!ok)_hud.QueueConsoleMessage("Invalid value. "+ConsoleControls.Help(store.Snapshot(),command.Command));}}
        var settings=store.Snapshot();bool active=_reader.Active;IntPtr player=_reader.Player;bool enabled=settings.SpeedometersEnabled&&(settings.Current.Enabled||settings.Landing.Enabled||settings.Vertical.Enabled);
        _sampler.SetContext(_reader.Velocity,_hud.GroundPlaneFromVelocity,enabled&&active);var speed=_sampler.Snapshot;
        GetWindowThreadProcessId(GetForegroundWindow(),out uint focused);bool show=active&&focused==_game.Id;
        SpeedometerReading Reading(MeterSettings m,float? value)=>new(show&&settings.SpeedometersEnabled&&m.Enabled,m.X,m.Y,value,true,m.Size,ColorTranslator.FromHtml(m.Color));
        _hud.Update(Reading(settings.Current,speed.Horizontal),Reading(settings.Landing,speed.Landing),Reading(settings.Vertical,speed.Vertical),settings.TintOpacity,player);
        if(settings.ModbotFix&&!_modbotAttempted){_modbotAttempted=true;try{_modbot=NativeModbotInputGuard.Attach(_game,_hud.State,_hud.Layout);_modbotStatus="Ready";}catch(Exception e){_modbotStatus="Unavailable";Log.Warning(e,"Modbot input protection unavailable");}}
        _modbot?.Update(settings.ModbotFix&&_reader.MaxHz is >0 and <1000);
        bool driver=_driver.Update(settings.HideDriverName);
        return(changed,$"Connected · {Version} · PID {_game.Id} · HUD {(_hud.CommandsRegistered?"ready":"starting")} · Modbot {_modbotStatus}"+(settings.TintOpacity<100&&!_hud.TintAttached?" · Tint unavailable":""));
    }
    public void Dispose(){try{_modbot?.Dispose();}finally{try{_sampler.Dispose();}finally{try{_driver.Dispose();}finally{try{_hud.Dispose();}finally{_game.Dispose();}}}}}
    [DllImport("user32.dll")]private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")]private static extern uint GetWindowThreadProcessId(IntPtr hwnd,out uint pid);
}
internal sealed class GameMonitor:IDisposable
{
    private readonly SettingsStore _store;private readonly CancellationTokenSource _stop=new();private readonly Task _worker;private readonly bool _preview;private int _disposed;
    internal event Action<bool,string>? Updated;
    internal string Status {get;private set;}="Game not running";
    internal bool Connected {get;private set;}
    internal GameMonitor(SettingsStore store,bool preview){_store=store;_preview=preview;_worker=Task.Run(Run);}
    private async Task Run()
    {
        GameSession? session=null;string lastPath="";long retryAt=0;string lastStatus="";
        try{while(!_stop.IsCancellationRequested){bool changed=false;string path=_store.Snapshot().GamePath;
            try{
                if(session is not null&&(session.Exited||!path.Equals(lastPath,StringComparison.OrdinalIgnoreCase))){session.Dispose();session=null;retryAt=0;}
                if(_preview){Status="Preview mode · no game connection";}
                else if(session is null&&Environment.TickCount64>=retryAt){retryAt=Environment.TickCount64+3000;lastPath=path;Status=string.IsNullOrEmpty(path)?"Select the game executable":"Game not running";if(File.Exists(path))foreach(var p in Process.GetProcessesByName("DOOMEternalx64vk")){try{if(p.MainModule?.FileName.Equals(path,StringComparison.OrdinalIgnoreCase)==true){session=new GameSession(p);break;}p.Dispose();}catch{p.Dispose();throw;}}}
                if(session is not null){var tick=session.Tick(_store);Status=tick.Status;changed=tick.Changed;}Connected=session is not null;
            }catch(Exception e){Log.Warning(e,"Game connection");session?.Dispose();session=null;Connected=false;Status=e is NotSupportedException?e.Message:e is NativeGameNotReadyException?"Waiting for game initialization":$"Connection unavailable: {e.Message}";retryAt=Environment.TickCount64+5000;}
            if(changed||Status!=lastStatus){lastStatus=Status;Updated?.Invoke(changed,Status);}await Task.Delay(50,_stop.Token);
        }}catch(OperationCanceledException){}finally{session?.Dispose();}
    }
    public void Dispose(){if(Interlocked.Exchange(ref _disposed,1)!=0)return;_stop.Cancel();_worker.GetAwaiter().GetResult();_stop.Dispose();}
}
