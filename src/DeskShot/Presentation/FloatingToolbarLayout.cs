namespace DeskShot.Presentation;

public sealed record FloatingToolbarPositions(Point Tools, Point Actions);

public static class FloatingToolbarLayout
{
    public static FloatingToolbarPositions Place(Rect selection, Rect viewport, Size tools, Size actions)
    {
        const double gap = 10, margin = 8;
        double ClampX(double value, double width) => Math.Clamp(value, viewport.Left + margin, Math.Max(viewport.Left + margin, viewport.Right - width - margin));
        double ClampY(double value, double height) => Math.Clamp(value, viewport.Top + margin, Math.Max(viewport.Top + margin, viewport.Bottom - height - margin));
        double tx = selection.Right + gap;
        if (tx + tools.Width > viewport.Right - margin) tx = selection.Left - tools.Width - gap;
        if (tx < viewport.Left + margin) tx = selection.Right - tools.Width - margin;
        var toolPosition = new Point(ClampX(tx, tools.Width), ClampY(selection.Top, tools.Height));
        double ay = selection.Bottom + gap;
        if (ay + actions.Height > viewport.Bottom - margin) ay = selection.Top - actions.Height - gap;
        if (ay < viewport.Top + margin) ay = viewport.Bottom - actions.Height - margin;
        var actionPosition = new Point(ClampX(selection.Right - actions.Width, actions.Width), ClampY(ay, actions.Height));
        if (new Rect(toolPosition, tools).IntersectsWith(new Rect(actionPosition, actions)))
        {
            var candidates = new[]
            {
                new Point(toolPosition.X - actions.Width - gap, actionPosition.Y),
                new Point(toolPosition.X + tools.Width + gap, actionPosition.Y),
                new Point(actionPosition.X, toolPosition.Y - actions.Height - gap),
                new Point(actionPosition.X, toolPosition.Y + tools.Height + gap)
            };
            var available = new Rect(viewport.Left + margin, viewport.Top + margin,
                Math.Max(0, viewport.Width - margin * 2), Math.Max(0, viewport.Height - margin * 2));
            foreach (var candidate in candidates)
            {
                var rect = new Rect(candidate, actions);
                if (available.Contains(rect) && !rect.IntersectsWith(new Rect(toolPosition, tools)))
                { actionPosition = candidate; break; }
            }
        }
        return new FloatingToolbarPositions(toolPosition, actionPosition);
    }
}
