using RugScale.Core.Drawing;
using Xunit;

namespace RugScale.Core.Tests;

public sealed class CurveFillRibbonCompoundFitSelectorTests
{
    [Fact]
    public void PreferSmootherSafeAlternative_SelectsMeasuredC069GreenSpiralAlternative()
    {
        Assert.True(
            CurveFillRibbonCompoundFitSelector.PreferSmootherSafeAlternative(
                selectedRoughness: 0.190283,
                selectedP95Deviation: 0.249430,
                selectedMaximumDeviation: 0.338908,
                selectedAnchors: 20,
                alternativeRoughness: 0.168047,
                alternativeP95Deviation: 0.370962,
                alternativeMaximumDeviation: 0.923372,
                alternativeAnchors: 18));
    }

    [Fact]
    public void PreferSmootherSafeAlternative_SelectsMeasuredC069LowerGreenAlternative()
    {
        Assert.True(
            CurveFillRibbonCompoundFitSelector.PreferSmootherSafeAlternative(
                selectedRoughness: 0.184167,
                selectedP95Deviation: 0.262109,
                selectedMaximumDeviation: 0.448120,
                selectedAnchors: 20,
                alternativeRoughness: 0.173056,
                alternativeP95Deviation: 0.524957,
                alternativeMaximumDeviation: 1.339880,
                alternativeAnchors: 16));
    }

    [Fact]
    public void PreferSmootherSafeAlternative_RejectsLargeShapeDriftDespiteBigRoughnessGain()
    {
        Assert.False(
            CurveFillRibbonCompoundFitSelector.PreferSmootherSafeAlternative(
                selectedRoughness: 0.121662,
                selectedP95Deviation: 0.815375,
                selectedMaximumDeviation: 1.892860,
                selectedAnchors: 20,
                alternativeRoughness: 0.064239,
                alternativeP95Deviation: 2.368301,
                alternativeMaximumDeviation: 3.281055,
                alternativeAnchors: 10));
    }

    [Fact]
    public void PreferSmootherSafeAlternative_RejectsTinyAestheticGain()
    {
        Assert.False(
            CurveFillRibbonCompoundFitSelector.PreferSmootherSafeAlternative(
                selectedRoughness: 0.234319,
                selectedP95Deviation: 1.681350,
                selectedMaximumDeviation: 3.056985,
                selectedAnchors: 20,
                alternativeRoughness: 0.227841,
                alternativeP95Deviation: 1.836153,
                alternativeMaximumDeviation: 2.883937,
                alternativeAnchors: 18));
    }
}
