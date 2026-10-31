using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using Lab.Runtime;
namespace Lab;
internal sealed class LabHost:Form
{
    private readonly WebView2 _browser=new(){Dock=DockStyle.Fill,DefaultBackgroundColor=Color.FromArgb(17,27,34),AllowExternalDrop=false};
    private readonly SettingsStore _store;private readonly GameMonitor _monitor;private readonly bool _preview;private readonly string[] _args;private readonly TaskCompletionSource _ready=new(TaskCreationOptions.RunContinuationsAsynchronously);private bool _closing;
    internal bool StartupError{get;private set;}
    internal LabHost(SettingsStore store,bool preview,string[] args)
    {
        _store=store;_preview=preview;_args=args;FormBorderStyle=FormBorderStyle.None;BackColor=Color.FromArgb(17,27,34);Text="LAB";Opacity=0;StartPosition=FormStartPosition.CenterScreen;
        Rectangle area=Screen.PrimaryScreen!.WorkingArea;ClientSize=new(Math.Min(1024,area.Width-40),Math.Min(1000,area.Height-40));MinimumSize=new(680,620);Icon=new(Path.Combine(AppContext.BaseDirectory,"Assets/lab.ico"));Controls.Add(_browser);_monitor=new(store,preview);_monitor.Updated+=(changed,status)=>{if(!IsDisposed&&!_closing&&IsHandleCreated)BeginInvoke(()=>Publish(changed?"Console setting saved":""));};
    }
    protected override async void OnShown(EventArgs e)
    {
        base.OnShown(e);Log.Information("Interface initialization started");
        try{
            var options=new CoreWebView2EnvironmentOptions{Language="en-US"};if(_preview&&int.TryParse(Environment.GetEnvironmentVariable("LAB_WEBVIEW_DEBUG_PORT"),out int port)&&port is >=1024 and <=65535)options.AdditionalBrowserArguments="--remote-debugging-port="+port;
            var environment=await CoreWebView2Environment.CreateAsync(null,Path.Combine(_store.DirectoryPath,"webview"),options);await _browser.EnsureCoreWebView2Async(environment);if(_closing)return;var core=_browser.CoreWebView2;
            core.SetVirtualHostNameToFolderMapping("app.lab.local",Path.Combine(AppContext.BaseDirectory,"Assets"),CoreWebView2HostResourceAccessKind.DenyCors);core.Settings.AreDefaultContextMenusEnabled=false;core.Settings.AreDevToolsEnabled=_preview;core.Settings.IsStatusBarEnabled=false;core.Settings.IsZoomControlEnabled=false;core.Settings.IsGeneralAutofillEnabled=false;core.Settings.IsPasswordAutosaveEnabled=false;core.Settings.IsNonClientRegionSupportEnabled=true;
            core.PermissionRequested+=(_,a)=>a.State=CoreWebView2PermissionState.Deny;core.NewWindowRequested+=(_,a)=>a.Handled=true;core.NavigationStarting+=(_,a)=>{if(!a.Uri.StartsWith("https://app.lab.local/",StringComparison.Ordinal))a.Cancel=true;};core.WebMessageReceived+=Receive;
            core.NavigationCompleted+=async(_,a)=>{if(!a.IsSuccess)_ready.TrySetException(new IOException("LAB interface navigation failed: "+a.WebErrorStatus));else Log.Information("Page diagnostics",await core.ExecuteScriptAsync("JSON.stringify({errors:window.labErrors||[],fonts:document.fonts.status,ready:typeof window.labUiSnapshot,body:document.body.innerText.slice(0,120)})"));};
            await core.AddScriptToExecuteOnDocumentCreatedAsync("window.labErrors=[];window.addEventListener('error',e=>labErrors.push(e.message+' @ '+e.filename+':'+e.lineno));window.addEventListener('unhandledrejection',e=>labErrors.push(String(e.reason)));");
            core.Navigate("https://app.lab.local/ui/index.html");await _ready.Task.WaitAsync(TimeSpan.FromSeconds(25));if(_closing)return;
            Publish();using var frame=new MemoryStream();await core.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png,frame);if(_closing)return;Opacity=1;Activate();Log.Information("LAB first frame ready; only WebView2 native child: {Count}",Controls.Count);
            int qa=Array.IndexOf(_args,"--qa-output");if(qa>=0&&qa+1<_args.Length)await RunUiChecks(_args[qa+1]);
        }catch(Exception ex){if(_closing)return;StartupError=true;Log.Error(ex,"Interface initialization failed");Close();}
    }
    private void Publish(string feedback="")
    {if(_browser.CoreWebView2 is null||!_ready.Task.IsCompletedSuccessfully)return;_browser.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(new{kind="config",settings=_store.Snapshot(),connected=_monitor.Connected,status=_monitor.Status,feedback},SettingsStore.Json));}
    private void Notice(string title,string text)=>_browser.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(new{kind="notice",title,text},SettingsStore.Json));
    private void Receive(object? sender,CoreWebView2WebMessageReceivedEventArgs e)
    {
        if(!e.Source.StartsWith("https://app.lab.local/",StringComparison.Ordinal))return;
        try{using var json=JsonDocument.Parse(e.WebMessageAsJson);var d=json.RootElement;switch(d.GetProperty("kind").GetString()){
            case "ready":_ready.TrySetResult();break;
            case "setting":if(!_store.Apply(d.GetProperty("key").GetString()??"",d.GetProperty("value")))throw new ArgumentException("The setting value is invalid.");GameLauncher.WriteLightingControl(_store);Publish("Preference saved");break;
            case "browse":BeginInvoke(()=>{using var picker=new OpenFileDialog{Title="Select DOOM Eternal",Filter="DOOM Eternal|DOOMEternalx64vk.exe",CheckFileExists=true,FileName="DOOMEternalx64vk.exe"};if(picker.ShowDialog(this)==DialogResult.OK){_store.Update(s=>{s.GamePath=picker.FileName;return true;});Publish("Game executable selected");}});break;
            case "launch":BeginInvoke(()=>{try{if(_preview){Notice("Preview mode","Game launch is disabled in preview mode.");return;}if(_monitor.Connected){Notice("Game running","The selected game is already running.");return;}GameLauncher.Start(_store);Publish("Game launch requested");}catch(Exception ex){Notice("Could not launch game",ex.Message);}});break;
            case "window":string? action=d.GetProperty("action").GetString();BeginInvoke(()=>{switch(action){case "minimize":WindowState=FormWindowState.Minimized;break;case "maximize":WindowState=WindowState==FormWindowState.Maximized?FormWindowState.Normal:FormWindowState.Maximized;break;case "close":Close();break;}});break;
        }}catch(Exception ex){Log.Warning(ex,"Interface action failed");Notice("LAB",ex.Message);}
    }
    private async Task RunUiChecks(string directory)
    {
        Directory.CreateDirectory(directory);await Task.Delay(150);var core=_browser.CoreWebView2;
        string report=await core.ExecuteScriptAsync("JSON.stringify({snapshot:labUiSnapshot(),fonts:[...new Set([...document.querySelectorAll('.lab-app *')].filter(e=>e.getClientRects().length).map(e=>getComputedStyle(e).fontFamily))],previews:document.querySelectorAll('.lab-preview,.lab-tint-preview').length,tagline:document.querySelector('.lab-heading .lab-small').textContent,overflow:document.documentElement.scrollWidth>document.documentElement.clientWidth,bodyOverflow:document.body.scrollWidth>innerWidth})");
        using(var decoded=JsonDocument.Parse(report))File.WriteAllText(Path.Combine(directory,"ui-report.json"),decoded.RootElement.GetString());using(var file=File.Create(Path.Combine(directory,"lab.png")))await core.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png,file);
        await core.ExecuteScriptAsync("for(const [key,value] of [['current','1.2'],['landing','2.3'],['vertical','3.4']]){const input=document.querySelector('[data-inline-size='+key+']');input.value=value;input.dispatchEvent(new Event('input',{bubbles:true}));}");await Task.Delay(500);
        var saved=new SettingsStore(_store.DirectoryPath).Snapshot();if(saved.Current.Size!=1.2||saved.Landing.Size!=2.3||saved.Vertical.Size!=3.4)throw new Exception("UI independent sizes did not persist.");
        using(var file=File.Create(Path.Combine(directory,"sizes.png")))await core.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png,file);
        await core.ExecuteScriptAsync("document.querySelector('[data-meter-color=vertical]').click();document.querySelector('[data-hex]').value='#123456';document.querySelector('[data-color-apply]').click();");await Task.Delay(200);if(_store.Snapshot().Vertical.Color!="#123456")throw new Exception("UI color did not persist.");
        await core.ExecuteScriptAsync("document.querySelector('[data-commands]').click()");await Task.Delay(80);using(var file=File.Create(Path.Combine(directory,"commands.png")))await core.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png,file);await core.ExecuteScriptAsync("document.querySelector('[data-close-commands]').click()");
        await core.ExecuteScriptAsync("document.querySelector('[data-window=maximize]').click()");await Task.Delay(150);bool maximized=WindowState==FormWindowState.Maximized;await core.ExecuteScriptAsync("document.querySelector('[data-window=maximize]').click()");await Task.Delay(150);bool restored=WindowState==FormWindowState.Normal;if(!maximized||!restored)throw new Exception("Window maximize/restore failed.");
        var originalSize=ClientSize;ClientSize=new(680,720);await Task.Delay(100);using(var file=File.Create(Path.Combine(directory,"compact.png")))await core.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png,file);ClientSize=originalSize;
        File.WriteAllText(Path.Combine(directory,"host-report.json"),JsonSerializer.Serialize(new{firstFrameReady=_ready.Task.IsCompletedSuccessfully,nativeChildren=Controls.Count,nativeChildType=Controls[0].GetType().Name,independentSizesPersisted=true,colorPersisted=true,maximized,restored},SettingsStore.Json));Close();
    }
    protected override void OnFormClosing(FormClosingEventArgs e){_closing=true;_ready.TrySetCanceled();_monitor.Dispose();base.OnFormClosing(e);}
    protected override void WndProc(ref Message m)
    {base.WndProc(ref m);if(m.Msg==0x84&&WindowState==FormWindowState.Normal){Point p=PointToClient(new Point(unchecked((short)(long)m.LParam),unchecked((short)((long)m.LParam>>16))));int edge=6;bool l=p.X<edge,r=p.X>=Width-edge,t=p.Y<edge,b=p.Y>=Height-edge;m.Result=(IntPtr)(t&&l?13:t&&r?14:b&&l?16:b&&r?17:l?10:r?11:t?12:b?15:1);}}
}
