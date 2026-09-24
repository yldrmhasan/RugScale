namespace RugScale.Core.Drawing;

/// <summary>
/// Pure pixel-grid rasterization for the shape tools (line, rectangle, ellipse outline).
/// Framework-independent so the App layer can use the exact same point list both for the
/// live drag preview and for the committed command.
/// </summary>
public static class Rasterizer
{
    /// <summary>Bresenham's line algorithm.</summary>
    public static IEnumerable<(int X, int Y)> Line(int x0, int y0, int x1, int y1)
    {
        var dx = Math.Abs(x1 - x0);
        var sx = x0 < x1 ? 1 : -1;
        var dy = -Math.Abs(y1 - y0);
        var sy = y0 < y1 ? 1 : -1;
        var error = dx + dy;

        var x = x0;
        var y = y0;

        while (true)
        {
            yield return (x, y);
            if (x == x1 && y == y1)
                yield break;

            var e2 = 2 * error;
            if (e2 >= dy)
            {
                error += dy;
                x += sx;
            }
            if (e2 <= dx)
            {
                error += dx;
                y += sy;
            }
        }
    }

    /// <summary>
    /// Post-processes an ORDERED point sequence (consecutive points must be actual neighbors,
    /// as Line's Bresenham output is) so that any diagonal-only (corner-adjacent) step between
    /// two consecutive points gets an extra pixel bridging them with a full shared edge instead.
    /// This is the "bindirmeli" (overlapping/connected) mode industry professionals asked for on
    /// the Line tool — the "bindirmesiz" (non-overlapping) mode is just the untouched Bresenham
    /// line, which already allows clean diagonal-only steps. Same underlying idea as
    /// EllipseOutlineConnected, just phrased as a generic reusable post-process instead of a
    /// dedicated scan, since Line's points already come out in walking order.
    /// </summary>
    public static IEnumerable<(int X, int Y)> ConnectDiagonalSteps(IEnumerable<(int X, int Y)> orderedPoints)
    {
        (int X, int Y)? previous = null;
        foreach (var point in orderedPoints)
        {
            if (previous is { } prev && Math.Abs(point.X - prev.X) == 1 && Math.Abs(point.Y - prev.Y) == 1)
                yield return (point.X, prev.Y); // bridge pixel — shares an edge with both neighbors

            yield return point;
            previous = point;
        }
    }

    /// <summary>The four border edges of the axis-aligned box between the two corners (no fill).</summary>
    public static IEnumerable<(int X, int Y)> RectangleOutline(int x0, int y0, int x1, int y1)
    {
        var left = Math.Min(x0, x1);
        var right = Math.Max(x0, x1);
        var top = Math.Min(y0, y1);
        var bottom = Math.Max(y0, y1);

        for (var x = left; x <= right; x++)
        {
            yield return (x, top);
            yield return (x, bottom);
        }
        for (var y = top; y <= bottom; y++)
        {
            yield return (left, y);
            yield return (right, y);
        }
    }

    /// <summary>Every cell of the axis-aligned box between the two corners.</summary>
    public static IEnumerable<(int X, int Y)> RectangleFilled(int x0, int y0, int x1, int y1)
    {
        var left = Math.Min(x0, x1);
        var right = Math.Max(x0, x1);
        var top = Math.Min(y0, y1);
        var bottom = Math.Max(y0, y1);

        for (var y = top; y <= bottom; y++)
            for (var x = left; x <= right; x++)
                yield return (x, y);
    }

    /// <summary>
    /// A rectangular brush (sizeX × sizeY) centered on (cx, cy) — used by the Pencil tool's
    /// adjustable, independently-wide/tall size. Not necessarily square: a design pixel isn't
    /// necessarily physically square either (warp/weft density can differ), so letting X and Y
    /// vary independently lets the user compensate the same way the shape tools' Shift-constrain
    /// does (see DesignCanvas.ApplyShapeModifiers).
    /// </summary>
    public static IEnumerable<(int X, int Y)> RectBrush(int cx, int cy, int sizeX, int sizeY)
    {
        if (sizeX <= 1 && sizeY <= 1)
        {
            yield return (cx, cy);
            yield break;
        }

        var beforeX = (sizeX - 1) / 2; // cells to the left of center
        var afterX = sizeX / 2;        // cells to the right of center
        var beforeY = (sizeY - 1) / 2; // cells above center
        var afterY = sizeY / 2;        // cells below center
        for (var y = cy - beforeY; y <= cy + afterY; y++)
            for (var x = cx - beforeX; x <= cx + afterX; x++)
                yield return (x, y);
    }

    /// <summary>Whether (x, y) falls inside (or on) the ellipse fit to the given bounding box.</summary>
    private static bool IsInsideEllipse(int x, int y, double cx, double cy, double rx, double ry)
    {
        var nx = (x - cx) / rx;
        var ny = (y - cy) / ry;
        return nx * nx + ny * ny <= 1.0;
    }

    /// <summary>
    /// Ellipse outline fit inside the axis-aligned box between the two corners, traced as "every
    /// inside cell that borders an outside cell" — i.e. the boundary of the filled region (see
    /// EllipseFilled/IsInsideEllipse), the same technique used for the Selection tool's
    /// mask outline (DesignCanvas.DrawSelectionOutline/DrawCaptureOutline). Two earlier versions
    /// of this method existed and both had uneven-thickness bugs: sampling by angle bunched
    /// points near the flat parts of the curve and gapped them near the steep parts; the
    /// textbook two-region midpoint algorithm fixed that but left an occasional 2px-thick corner
    /// right at the seam between its two regions. Boundary-of-region tracing has neither problem
    /// by construction — a cell is either on the boundary or it isn't, there's no seam to get
    /// wrong — at the cost of being O(width×height) instead of O(circumference), which is
    /// irrelevant at design-grid sizes.
    /// Radii cover the inclusive pixel footprint, from left-0.5 to right+0.5 and
    /// top-0.5 to bottom+0.5. Using the distance between pixel centers instead shrinks
    /// even-sized ovals and leaves a 2x2 ellipse completely empty.
    /// </summary>
    public static IEnumerable<(int X, int Y)> EllipseOutline(int x0, int y0, int x1, int y1)
    {
        var left = Math.Min(x0, x1);
        var right = Math.Max(x0, x1);
        var top = Math.Min(y0, y1);
        var bottom = Math.Max(y0, y1);

        var rx = (right - left + 1) / 2.0;
        var ry = (bottom - top + 1) / 2.0;

        if (left == right || top == bottom)
        {
            foreach (var p in RectangleOutline(x0, y0, x1, y1))
                yield return p;
            yield break;
        }

        var cx = (left + right) / 2.0;
        var cy = (top + bottom) / 2.0;

        for (var y = top; y <= bottom; y++)
        {
            for (var x = left; x <= right; x++)
            {
                if (!IsInsideEllipse(x, y, cx, cy, rx, ry))
                    continue;

                var isBoundary =
                    !IsInsideEllipse(x - 1, y, cx, cy, rx, ry) ||
                    !IsInsideEllipse(x + 1, y, cx, cy, rx, ry) ||
                    !IsInsideEllipse(x, y - 1, cx, cy, rx, ry) ||
                    !IsInsideEllipse(x, y + 1, cx, cy, rx, ry);

                if (isBoundary)
                    yield return (x, y);
            }
        }
    }

    /// <summary>
    /// Solid ellipse fit inside the axis-aligned box between the two corners. Shares
    /// IsInsideEllipse with EllipseOutline so the two are always consistent (the outline is
    /// always exactly the filled shape's boundary, never a pixel off from it).
    /// </summary>
    public static IEnumerable<(int X, int Y)> EllipseFilled(int x0, int y0, int x1, int y1)
    {
        var left = Math.Min(x0, x1);
        var right = Math.Max(x0, x1);
        var top = Math.Min(y0, y1);
        var bottom = Math.Max(y0, y1);

        var rx = (right - left + 1) / 2.0;
        var ry = (bottom - top + 1) / 2.0;

        if (left == right || top == bottom)
        {
            foreach (var p in RectangleFilled(x0, y0, x1, y1))
                yield return p;
            yield break;
        }

        var cx = (left + right) / 2.0;
        var cy = (top + bottom) / 2.0;

        for (var y = top; y <= bottom; y++)
            for (var x = left; x <= right; x++)
                if (IsInsideEllipse(x, y, cx, cy, rx, ry))
                    yield return (x, y);
    }

    /// <summary>
    /// Ellipse outline in "Pixel Cord" ON mode: takes EllipseOutline's true boundary (which
    /// allows plain corner-only diagonal touches between adjacent cells, e.g. "X ." / ". X") and
    /// applies exactly the same bridging rule as ConnectDiagonalSteps — wherever two boundary
    /// cells touch only diagonally, one orthogonal bridge cell is added between them so they
    /// share a full edge instead (e.g. "X X" / ". X"). The original boundary is never removed or
    /// moved, only bridge cells are added, so this is never smaller/thinner than EllipseOutline
    /// and never changes the ellipse's size. Of the two possible bridge cells for a given
    /// diagonal pair, the one further outside the ellipse is chosen (keeping the added pixel a
    /// pad on the outward side rather than eating into the interior).
    /// </summary>
    public static IEnumerable<(int X, int Y)> EllipseOutlineConnected(int x0, int y0, int x1, int y1)
    {
        var left = Math.Min(x0, x1);
        var right = Math.Max(x0, x1);
        var top = Math.Min(y0, y1);
        var bottom = Math.Max(y0, y1);

        var rx = (right - left + 1) / 2.0;
        var ry = (bottom - top + 1) / 2.0;

        if (left == right || top == bottom)
        {
            foreach (var p in RectangleOutline(x0, y0, x1, y1))
                yield return p;
            yield break;
        }

        var cx = (left + right) / 2.0;
        var cy = (top + bottom) / 2.0;

        var outline = new HashSet<(int X, int Y)>(EllipseOutline(x0, y0, x1, y1));
        var result = new HashSet<(int X, int Y)>(outline);

        foreach (var (x, y) in outline)
        {
            foreach (var (dx, dy) in DiagonalDirections)
            {
                var diag = (X: x + dx, Y: y + dy);
                if (!outline.Contains(diag))
                    continue;

                var bridgeA = (X: x + dx, Y: y);
                var bridgeB = (X: x, Y: y + dy);
                if (result.Contains(bridgeA) || result.Contains(bridgeB))
                    continue; // already 4-connected via an existing bridge

                var bridge = IsInsideEllipse(bridgeA.X, bridgeA.Y, cx, cy, rx, ry)
                    ? bridgeB
                    : bridgeA;
                result.Add(bridge);
            }
        }

        foreach (var point in result)
            yield return point;
    }

    private static readonly (int X, int Y)[] DiagonalDirections = { (1, 1), (1, -1), (-1, 1), (-1, -1) };

    /// <summary>
    /// "Thickens" any set of points (an outline, a line) by stamping a sizeX×sizeY RectBrush at
    /// each one and taking the union — used for the shape tools' adjustable Pen Size. Applying
    /// this to an outline rather than baking pen width into the outline algorithm itself keeps
    /// the two concerns (what shape, how thick) independent and reusable for Line/Rectangle/
    /// Ellipse alike.
    /// </summary>
    public static IEnumerable<(int X, int Y)> Dilate(IEnumerable<(int X, int Y)> points, int sizeX, int sizeY)
    {
        if (sizeX <= 1 && sizeY <= 1)
            return points;

        var result = new HashSet<(int, int)>();
        foreach (var (x, y) in points)
            foreach (var brushPoint in RectBrush(x, y, sizeX, sizeY))
                result.Add(brushPoint);
        return result;
    }
}
