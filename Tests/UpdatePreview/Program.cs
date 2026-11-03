using Lab.Updates;
using System.Text.Json;

internal static class Program
{
    [STAThread]
    private static int Main()
    {
        string output = Path.Combine(AppContext.BaseDirectory, "preview-output"); Directory.CreateDirectory(output);
        Environment.SetEnvironmentVariable("LAB_PREVIEW", "1");
        Environment.SetEnvironmentVariable("LAB_DATA_DIRECTORY", Path.Combine(output, "isolated-data"));
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
        try
        {
            foreach (var (choice, expected) in new[] { ("no", DialogResult.No), ("skip", DialogResult.Ignore), ("yes", DialogResult.Yes) })
            {
                using var prompt = new UpdateDialog("v1.2") { QaOutput = Path.Combine(output, choice), QaChoice = choice };
                Application.Run(prompt);
                if (prompt.StartupError is not null) throw prompt.StartupError;
                if (prompt.DialogResult != expected) throw new Exception("Wrong update result for " + choice);
            }
            InstallLifecycle(output);
            File.WriteAllText(Path.Combine(output, "result.txt"), "PASS");
            return 0;
        }
        catch (Exception error) { File.WriteAllText(Path.Combine(output, "error.txt"), error.ToString()); File.WriteAllText(Path.Combine(output, "result.txt"), "FAIL"); return 1; }
    }

    private static void InstallLifecycle(string output)
    {
        string fixture = Path.Combine(output, "lifecycle-" + Guid.NewGuid().ToString("N"));
        string target = Path.Combine(fixture, "installation"), payload = Path.Combine(fixture, "release");
        var files = Directory.GetFiles(AppContext.BaseDirectory, "*", SearchOption.AllDirectories)
            .Select(file => (File: file, Relative: Path.GetRelativePath(AppContext.BaseDirectory, file)))
            .Where(file => UpdatePackage.IsRuntimeFile(file.Relative)).ToArray();
        foreach (var file in files)
            foreach (string directory in new[] { target, payload })
            {
                string destination = UpdatePackage.SafePath(directory, file.Relative);
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!); File.Copy(file.File, destination);
            }
        File.WriteAllText(Path.Combine(target, "Assets", "update-proof.txt"), "old");
        File.WriteAllText(Path.Combine(payload, "Assets", "update-proof.txt"), "new");
        File.WriteAllText(Path.Combine(target, "settings.json"), "user settings");
        Directory.CreateDirectory(Path.Combine(target, "logs")); File.WriteAllText(Path.Combine(target, "logs", "history.log"), "user history");
        string zip = Path.Combine(fixture, "release.zip"); System.IO.Compression.ZipFile.CreateFromDirectory(payload, zip);
        byte[] bytes = File.ReadAllBytes(zip);
        using var client = new System.Net.Http.HttpClient(new PackageHandler(bytes));
        AppVersion.TryParse(AppVersion.Number, out var version);
        var release = new ReleaseUpdate(AppVersion.Display, version, new Uri("https://github.com/IlyaTsyganov/LAB/releases/download/test/LAB.zip"), bytes.Length,
            "sha256:" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)));
        string job = UpdateInstaller.PrepareAsync(client, release, target, default).GetAwaiter().GetResult();
        File.WriteAllText(Path.Combine(output, "integration.json"), JsonSerializer.Serialize(new { target, job }));
        Environment.SetEnvironmentVariable("LAB_UPDATE_QA_OUTPUT", Path.Combine(output, "restarted-main"));
        UpdateInstaller.StartHelper(job);
    }

    private sealed class PackageHandler(byte[] package) : System.Net.Http.HttpMessageHandler
    {
        protected override Task<System.Net.Http.HttpResponseMessage> SendAsync(System.Net.Http.HttpRequestMessage request, CancellationToken token)
            => Task.FromResult(new System.Net.Http.HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new System.Net.Http.ByteArrayContent(package) });
    }
}
