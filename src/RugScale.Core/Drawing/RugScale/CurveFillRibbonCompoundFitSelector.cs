namespace RugScale.Core.Drawing;

/// <summary>
/// Chooses a smoother already-safe compound ribbon candidate without allowing aesthetic smoothing
/// to buy large source-shape drift.
///
/// Compound fitting can produce a 20-anchor candidate that hugs residual skeleton staircase and a
/// slightly lower-anchor candidate that is visibly smoother while both remain inside the hard
/// source corridor. This selector gives the smoother alternative authority only when the
/// roughness gain is meaningful and the extra source deviation is tightly bounded.
/// </summary>
internal static class CurveFillRibbonCompoundFitSelector
{
    private const double RequiredRoughnessRatio = 0.95;
    private const double MaximumP95Regression = 0.65;
    private const double MaximumDeviationRegression = 1.00;

    public static bool PreferSmootherSafeAlternative(
        double selectedRoughness,
        double selectedP95Deviation,
        double selectedMaximumDeviation,
        int selectedAnchors,
        double alternativeRoughness,
        double alternativeP95Deviation,
        double alternativeMaximumDeviation,
        int alternativeAnchors)
    {
        if (!double.IsFinite(
                selectedRoughness) ||
            !double.IsFinite(
                alternativeRoughness) ||
            selectedRoughness <= 1e-9 ||
            alternativeRoughness < 0d)
        {
            return false;
        }

        if (alternativeAnchors >
            selectedAnchors)
        {
            return false;
        }

        if (alternativeRoughness >
            selectedRoughness *
            RequiredRoughnessRatio)
        {
            return false;
        }

        if (alternativeP95Deviation >
            selectedP95Deviation +
            MaximumP95Regression)
        {
            return false;
        }

        if (alternativeMaximumDeviation >
            selectedMaximumDeviation +
            MaximumDeviationRegression)
        {
            return false;
        }

        return true;
    }
}
