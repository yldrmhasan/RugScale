namespace RugScale.Core.Drawing;

/// <summary>
/// Joint target-scale optimizer for the two visible designer sides of a leaf/petal.
///
/// Individual source fits can each be excellent while their independent roundness choices create
/// a subtle shoulder or width pulse when anisotropically resized. This stage keeps every learned
/// source control point fixed and changes ONLY Curve-tool roundness. Therefore it cannot invent a
/// new path; it selects the pair of RugScale curves that best preserves source flow and paired width
/// at the actual requested target size.
/// </summary>
internal static class LeafPetalBoundaryPairOptimizer
{
    private static readonly double[] StandardRoundness =
    [
        0.10,
        0.15,
        0.20,
        0.25,
        0.35,
        0.50,
        0.70,
        0.85,
        1.00,
    ];

    private const int WidthSamples = 28;

    public static LeafPetalBoundaryCurveModel Optimize(
        LeafPetalBoundaryCurveModel model,
        double scaleX,
        double scaleY)
    {
        ArgumentNullException.ThrowIfNull(model);

        if (model.LeftFit.Controls.Count < 2 ||
            model.RightFit.Controls.Count < 2)
        {
            return model;
        }

        // Search the complete Curve-tool roundness vocabulary. Restricting this to values near
        // the independent side fits can permanently exclude the real designer setting: e.g. one
        // staircase-overfit side may initially land on 0.85 even when the paired leaf was drawn at
        // 0.35. Rendering is cached per side, so the full 9x9 pair search remains cheap.
        var leftRounds =
            CompleteRoundnessSet(
                model.LeftFit.Roundness);
        var rightRounds =
            CompleteRoundnessSet(
                model.RightFit.Roundness);

        var mappedSourceLeft =
            MapSourcePath(
                model.LeftSourcePath,
                scaleX,
                scaleY);
        var mappedSourceRight =
            MapSourcePath(
                model.RightSourcePath,
                scaleX,
                scaleY);

        var leftRendered =
            leftRounds
                .Select(roundness =>
                    (
                        Roundness: roundness,
                        Path: RenderTarget(
                            model.LeftFit,
                            roundness,
                            scaleX,
                            scaleY)))
                .Where(item =>
                    item.Path.Count >= 4)
                .ToArray();
        var rightRendered =
            rightRounds
                .Select(roundness =>
                    (
                        Roundness: roundness,
                        Path: RenderTarget(
                            model.RightFit,
                            roundness,
                            scaleX,
                            scaleY)))
                .Where(item =>
                    item.Path.Count >= 4)
                .ToArray();

        PairScore? best = null;
        var originalLeft =
            model.LeftFit.Roundness;
        var originalRight =
            model.RightFit.Roundness;

        foreach (var leftCandidate in leftRendered)
        {
            foreach (var rightCandidate in rightRendered)
            {
                var candidate =
                    Evaluate(
                        leftCandidate.Path,
                        rightCandidate.Path,
                        mappedSourceLeft,
                        mappedSourceRight,
                        leftCandidate.Roundness,
                        rightCandidate.Roundness,
                        originalLeft,
                        originalRight);

                if (candidate is null)
                    continue;

                if (best is null ||
                    candidate.Value.Composite >
                    best.Value.Composite)
                {
                    best =
                        candidate;
                }
            }
        }

        if (best is null)
            return model;

        // Do not churn roundness for a microscopic score tie. The original individual fits remain
        // authoritative unless joint target-scale behaviour buys a real improvement.
        var original =
            Evaluate(
                RenderTarget(
                    model.LeftFit,
                    originalLeft,
                    scaleX,
                    scaleY),
                RenderTarget(
                    model.RightFit,
                    originalRight,
                    scaleX,
                    scaleY),
                mappedSourceLeft,
                mappedSourceRight,
                originalLeft,
                originalRight,
                originalLeft,
                originalRight);

        if (original is not null &&
            best.Value.Composite <
            original.Value.Composite +
            0.003)
        {
            return model;
        }

        return model with
        {
            LeftFit =
                model.LeftFit with
                {
                    Roundness =
                        best.Value.LeftRoundness,
                },
            RightFit =
                model.RightFit with
                {
                    Roundness =
                        best.Value.RightRoundness,
                },
        };
    }

    private static PairScore? Evaluate(
        IReadOnlyList<(int X, int Y)> left,
        IReadOnlyList<(int X, int Y)> right,
        IReadOnlyList<(int X, int Y)> sourceLeft,
        IReadOnlyList<(int X, int Y)> sourceRight,
        double leftRoundness,
        double rightRoundness,
        double originalLeft,
        double originalRight)
    {
        var leftSupport =
            NearF1(
                left,
                sourceLeft,
                radius: 1);
        var rightSupport =
            NearF1(
                right,
                sourceRight,
                radius: 1);

        // Joint optimization is never allowed to buy aesthetics by leaving source geometry.
        if (leftSupport < 0.94 ||
            rightSupport < 0.94)
        {
            return null;
        }

        var widthAgreement =
            WidthProfileAgreement(
                left,
                right,
                sourceLeft,
                sourceRight,
                out var widthSmoothness);

        if (widthAgreement < 0.86)
            return null;

        var leftFlow =
            MacroTangentAgreement(
                left,
                sourceLeft);
        var rightFlow =
            MacroTangentAgreement(
                right,
                sourceRight);
        var leftTurnProfile =
            TurnProfileAgreement(
                left,
                sourceLeft);
        var rightTurnProfile =
            TurnProfileAgreement(
                right,
                sourceRight);

        if (leftFlow < 0.88 ||
            rightFlow < 0.88 ||
            leftTurnProfile < 0.82 ||
            rightTurnProfile < 0.82)
        {
            return null;
        }

        var roundnessDrift =
            (
                Math.Abs(
                    leftRoundness -
                    originalLeft) +
                Math.Abs(
                    rightRoundness -
                    originalRight)) /
            2d;

        var composite =
            leftSupport * 0.14 +
            rightSupport * 0.14 +
            leftFlow * 0.11 +
            rightFlow * 0.11 +
            leftTurnProfile * 0.10 +
            rightTurnProfile * 0.10 +
            widthAgreement * 0.20 +
            widthSmoothness * 0.10 -
            roundnessDrift * 0.010;

        return new PairScore(
            composite,
            leftRoundness,
            rightRoundness);
    }

    private static double[] CompleteRoundnessSet(
        double learnedRoundness) =>
        StandardRoundness
            .Append(
                Math.Clamp(
                    learnedRoundness,
                    0.10,
                    1.00))
            .Select(value =>
                Math.Round(
                    value,
                    3))
            .Distinct()
            .OrderBy(value =>
                value)
            .ToArray();

    private static IReadOnlyList<(int X, int Y)> RenderTarget(
        ToolFaithfulCurveStyleFit fit,
        double roundness,
        double scaleX,
        double scaleY)
    {
        var controls =
            fit.Controls
                .Select(point =>
                    Map(
                        point,
                        scaleX,
                        scaleY))
                .ToArray();

        var result =
            new List<(int X, int Y)>();

        foreach (var point in Rasterizer.ConnectDiagonalSteps(
                     CurveRasterizer.Draw(
                         controls,
                         fit.Type,
                         roundness)))
        {
            if (result.Count == 0 ||
                result[^1] !=
                point)
            {
                result.Add(
                    point);
            }
        }

        return result;
    }

    private static IReadOnlyList<(int X, int Y)> MapSourcePath(
        IReadOnlyList<(int X, int Y)> path,
        double scaleX,
        double scaleY)
    {
        var result =
            new List<(int X, int Y)>();

        for (var i = 0;
             i < path.Count;
             i++)
        {
            var mapped =
                Map(
                    path[i],
                    scaleX,
                    scaleY);

            if (result.Count == 0)
            {
                result.Add(
                    mapped);
                continue;
            }

            foreach (var point in Rasterizer.ConnectDiagonalSteps(
                         Rasterizer.Line(
                             result[^1].X,
                             result[^1].Y,
                             mapped.X,
                             mapped.Y)))
            {
                if (result.Count == 0 ||
                    result[^1] !=
                    point)
                {
                    result.Add(
                        point);
                }
            }
        }

        return result;
    }

    private static (int X, int Y) Map(
        (int X, int Y) point,
        double scaleX,
        double scaleY) =>
        (
            X: (int)Math.Round(
                (point.X + 0.5) *
                scaleX -
                0.5),
            Y: (int)Math.Round(
                (point.Y + 0.5) *
                scaleY -
                0.5));

    private static double WidthProfileAgreement(
        IReadOnlyList<(int X, int Y)> left,
        IReadOnlyList<(int X, int Y)> right,
        IReadOnlyList<(int X, int Y)> sourceLeft,
        IReadOnlyList<(int X, int Y)> sourceRight,
        out double smoothness)
    {
        var totalAgreement = 0d;
        var used = 0;
        var actualWidths =
            new double[WidthSamples];

        for (var sample = 0;
             sample < WidthSamples;
             sample++)
        {
            var t =
                sample /
                (double)(WidthSamples - 1);
            var actualLeft =
                Sample(
                    left,
                    t);
            var actualRight =
                Sample(
                    right,
                    t);
            var expectedLeft =
                Sample(
                    sourceLeft,
                    t);
            var expectedRight =
                Sample(
                    sourceRight,
                    t);
            var actualWidth =
                Distance(
                    actualLeft,
                    actualRight);
            var expectedWidth =
                Distance(
                    expectedLeft,
                    expectedRight);
            actualWidths[sample] =
                actualWidth;

            var tolerance =
                Math.Max(
                    1.5,
                    expectedWidth *
                    0.20);
            var error =
                Math.Abs(
                    actualWidth -
                    expectedWidth);
            var agreement =
                1d -
                Math.Clamp(
                    error /
                    tolerance,
                    0d,
                    1d);

            totalAgreement +=
                agreement;
            used++;
        }

        var roughness = 0d;
        var roughnessSamples = 0;

        for (var i = 1;
             i <
             actualWidths.Length -
             1;
             i++)
        {
            var secondDifference =
                actualWidths[i + 1] -
                2d *
                actualWidths[i] +
                actualWidths[i - 1];
            var localScale =
                Math.Max(
                    2d,
                    (
                        actualWidths[i - 1] +
                        actualWidths[i] +
                        actualWidths[i + 1]) /
                    3d);

            roughness +=
                Math.Min(
                    1d,
                    Math.Abs(
                        secondDifference) /
                    localScale);
            roughnessSamples++;
        }

        var averageRoughness =
            roughnessSamples == 0
                ? 0d
                : roughness /
                  roughnessSamples;
        smoothness =
            1d -
            Math.Clamp(
                averageRoughness *
                1.8,
                0d,
                1d);

        return used == 0
            ? 0d
            : totalAgreement /
              used;
    }

    private static double TurnProfileAgreement(
        IReadOnlyList<(int X, int Y)> actual,
        IReadOnlyList<(int X, int Y)> expected)
    {
        const int Samples = 11;
        var actualTangents =
            new (double X, double Y)[Samples];
        var expectedTangents =
            new (double X, double Y)[Samples];

        for (var sample = 0;
             sample < Samples;
             sample++)
        {
            var t =
                sample /
                (double)(Samples - 1);
            actualTangents[sample] =
                Tangent(
                    actual,
                    t);
            expectedTangents[sample] =
                Tangent(
                    expected,
                    t);
        }

        var total = 0d;
        var used = 0;

        for (var sample = 1;
             sample < Samples;
             sample++)
        {
            if (!TrySignedTurn(
                    actualTangents[sample - 1],
                    actualTangents[sample],
                    out var actualTurn) ||
                !TrySignedTurn(
                    expectedTangents[sample - 1],
                    expectedTangents[sample],
                    out var expectedTurn))
            {
                continue;
            }

            // One tenth of a radian (~5.7 degrees) is effectively the same macro turn for an
            // indexed raster. Larger disagreement is graded smoothly; a reversed bend is heavily
            // penalized.
            var difference =
                Math.Abs(
                    NormalizeAngle(
                        actualTurn -
                        expectedTurn));
            var agreement =
                1d -
                Math.Clamp(
                    difference /
                    0.55,
                    0d,
                    1d);

            total +=
                agreement;
            used++;
        }

        return used == 0
            ? 1d
            : total /
              used;
    }

    private static bool TrySignedTurn(
        (double X, double Y) first,
        (double X, double Y) second,
        out double angle)
    {
        angle = 0d;

        if ((first.X == 0d &&
             first.Y == 0d) ||
            (second.X == 0d &&
             second.Y == 0d))
        {
            return false;
        }

        var cross =
            first.X *
            second.Y -
            first.Y *
            second.X;
        var dot =
            Math.Clamp(
                first.X *
                second.X +
                first.Y *
                second.Y,
                -1d,
                1d);
        angle =
            Math.Atan2(
                cross,
                dot);

        return true;
    }

    private static double NormalizeAngle(
        double angle)
    {
        while (angle >
               Math.PI)
        {
            angle -=
                2d *
                Math.PI;
        }

        while (angle <
               -Math.PI)
        {
            angle +=
                2d *
                Math.PI;
        }

        return angle;
    }

    private static double MacroTangentAgreement(
        IReadOnlyList<(int X, int Y)> actual,
        IReadOnlyList<(int X, int Y)> expected)
    {
        const int Samples = 9;
        var total = 0d;
        var used = 0;

        for (var sample = 0;
             sample < Samples;
             sample++)
        {
            var t =
                sample /
                (double)(Samples - 1);
            var actualTangent =
                Tangent(
                    actual,
                    t);
            var expectedTangent =
                Tangent(
                    expected,
                    t);

            if ((actualTangent.X == 0d &&
                 actualTangent.Y == 0d) ||
                (expectedTangent.X == 0d &&
                 expectedTangent.Y == 0d))
            {
                continue;
            }

            var cosine =
                Math.Clamp(
                    actualTangent.X *
                    expectedTangent.X +
                    actualTangent.Y *
                    expectedTangent.Y,
                    -1d,
                    1d);

            total +=
                (cosine +
                 1d) /
                2d;
            used++;
        }

        return used == 0
            ? 0d
            : total /
              used;
    }

    private static (double X, double Y) Tangent(
        IReadOnlyList<(int X, int Y)> path,
        double t)
    {
        if (path.Count < 2)
            return (0d, 0d);

        var center =
            (int)Math.Round(
                Math.Clamp(
                    t,
                    0d,
                    1d) *
                (path.Count - 1));
        var span =
            Math.Clamp(
                path.Count /
                10,
                2,
                10);
        var before =
            Math.Max(
                0,
                center -
                span);
        var after =
            Math.Min(
                path.Count - 1,
                center +
                span);
        var x =
            path[after].X -
            path[before].X;
        var y =
            path[after].Y -
            path[before].Y;
        var length =
            Math.Sqrt(
                x *
                x +
                y *
                y);

        return length <= 1e-9
            ? (0d, 0d)
            : (
                x /
                length,
                y /
                length);
    }

    private static double NearF1(
        IReadOnlyList<(int X, int Y)> actual,
        IReadOnlyList<(int X, int Y)> expected,
        int radius)
    {
        var actualSet =
            actual.ToHashSet();
        var expectedSet =
            expected.ToHashSet();

        if (actualSet.Count == 0 ||
            expectedSet.Count == 0)
        {
            return 0d;
        }

        var actualSupported =
            actualSet.Count(point =>
                HasNear(
                    expectedSet,
                    point,
                    radius));
        var expectedSupported =
            expectedSet.Count(point =>
                HasNear(
                    actualSet,
                    point,
                    radius));
        var precision =
            actualSupported /
            (double)actualSet.Count;
        var recall =
            expectedSupported /
            (double)expectedSet.Count;
        var sum =
            precision +
            recall;

        return sum <= 0d
            ? 0d
            : 2d *
              precision *
              recall /
              sum;
    }

    private static bool HasNear(
        IReadOnlySet<(int X, int Y)> points,
        (int X, int Y) point,
        int radius)
    {
        for (var dy = -radius;
             dy <= radius;
             dy++)
        {
            for (var dx = -radius;
                 dx <= radius;
                 dx++)
            {
                if (points.Contains(
                        (
                            point.X +
                            dx,
                            point.Y +
                            dy)))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static (double X, double Y) Sample(
        IReadOnlyList<(int X, int Y)> path,
        double t)
    {
        if (path.Count == 0)
            return (0d, 0d);

        if (path.Count == 1)
            return path[0];

        var position =
            Math.Clamp(
                t,
                0d,
                1d) *
            (path.Count - 1);
        var lower =
            (int)Math.Floor(
                position);
        var upper =
            Math.Min(
                path.Count - 1,
                lower +
                1);
        var fraction =
            position -
            lower;

        return (
            path[lower].X +
            (path[upper].X -
             path[lower].X) *
            fraction,
            path[lower].Y +
            (path[upper].Y -
             path[lower].Y) *
            fraction);
    }

    private static double Distance(
        (double X, double Y) left,
        (double X, double Y) right)
    {
        var dx =
            left.X -
            right.X;
        var dy =
            left.Y -
            right.Y;

        return Math.Sqrt(
            dx *
            dx +
            dy *
            dy);
    }

    private readonly record struct PairScore(
        double Composite,
        double LeftRoundness,
        double RightRoundness);
}
