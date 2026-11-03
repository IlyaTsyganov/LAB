using Lab.Runtime;

namespace Lab.Updates;

internal sealed class StartupContext : ApplicationContext
{
    private LabHost? _main;
    internal bool StartupError { get; private set; }
    internal StartupContext(bool preview, string[] args)
    {
        var dispatcher = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();
        SynchronizationContext.SetSynchronizationContext(dispatcher);
        dispatcher.Post(async _ =>
        {
            try
            {
                if (!preview && await StartupUpdates.RunAsync()) { ExitThread(); return; }
                _main = new LabHost(new SettingsStore(), preview, args);
                _main.FormClosed += (_, _) => { StartupError = _main.StartupError; ExitThread(); };
                _main.Show();
            }
            catch (Exception error) { Log.Error(error, "LAB startup failed"); StartupError = true; ExitThread(); }
        }, null);
    }
    protected override void Dispose(bool disposing) { if (disposing) _main?.Dispose(); base.Dispose(disposing); }
}

internal static class StartupUpdates
{
    internal static async Task<bool> RunAsync()
    {
        string data = Environment.GetEnvironmentVariable("LAB_DATA_DIRECTORY")
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "lab");
        var preferences = new UpdatePreferences(data);
        using var client = ReleaseUpdates.CreateClient();
        var release = await new ReleaseUpdates(client).CheckAsync(AppVersion.Number, preferences);
        if (release is null) return false;
        using var question = new UpdateDialog(release.DisplayVersion);
        if (question.ShowDialog() == DialogResult.Ignore)
        {
            try { preferences.Skip(release.Version); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { Log.Warning(error, "Update preference could not be saved"); }
        }
        if (question.DialogResult != DialogResult.Yes) return false;
        using var download = new UpdateDialog(release.DisplayVersion, client, release);
        download.ShowDialog();
        return download.HandedOff;
    }
}
