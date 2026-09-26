namespace RugScale.Core.Drawing;

/// <summary>
/// Chooses whether a low-control geometric Through-Points fit is a better designer redraw than an
/// already-safe source-faithful Curve-tool fit.
///
/// The alternative must buy a visible reduction in curvature roughness without paying for it with
/// meaningful source-shape drift. This is intentionally conservative: the selector is used only
/// after both candidates have independently passed their own geometry safety checks.
/// </summary>
internal static class CurveFillRibbonFitSelector
{
    private const double RequiredRoughnessRatio = 0.75;
    private const double MaximumDeviationRegression = 0.35;
    private const double MaximumAlternativeDeviation = 1.65;

    public static bool PreferGeometricThroughPoints(
        ElegantArcFit sourceFaithful,
        double sourceFaithfulRoughness,
        ElegantArcFit geometric,
        double geometricRoughness)
    {
        ArgumentNullException.ThrowIfNull(sourceFaithful);
        ArgumentNullException.ThrowIfNull(geometric);

        if (!sourceFaithful.IsSafe ||
            !geometric.IsSafe ||
            !double.IsFinite(
                sourceFaithfulRoughness) ||
            !double.IsFinite(
                geometricRoughness) ||
            sourceFaithfulRoughness <= 1e-9)
        {
            return false;
        }

        if (geometric.CurvatureSignFlips >
            sourceFaithful.CurvatureSignFlips)
        {
            return false;
        }

        if (geometricRoughness >
            sourceFaithfulRoughness *
            RequiredRoughnessRatio)
        {
            return false;
        }

        if (geometric.MaximumCenterlineDeviation >
            MaximumAlternativeDeviation)
        {
            return false;
        }

        return geometric.MaximumCenterlineDeviation <=
               sourceFaithful.MaximumCenterlineDeviation +
               MaximumDeviationRegression;
    }
}
