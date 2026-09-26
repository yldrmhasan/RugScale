using RugScale.Core.Drawing;
using Xunit;

namespace RugScale.Core.Tests;

public sealed class CurveFillRibbonFitSelectorTests
{
    [Fact]
    public void PreferGeometricThroughPoints_SelectsSmootherGreenArchWhenSourceFitDoesNotDrift()
    {
        var source =
            SafeFit(
                deviation: 0.528108);
        var geometric =
            SafeFit(
                deviation: 0.503563);

        Assert.True(
            CurveFillRibbonFitSelector.PreferGeometricThroughPoints(
                source,
                sourceFaithfulRoughness: 0.042562,
                geometric,
                geometricRoughness: 0.010735));
    }

    [Fact]
    public void PreferGeometricThroughPoints_SelectsSmootherGoldArchAtComparableDeviation()
    {
        var source =
            SafeFit(
                deviation: 0.866005);
        var geometric =
            SafeFit(
                deviation: 0.847293);

        Assert.True(
            CurveFillRibbonFitSelector.PreferGeometricThroughPoints(
                source,
                sourceFaithfulRoughness: 0.037226,
                geometric,
                geometricRoughness: 0.020270));
    }

    [Fact]
    public void PreferGeometricThroughPoints_RejectsSmootherFitWhenShapeDriftsTooFar()
    {
        var source =
            SafeFit(
                deviation: 1.264293);
        var geometric =
            SafeFit(
                deviation: 2.344257);

        Assert.False(
            CurveFillRibbonFitSelector.PreferGeometricThroughPoints(
                source,
                sourceFaithfulRoughness: 0.053394,
                geometric,
                geometricRoughness: 0.022988));
    }

    private static ElegantArcFit SafeFit(
        double deviation) =>
        new(
            new[]
            {
                new ElegantArcPoint(0d, 0d, 3d),
                new ElegantArcPoint(1d, 1d, 3d),
                new ElegantArcPoint(2d, 1.5d, 3d),
                new ElegantArcPoint(3d, 1d, 3d),
                new ElegantArcPoint(4d, 0d, 3d),
            },
            IsSafe: true,
            IsMonotonic: true,
            CurvatureSignFlips: 0,
            MaximumCenterlineDeviation: deviation);
}
