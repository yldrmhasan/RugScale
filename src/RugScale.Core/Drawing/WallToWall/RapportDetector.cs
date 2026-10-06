using RugScale.Core.Models;

namespace RugScale.Core.Drawing.WallToWall;

/// <summary>What <see cref="RapportDetector"/> found, with the match scores behind it.</summary>
public sealed record RapportDetection(
    RapportTile Tile,
    double HorizontalScore,
    double VerticalScore,
    bool HorizontalFound,
    bool VerticalFound,
    EdgeMarkers? Markers);

/// <summary>
/// Finds the repeat unit (rapport) of a roll / wall-to-wall design.
///
/// The technical marker columns on the edges are set aside first. A horizontal period P is a shift
/// at which the design matches itself far better than at a typical shift (score = share of equal
/// pixels between x and x + P over a sampled grid); the smallest shift scoring within
/// <see cref="Tolerance"/> of the best one is taken, so a double period is never chosen over the
/// fundamental one. The vertical period works the same way. When both exist, a drop (half-drop
/// or any vertical offset between neighbouring repeats) is accepted if it matches clearly better
/// than a straight repeat. An axis without a period keeps the whole design extent: the design is
/// itself one rapport along that axis.
/// </summary>
public static class RapportDetector
{
    /// <summary>Minimum share of equal pixels for a period.</summary>
    internal const double MinimumScore = 0.75;

    /// <summary>A period must beat the median shift by this much.</summary>
    internal const double MinimumLift = 0.2;

    internal const double Tolerance = 0.02;

    /// <summary>A period must beat the shifts this far on either side by <see cref="MinimumSharpness"/>.</summary>
    internal const int SharpnessDistance = 4;

    internal const double MinimumSharpness = 0.05;

    private const int Sample = 3;

    public static RapportDetection Detect(
        DesignDocument source)
    {
        ArgumentNullException.ThrowIfNull(source);

        var w =
            source.Width;
        var h =
            source.Height;
        var px =
            new byte[w * h];

        for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
                px[y * w + x] = source.GetPixel(x, y);

        var markers =
            FindEdgeMarkers(source);
        var x0 =
            markers?.Left ?? 0;
        var x1 =
            w - (markers?.Right ?? 0);
        var cw =
            x1 - x0;

        double Score(
            int dx,
            int dy) =>
            ScoreAt(dx, dy, Sample);

        double ScoreAt(
            int dx,
            int dy,
            int sample)
        {
            var equal = 0;
            var total = 0;

            for (var y = Math.Max(0, -dy); y < h && y + dy < h; y += sample)
            {
                var row =
                    y * w;
                var row2 =
                    (y + dy) * w;

                for (var x = Math.Max(x0, x0 - dx); x < x1 && x + dx < x1; x += sample)
                {
                    total++;

                    if (px[row + x] == px[row2 + x + dx])
                        equal++;
                }
            }

            return total < 400
                ? 0
                : equal / (double)total;
        }

        var (periodX, scoreX) =
            FindPeriod(
                Math.Max(8, cw / 16),
                cw - Math.Max(8, cw / 16),
                shift => Score(shift, 0));
        var (periodY, scoreY) =
            FindPeriod(
                Math.Max(8, h / 16),
                h - Math.Max(8, h / 16),
                shift => Score(0, shift));

        var halfDrop = 0;

        // A dropped repeat (half-drop, third-drop ...) has no pure horizontal period: search the
        // usual drop fractions of the vertical period.
        if (periodX == 0 &&
            periodY > 0)
        {
            var drops =
                new[] { periodY / 2, periodY / 3, 2 * periodY / 3, periodY / 4, 3 * periodY / 4 }
                    .Where(d => d > 0)
                    .Distinct()
                    .ToArray();
            var coarse =
                Math.Max(Sample, 6);
            var best = 0.0;
            var bestShift = 0;
            var bestDrop = 0;

            for (var shift = Math.Max(8, cw / 16); shift <= cw - Math.Max(8, cw / 16); shift += 2)
            {
                foreach (var d in drops)
                {
                    var score =
                        ScoreAt(shift, d, coarse);

                    if (score > best)
                    {
                        best = score;
                        bestShift = shift;
                        bestDrop = d;
                    }
                }
            }

            if (best >= MinimumScore)
            {
                // Refine around the coarse hit at full sampling.
                var refined = 0.0;

                for (var shift = Math.Max(1, bestShift - 2); shift <= bestShift + 2; shift++)
                {
                    for (var d = bestDrop - 2; d <= bestDrop + 2; d++)
                    {
                        var score =
                            Score(shift, d);

                        if (score > refined)
                        {
                            refined = score;
                            periodX = shift;
                            halfDrop = d;
                        }
                    }
                }

                scoreX = refined;
            }
        }

        // A half-drop repeat also matches straight at twice its width (third-drop at three
        // times): prefer the fundamental rapport with its drop.
        if (halfDrop == 0 &&
            periodX > 0 &&
            periodY > 0)
        {
            foreach (var parts in new[] { 2, 3 })
            {
                var sub =
                    (int)Math.Round(periodX / (double)parts);

                if (sub < 8)
                    continue;

                for (var k = 1; k < parts; k++)
                {
                    var bestScore = 0.0;
                    var bestShift = 0;
                    var bestDrop = 0;

                    for (var shift = sub - 2; shift <= sub + 2; shift++)
                    {
                        for (var d = k * periodY / parts - 2; d <= k * periodY / parts + 2; d++)
                        {
                            var score =
                                Score(shift, d);

                            if (score > bestScore)
                            {
                                bestScore = score;
                                bestShift = shift;
                                bestDrop = d;
                            }
                        }
                    }

                    if (bestScore >= scoreX - Tolerance)
                    {
                        periodX = bestShift;
                        halfDrop = bestDrop;
                        scoreX = bestScore;
                        break;
                    }
                }

                if (halfDrop != 0)
                    break;
            }
        }

        var tileWidth =
            periodX > 0
                ? periodX
                : cw;
        var tileHeight =
            periodY > 0
                ? periodY
                : h;
        var drop =
            halfDrop;

        if (halfDrop == 0 &&
            periodX > 0 &&
            periodY > 0)
        {
            var straight =
                Score(periodX, 0);
            var bestDrop = 0;
            var bestScore = straight;

            for (var d = -periodY / 2; d <= periodY / 2; d += 2)
            {
                if (d == 0)
                    continue;

                var score =
                    Score(periodX, d);

                if (score > bestScore)
                {
                    bestScore = score;
                    bestDrop = d;
                }
            }

            if (bestScore > straight + 0.05)
            {
                drop =
                    ((bestDrop % periodY) + periodY) % periodY;
            }
        }

        // Signed drop: a few rows up reads better than almost a whole rapport down.
        if (tileHeight > 0)
        {
            drop =
                ((drop % tileHeight) + tileHeight) % tileHeight;

            if (drop > tileHeight / 2)
                drop -= tileHeight;
        }

        return new RapportDetection(
            new RapportTile(
                x0,
                0,
                tileWidth,
                tileHeight,
                drop),
            scoreX,
            scoreY,
            periodX > 0,
            periodY > 0,
            markers);
    }

    /// <summary>
    /// Columns at the left / right edge filled with one colour that occurs nowhere else
    /// (RugCAD technical markers); null when there are none.
    /// </summary>
    public static EdgeMarkers? FindEdgeMarkers(
        DesignDocument source)
    {
        ArgumentNullException.ThrowIfNull(source);

        var w =
            source.Width;
        var h =
            source.Height;

        if (w < 4)
            return null;

        var counts =
            new int[256];

        for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
                counts[source.GetPixel(x, y)]++;

        bool Uniform(
            int x,
            out byte color)
        {
            color = source.GetPixel(x, 0);

            for (var y = 1; y < h; y++)
            {
                if (source.GetPixel(x, y) != color)
                    return false;
            }

            return true;
        }

        byte? marker = null;
        var left = 0;

        while (left < w / 4 &&
               Uniform(left, out var color) &&
               (marker is null || marker == color))
        {
            marker = color;
            left++;
        }

        var right = 0;

        while (right < w / 4 &&
               Uniform(w - 1 - right, out var color) &&
               (marker is null || marker == color))
        {
            marker = color;
            right++;
        }

        if (marker is null ||
            counts[marker.Value] != (left + right) * h)
        {
            return null;
        }

        return new EdgeMarkers(
            left,
            right,
            marker.Value);
    }

    private static (int Period, double Score) FindPeriod(
        int from,
        int to,
        Func<int, double> score)
    {
        if (to <= from)
            return (0, 0);

        var scores =
            new List<(int Shift, double Score)>();

        for (var shift = from; shift <= to; shift++)
            scores.Add((shift, score(shift)));

        var best =
            scores.Max(item => item.Score);
        var median =
            scores
                .Select(item => item.Score)
                .OrderBy(value => value)
                .ElementAt(scores.Count / 2);

        if (best < MinimumScore ||
            best - median < MinimumLift)
        {
            return (0, best);
        }

        // The fundamental period: the first local peak close to the best score.
        for (var k = 0; k < scores.Count; k++)
        {
            var (shift, value) =
                scores[k];

            if (value < best - Tolerance)
                continue;

            // Climb to the local peak.
            while (k + 1 < scores.Count &&
                   scores[k + 1].Score > scores[k].Score)
            {
                k++;
            }

            // A true repeat is a sharp peak; streaks or a smooth texture only fade slowly with
            // the shift (B390A: 82 % at 24 px, as much at 20 or 28 px).
            var flank = 0.0;

            foreach (var side in new[] { k - SharpnessDistance, k + SharpnessDistance })
            {
                if (side >= 0 &&
                    side < scores.Count)
                {
                    flank = Math.Max(flank, scores[side].Score);
                }
            }

            return scores[k].Score - flank >= MinimumSharpness
                ? scores[k]
                : (0, scores[k].Score);
        }

        return (0, best);
    }
}
