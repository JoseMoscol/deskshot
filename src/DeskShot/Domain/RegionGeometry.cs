namespace DeskShot.Domain;

public enum RegionHandle { TopLeft, Top, TopRight, Right, BottomRight, Bottom, BottomLeft, Left, Move, New }

public static class RegionGeometry
{
    public static IReadOnlyList<(RegionHandle Handle, Point Point)> Handles(Int32Rect region)
    {
        double x = region.X, y = region.Y, right = x + region.Width, bottom = y + region.Height;
        double cx = (x + right) / 2, cy = (y + bottom) / 2;
        return [(RegionHandle.TopLeft, new(x, y)), (RegionHandle.Top, new(cx, y)),
            (RegionHandle.TopRight, new(right, y)), (RegionHandle.Right, new(right, cy)),
            (RegionHandle.BottomRight, new(right, bottom)), (RegionHandle.Bottom, new(cx, bottom)),
            (RegionHandle.BottomLeft, new(x, bottom)), (RegionHandle.Left, new(x, cy))];
    }

    public static RegionHandle? HitHandle(Int32Rect region, Point point, double tolerance)
    {
        var handle = Handles(region).Where(h => Math.Abs(h.Point.X - point.X) <= tolerance && Math.Abs(h.Point.Y - point.Y) <= tolerance)
            .OrderBy(h => (h.Point - point).LengthSquared).Select(h => (RegionHandle?)h.Handle).FirstOrDefault();
        if (handle is not null) return handle;
        double right = region.X + region.Width, bottom = region.Y + region.Height;
        if (point.X >= region.X && point.X <= right)
        {
            if (Math.Abs(point.Y - region.Y) <= tolerance) return RegionHandle.Top;
            if (Math.Abs(point.Y - bottom) <= tolerance) return RegionHandle.Bottom;
        }
        if (point.Y >= region.Y && point.Y <= bottom)
        {
            if (Math.Abs(point.X - region.X) <= tolerance) return RegionHandle.Left;
            if (Math.Abs(point.X - right) <= tolerance) return RegionHandle.Right;
        }
        return null;
    }

    public static Int32Rect Adjust(Int32Rect original, RegionHandle handle, Point start, Point current, int width, int height)
    {
        if (handle == RegionHandle.New) return SelectionMath.FromPoints(start, current, width, height);
        if (handle == RegionHandle.Move)
        {
            int x = (int)Math.Clamp(Math.Round(original.X + current.X - start.X), 0, width - original.Width);
            int y = (int)Math.Clamp(Math.Round(original.Y + current.Y - start.Y), 0, height - original.Height);
            return new Int32Rect(x, y, original.Width, original.Height);
        }
        int left = original.X, top = original.Y, right = left + original.Width, bottom = top + original.Height;
        int px = (int)Math.Clamp(Math.Round(current.X), 0, width), py = (int)Math.Clamp(Math.Round(current.Y), 0, height);
        if (handle is RegionHandle.TopLeft or RegionHandle.Left or RegionHandle.BottomLeft) left = Math.Clamp(px, 0, right - 2);
        if (handle is RegionHandle.TopRight or RegionHandle.Right or RegionHandle.BottomRight) right = Math.Clamp(px, left + 2, width);
        if (handle is RegionHandle.TopLeft or RegionHandle.Top or RegionHandle.TopRight) top = Math.Clamp(py, 0, bottom - 2);
        if (handle is RegionHandle.BottomLeft or RegionHandle.Bottom or RegionHandle.BottomRight) bottom = Math.Clamp(py, top + 2, height);
        return new Int32Rect(left, top, right - left, bottom - top);
    }
}
