using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;

namespace Lab.Updates;

internal sealed record ReleaseUpdate(string DisplayVersion, Version Version, Uri DownloadUrl, long Size, string? Digest);

internal sealed class ReleaseUpdates(HttpClient client)
{
    internal const string LatestUrl = "https://api.github.com/repos/IlyaTsyganov/LAB/releases/latest";
    internal static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = Timeout.InfiniteTimeSpan, MaxResponseContentBufferSize = 2 * 1024 * 1024 };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("LAB", AppVersion.Number));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return client;
    }

    internal async Task<ReleaseUpdate?> CheckAsync(string currentVersion, UpdatePreferences preferences, CancellationToken token = default)
    {
        // Do not fall back to a cached release: an offline start must never prompt.
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(3));
        try
        {
            using var response = await client.GetAsync(LatestUrl, timeout.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) return null;
            string json = await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false);
            return Parse(json, currentVersion, preferences);
        }
        catch (Exception error) when (error is HttpRequestException or OperationCanceledException or JsonException or InvalidOperationException)
        { return null; }
    }

    internal static ReleaseUpdate? Parse(string json, string currentVersion, UpdatePreferences preferences)
    {
        using var document = JsonDocument.Parse(json);
        var release = document.RootElement;
        if (release.ValueKind != JsonValueKind.Object || !AppVersion.TryParse(currentVersion, out var current)
            || !release.TryGetProperty("draft", out var draft) || draft.ValueKind != JsonValueKind.False
            || !release.TryGetProperty("prerelease", out var prerelease) || prerelease.ValueKind != JsonValueKind.False
            || !release.TryGetProperty("tag_name", out var tag) || tag.ValueKind != JsonValueKind.String
            || !AppVersion.TryParse(tag.GetString(), out var latest) || latest <= current || preferences.IsSkipped(latest)
            || !release.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array) return null;
        string number = tag.GetString()!.Trim().TrimStart('v', 'V');
        foreach (var asset in assets.EnumerateArray())
        {
            if (asset.ValueKind != JsonValueKind.Object || !asset.TryGetProperty("name", out var name) || name.ValueKind != JsonValueKind.String) continue;
            string? file = name.GetString();
            if (!string.Equals(file, "LAB.zip", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(file, $"lab-{number}-win-x64.zip", StringComparison.OrdinalIgnoreCase)) continue;
            if (!asset.TryGetProperty("browser_download_url", out var download) || download.ValueKind != JsonValueKind.String
                || !Uri.TryCreate(download.GetString(), UriKind.Absolute, out var url) || url.Scheme != "https"
                || !url.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase) || !url.IsDefaultPort || url.UserInfo.Length != 0
                || !url.AbsolutePath.StartsWith("/IlyaTsyganov/LAB/releases/download/", StringComparison.Ordinal)
                || !asset.TryGetProperty("size", out var size) || !size.TryGetInt64(out long bytes) || bytes is <= 0 or > UpdatePackage.MaxArchiveBytes) continue;
            string? digest = asset.TryGetProperty("digest", out var hash) && hash.ValueKind == JsonValueKind.String ? hash.GetString() : null;
            return new ReleaseUpdate("v" + number, latest, url, bytes, digest);
        }
        return null;
    }
}

internal sealed class UpdatePreferences
{
    private readonly string _path;
    private readonly HashSet<string> _skipped = new(StringComparer.Ordinal);
    internal UpdatePreferences(string directory)
    {
        _path = Path.Combine(directory, "update-preferences.json");
        try
        {
            if (File.Exists(_path))
                foreach (string value in JsonSerializer.Deserialize<string[]>(File.ReadAllText(_path)) ?? [])
                    if (AppVersion.TryParse(value, out var version)) _skipped.Add(version.ToString());
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException) { }
    }
    internal bool IsSkipped(Version version) => _skipped.Contains(version.ToString());
    internal void Skip(Version version)
    {
        _skipped.Add(version.ToString());
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        string temporary = _path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(_skipped));
        File.Move(temporary, _path, overwrite: true);
    }
}
