namespace HnIptv.Translation;

public sealed record ScreenTranslationRegion(double X, double Y, double Width, double Height,
    string Text, string Background = "#101010", string Foreground = "#FFFFFF");
public sealed record TrackedScreenRegion(long Id, ScreenTranslationRegion Region);
public readonly record struct OverlayRectangle(double X, double Y, double Width, double Height);

public static class ScreenOverlayLayout
{
    public static OverlayRectangle Viewport(double width, double height, double aspect)
    {
        if (!double.IsFinite(width) || !double.IsFinite(height) || width <= 0 || height <= 0) return default;
        if (!double.IsFinite(aspect) || aspect <= 0) aspect = width / height;
        double contentWidth = Math.Min(width, height * aspect), contentHeight = contentWidth / aspect;
        return new((width - contentWidth) / 2, (height - contentHeight) / 2, contentWidth, contentHeight);
    }
    public static OverlayRectangle Map(ScreenTranslationRegion region, OverlayRectangle viewport)
    {
        if (!Valid(region)) return default;
        double x = Math.Clamp(region.X, 0, 1), y = Math.Clamp(region.Y, 0, 1);
        return new(viewport.X + x * viewport.Width, viewport.Y + y * viewport.Height,
            (Math.Min(1, region.X + region.Width) - x) * viewport.Width, (Math.Min(1, region.Y + region.Height) - y) * viewport.Height);
    }
    public static bool Valid(ScreenTranslationRegion r) =>
        double.IsFinite(r.X) && double.IsFinite(r.Y) && double.IsFinite(r.Width) && double.IsFinite(r.Height) &&
        r.Width > 0 && r.Height > 0 && r.X < 1 && r.Y < 1 && r.X + r.Width > 0 && r.Y + r.Height > 0 &&
        !string.IsNullOrWhiteSpace(r.Text) && r.Text.Length <= 2000;
}

public sealed class ScreenRegionTracker
{
    private sealed class Entry(long id, ScreenTranslationRegion region)
    {
        public long Id = id;
        public ScreenTranslationRegion Region = region;
        public int Misses;
    }
    private readonly List<Entry> _entries = [];
    private long _nextId;
    public void Clear() => _entries.Clear();
    public IReadOnlyList<TrackedScreenRegion> Update(IReadOnlyList<ScreenTranslationRegion> observed)
    {
        var remaining = _entries.ToList();
        var updated = new List<Entry>();
        foreach (var input in observed.Where(ScreenOverlayLayout.Valid).Take(32))
        {
            double x = Math.Clamp(input.X, 0, 1), y = Math.Clamp(input.Y, 0, 1);
            var r = input with { X = x, Y = y, Width = Math.Min(1, input.X+input.Width)-x, Height = Math.Min(1, input.Y+input.Height)-y };
            var best = remaining.Select(old => (old, overlap: Overlap(old.Region, r)))
                .OrderByDescending(pair => pair.overlap).FirstOrDefault();
            Entry entry;
            if (best.old != null && best.overlap > 0.25)
            {
                entry = best.old; remaining.Remove(entry);
                if (entry.Region.Text == r.Text)
                    r = r with { X = entry.Region.X * 0.65 + r.X * 0.35,
                        Y = entry.Region.Y * 0.65 + r.Y * 0.35,
                        Width = entry.Region.Width * 0.65 + r.Width * 0.35,
                        Height = entry.Region.Height * 0.65 + r.Height * 0.35 };
                entry.Region = r; entry.Misses = 0;
            }
            else entry = new(++_nextId, r);
            updated.Add(entry);
        }
        foreach (var old in remaining)
            if (++old.Misses < 2) updated.Add(old);
        _entries.Clear(); _entries.AddRange(updated.Take(32));
        return _entries.Select(e => new TrackedScreenRegion(e.Id, e.Region)).ToArray();
    }
    private static double Overlap(ScreenTranslationRegion a, ScreenTranslationRegion b)
    {
        double width = Math.Max(0, Math.Min(a.X+a.Width, b.X+b.Width) - Math.Max(a.X,b.X));
        double height = Math.Max(0, Math.Min(a.Y+a.Height, b.Y+b.Height) - Math.Max(a.Y,b.Y));
        double intersection = width * height;
        return intersection / Math.Max(0.000001, a.Width*a.Height+b.Width*b.Height-intersection);
    }
}
