using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace Lab.Updates;

internal sealed class UpdateDialog : Form
{
    private readonly WebView2 _browser = new() { Dock = DockStyle.Fill, DefaultBackgroundColor = Color.FromArgb(17, 27, 34), AllowExternalDrop = false };
    private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly CancellationTokenSource _cancel = new(TimeSpan.FromMinutes(5));
    private readonly System.Net.Http.HttpClient? _client;
    private readonly ReleaseUpdate? _release;
    private readonly string _version;
    private bool _closing;
    internal bool HandedOff { get; private set; }
    internal string? QaOutput;
    internal string QaChoice = "no";
    internal Exception? StartupError { get; private set; }

    internal UpdateDialog(string version, System.Net.Http.HttpClient? client = null, ReleaseUpdate? release = null)
    {
        _version = version; _client = client; _release = release;
        Text = release is null ? "Update available" : "Updating LAB";
        ClientSize = new Size(600, 300); MinimumSize = new Size(600, 300);
        FormBorderStyle = FormBorderStyle.None; StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.FromArgb(17, 27, 34); Opacity = 0;
        Icon = new Icon(Path.Combine(AppContext.BaseDirectory, "Assets", "lab.ico"));
        Controls.Add(_browser);
    }

    protected override async void OnShown(EventArgs e)
    {
        base.OnShown(e);
        try
        {
            string data = Environment.GetEnvironmentVariable("LAB_DATA_DIRECTORY")
                ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "lab");
            var environment = await CoreWebView2Environment.CreateAsync(null, Path.Combine(data, "webview"), new CoreWebView2EnvironmentOptions { Language = "en-US" });
            await _browser.EnsureCoreWebView2Async(environment);
            if (_closing) return;
            var core = _browser.CoreWebView2;
            core.SetVirtualHostNameToFolderMapping("app.lab.local", Path.Combine(AppContext.BaseDirectory, "Assets"), CoreWebView2HostResourceAccessKind.DenyCors);
            core.Settings.AreDefaultContextMenusEnabled = false; core.Settings.AreDevToolsEnabled = false;
            core.Settings.IsStatusBarEnabled = false; core.Settings.IsZoomControlEnabled = false;
            core.Settings.IsGeneralAutofillEnabled = false; core.Settings.IsPasswordAutosaveEnabled = false; core.Settings.IsNonClientRegionSupportEnabled = true;
            core.PermissionRequested += (_, args) => args.State = CoreWebView2PermissionState.Deny;
            core.NewWindowRequested += (_, args) => args.Handled = true;
            core.NavigationStarting += (_, args) => { if (!args.Uri.StartsWith("https://app.lab.local/", StringComparison.Ordinal)) args.Cancel = true; };
            core.NavigationCompleted += (_, args) => { if (!args.IsSuccess) _ready.TrySetException(new IOException("Update interface navigation failed.")); };
            core.WebMessageReceived += (_, args) =>
            {
                if (!args.Source.StartsWith("https://app.lab.local/", StringComparison.Ordinal)) return;
                using var json = JsonDocument.Parse(args.WebMessageAsJson);
                string? action = json.RootElement.GetProperty("action").GetString();
                if (action == "ready") { _ready.TrySetResult(); return; }
                BeginInvoke(() =>
                {
                    if (action == "minimize") { WindowState = FormWindowState.Minimized; return; }
                    DialogResult = action switch { "yes" => DialogResult.Yes, "skip" => DialogResult.Ignore, _ => DialogResult.No };
                    Close();
                });
            };
            core.Navigate("https://app.lab.local/ui/update.html");
            await _ready.Task.WaitAsync(TimeSpan.FromSeconds(20));
            if (_closing) return;
            Publish(_release is null ? $"A new version of LAB is available: {_version}.\n\nWould you like to update?" : $"Downloading LAB {_version}…", _release is not null);
            await core.ExecuteScriptAsync("document.fonts.ready");
            using var frame = new MemoryStream(); await core.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, frame);
            if (_closing) return;
            Opacity = 1; Activate();
            if (QaOutput is not null) { await CheckUi(); return; }
            if (_release is not null) await Download();
        }
        catch (Exception error) { if (!_closing) { StartupError = error; Runtime.Log.Warning(error, "Update interface failed"); Close(); } }
    }

    private void Publish(string message, bool downloading, bool failed = false)
        => _browser.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(new { appVersion = AppVersion.Display, message, downloading, failed }));

    private async Task Download()
    {
        try
        {
            string job = await UpdateInstaller.PrepareAsync(_client!, _release!, AppContext.BaseDirectory, _cancel.Token);
            _cancel.Token.ThrowIfCancellationRequested();
            Publish("Installing the update. LAB will restart automatically…", true);
            UpdateInstaller.StartHelper(job); HandedOff = true; DialogResult = DialogResult.OK; Close();
        }
        catch (Exception error) when (error is OperationCanceledException or IOException or InvalidDataException or UnauthorizedAccessException
            or System.Net.Http.HttpRequestException or System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            if (!_closing) Publish("The update could not be installed. You can continue using this version.\n\n" + error.Message, false, true);
        }
    }

    private async Task CheckUi()
    {
        await Task.Delay(100);
        var core = _browser.CoreWebView2;
        string report = await core.ExecuteScriptAsync("JSON.stringify({version:document.querySelector('[data-app-version]').textContent,font:getComputedStyle(document.querySelector('[data-app-version]')).fontFamily,size:getComputedStyle(document.querySelector('[data-app-version]')).fontSize,weight:getComputedStyle(document.querySelector('[data-app-version]')).fontWeight,choices:[...document.querySelectorAll('[data-choice]')].filter(b=>!b.hidden).map(b=>b.textContent),text:document.body.innerText,overflow:document.documentElement.scrollWidth>innerWidth||document.querySelector('main').scrollHeight>document.querySelector('main').clientHeight})");
        using var parsed = JsonDocument.Parse(JsonSerializer.Deserialize<string>(report)!);
        var state = parsed.RootElement;
        if (state.GetProperty("version").GetString() != AppVersion.Display || state.GetProperty("font").GetString() != "LABUI"
            || state.GetProperty("size").GetString() != "12px" || state.GetProperty("weight").GetString() != "400"
            || state.GetProperty("choices").GetArrayLength() != 3 || state.GetProperty("overflow").GetBoolean() || ClientSize != new Size(600, 300))
            throw new Exception("Update dialog layout or version is incorrect: " + report);
        Directory.CreateDirectory(QaOutput!);
        File.WriteAllText(Path.Combine(QaOutput!, "update-layout.json"), state.GetRawText());
        await using var image = File.Create(Path.Combine(QaOutput!, "update.png"));
        await core.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, image);
        await core.ExecuteScriptAsync("document.querySelector('[data-choice=" + QaChoice + "]').click()");
    }

    protected override void OnFormClosing(FormClosingEventArgs e) { _closing = true; if (!HandedOff) _cancel.Cancel(); base.OnFormClosing(e); }
}
