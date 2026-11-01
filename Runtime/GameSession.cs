using System.Diagnostics;
using System.Runtime.InteropServices;
using Lab.Memory;
using Lab.Overlay;
namespace Lab.Runtime;
internal sealed class GameSession:IDisposable
{
    private readonly Process _game;private readonly GameReader _reader;private readonly SpeedSampler _sampler;private readonly NativeSpeedometer _hud;private readonly PerformanceMetrics? _metrics;
    private NativeModbotInputGuard? _modbot;private readonly NativeAttachRetry _modbotRetry=new();private string? _modbotFailure;
    private readonly ProtectedCvarSampler? _cvars;
    private readonly PlayerCheatMonitor? _playerCheats;
    internal FeatureStatus? Features {get;private set;}
    internal string Version {get;}
    internal int Pid=>_game.Id;
    internal bool Exited=>_game.HasExited;
    internal GameSession(Process game,GameProfile? profile=null)
    {
        _game=game;var p=profile??GameProfile.Resolve(game.MainModule!.FileName);Version=p.Name;_reader=new(game,p);_hud=NativeSpeedometer.Attach(game);_sampler=new(game);
        try{if(p.Metrics==0)throw new NotSupportedException("Missing metrics profile");_metrics=PerformanceMetrics.Attach(game,_hud.Layout,p.Metrics);}catch(Exception e){Log.Warning(e,"Performance metrics unavailable");}
        if(_metrics is not null) {
            if(_hud.Layout.PlayerCheats is {} playerCheats) _playerCheats = new(game, playerCheats);
            else Log.Warning("Player cheat state unavailable", _hud.Layout.PlayerCheatsFailure ?? "Missing player cheat metadata");
            if(_hud.Layout.Cvars is {} cvars) {
                try {
                    var monitor = new ProtectedCvarMonitor(game, cvars);
                    _cvars = new(monitor, new CheatCvarSession(game, _hud.State, monitor));
                } catch(Exception e) { Log.Warning(e, "Cheat cvar session unavailable; other LAB features remain active"); }
            }
            else Log.Warning("Cheat cvar monitor unavailable", _hud.Layout.CvarsFailure ?? "Missing cvar metadata");
        }
    }
    internal bool Tick(SettingsStore store)
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
        // These commands toggle packed idPlayer bits and need no cvar argument.
        // Read actual state after filtering cvars, so even pre-existing player
        // cheats remain visible independently of the cvar session baseline.
        var cvars = _cvars?.Snapshot ?? Array.Empty<CvarDifference>();
        var playerCheats = _playerCheats?.Read(active ? player : IntPtr.Zero) ?? default;
        _metrics?.Update(settings,playerCheats.AddTo(cvars),_hud.GlyphHeight);
        Features=FeatureStatus.Describe(settings,Version,_reader.DriverName,GameLauncher.LightingActive(_game.Id),_modbot is not null,maxHz,_modbotFailure,_hud.TintAttached);
        return changed;
    }
    public void Dispose(){try{_cvars?.Dispose();}finally{try{_modbot?.Dispose();}finally{try{_sampler.Dispose();}finally{try{_metrics?.Dispose();}finally{_hud.Dispose();}}}}}
    [DllImport("user32.dll")]private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")]private static extern uint GetWindowThreadProcessId(IntPtr hwnd,out uint pid);
}
internal sealed class GameMonitor:IDisposable
{
    private readonly SettingsStore _store;
    private readonly Func<string,Process?> _findGame;
    private readonly CancellationTokenSource _stop=new();
    private readonly Task _worker;
    private readonly bool _preview;
    private readonly DateTime _startedUtc;
    private readonly object _launchGate=new();
    private bool _launchPending;
    private long _launchDeadline;
    private int _disposed;
    internal event Action<bool,string>? Updated;
    internal string Status {get;private set;}=GameStatus.NotRunning;
    internal bool Connected {get;private set;}
    internal bool RequiresRestart {get;private set;}
    internal bool CanApplyFeatures=>Connected&&!RequiresRestart;
    internal bool LaunchPending {get{lock(_launchGate)return _launchPending;}}
    internal FeatureStatus? Features {get;private set;}

    internal GameMonitor(SettingsStore store,bool preview,DateTime? startedUtc=null,Func<string,Process?>? findGame=null)
    {
        _store=store;_preview=preview;_findGame=findGame??FindGame;
        using var owner=Process.GetCurrentProcess();
        _startedUtc=startedUtc??owner.StartTime.ToUniversalTime();
        _worker=Task.Run(Run);
    }
    internal bool TryBeginLaunch()
    {
        lock(_launchGate){
            if(Connected||_launchPending)return false;
            _launchPending=true;_launchDeadline=Environment.TickCount64+60000;return true;
        }
    }
    internal void CancelLaunch(){lock(_launchGate)_launchPending=false;}
    private void ExpireLaunch(long now)
    {
        lock(_launchGate){
            if(_launchPending&&now>=_launchDeadline){
                _launchPending=false;Log.Warning("Game launch was not detected before the launch timeout");
            }
        }
    }
    private static bool Alive(Process game)
    {
        try{return !game.HasExited;}
        catch(Exception e)when(e is InvalidOperationException or System.ComponentModel.Win32Exception){return false;}
    }
    private static void CloseAdapter<T>(ref T? adapter)where T:class,IDisposable
    {
        var previous=adapter;adapter=null;
        try{previous?.Dispose();}
        catch(Exception e){Log.Warning(e,"Game adapter cleanup");}
    }
    private static Process? FindGame(string selectedPath)
    {
        if(string.IsNullOrEmpty(selectedPath))return null;
        foreach(var process in Process.GetProcessesByName("DOOMEternalx64vk")){
            try{
                if(Alive(process)&&process.MainModule?.FileName.Equals(selectedPath,StringComparison.OrdinalIgnoreCase)==true)
                    return process;
            }catch(Exception e)when(e is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException){
                Log.Warning(e,"Game process discovery");
            }
            process.Dispose();
        }
        return null;
    }
    private async Task Run()
    {
        Process? game=null;GameSession? session=null;PerformanceMetrics? notice=null;GameProfile? profile=null;
        string observedPath="",version="unknown",lastStatus="";
        bool launchedByLab=false,preexisting=false,lastConnected=false,lastPending=false;
        bool? lastGate=null;
        long detectAt=0,attachAt=0,identityAt=0;
        FeatureStatus? lastFeatures=null;
        try{
            while(!_stop.IsCancellationRequested){
                bool changed=false;
                try{
                    long now=Environment.TickCount64;string selectedPath=_store.Snapshot().GamePath;
                    ExpireLaunch(now);
                    if(game is not null&&(!Alive(game)||!selectedPath.Equals(observedPath,StringComparison.OrdinalIgnoreCase))){
                        CloseAdapter(ref session);CloseAdapter(ref notice);game.Dispose();game=null;profile=null;detectAt=0;attachAt=0;
                    }
                    if(!_preview&&game is null&&now>=detectAt){
                        detectAt=now+1000;game=_findGame(selectedPath);
                        if(game is not null){
                            observedPath=selectedPath;version="unknown";profile=null;
                            preexisting=game.StartTime.ToUniversalTime()<_startedUtc;
                            launchedByLab=GameLauncher.WasLaunchedByLab(game,_store);identityAt=now+1000;
                            try{profile=GameProfile.Resolve(observedPath);version=profile.Name;}
                            catch(Exception e){Log.Warning(e,"Game version detection");}
                        }
                    }
                    Connected=!_preview&&game is not null&&Alive(game);
                    if(Connected){
                        CancelLaunch();
                        if(!launchedByLab&&now>=identityAt){
                            identityAt=now+1000;launchedByLab=GameLauncher.WasLaunchedByLab(game!,_store);
                        }
                        RequiresRestart=!launchedByLab;
                        if(RequiresRestart){
                            CloseAdapter(ref session);
                            if(lastGate!=false){GameLauncher.WriteLightingControl(_store,false);lastGate=false;}
                            // An externally launched game is observed only; even
                            // the restart notice must not install an in-game hook.
                            CloseAdapter(ref notice);
                            Features=FeatureStatus.Restart;
                        }else{
                            CloseAdapter(ref notice);
                            if(session is null&&profile is not null&&now>=attachAt){
                                attachAt=now+3000;
                                try{session=new GameSession(game!,profile);}
                                catch(Exception e){Log.Warning(e,"Game adapter initialization");}
                            }
                            if(session is not null){
                                try{changed=session.Tick(_store);}
                                catch(Exception e){Log.Warning(e,"Game connection");CloseAdapter(ref session);attachAt=now+1000;}
                            }
                            Features=session?.Features??FeatureStatus.Pending(_store.Snapshot());
                        }
                        Connected=Alive(game!);
                        Status=GameStatus.Format(Connected,version,preexisting,launchedByLab);
                        if(!Connected){RequiresRestart=false;Features=null;}
                    }else{RequiresRestart=false;Features=null;Status=GameStatus.NotRunning;}
                    if(lastGate!=CanApplyFeatures){
                        GameLauncher.WriteLightingControl(_store,CanApplyFeatures);lastGate=CanApplyFeatures;
                    }
                }catch(Exception e){
                    Log.Warning(e,"Game monitoring");
                    CloseAdapter(ref session);CloseAdapter(ref notice);game?.Dispose();game=null;profile=null;
                    Connected=false;RequiresRestart=false;Features=null;Status=GameStatus.NotRunning;detectAt=Environment.TickCount64+1000;
                    try{GameLauncher.WriteLightingControl(_store,false);lastGate=false;}
                    catch(Exception controlError){Log.Warning(controlError,"Lighting control");}
                }
                bool pending=LaunchPending;
                if(changed||Status!=lastStatus||Connected!=lastConnected||pending!=lastPending||Features!=lastFeatures){
                    if(Status!=lastStatus)Log.Information("Game status",Status);
                    lastStatus=Status;lastConnected=Connected;lastPending=pending;lastFeatures=Features;Updated?.Invoke(changed,Status);
                }
                await Task.Delay(50,_stop.Token);
            }
        }catch(OperationCanceledException){}
        finally{CloseAdapter(ref session);CloseAdapter(ref notice);game?.Dispose();}
    }
    public void Dispose()
    {
        if(Interlocked.Exchange(ref _disposed,1)!=0)return;
        _stop.Cancel();_worker.GetAwaiter().GetResult();_stop.Dispose();
    }
}
