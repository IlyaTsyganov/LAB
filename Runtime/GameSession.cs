using System.Diagnostics;
using System.Runtime.InteropServices;
using Lab.Memory;
using Lab.Overlay;
namespace Lab.Runtime;
internal sealed class GameSession:IDisposable
{
    private readonly Process _game;private readonly GameReader _reader;private readonly SpeedSampler _sampler;private readonly NativeSpeedometer _hud;private readonly PerformanceMetrics? _metrics;
    private NativeModbotInputGuard? _modbot;private readonly NativeAttachRetry _modbotRetry=new();private string? _modbotFailure;
    internal FeatureStatus? Features {get;private set;}
    internal string Version {get;}
    internal int Pid=>_game.Id;
    internal bool Exited=>_game.HasExited;
    internal GameSession(Process game)
    {
        _game=game;var p=GameProfile.Resolve(game.MainModule!.FileName);Version=p.Name;_reader=new(game,p);_hud=NativeSpeedometer.Attach(game);_sampler=new(game);
        try{if(p.Metrics==0)throw new NotSupportedException("Missing metrics profile");_metrics=PerformanceMetrics.Attach(game,_hud.Layout,p.Metrics);}catch(Exception e){Log.Warning(e,"Performance metrics unavailable");}
    }
    internal (bool Changed,string Status) Tick(SettingsStore store)
    {
        bool changed=false;foreach(var command in _hud.ReadCommands()){if(command.Arguments.Length==0)_hud.QueueConsoleMessage(ConsoleControls.Help(store.Snapshot(),command.Command));else{bool ok=store.Update(s=>ConsoleControls.Apply(s,command.Command,command.Arguments));changed|=ok;if(!ok)_hud.QueueConsoleMessage("Invalid value. "+ConsoleControls.Help(store.Snapshot(),command.Command));}}
        var settings=store.Snapshot();bool active=_reader.Active;IntPtr player=_reader.Player;bool enabled=settings.SpeedometersEnabled&&(settings.Current.Enabled||settings.Landing.Enabled||settings.Vertical.Enabled);
        _sampler.SetContext(_reader.Velocity,_hud.GroundPlaneFromVelocity,enabled&&active);var speed=_sampler.Snapshot;
        GetWindowThreadProcessId(GetForegroundWindow(),out uint focused);bool show=active&&focused==_game.Id;
        SpeedometerReading Reading(MeterSettings m,float? value)=>new(show&&settings.SpeedometersEnabled&&m.Enabled,m.X,m.Y,value,true,m.Size,ColorTranslator.FromHtml(m.Color));
        _hud.Update(Reading(settings.Current,speed.Horizontal),Reading(settings.Landing,speed.Landing),Reading(settings.Vertical,SpeedSampler.VerticalSpeed(speed.Vertical,settings.VerticalShowSign)) with {ShowSign=settings.VerticalShowSign},settings.TintOpacity,player);
        if(settings.ModbotFix&&_modbot is null&&_modbotRetry.CanAttempt(Environment.TickCount64)){
            try{_modbot=NativeModbotInputGuard.Attach(_game,_hud.State,_hud.Layout);_modbotFailure=null;}
            catch(NotSupportedException e){_modbotRetry.RejectUnsupported();_modbotFailure=e.Message;Log.Warning(e,"Modbot input protection unavailable");}
            catch(Exception e){_modbotRetry.RetryLater(Environment.TickCount64);_modbotFailure=e.Message;Log.Warning(e,"Modbot input protection will retry");}
        }
        int maxHz=_reader.MaxHz;
        _modbot?.Update(settings.ModbotFix&&maxHz is >0 and <1000);
        // Letters report the selected preferences. Native availability and standby
        // conditions are reported separately in the UI instead of hiding letters.
        _metrics?.Update(settings);
        Features=FeatureStatus.Describe(settings,Version,_reader.DriverName,GameLauncher.LightingActive(_game.Id),_modbot is not null,maxHz,_modbotFailure,_hud.TintAttached);
        return(changed,$"Connected · {Version} · PID {_game.Id} · HUD {(_hud.CommandsRegistered?"ready":"starting")}");
    }
    public void Dispose(){try{_modbot?.Dispose();}finally{try{_sampler.Dispose();}finally{try{_metrics?.Dispose();}finally{try{_hud.Dispose();}finally{_game.Dispose();}}}}}
    [DllImport("user32.dll")]private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")]private static extern uint GetWindowThreadProcessId(IntPtr hwnd,out uint pid);
}
internal sealed class GameMonitor:IDisposable
{
    private readonly SettingsStore _store;private readonly CancellationTokenSource _stop=new();private readonly Task _worker;private readonly bool _preview;private int _disposed;
    internal event Action<bool,string>? Updated;
    internal string Status {get;private set;}="Game not running";
    internal bool Connected {get;private set;}
    internal FeatureStatus? Features {get;private set;}
    internal GameMonitor(SettingsStore store,bool preview){_store=store;_preview=preview;_worker=Task.Run(Run);}
    private async Task Run()
    {
        GameSession? session=null;string lastPath="";long retryAt=0;string lastStatus="";FeatureStatus? lastFeatures=null;
        try{while(!_stop.IsCancellationRequested){bool changed=false;string path=_store.Snapshot().GamePath;
            try{
                if(session is not null&&(session.Exited||!path.Equals(lastPath,StringComparison.OrdinalIgnoreCase))){session.Dispose();session=null;retryAt=0;}
                if(_preview){Status="Preview mode · no game connection";}
                else if(session is null&&Environment.TickCount64>=retryAt){retryAt=Environment.TickCount64+3000;lastPath=path;Status=string.IsNullOrEmpty(path)?"Select the game executable":"Game not running";if(File.Exists(path))foreach(var p in Process.GetProcessesByName("DOOMEternalx64vk")){try{if(p.MainModule?.FileName.Equals(path,StringComparison.OrdinalIgnoreCase)==true){session=new GameSession(p);break;}p.Dispose();}catch{p.Dispose();throw;}}}
                if(session is not null){var tick=session.Tick(_store);Status=tick.Status;changed=tick.Changed;}Connected=session is not null;Features=session?.Features;
            }catch(Exception e){Log.Warning(e,"Game connection");session?.Dispose();session=null;Connected=false;Features=null;Status=e is NotSupportedException?e.Message:e is NativeGameNotReadyException?"Waiting for game initialization":$"Connection unavailable: {e.Message}";retryAt=Environment.TickCount64+5000;}
            if(changed||Status!=lastStatus||Features!=lastFeatures){lastStatus=Status;lastFeatures=Features;Updated?.Invoke(changed,Status);}await Task.Delay(50,_stop.Token);
        }}catch(OperationCanceledException){}finally{session?.Dispose();}
    }
    public void Dispose(){if(Interlocked.Exchange(ref _disposed,1)!=0)return;_stop.Cancel();_worker.GetAwaiter().GetResult();_stop.Dispose();}
}
