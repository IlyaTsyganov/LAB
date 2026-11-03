using System.Diagnostics;
using Lab.Updates;

namespace Lab;

internal static class UpdateHelper
{
    internal static void Run(string job)
    {
        UpdateJob? plan = null;
        bool safeToRestart = false;
        try
        {
            plan = UpdateInstaller.ReadJob(job);
            if (!Path.GetFullPath(AppContext.BaseDirectory).TrimEnd(Path.DirectorySeparatorChar)
                .Equals(Path.Combine(job, "helper"), StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Invalid update helper location.");
            try
            {
                using var parent = Process.GetProcessById(plan.ProcessId);
                if (parent.StartTime.ToUniversalTime().Ticks == plan.ProcessStart && !parent.WaitForExit(30000))
                    throw new IOException("LAB Plus is still running. Close it and try updating again.");
            }
            catch (ArgumentException) { /* The original process has already exited. */ }
            safeToRestart = true;
            UpdatePackage.Install(Path.Combine(job, "payload"), plan.Target, Path.Combine(job, "backup"));
            var start = new ProcessStartInfo(Path.Combine(plan.Target, "LAB.exe")) { UseShellExecute = true, WorkingDirectory = plan.Target };
            start.ArgumentList.Add("--finish-update"); start.ArgumentList.Add(job);
            using var restarted = Process.Start(start) ?? throw new IOException("The updated application could not restart.");
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or InvalidOperationException
            or System.ComponentModel.Win32Exception or System.Text.Json.JsonException or AggregateException)
        {
            MessageBox.Show("The update could not be completed.\n\n" + error.Message, "LAB Plus update", MessageBoxButtons.OK, MessageBoxIcon.Error);
            if (safeToRestart && plan is not null && error is not AggregateException)
                try { using var restarted = Process.Start(new ProcessStartInfo(Path.Combine(plan.Target, "LAB.exe")) { UseShellExecute = true, WorkingDirectory = plan.Target }); }
                catch (Exception restartError) when (restartError is IOException or System.ComponentModel.Win32Exception) { }
        }
    }

    internal static void CleanCompleted(string job)
    {
        // Cleanup is limited to a completed job targeting this exact installation.
        try
        {
            var plan = UpdateInstaller.ReadJob(job);
            if (!Path.GetFullPath(plan.Target).TrimEnd(Path.DirectorySeparatorChar).Equals(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase)) return;
            _ = Task.Run(async () =>
            {
                for (int attempt = 0; attempt < 20; attempt++)
                {
                    await Task.Delay(500);
                    try { Directory.Delete(job, recursive: true); return; }
                    catch (IOException) { } catch (UnauthorizedAccessException) { return; }
                }
            });
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or System.Text.Json.JsonException) { }
    }
}
