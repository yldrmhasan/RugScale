using RugScale.Core.Models;

namespace RugScale.Core.Drawing;

/// <summary>
/// Indexed contour-and-fill scaler for curve-heavy carpet artwork.
///
/// Unlike motif RugScale, this mode does not skeletonize a filled ornament and does not try to
/// reinterpret leaf interiors as branches. Every USED palette index is treated as a categorical
/// filled region. An exact anisotropic Euclidean signed-distance field is reconstructed from the
/// original indexed boundaries, sampled with bounded bicubic interpolation at the target grid,
/// and the strongest region wins each target pixel.
///
/// Consequences:
/// - outer/inner contours are re-rasterized as continuous oval/curved boundaries instead of
///   copying block staircases;
/// - enclosed source colours remain first-class regions and are not "painted over" by a stroke
///   pass;
/// - output is still pure indexed colour: no RGB blending and no new palette index;
/// - source pixel aspect is derived from warp/weft quality so curve distance is measured in the
///   carpet's physical grid rather than pretending pixels are always square.
/// </summary>
internal static class CurveFillScaleEngine
{
    private const float Infinity = 1_000_000f;
    private const float TieBias = 0.0005f;

    public static void Resize(
        DesignDocument source,
        DesignDocument destination,
        int sourceWarpDensity,
        int sourceWeftDensity,
        int targetWarpDensity,
        int targetWeftDensity) =>
        _ = ResizeWithDiagnostics(
            source,
            destination,
            sourceWarpDensity,
            sourceWeftDensity,
            targetWarpDensity,
            targetWeftDensity);

    internal static ToolFaithfulOverlayReport ResizeWithDiagnostics(
        DesignDocument source,
        DesignDocument destination,
        int sourceWarpDensity,
        int sourceWeftDensity,
        int targetWarpDensity,
        int targetWeftDensity)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(destination);

        if (source.Width == destination.Width &&
            source.Height == destination.Height)
        {
            Copy(source, destination);
            return default;
        }

        var used = UsedIndices(source);
        if (used.Count <= 1)
        {
            CopySingleColor(
                destination,
                used.Count == 0
                    ? (byte)0
                    : used[0]);
            return default;
        }

        var sourceCount =
            checked(
                source.Width *
                source.Height);
        var sourcePixels =
            new byte[sourceCount];

        for (var y = 0; y < source.Height; y++)
        {
            for (var x = 0; x < source.Width; x++)
            {
                sourcePixels[
                    y *
                    source.Width +
                    x] =
                    source.GetPixel(
                        x,
                        y);
            }
        }

        var targetCount =
            checked(
                destination.Width *
                destination.Height);
        var bestScore =
            new float[targetCount];
        Array.Fill(
            bestScore,
            float.NegativeInfinity);

        // Nearest source colour is the deterministic tie-break only. The actual contour decision
        // comes from the signed fields below.
        var nearest =
            new byte[targetCount];

        for (var ty = 0; ty < destination.Height; ty++)
        {
            var sy = Math.Clamp(
                (int)Math.Floor(
                    (ty + 0.5) *
                    source.Height /
                    destination.Height),
                0,
                source.Height - 1);

            for (var tx = 0; tx < destination.Width; tx++)
            {
                var sx = Math.Clamp(
                    (int)Math.Floor(
                        (tx + 0.5) *
                        source.Width /
                        destination.Width),
                    0,
                    source.Width - 1);

                nearest[
                    ty *
                    destination.Width +
                    tx] =
                    sourcePixels[
                        sy *
                        source.Width +
                        sx];
            }
        }

        // Distances are measured in source physical-pixel units. Horizontal source pixels are
        // warp samples; vertical pixels are weft samples. Normalize X to 1 and scale Y so a
        // 40x50 design is not incorrectly interpreted on a square grid.
        var xStep = 1f;
        var yStep =
            sourceWarpDensity > 0 &&
            sourceWeftDensity > 0
                ? sourceWarpDensity /
                  (float)sourceWeftDensity
                : 1f;

        var mask =
            new bool[sourceCount];
        var distanceToColor =
            new float[sourceCount];
        var distanceToOther =
            new float[sourceCount];

        foreach (var color in used)
        {
            for (var i = 0; i < sourceCount; i++)
                mask[i] = sourcePixels[i] == color;

            ComputeExactDistance(
                mask,
                source.Width,
                source.Height,
                distanceToColor,
                zeroWhereMaskIsTrue: true,
                xStep,
                yStep);

            ComputeExactDistance(
                mask,
                source.Width,
                source.Height,
                distanceToOther,
                zeroWhereMaskIsTrue: false,
                xStep,
                yStep);

            var signedField =
                new float[sourceCount];

            for (var i = 0;
                 i < sourceCount;
                 i++)
            {
                signedField[i] =
                    sourcePixels[i] == color
                        ? distanceToOther[i]
                        : -distanceToColor[i];
            }

            for (var ty = 0; ty < destination.Height; ty++)
            {
                var sourceY =
                    (ty + 0.5) *
                    source.Height /
                    destination.Height -
                    0.5;

                for (var tx = 0; tx < destination.Width; tx++)
                {
                    var sourceX =
                        (tx + 0.5) *
                        source.Width /
                        destination.Width -
                        0.5;

                    var score =
                        SampleSignedFieldBicubic(
                            signedField,
                            source.Width,
                            source.Height,
                            sourceX,
                            sourceY);

                    var target =
                        ty *
                        destination.Width +
                        tx;

                    if (nearest[target] == color)
                        score += TieBias;

                    if (score <= bestScore[target])
                        continue;

                    bestScore[target] = score;
                    destination.SetPixel(
                        tx,
                        ty,
                        color);
                }
            }
        }

        // Smooth categorical interpolation is allowed to move a boundary locally, but a fill
        // must never jump across a third-colour separator. Lock region ownership BEFORE replaying
        // the tool-faithful outline so the final stroke remains the hard visual barrier.
        var ownership =
            CurveFillRegionOwnershipGuard.Apply(
                source,
                destination);

        // Replay any source 1x1 Curve/Pixel-Cord strokes through RugScale's own rasterizer.
        // Filled regions stay owned by the guarded reconstruction above; this pass only reasserts
        // genuine tool-style line work and never skeletonizes a filled ornament.
        var overlay =
            ToolFaithfulPixelCordOverlay.Apply(
                source,
                destination,
                sourceWarpDensity,
                sourceWeftDensity,
                targetWarpDensity,
                targetWeftDensity);

        // Cleanup of the block-expanded old stroke happens inside the tool overlay. Validate its
        // replacement colours as well: this second pass catches a rare narrow-junction case where
        // a cleanup pixel can choose the geometrically close but topologically opposite fill.
        var finalOwnership =
            CurveFillRegionOwnershipGuard.Apply(
                source,
                destination);

        // Long filled oval/ribbon arcs need a different authority than the Pixel-Cord learner:
        // they are categorical regions, not 1x1 strokes. Refine only high-confidence, stable-width
        // curved ribbons as a smooth centreline + paired boundaries. The specialist rasterizer
        // protects separator palette roles and only moves the local two-colour boundary band, so
        // do not run the global ownership guard again afterwards (that would undo the aesthetic
        // correction by snapping it back onto the source staircase).
        var ribbonArc =
            CurveFillRibbonArcRefiner.ApplyWithDiagnostics(
                source,
                destination);

        PreserveExactSourceSymmetry(
            source,
            destination);

        return overlay with
        {
            RegionOwnershipCorrections =
                ownership.UnsupportedColourCorrections +
                finalOwnership.UnsupportedColourCorrections,
            BarrierCrossingCorrections =
                ownership.BarrierCrossingCorrections +
                finalOwnership.BarrierCrossingCorrections,
            RibbonArcCandidates =
                ribbonArc.RibbonGeometryAccepted,
            RibbonArcRefined =
                ribbonArc.Refined,
            RibbonArcPixelsChanged =
                ribbonArc.BoundaryPixelsChanged,
            RibbonArcCurveToolFits =
                ribbonArc.CurveToolFits,
            RibbonArcGeometricThroughFits =
                ribbonArc.GeometricThroughFits,
            RibbonArcGeometricThroughAttempts =
                ribbonArc.GeometricThroughAttempts,
            RibbonArcGeometricThroughMeanRoundness =
                ribbonArc.MeanGeometricThroughRoundness,
            RibbonArcGeometricThroughMaxP95Deviation =
                ribbonArc.MaxGeometricThroughP95Deviation,
            RibbonArcBroadOvalFits =
                ribbonArc.BroadOvalFits,
            RibbonArcBroadOvalAttempts =
                ribbonArc.BroadOvalAttempts,
            RibbonArcCubicBezierFits =
                ribbonArc.CubicBezierFits,
            RibbonArcOutlinedRefined =
                ribbonArc.OutlinedRefined,
        };
    }

    private static float SampleSignedFieldBicubic(
        IReadOnlyList<float> field,
        int width,
        int height,
        double x,
        double y)
    {
        var ix =
            (int)Math.Floor(x);
        var iy =
            (int)Math.Floor(y);
        var tx =
            (float)(x - ix);
        var ty =
            (float)(y - iy);

        Span<float> rows =
            stackalloc float[4];
        Span<float> samples =
            stackalloc float[4];

        var minimum =
            float.PositiveInfinity;
        var maximum =
            float.NegativeInfinity;

        for (var row = -1;
             row <= 2;
             row++)
        {
            var sy =
                Math.Clamp(
                    iy + row,
                    0,
                    height - 1);

            for (var column = -1;
                 column <= 2;
                 column++)
            {
                var sx =
                    Math.Clamp(
                        ix + column,
                        0,
                        width - 1);
                var value =
                    field[
                        sy *
                        width +
                        sx];

                samples[column + 1] = value;
                minimum =
                    Math.Min(
                        minimum,
                        value);
                maximum =
                    Math.Max(
                        maximum,
                        value);
            }

            rows[row + 1] =
                CubicCatmullRom(
                    samples[0],
                    samples[1],
                    samples[2],
                    samples[3],
                    tx);
        }

        var result =
            CubicCatmullRom(
                rows[0],
                rows[1],
                rows[2],
                rows[3],
                ty);

        // Catmull-Rom can overshoot around a sharp categorical corner. A signed-distance field
        // should not invent a stronger interior/exterior value than its 4x4 source neighbourhood.
        return Math.Clamp(
            result,
            minimum,
            maximum);
    }

    private static float CubicCatmullRom(
        float p0,
        float p1,
        float p2,
        float p3,
        float t)
    {
        var t2 =
            t * t;
        var t3 =
            t2 * t;

        return 0.5f *
               (2f * p1 +
                (-p0 + p2) * t +
                (2f * p0 -
                 5f * p1 +
                 4f * p2 -
                 p3) *
                t2 +
                (-p0 +
                 3f * p1 -
                 3f * p2 +
                 p3) *
                t3);
    }

    private static void ComputeExactDistance(
        IReadOnlyList<bool> mask,
        int width,
        int height,
        float[] distance,
        bool zeroWhereMaskIsTrue,
        float xStep,
        float yStep)
    {
        var total =
            checked(
                width *
                height);
        var temporary =
            new double[total];
        var maximumLength =
            Math.Max(
                width,
                height);
        var input =
            new double[maximumLength];
        var output =
            new double[maximumLength];
        var envelope =
            new int[maximumLength];
        var intersections =
            new double[
                maximumLength + 1];

        const double Huge = 1e20;

        var xWeight =
            Math.Max(
                1e-9,
                xStep *
                xStep);
        var yWeight =
            Math.Max(
                1e-9,
                yStep *
                yStep);

        // Horizontal exact squared Euclidean transform.
        for (var y = 0;
             y < height;
             y++)
        {
            var offset =
                y *
                width;

            for (var x = 0;
                 x < width;
                 x++)
            {
                var zero =
                    zeroWhereMaskIsTrue
                        ? mask[
                            offset +
                            x]
                        : !mask[
                            offset +
                            x];

                input[x] =
                    zero
                        ? 0d
                        : Huge;
            }

            DistanceTransform1D(
                input,
                output,
                width,
                xWeight,
                envelope,
                intersections,
                Huge);

            for (var x = 0;
                 x < width;
                 x++)
            {
                temporary[
                    offset +
                    x] =
                    output[x];
            }
        }

        // Vertical transform completes the separable anisotropic Euclidean distance.
        for (var x = 0;
             x < width;
             x++)
        {
            for (var y = 0;
                 y < height;
                 y++)
            {
                input[y] =
                    temporary[
                        y *
                        width +
                        x];
            }

            DistanceTransform1D(
                input,
                output,
                height,
                yWeight,
                envelope,
                intersections,
                Huge);

            for (var y = 0;
                 y < height;
                 y++)
            {
                distance[
                    y *
                    width +
                    x] =
                    output[y] >=
                    Huge * 0.5
                        ? Infinity
                        : (float)Math.Sqrt(
                            Math.Max(
                                0d,
                                output[y]));
            }
        }
    }

    /// <summary>
    /// Felzenszwalb/Huttenlocher lower-envelope transform for
    /// min_q(f[q] + weight * (p-q)^2). Infinite source sites are skipped explicitly so rows with
    /// no seed remain numerically stable and are resolved by the second dimension.
    /// </summary>
    private static void DistanceTransform1D(
        IReadOnlyList<double> input,
        double[] output,
        int length,
        double weight,
        int[] envelope,
        double[] intersections,
        double huge)
    {
        var k = -1;

        for (var q = 0;
             q < length;
             q++)
        {
            if (input[q] >=
                huge * 0.5)
            {
                continue;
            }

            if (k < 0)
            {
                k = 0;
                envelope[0] = q;
                intersections[0] =
                    double.NegativeInfinity;
                intersections[1] =
                    double.PositiveInfinity;
                continue;
            }

            double s;

            while (true)
            {
                var p =
                    envelope[k];

                s =
                    ((input[q] +
                      weight *
                      q *
                      q) -
                     (input[p] +
                      weight *
                      p *
                      p)) /
                    (2d *
                     weight *
                     (q - p));

                if (s >
                    intersections[k] ||
                    k == 0)
                {
                    break;
                }

                k--;
            }

            if (k == 0 &&
                s <=
                intersections[0])
            {
                k = -1;
                q--;
                continue;
            }

            k++;
            envelope[k] = q;
            intersections[k] = s;
            intersections[k + 1] =
                double.PositiveInfinity;
        }

        if (k < 0)
        {
            for (var q = 0;
                 q < length;
                 q++)
            {
                output[q] = huge;
            }

            return;
        }

        var active = 0;

        for (var q = 0;
             q < length;
             q++)
        {
            while (active < k &&
                   intersections[active + 1] <
                   q)
            {
                active++;
            }

            var p =
                envelope[active];
            var delta =
                q - p;

            output[q] =
                input[p] +
                weight *
                delta *
                delta;
        }
    }

    private static List<byte> UsedIndices(
        DesignDocument source)
    {
        var used =
            new bool[256];

        for (var y = 0; y < source.Height; y++)
        {
            for (var x = 0; x < source.Width; x++)
                used[source.GetPixel(x, y)] = true;
        }

        var result =
            new List<byte>();

        for (var index = 0; index < used.Length; index++)
        {
            if (used[index])
                result.Add((byte)index);
        }

        return result;
    }

    private static void PreserveExactSourceSymmetry(
        DesignDocument source,
        DesignDocument destination)
    {
        if (IsExactlySymmetric(
                source,
                leftRight: true))
        {
            for (var y = 0; y < destination.Height; y++)
            {
                for (var x = 0; x < destination.Width / 2; x++)
                {
                    destination.SetPixel(
                        destination.Width - 1 - x,
                        y,
                        destination.GetPixel(
                            x,
                            y));
                }
            }
        }

        if (IsExactlySymmetric(
                source,
                leftRight: false))
        {
            for (var y = 0; y < destination.Height / 2; y++)
            {
                var mirrorY =
                    destination.Height - 1 - y;

                for (var x = 0; x < destination.Width; x++)
                {
                    destination.SetPixel(
                        x,
                        mirrorY,
                        destination.GetPixel(
                            x,
                            y));
                }
            }
        }
    }

    private static bool IsExactlySymmetric(
        DesignDocument source,
        bool leftRight)
    {
        for (var y = 0; y < source.Height; y++)
        {
            for (var x = 0; x < source.Width; x++)
            {
                var mirrorX =
                    leftRight
                        ? source.Width - 1 - x
                        : x;
                var mirrorY =
                    leftRight
                        ? y
                        : source.Height - 1 - y;

                if (source.GetPixel(x, y) !=
                    source.GetPixel(
                        mirrorX,
                        mirrorY))
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static void Copy(
        DesignDocument source,
        DesignDocument destination)
    {
        for (var y = 0; y < source.Height; y++)
        {
            for (var x = 0; x < source.Width; x++)
            {
                destination.SetPixel(
                    x,
                    y,
                    source.GetPixel(x, y));
            }
        }
    }

    private static void CopySingleColor(
        DesignDocument destination,
        byte color)
    {
        for (var y = 0; y < destination.Height; y++)
        {
            for (var x = 0; x < destination.Width; x++)
                destination.SetPixel(x, y, color);
        }
    }
}
