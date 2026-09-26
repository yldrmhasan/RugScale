namespace RugScale.Core.Drawing;

/// <summary>
/// Compares the ordered pixel-step language of two rasterized curves.
///
/// Set overlap answers whether two curves occupy nearby pixels, but Pixel-Cord also has a drawing
/// cadence: horizontal/vertical/diagonal step proportions and, more importantly, how those step
/// directions transition. Two paths can be within one pixel of each other while one still has the
/// wrong staircase rhythm.
///
/// The score is 0..1. Direction distribution carries 35%; ordered direction-transition cadence
/// carries 65%. Repeated identical points are ignored and longer jumps are reduced to their sign
/// direction so the metric is stable across mapped target grids.
/// </summary>
internal static class CurvePixelCadence
{
    public static double Measure(
        IReadOnlyList<(int X, int Y)> actual,
        IReadOnlyList<(int X, int Y)> expected)
    {
        ArgumentNullException.ThrowIfNull(actual);
        ArgumentNullException.ThrowIfNull(expected);

        if (actual.Count < 2 ||
            expected.Count < 2)
        {
            return 0d;
        }

        var actualFeatures =
            Features(
                actual);
        var expectedFeatures =
            Features(
                expected);
        var directionSimilarity =
            DistributionSimilarity(
                actualFeatures.Directions,
                expectedFeatures.Directions);
        var transitionSimilarity =
            DistributionSimilarity(
                actualFeatures.Transitions,
                expectedFeatures.Transitions);

        return directionSimilarity *
                   0.35 +
               transitionSimilarity *
                   0.65;
    }

    private static (double[] Directions, double[] Transitions) Features(
        IReadOnlyList<(int X, int Y)> path)
    {
        var directions =
            new double[8];
        var transitions =
            new double[64];
        var ordered =
            new List<int>(
                Math.Max(
                    0,
                    path.Count - 1));

        for (var index = 1;
             index < path.Count;
             index++)
        {
            var direction =
                Direction(
                    path[index - 1],
                    path[index]);

            if (direction < 0)
                continue;

            ordered.Add(
                direction);
            directions[direction]++;
        }

        for (var index = 1;
             index < ordered.Count;
             index++)
        {
            transitions[
                ordered[index - 1] *
                    8 +
                ordered[index]]++;
        }

        Normalize(
            directions);
        Normalize(
            transitions);

        return
            (
                directions,
                transitions
            );
    }

    private static int Direction(
        (int X, int Y) a,
        (int X, int Y) b)
    {
        var dx =
            Math.Sign(
                b.X -
                a.X);
        var dy =
            Math.Sign(
                b.Y -
                a.Y);

        return (dx, dy) switch
        {
            (1, 0) => 0,
            (1, 1) => 1,
            (0, 1) => 2,
            (-1, 1) => 3,
            (-1, 0) => 4,
            (-1, -1) => 5,
            (0, -1) => 6,
            (1, -1) => 7,
            _ => -1,
        };
    }

    private static void Normalize(
        double[] values)
    {
        var total =
            values.Sum();

        if (total <= 0d)
            return;

        for (var index = 0;
             index < values.Length;
             index++)
        {
            values[index] /=
                total;
        }
    }

    private static double DistributionSimilarity(
        IReadOnlyList<double> left,
        IReadOnlyList<double> right)
    {
        var l1 = 0d;

        for (var index = 0;
             index < left.Count;
             index++)
        {
            l1 +=
                Math.Abs(
                    left[index] -
                    right[index]);
        }

        return Math.Clamp(
            1d -
            l1 *
                0.5,
            0d,
            1d);
    }
}
