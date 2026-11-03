using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using Lab.Updates;

string root = Path.Combine(Path.GetTempPath(), "lab-update-tests-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
int checks = 0;
void Check(bool value, string message) { if (!value) throw new Exception(message); checks++; }
void Throws(Action action, string message) { try { action(); } catch (Exception error) when (error is IOException or InvalidDataException) { checks++; return; } throw new Exception(message); }
async Task RejectDownload(byte[] data, long size, string? digest)
{
    using var client = new HttpClient(new FakeHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(data) })));
    try { await UpdatePackage.DownloadAsync(client, new("v1.1", new Version(1, 1, 0, 0), new Uri("https://github.com/test.zip"), size, digest), Path.Combine(root, Guid.NewGuid() + ".zip"), default); }
    catch (InvalidDataException) { checks++; return; }
    throw new Exception("Invalid download accepted");
}
string Release(string tag = "v1.1", bool draft = false, bool prerelease = false, string url = "https://github.com/IlyaTsyganov/LAB/releases/download/v1.1/LAB.zip", string asset = "LAB.zip")
    => JsonSerializer.Serialize(new { tag_name = tag, draft, prerelease, assets = new[] { new { name = asset, browser_download_url = url, size = 100 } } });
void Payload(string dir, string marker)
{
    foreach (string relative in UpdatePackage.RequiredFiles)
    { string path = UpdatePackage.SafePath(dir, relative); Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllText(path, marker + relative); }
}
try
{
    var preferences = new UpdatePreferences(Path.Combine(root, "preferences"));
    Check(AppVersion.TryParse("v1.10", out var ten) && AppVersion.TryParse("1.9.0", out var nine) && ten > nine, "Versions compared as strings");
    Check(AppVersion.TryParse("v1.0", out var a) && AppVersion.TryParse("1.0.0", out var b) && a == b, "Equivalent tag formats differ");
    Check(!AppVersion.TryParse("1.1-rc1", out _) && !AppVersion.TryParse("garbage", out _), "Invalid version accepted");
    Check(ReleaseUpdates.Parse(Release(), "1.0.2", preferences)?.DisplayVersion == "v1.1", "New release not detected");
    Check(ReleaseUpdates.Parse(Release(tag: "v1.0.2"), "1.0.2", preferences) is null, "Current release prompts");
    Check(ReleaseUpdates.Parse(Release(tag: "v1.0"), "1.0.2", preferences) is null, "Downgrade prompts");
    Check(ReleaseUpdates.Parse(Release(draft: true), "1.0.2", preferences) is null, "Draft prompts");
    Check(ReleaseUpdates.Parse(Release(prerelease: true), "1.0.2", preferences) is null, "Prerelease prompts");
    Check(ReleaseUpdates.Parse(Release(url: "https://github.com/other/repo/file.zip"), "1.0.2", preferences) is null, "Foreign repository accepted");
    Check(ReleaseUpdates.Parse(Release(asset: "lab-1.1-win-x64.zip"), "1.0.2", preferences) is not null, "Published package naming unsupported");
    preferences.Skip(new Version(1, 1, 0, 0));
    preferences = new UpdatePreferences(Path.Combine(root, "preferences"));
    Check(ReleaseUpdates.Parse(Release(), "1.0.2", preferences) is null, "Skipped version not persisted");
    Check(ReleaseUpdates.Parse(Release(tag: "v1.2"), "1.0.2", preferences) is not null, "Skip blocked later versions");
    // An offline check must ignore a previously detected release completely.
    using (var client = new HttpClient(new FakeHandler((_, _) => throw new HttpRequestException("offline"))))
        Check(await new ReleaseUpdates(client).CheckAsync("1.0.2", preferences) is null, "Offline check prompts");
    using (var client = new HttpClient(new FakeHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden)))))
        Check(await new ReleaseUpdates(client).CheckAsync("1.0.2", preferences) is null, "API failure prompts");
    using (var client = new HttpClient(new FakeHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("broken JSON") }))))
        Check(await new ReleaseUpdates(client).CheckAsync("1.0.2", preferences) is null, "Invalid API response prompts");
    using (var client = new HttpClient(new FakeHandler(async (_, token) => { await Task.Delay(Timeout.Infinite, token); return new HttpResponseMessage(); })))
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        Check(await new ReleaseUpdates(client).CheckAsync("1.0.2", preferences) is null && watch.Elapsed < TimeSpan.FromSeconds(5), "Check has no bounded timeout");
    }

    string payload = Path.Combine(root, "payload"), target = Path.Combine(root, "installed");
    Payload(payload, "new:"); Payload(target, "old:");
    Directory.CreateDirectory(Path.Combine(payload, "Assets", "new"));
    File.WriteAllText(Path.Combine(payload, "Assets", "new", "file.txt"), "added");
    File.WriteAllText(Path.Combine(target, "settings.json"), "user settings");
    Directory.CreateDirectory(Path.Combine(target, "logs")); File.WriteAllText(Path.Combine(target, "logs", "history.log"), "user log");
    UpdatePackage.Install(payload, target, Path.Combine(root, "backup-success"));
    Check(File.ReadAllText(Path.Combine(target, "LAB.exe")) == "new:LAB.exe", "Update files not installed");
    Check(File.ReadAllText(Path.Combine(target, "settings.json")) == "user settings" && File.ReadAllText(Path.Combine(target, "logs", "history.log")) == "user log", "Update replaced user data");
    Payload(target, "old:"); File.Delete(Path.Combine(target, "Assets", "new", "file.txt"));
    int copied = 0;
    Throws(() => UpdatePackage.Install(payload, target, Path.Combine(root, "backup-failure"), (source, destination) =>
    { File.Copy(source, destination, true); if (++copied == 5) throw new IOException("simulated failed copy after writing"); }), "Copy failure was swallowed");
    Check(UpdatePackage.RequiredFiles.All(f => File.ReadAllText(Path.Combine(target, f)) == "old:" + f), "Rollback failed to restore original files");
    Check(!File.Exists(Path.Combine(target, "Assets", "new", "file.txt")), "Rollback left a new file");
    string zipPath = Path.Combine(root, "valid.zip"); ZipFile.CreateFromDirectory(payload, zipPath);
    UpdatePackage.Extract(zipPath, Path.Combine(root, "extracted")); checks++;
    foreach (string invalid in new[] { "../escape.txt", "Assets/../../escape.txt", "C:/escape.txt", "settings.json", "Assets/evil.txt:stream" })
    {
        string zip = Path.Combine(root, Guid.NewGuid() + ".zip");
        using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
        { archive.CreateEntry("LAB.exe"); archive.CreateEntry(invalid); }
        Throws(() => UpdatePackage.Extract(zip, Path.Combine(root, Guid.NewGuid().ToString("N"))), "Unsafe archive accepted: " + invalid);
    }
    string duplicate = Path.Combine(root, "duplicate.zip");
    using (var archive = ZipFile.Open(duplicate, ZipArchiveMode.Create)) { archive.CreateEntry("LAB.exe"); archive.CreateEntry("lab.EXE"); }
    Throws(() => UpdatePackage.Extract(duplicate, Path.Combine(root, "duplicate")), "Duplicate archive accepted");
    string incomplete = Path.Combine(root, "incomplete.zip");
    using (var archive = ZipFile.Open(incomplete, ZipArchiveMode.Create)) archive.CreateEntry("LAB.exe");
    Throws(() => UpdatePackage.Extract(incomplete, Path.Combine(root, "incomplete")), "Incomplete runtime accepted");
    await RejectDownload([1, 2, 3], 4, null);
    await RejectDownload([1, 2, 3], 3, "sha256:" + new string('0', 64));
    byte[] bytes = File.ReadAllBytes(zipPath);
    using (var client = new HttpClient(new FakeHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) }))))
    {
        await UpdatePackage.DownloadAsync(client, new("v1.1", new Version(1, 1, 0, 0), new Uri("https://github.com/test.zip"), bytes.Length, "sha256:" + Convert.ToHexString(SHA256.HashData(bytes))), Path.Combine(root, "download.zip"), default);
        checks++;
    }
    Check(!UpdateInstaller.IsJobDirectory(target) && UpdateInstaller.IsJobDirectory(Path.Combine(UpdateInstaller.JobsDirectory, Guid.NewGuid().ToString("N"))), "Update cleanup path guard failed");
    Console.WriteLine($"PASS: {checks} update checks (offline/API/timeout, version skips, downloads/checksums, package paths, install and rollback).");
}
finally { Directory.Delete(root, true); }

sealed class FakeHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => send(request, token);
}
