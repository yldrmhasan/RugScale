namespace RugScale.Core.Drawing;

public enum CurveType { Bezier, Spline, SplineThroughPoints }

public static class CurveRasterizer
{
    public static IEnumerable<(int X, int Y)> Draw(IReadOnlyList<(int X, int Y)> points, CurveType type, double roundness) =>
        ThinSampledPath(Sample(points, type, roundness), type == CurveType.SplineThroughPoints ? points.ToHashSet() : new HashSet<(int X, int Y)>());

    // Dense subpixel sampling can cross X and Y grid boundaries in separate samples.
    // Remove the resulting one-cell elbow before Pixel Cord optionally bridges diagonals.
    private static IEnumerable<(int X, int Y)> ThinSampledPath(IEnumerable<(int X, int Y)> samples, HashSet<(int X, int Y)> protectedPoints)
    {
        (int X, int Y)? anchor = null, pending = null;
        foreach (var point in samples)
        {
            if (pending == point || pending == null && anchor == point) continue;
            if (anchor == null) { anchor = point; yield return point; continue; }
            if (pending == null) { pending = point; continue; }
            var a = anchor.Value; var b = pending.Value;
            var elbow = Math.Abs(point.X - a.X) == 1 && Math.Abs(point.Y - a.Y) == 1 &&
                Math.Abs(b.X - a.X) + Math.Abs(b.Y - a.Y) == 1 &&
                Math.Abs(point.X - b.X) + Math.Abs(point.Y - b.Y) == 1;
            if (elbow && !protectedPoints.Contains(b)) { pending = point; continue; }
            yield return b; anchor = b; pending = point;
        }
        if (pending is { } last) yield return last;
    }

    private static IEnumerable<(int X, int Y)> Sample(IReadOnlyList<(int X, int Y)> points, CurveType type, double roundness)
    {
        if (points.Count < 2) { if (points.Count == 1) yield return points[0]; yield break; }
        roundness = Math.Clamp(roundness, 0, 1);
        var length = 0.0;
        for (var i = 1; i < points.Count; i++) length += Math.Abs((double)points[i].X - points[i - 1].X) + Math.Abs((double)points[i].Y - points[i - 1].Y);
        var steps = (int)Math.Clamp(Math.Ceiling(length * 2), Math.Min(points.Count * 2, 200_000), 200_000);
        if (type == CurveType.SplineThroughPoints) steps = ((steps + points.Count - 2) / (points.Count - 1)) * (points.Count - 1);
        var previous = points[0];
        for (var i = 0; i <= steps; i++)
        {
            var t = (double)i / steps;
            var value = Evaluate(points, type, roundness, t);
            var current = (X: (int)Math.Round(value.X), Y: (int)Math.Round(value.Y));
            foreach (var pixel in Rasterizer.Line(previous.X, previous.Y, current.X, current.Y)) yield return pixel;
            previous = current;
        }
    }
    private static (double X, double Y) Evaluate(IReadOnlyList<(int X, int Y)> p, CurveType type, double r, double t)
    {
        if (t >= 1) return p[^1];
        if (type == CurveType.SplineThroughPoints)
        {
            // Use the full slider range: 25% is standard cardinal smoothing;
            // higher values extend tangents for visibly rounder bends.
            r *= 4;
            var position = t * (p.Count - 1); var i = Math.Min(p.Count - 2, (int)position); var u = position - i;
            var a = p[i]; var b = p[i + 1]; var prev = p[Math.Max(0, i - 1)]; var next = p[Math.Min(p.Count - 1, i + 2)];
            var h00 = 2 * u * u * u - 3 * u * u + 1; var h10 = u * u * u - 2 * u * u + u;
            var h01 = -2 * u * u * u + 3 * u * u; var h11 = u * u * u - u * u;
            return (h00 * a.X + h01 * b.X + h10 * (b.X - prev.X) * r / 2 + h11 * (next.X - a.X) * r / 2,
                h00 * a.Y + h01 * b.Y + h10 * (b.Y - prev.Y) * r / 2 + h11 * (next.Y - a.Y) * r / 2);
        }
        if (type == CurveType.Bezier)
        {
            var segments = (p.Count - 1 + 2) / 3; var position = t * segments;
            var start = Math.Min(segments - 1, (int)position) * 3; var degree = Math.Min(3, p.Count - 1 - start);
            var u = position - (int)position;
            Span<double> x = stackalloc double[4]; Span<double> y = stackalloc double[4];
            for (var i = 0; i <= degree; i++)
            {
                var baselineX = p[start].X + ((double)p[start + degree].X - p[start].X) * i / degree;
                var baselineY = p[start].Y + ((double)p[start + degree].Y - p[start].Y) * i / degree;
                x[i] = baselineX + (p[start + i].X - baselineX) * r; y[i] = baselineY + (p[start + i].Y - baselineY) * r;
            }
            for (var level = degree; level > 0; level--) for (var i = 0; i < level; i++)
            { x[i] = (1 - u) * x[i] + u * x[i + 1]; y[i] = (1 - u) * y[i] + u * y[i + 1]; }
            return (x[0], y[0]);
        }
        // Clamped uniform B-spline: endpoints interpolate, interior vertices are control points.
        var d = Math.Min(3, p.Count - 1); var max = p.Count - d; var parameter = t * max;
        var span = Math.Min(p.Count - 1, (int)parameter + d);
        double Knot(int i) => i <= d ? 0 : i >= p.Count ? max : i - d;
        Span<double> sx = stackalloc double[4]; Span<double> sy = stackalloc double[4];
        for (var j = 0; j <= d; j++)
        {
            var index = span - d + j; var f = (double)index / (p.Count - 1);
            var bx = p[0].X + ((double)p[^1].X - p[0].X) * f; var by = p[0].Y + ((double)p[^1].Y - p[0].Y) * f;
            sx[j] = bx + (p[index].X - bx) * r; sy[j] = by + (p[index].Y - by) * r;
        }
        for (var level = 1; level <= d; level++) for (var j = d; j >= level; j--)
        {
            var k = span - d + j; var denominator = Knot(k + d - level + 1) - Knot(k);
            var alpha = denominator == 0 ? 0 : (parameter - Knot(k)) / denominator;
            sx[j] = (1 - alpha) * sx[j - 1] + alpha * sx[j]; sy[j] = (1 - alpha) * sy[j - 1] + alpha * sy[j];
        }
        return (sx[d], sy[d]);
    }
}
