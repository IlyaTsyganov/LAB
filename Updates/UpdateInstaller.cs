using System.Diagnostics;
using System.Text.Json;

namespace Lab.Updates;

internal sealed record UpdateJob(string Target, int ProcessId, long ProcessStart, string Version);

internal static class UpdateInstaller
{
    internal static string JobsDirectory => Path.Combine(Path.GetTempPath(), "lab-updates");

    internal static async Task<string> PrepareAsync(System.Net.Http.HttpClient client, ReleaseUpdate release, string target, CancellationToken token)
    {
        string job = Path.Combine(JobsDirectory, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(job);
        string archive = Path.Combine(job, "release.zip"), payload = Path.Combine(job, "payload"), helper = Path.Combine(job, "helper");
        try
        {
            await UpdatePackage.DownloadAsync(client, release, archive, token).ConfigureAwait(false);
            await Task.Run(() =>
            {
                token.ThrowIfCancellationRequested();
                UpdatePackage.Extract(archive, payload);
                string? version = FileVersionInfo.GetVersionInfo(Path.Combine(payload, "LAB.dll")).ProductVersion?.Split('+')[0];
                if (!AppVersion.TryParse(version, out var actual) || actual != release.Version) throw new InvalidDataException("The downloaded application version does not match the release.");
                // Run a private copy of this updater so its own binaries are never locked.
                foreach (string file in Directory.GetFiles(target, "*", SearchOption.AllDirectories))
                {
                    token.ThrowIfCancellationRequested();
                    string relative = Path.GetRelativePath(target, file);
                    if (!UpdatePackage.IsRuntimeFile(relative)) continue;
                    string destination = UpdatePackage.SafePath(helper, relative);
                    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                    File.Copy(file, destination);
                }
                UpdatePackage.Validate(helper);
                File.Copy(Path.Combine(helper, "LAB.exe"), Path.Combine(helper, "LAB.Update.exe"));
                using var process = Process.GetCurrentProcess();
                var plan = new UpdateJob(Path.GetFullPath(target), process.Id, process.StartTime.ToUniversalTime().Ticks, release.DisplayVersion);
                File.WriteAllText(Path.Combine(job, "job.json"), JsonSerializer.Serialize(plan));
                token.ThrowIfCancellationRequested();
            }, token).ConfigureAwait(false);
            return job;
        }
        catch
        {
            // This directory was freshly created here and contains no user data.
            try { Directory.Delete(job, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            throw;
        }
    }

    internal static void StartHelper(string job)
    {
        var start = new ProcessStartInfo(Path.Combine(job, "helper", "LAB.Update.exe")) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = job };
        start.ArgumentList.Add("--apply-update"); start.ArgumentList.Add(job);
        using var process = Process.Start(start) ?? throw new IOException("The update installer could not start.");
    }

    internal static bool IsJobDirectory(string path) => Path.GetDirectoryName(Path.GetFullPath(path))?.Equals(Path.GetFullPath(JobsDirectory), StringComparison.OrdinalIgnoreCase) == true
        && Guid.TryParseExact(Path.GetFileName(path), "N", out _);

    internal static UpdateJob ReadJob(string job)
    {
        if (!IsJobDirectory(job)) throw new InvalidDataException("Invalid update directory.");
        var plan = JsonSerializer.Deserialize<UpdateJob>(File.ReadAllText(Path.Combine(job, "job.json"))) ?? throw new InvalidDataException("Invalid update plan.");
        if (!Path.IsPathFullyQualified(plan.Target) || !File.Exists(Path.Combine(plan.Target, "LAB.exe"))
            || !AppVersion.TryParse(plan.Version, out _) || plan.ProcessId <= 0) throw new InvalidDataException("Invalid update target.");
        return plan;
    }
}
