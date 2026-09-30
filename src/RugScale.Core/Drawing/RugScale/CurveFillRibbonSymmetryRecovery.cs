namespace RugScale.Core.Drawing;

/// <summary>
/// Recovers the visual centreline of a broad branched ribbon from exact/near-exact source symmetry.
///
/// A connected carpet ornament can contain a smooth symmetric arch plus small same-colour shoulder
/// branches. Zhang-Suen preserves that topology correctly, but endpoint/path tie-breaking can make
/// the selected principal spine asymmetric even when the SOURCE region is perfectly mirrored.
/// Fitting that asymmetric digital path is what creates false curvature flips.
///
/// This helper is deliberately narrow: it only acts when the complete indexed source region has a
/// very strong LR/TB mirror match, the two principal-path endpoints mirror each other, and the
/// required correction stays inside a strict per-sample source corridor.
/// </summary>
internal static class CurveFillRibbonSymmetryRecovery
{
    private const double MinimumMirrorAgreement = 0.965;
    private const double MaximumEndpointMirrorError = 3.25;
    private const double MaximumSampleShift = 4.00;

    public static bool TryRecover(
        LeafPetalArcModel model,
        int sourceWidth,
        out LeafPetalArcModel recovered,
        out RibbonSymmetryRecoveryDiagnostics diagnostics)
    {
        ArgumentNullException.ThrowIfNull(model);

        recovered = model;
        diagnostics = default;

        var samples =
            model.Samples;
        var region =
            model.Candidate.Region;

        if (samples.Count < 8 ||
            region.Pixels.Count < 16 ||
            sourceWidth <= 0)
        {
            diagnostics =
                new RibbonSymmetryRecoveryDiagnostics(
                    "insufficient-evidence",
                    "none",
                    0d,
                    double.PositiveInfinity,
                    0d,
                    0d);
            return false;
        }

        var pixels =
            region.Pixels.ToHashSet();
        var lrAgreement =
            MirrorAgreement(
                region,
                pixels,
                sourceWidth,
                leftRight: true);
        var tbAgreement =
            MirrorAgreement(
                region,
                pixels,
                sourceWidth,
                leftRight: false);

        var useLeftRight =
            lrAgreement >=
                tbAgreement;
        var agreement =
            useLeftRight
                ? lrAgreement
                : tbAgreement;

        if (agreement <
            MinimumMirrorAgreement)
        {
            diagnostics =
                new RibbonSymmetryRecoveryDiagnostics(
                    "mirror-agreement",
                    useLeftRight
                        ? "LR"
                        : "TB",
                    agreement,
                    double.PositiveInfinity,
                    0d,
                    0d);
            return false;
        }

        var first =
            samples[0];
        var last =
            samples[^1];
        var mirroredLast =
            Mirror(
                last.X,
                last.Y,
                region,
                useLeftRight);
        var endpointError =
            Distance(
                first.X,
                first.Y,
                mirroredLast.X,
                mirroredLast.Y);

        if (endpointError >
            MaximumEndpointMirrorError)
        {
            diagnostics =
                new RibbonSymmetryRecoveryDiagnostics(
                    "endpoint-mirror",
                    useLeftRight
                        ? "LR"
                        : "TB",
                    agreement,
                    endpointError,
                    0d,
                    0d);
            return false;
        }

        var symmetric =
            new LeafPetalAxisSample[
                samples.Count];
        var shiftSum = 0d;
        var maximumShift = 0d;

        for (var index = 0;
             index < samples.Count;
             index++)
        {
            var current =
                samples[index];
            var opposite =
                samples[
                    samples.Count -
                    1 -
                    index];
            var mirroredOpposite =
                Mirror(
                    opposite.X,
                    opposite.Y,
                    region,
                    useLeftRight);
            var x =
                (current.X +
                 mirroredOpposite.X) *
                0.5;
            var y =
                (current.Y +
                 mirroredOpposite.Y) *
                0.5;
            var halfWidth =
                Math.Max(
                    0.5,
                    (current.HalfWidth +
                     opposite.HalfWidth) *
                    0.5);
            var shift =
                Distance(
                    current.X,
                    current.Y,
                    x,
                    y);

            maximumShift =
                Math.Max(
                    maximumShift,
                    shift);
            shiftSum +=
                shift;

            symmetric[index] =
                current with
                {
                    X = x,
                    Y = y,
                    HalfWidth = halfWidth,
                };
        }

        var meanShift =
            shiftSum /
            Math.Max(
                1,
                samples.Count);

        if (maximumShift >
            MaximumSampleShift)
        {
            diagnostics =
                new RibbonSymmetryRecoveryDiagnostics(
                    "sample-shift",
                    useLeftRight
                        ? "LR"
                        : "TB",
                    agreement,
                    endpointError,
                    meanShift,
                    maximumShift);
            return false;
        }

        var terminalWidth =
            Math.Max(
                0.5,
                (symmetric[0].HalfWidth +
                 symmetric[^1].HalfWidth) *
                0.5);

        recovered =
            new LeafPetalArcModel(
                model.Candidate,
                symmetric,
                ReversedForApex: false,
                BaseWidth: terminalWidth,
                ApexWidth: terminalWidth,
                SkeletonCoverage:
                    model.SkeletonCoverage);

        diagnostics =
            new RibbonSymmetryRecoveryDiagnostics(
                "ok",
                useLeftRight
                    ? "LR"
                    : "TB",
                agreement,
                endpointError,
                meanShift,
                maximumShift);

        return true;
    }

    private static double MirrorAgreement(
        LeafPetalRegion region,
        IReadOnlySet<int> pixels,
        int sourceWidth,
        bool leftRight)
    {
        var matched = 0;

        foreach (var pixel in region.Pixels)
        {
            var x =
                pixel %
                sourceWidth;
            var y =
                pixel /
                sourceWidth;
            var mirrorX =
                leftRight
                    ? region.MinX +
                      region.MaxX -
                      x
                    : x;
            var mirrorY =
                leftRight
                    ? y
                    : region.MinY +
                      region.MaxY -
                      y;
            var mirror =
                mirrorY *
                    sourceWidth +
                mirrorX;

            if (pixels.Contains(
                    mirror))
            {
                matched++;
            }
        }

        return matched /
               (double)Math.Max(
                   1,
                   region.Pixels.Count);
    }

    private static (double X, double Y) Mirror(
        double x,
        double y,
        LeafPetalRegion region,
        bool leftRight) =>
        leftRight
            ? (
                region.MinX +
                region.MaxX -
                x,
                y
            )
            : (
                x,
                region.MinY +
                region.MaxY -
                y
            );

    private static double Distance(
        double ax,
        double ay,
        double bx,
        double by)
    {
        var dx =
            ax -
            bx;
        var dy =
            ay -
            by;

        return Math.Sqrt(
            dx *
                dx +
            dy *
                dy);
    }
}

internal readonly record struct RibbonSymmetryRecoveryDiagnostics(
    string Reason,
    string Axis,
    double MirrorAgreement,
    double EndpointMirrorError,
    double MeanSampleShift,
    double MaximumSampleShift);
