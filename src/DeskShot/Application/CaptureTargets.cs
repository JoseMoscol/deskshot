using DeskShot.Domain;

namespace DeskShot.Application;

public sealed record CaptureTarget(Int32Rect Region, string Label, int WindowId);

public interface ICaptureTargetDetector
{
    CaptureTarget? WindowAt(Point imagePoint);
    Task<IReadOnlyList<CaptureTarget>> QueryAsync(Point imagePoint, CancellationToken token);
}

public static class CaptureTargetGeometry
{
    public static bool Contains(Int32Rect rect, Point point) => point.X >= rect.X && point.Y >= rect.Y && point.X < (long)rect.X + rect.Width && point.Y < (long)rect.Y + rect.Height;

    public static Int32Rect? Clip(PixelBounds physical, PixelBounds desktop)
    {
        long left = Math.Max(physical.X, desktop.X), top = Math.Max(physical.Y, desktop.Y);
        long right = Math.Min((long)physical.X + physical.Width, (long)desktop.X + desktop.Width);
        long bottom = Math.Min((long)physical.Y + physical.Height, (long)desktop.Y + desktop.Height);
        if (right - left < 2 || bottom - top < 2) return null;
        return new((int)(left - desktop.X), (int)(top - desktop.Y), (int)(right - left), (int)(bottom - top));
    }

    public static IReadOnlyList<CaptureTarget> Order(IEnumerable<CaptureTarget> targets, Point point) => targets
        .Where(t => t.Region.Width >= 2 && t.Region.Height >= 2 && Contains(t.Region, point))
        .GroupBy(t => t.Region).Select(g => g.First())
        .OrderBy(t => (long)t.Region.Width * t.Region.Height).ToArray();

    public static bool IsClick(Point start, Point end, double pixelsPerDip) => (end - start).Length <= 5 * pixelsPerDip;
}
