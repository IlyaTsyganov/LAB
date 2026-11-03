using System.IO.Compression;
using System.Security.Cryptography;

namespace Lab.Updates;

internal static class UpdatePackage
{
    internal const long MaxArchiveBytes = 256L * 1024 * 1024;
    private const long MaxExpandedBytes = 768L * 1024 * 1024;
    internal static readonly string[] RequiredFiles = [
        "LAB.exe", "LAB.dll", "LAB.deps.json", "LAB.runtimeconfig.json",
        "Microsoft.Web.WebView2.Core.dll", "Microsoft.Web.WebView2.WinForms.dll",
        "runtimes/win-x64/native/WebView2Loader.dll", "LICENSE",
        "Native/Lighting/lab-lighting.dll", "Assets/ui/index.html", "Assets/ui/update.html",
        "Assets/fonts/EternalUi2Regular.ttf"
    ];

    internal static bool IsRuntimeFile(string relative)
    {
        string path = relative.Replace('\\', '/');
        return RequiredFiles.Contains(path, StringComparer.OrdinalIgnoreCase)
            || path.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("licenses/", StringComparison.OrdinalIgnoreCase);
    }

    internal static string SafePath(string root, string relative)
    {
        if (relative.Contains(':') || relative.Split('/', '\\').Any(p => p is "." or ".." || p.EndsWith(' ') || p.EndsWith('.')))
            throw new InvalidDataException("Invalid path in update package.");
        string prefix = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        string path = Path.GetFullPath(Path.Combine(prefix, relative.Replace('/', Path.DirectorySeparatorChar)));
        if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Update path is outside the installation.");
        // Refuse junctions/symlinks in an existing installation, including parent directories.
        for (string? ancestor = Path.GetDirectoryName(path); ancestor is not null; ancestor = Path.GetDirectoryName(ancestor))
            if (Directory.Exists(ancestor) && (File.GetAttributes(ancestor) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("An update directory is a symbolic link or junction.");
        if (File.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new IOException("An update file is a symbolic link.");
        return path;
    }

    internal static void Validate(string directory)
    {
        foreach (string file in RequiredFiles)
            if (!File.Exists(SafePath(directory, file))) throw new InvalidDataException("The update package is missing " + file + ".");
    }

    internal static void Extract(string zipPath, string destination)
    {
        using var zip = ZipFile.OpenRead(zipPath);
        if (zip.Entries.Count > 4096) throw new InvalidDataException("The update package contains too many files.");
        string prefix = zip.Entries.Any(e => e.FullName.Equals("LAB.exe", StringComparison.OrdinalIgnoreCase)) ? ""
            : zip.Entries.FirstOrDefault(e => e.FullName.Equals("LAB/LAB.exe", StringComparison.OrdinalIgnoreCase)) is not null ? "LAB/"
            : throw new InvalidDataException("The update package does not contain LAB.exe.");
        long expanded = 0;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in zip.Entries)
        {
            if (entry.FullName.EndsWith('/')) continue;
            // Validate every path, even files that are not application payload.
            SafePath(destination, entry.FullName);
            if (!entry.FullName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Unexpected update package layout.");
            string relative = entry.FullName[prefix.Length..];
            if (!IsRuntimeFile(relative)) throw new InvalidDataException("Unexpected file in update package: " + relative);
            if (!seen.Add(relative.Replace('\\', '/'))) throw new InvalidDataException("Duplicate update file.");
            expanded = checked(expanded + entry.Length);
            if (expanded > MaxExpandedBytes) throw new InvalidDataException("The expanded update package is too large.");
            string target = SafePath(destination, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            entry.ExtractToFile(target);
        }
        Validate(destination);
    }

    internal static async Task DownloadAsync(System.Net.Http.HttpClient client, ReleaseUpdate release, string destination, CancellationToken token)
    {
        using var response = await client.GetAsync(release.DownloadUrl, System.Net.Http.HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength is long size && size != release.Size) throw new InvalidDataException("The update download size is incorrect.");
        await using var source = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
        await using var output = File.Create(destination);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        byte[] buffer = new byte[81920]; long total = 0;
        int count;
        while ((count = await source.ReadAsync(buffer, token).ConfigureAwait(false)) != 0)
        {
            total += count;
            if (total > release.Size || total > MaxArchiveBytes) throw new InvalidDataException("The update download is too large.");
            hash.AppendData(buffer, 0, count);
            await output.WriteAsync(buffer.AsMemory(0, count), token).ConfigureAwait(false);
        }
        if (total != release.Size) throw new InvalidDataException("The update download is incomplete.");
        if (release.Digest is { Length: > 0 } digest && (!digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase)
            || !Convert.ToHexString(hash.GetHashAndReset()).Equals(digest[7..], StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException("The update checksum is incorrect.");
    }

    internal static void Install(string payload, string target, string backup, Action<string, string>? copy = null)
    {
        Validate(payload);
        copy ??= (source, destination) => File.Copy(source, destination, overwrite: true);
        var replaced = new List<(string Target, string? Backup)>();
        try
        {
            // Back up all files before touching the current installation.
            var files = Directory.GetFiles(payload, "*", SearchOption.AllDirectories).Select(file => Path.GetRelativePath(payload, file)).ToArray();
            foreach (string relative in files)
            {
                if (!IsRuntimeFile(relative)) throw new InvalidDataException("Unexpected update file.");
                string destination = SafePath(target, relative);
                string saved = SafePath(backup, relative);
                if (File.Exists(destination)) { Directory.CreateDirectory(Path.GetDirectoryName(saved)!); File.Copy(destination, saved); }
            }
            foreach (string relative in files)
            {
                string destination = SafePath(target, relative), saved = SafePath(backup, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                replaced.Add((destination, File.Exists(saved) ? saved : null));
                copy(SafePath(payload, relative), destination);
            }
        }
        catch (Exception installError)
        {
            var rollbackErrors = new List<Exception>();
            foreach (var file in replaced.AsEnumerable().Reverse())
                try { if (file.Backup is null) File.Delete(file.Target); else File.Copy(file.Backup, file.Target, overwrite: true); }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException) { rollbackErrors.Add(error); }
            if (rollbackErrors.Count > 0) throw new AggregateException("Update failed. Recovery copies are in " + backup, new[] { installError }.Concat(rollbackErrors));
            throw;
        }
    }
}
