using RugScale.Core.Drawing;
using RugScale.Core.Models;
using Xunit;

namespace RugScale.Core.Tests;

public sealed class CurveFillRibbonFidelityGuardTests
{
    // 0 background, 1 white Pixel-Cord, 2 cyan fill, 3 navy band.
    private static Palette Palette() =>
        new(
            new[]
            {
                new RugColor(230, 224, 218),
                new RugColor(255, 255, 255),
                new RugColor(44, 128, 166),
                new RugColor(0, 0, 102),
            });

    private static DesignDocument Fill(
        int width,
        int height,
        byte color)
    {
        var document =
            new DesignDocument(
                width,
                height,
                Palette());

        for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
                document.SetPixel(x, y, color);

        return document;
    }

    private static DesignDocument Clone(
        DesignDocument source)
    {
        var copy =
            new DesignDocument(
                source.Width,
                source.Height,
                source.Palette);

        for (var y = 0; y < source.Height; y++)
            for (var x = 0; x < source.Width; x++)
                copy.SetPixel(x, y, source.GetPixel(x, y));

        return copy;
    }

    /// <summary>Source: cyan rows 0..4, white row 5, navy rows 6..10. Cyan never touches navy.</summary>
    private static DesignDocument LayeredSource()
    {
        var source =
            Fill(
                24,
                11,
                0);

        for (var x = 0; x < source.Width; x++)
        {
            for (var y = 0; y <= 4; y++)
                source.SetPixel(x, y, 2);

            source.SetPixel(x, 5, 1);

            for (var y = 6; y <= 10; y++)
                source.SetPixel(x, y, 3);
        }

        return source;
    }

    [Fact]
    public void Barriers_RedrawSeparatorWhereStageMadeSourceSeparatedColoursTouch()
    {
        var source =
            LayeredSource();
        var destination =
            Clone(
                source);
        var baseline =
            CurveFillRibbonFidelityGuard.Snapshot(
                destination);

        // A redraw stage pushed cyan through the white cord for three columns.
        for (var x = 10; x <= 12; x++)
            destination.SetPixel(x, 5, 2);

        var report =
            CurveFillRibbonFidelityGuard.EnforceSeparatorBarriers(
                source,
                baseline,
                destination,
                new HashSet<byte> { 1 });

        Assert.True(
            report.BreachesFound > 0);

        for (var x = 0; x < destination.Width; x++)
        {
            for (var y = 0; y + 1 < destination.Height; y++)
            {
                var a = destination.GetPixel(x, y);
                var b = destination.GetPixel(x, y + 1);
                Assert.False(
                    (a == 2 && b == 3) || (a == 3 && b == 2),
                    $"cyan touches navy at ({x},{y})");
            }
        }
    }

    [Fact]
    public void Barriers_LeaveStageUntouchedWhenItCreatedNoBreach()
    {
        var source =
            LayeredSource();
        var destination =
            Clone(
                source);
        var baseline =
            CurveFillRibbonFidelityGuard.Snapshot(
                destination);

        // Legal stage edit: the cord thickens by one row into the navy band. The source ordering
        // cyan | white | navy is still intact, so the guard must not touch anything.
        for (var x = 4; x <= 8; x++)
            destination.SetPixel(x, 6, 1);

        var before =
            CurveFillRibbonFidelityGuard.Snapshot(
                destination);
        var report =
            CurveFillRibbonFidelityGuard.EnforceSeparatorBarriers(
                source,
                baseline,
                destination,
                new HashSet<byte> { 1 });

        Assert.Equal(
            0,
            report.BreachesFound);
        Assert.Equal(
            before,
            CurveFillRibbonFidelityGuard.Snapshot(
                destination));
    }

    [Fact]
    public void Appendages_RestoreDeepCompactToothButNotShallowSmoothing()
    {
        // Source: a cyan ribbon (rows 10..15) with a cyan tooth (cols 20..23, rows 2..9)
        // outlined in white, on background.
        var source =
            Fill(
                48,
                24,
                0);

        for (var x = 2; x < 46; x++)
            for (var y = 10; y <= 15; y++)
                source.SetPixel(x, y, 2);

        for (var x = 20; x <= 23; x++)
            for (var y = 2; y <= 9; y++)
                source.SetPixel(x, y, 2);

        var baseline =
            CurveFillRibbonFidelityGuard.Snapshot(
                source);
        var destination =
            Clone(
                source);

        // A ribbon stage erased the whole tooth (to white) and also shaved one shallow row of
        // the ribbon top elsewhere (genuine smoothing).
        for (var x = 20; x <= 23; x++)
            for (var y = 2; y <= 9; y++)
                destination.SetPixel(x, y, 1);

        for (var x = 30; x <= 40; x++)
            destination.SetPixel(x, 10, 0);

        var report =
            CurveFillRibbonFidelityGuard.RestoreLostAppendages(
                source,
                baseline,
                destination,
                new HashSet<byte> { 1 });

        Assert.Equal(
            1,
            report.Appendages);

        // Tooth tip is cyan again.
        Assert.Equal(
            2,
            destination.GetPixel(21, 3));
        Assert.Equal(
            2,
            destination.GetPixel(22, 4));

        // Shallow smoothing stays as the stage decided.
        for (var x = 30; x <= 40; x++)
            Assert.Equal(0, destination.GetPixel(x, 10));
    }

    [Fact]
    public void Cords_BridgeStageGapAndRemoveStrandedDust()
    {
        // Source: a long white cord on background (row 8) separating background from background,
        // so the barrier rule alone cannot see a gap.
        var source =
            Fill(
                40,
                16,
                0);

        for (var x = 1; x < 39; x++)
            source.SetPixel(x, 8, 1);

        var baseline =
            CurveFillRibbonFidelityGuard.Snapshot(
                source);
        var destination =
            Clone(
                source);

        // The stage cut the cord for four cells and left a stray white speck elsewhere.
        for (var x = 18; x <= 21; x++)
            destination.SetPixel(x, 8, 0);

        destination.SetPixel(10, 3, 1);

        var report =
            CurveFillRibbonFidelityGuard.RepairCordContinuity(
                source,
                baseline,
                destination,
                new HashSet<byte> { 1 });

        Assert.True(
            report.BridgesRestored >= 1);

        for (var x = 1; x < 39; x++)
            Assert.Equal(1, destination.GetPixel(x, 8));

        Assert.Equal(
            0,
            destination.GetPixel(10, 3));
    }
    [Fact]
    public void Barriers_UseLocalSourceEvidenceForFillContacts()
    {
        // Navy (3) touches the field (0) only at the far left of the source (x < 3); everywhere
        // else a white cord (1) separates them. A stage that drops the cord on the right must be
        // repaired even though navy|field is legal somewhere in the design.
        var source =
            Fill(
                40,
                12,
                0);

        for (var x = 0; x < 40; x++)
        {
            for (var y = 0; y <= 4; y++)
                source.SetPixel(x, y, 3);

            source.SetPixel(x, 5, x < 3 ? (byte)0 : (byte)1);
        }

        var destination =
            Clone(
                source);
        var baseline =
            CurveFillRibbonFidelityGuard.Snapshot(
                destination);

        for (var x = 25; x <= 30; x++)
            destination.SetPixel(x, 5, 0);

        CurveFillRibbonFidelityGuard.EnforceSeparatorBarriers(
            source,
            baseline,
            destination,
            new HashSet<byte> { 1 });

        for (var x = 25; x <= 30; x++)
        {
            Assert.False(
                destination.GetPixel(x, 4) == 3 &&
                destination.GetPixel(x, 5) == 0,
                $"navy touches the field at x={x}");
        }
    }

    [Fact]
    public void CordNotches_CloseEnclosedFillCellsButKeepSourceDetails()
    {
        var source =
            Fill(
                30,
                30,
                0);

        // A straight cord plus, far away, a genuine one-cell navy dot enclosed by cord.
        for (var x = 0; x < 30; x++)
            source.SetPixel(x, 10, 1);

        source.SetPixel(20, 20, 3);
        source.SetPixel(19, 20, 1);
        source.SetPixel(21, 20, 1);
        source.SetPixel(20, 19, 1);
        source.SetPixel(20, 21, 1);

        var destination =
            Clone(
                source);

        // Comb defect: a background hole punched into the cord with cord on three sides.
        destination.SetPixel(5, 10, 0);
        destination.SetPixel(5, 11, 1);

        var closed =
            CurveFillRibbonFidelityGuard.CloseCordNotches(
                source,
                destination,
                new HashSet<byte> { 1 });

        Assert.True(
            closed >= 1);
        Assert.Equal(
            1,
            destination.GetPixel(5, 10));
        Assert.Equal(
            3,
            destination.GetPixel(20, 20));
    }
}
