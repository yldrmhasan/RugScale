using RugScale.Core.Drawing;
using Xunit;

namespace RugScale.Core.Tests;

public sealed class CurveFillRibbonRasterScopeTests
{
    [Fact]
    public void Contains_MainSweepAllowsInteriorButProtectsTrimmedHookAndJoin()
    {
        var points =
            Enumerable.Range(
                    0,
                    101)
                .Select(index =>
                {
                    var t =
                        index /
                        100d;
                    var x =
                        20d +
                        80d *
                            t;
                    var y =
                        70d -
                        28d *
                            Math.Sin(
                                Math.PI *
                                t);

                    return new ElegantArcPoint(
                        x,
                        y,
                        3.0);
                })
                .ToArray();
        var fit =
            new ElegantArcFit(
                points,
                true,
                true,
                0,
                0.5);

        Assert.True(
            CurveFillRibbonRasterScope.Contains(
                fit,
                60d,
                42d,
                extraMargin: 2.0),
            "Interior pixels around the fitted designer sweep must remain editable.");

        Assert.False(
            CurveFillRibbonRasterScope.Contains(
                fit,
                15d,
                61d,
                extraMargin: 2.0),
            "A decorative hook outside the fitted sweep corridor must stay on the categorical baseline.");

        Assert.False(
            CurveFillRibbonRasterScope.Contains(
                fit,
                20.5,
                69.5,
                extraMargin: 2.0),
            "The short terminal join is intentionally protected so an untouched hook can reconnect cleanly.");

        Assert.False(
            CurveFillRibbonRasterScope.Contains(
                fit,
                99.5,
                69.5,
                extraMargin: 2.0),
            "Both extracted sweep terminals need the same transition protection.");
    }
}
