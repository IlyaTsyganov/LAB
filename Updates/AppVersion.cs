using System.Reflection;

namespace Lab.Updates;

internal static class AppVersion
{
    internal static string Number { get; } = typeof(AppVersion).Assembly
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion.Split('+')[0];
    internal static string Display => "v" + Number;

    internal static bool TryParse(string? text, out Version version)
    {
        version = new Version(0, 0, 0, 0);
        if (string.IsNullOrWhiteSpace(text)) return false;
        string value = text.Trim().TrimStart('v', 'V');
        string[] parts = value.Split('.');
        if (parts.Length is < 2 or > 4 || parts.Any(p => p.Length == 0 || !p.All(char.IsAsciiDigit))) return false;
        if (!Version.TryParse(value, out var parsed)) return false;
        version = new Version(parsed.Major, parsed.Minor, Math.Max(0, parsed.Build), Math.Max(0, parsed.Revision));
        return true;
    }
}
