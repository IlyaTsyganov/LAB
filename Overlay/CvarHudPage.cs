using Lab.Memory;

namespace Lab.Overlay;

internal static class CvarHudPage
{
    internal const long PageDuration = 6000;

    internal static int Capacity(int viewportHeight, float glyphHeight, float scale, bool desru)
    {
        float line = glyphHeight * scale;
        if(!float.IsFinite(line) || line <= 0) line = 24;
        // Reserve the upper quarter and the existing metrics rows. Cap the
        // coordinate space at the game's standard virtual 1080-unit HUD.
        return Math.Clamp((int)(Math.Clamp(viewportHeight, 240, 1080) * .75f / line) - (desru ? 12 : 3), 3, 512);
    }

    internal static string[] Lines(IReadOnlyList<CvarDifference> differences, int viewportHeight, float glyphHeight, float scale, bool desru, long now)
    {
        int capacity = Capacity(viewportHeight, glyphHeight, scale, desru);
        int pageSize = differences.Count <= capacity ? capacity : capacity - 1;
        int pages = Math.Max(1, (differences.Count + pageSize - 1) / pageSize);
        int page = (int)(Math.Max(0, now) / PageDuration % pages);
        var lines = new List<string>();
        if(pages > 1) lines.Add($"Cheat cvars: page {page + 1}/{pages} ({differences.Count} changed)");
        for(int i = page * pageSize; i < Math.Min(differences.Count, (page + 1) * pageSize); i++) {
            var difference = differences[i];
            string value = new(difference.Value.Select(c => char.IsControl(c) ? ' ' : c).ToArray());
            lines.Add(difference.Name + " = " + value);
        }
        return lines.ToArray();
    }
}
