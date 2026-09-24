namespace RugScale.Core.Drawing;

internal static class LeafPetalMedialAxisBuilder
{
    // A sharp 1x1 apex produces sparse projected bins; slightly wider bins preserve the real
    // base-to-apex flow while still rejecting genuinely disconnected/branched masses below.
    private const double TargetBinWidth = 1.50;
    private const double MinimumCoverage = 0.58;
    private const int MaximumEmptyBinRun = 4;

    private sealed class Bin
    {
        public int Count;
        public double SumX;
        public double SumY;
        public double MinN = double.PositiveInfinity;
        public double MaxN = double.NegativeInfinity;
    }

    public static bool TryBuild(
        LeafPetalArcCandidate candidate,
        int sourceWidth,
        out LeafPetalArcModel model)
    {
        model = null!;
        var region = candidate.Region;
        var items = new (int Pixel, double Major, double Normal)[region.Pixels.Count];
        var minMajor = double.PositiveInfinity;
        var maxMajor = double.NegativeInfinity;

        for (var i = 0; i < region.Pixels.Count; i++)
        {
            var pixel = region.Pixels[i];
            var x = pixel % sourceWidth;
            var y = pixel / sourceWidth;
            var dx = x - candidate.CenterX;
            var dy = y - candidate.CenterY;
            var major = dx * candidate.AxisX + dy * candidate.AxisY;
            var normal = dx * candidate.NormalX + dy * candidate.NormalY;

            items[i] = (pixel, major, normal);
            minMajor = Math.Min(minMajor, major);
            maxMajor = Math.Max(maxMajor, major);
        }

        var span = maxMajor - minMajor;
        if (span < 6d)
            return false;

        var binCount = Math.Clamp(
            (int)Math.Round(span / TargetBinWidth) + 1,
            8,
            192);
        var bins = Enumerable.Range(0, binCount)
            .Select(_ => new Bin())
            .ToArray();

        foreach (var item in items)
        {
            var normalized = (item.Major - minMajor) / Math.Max(1e-9, span);
            var index = Math.Clamp(
                (int)Math.Round(normalized * (binCount - 1)),
                0,
                binCount - 1);
            var bin = bins[index];
            var x = item.Pixel % sourceWidth;
            var y = item.Pixel / sourceWidth;

            bin.Count++;
            bin.SumX += x;
            bin.SumY += y;
            bin.MinN = Math.Min(bin.MinN, item.Normal);
            bin.MaxN = Math.Max(bin.MaxN, item.Normal);
        }

        var occupied = bins.Count(bin => bin.Count > 0);
        var coverage = occupied / (double)binCount;
        if (coverage < MinimumCoverage)
            return false;

        var maxGap = 0;
        var gap = 0;
        foreach (var bin in bins)
        {
            if (bin.Count == 0)
            {
                gap++;
                maxGap = Math.Max(maxGap, gap);
            }
            else
            {
                gap = 0;
            }
        }

        if (maxGap > MaximumEmptyBinRun)
            return false;

        var samples = new List<LeafPetalAxisSample>(occupied);
        for (var index = 0; index < bins.Length; index++)
        {
            var bin = bins[index];
            if (bin.Count == 0)
                continue;

            samples.Add(
                new LeafPetalAxisSample(
                    bin.SumX / bin.Count,
                    bin.SumY / bin.Count,
                    Math.Max(0.5, (bin.MaxN - bin.MinN + 1d) / 2d),
                    index / (double)Math.Max(1, bins.Length - 1)));
        }

        if (samples.Count < 7)
            return false;

        for (var i = 1; i < samples.Count; i++)
        {
            var dx = samples[i].X - samples[i - 1].X;
            var dy = samples[i].Y - samples[i - 1].Y;
            if (Math.Sqrt(dx * dx + dy * dy) > 5.50)
                return false;
        }

        var edgeWindow = Math.Clamp(samples.Count / 8, 2, 5);
        var firstWidth = samples.Take(edgeWindow).Average(s => s.HalfWidth);
        var lastWidth = samples.TakeLast(edgeWindow).Average(s => s.HalfWidth);
        var reversed = firstWidth < lastWidth;

        if (reversed)
        {
            samples.Reverse();
            var remapped = new List<LeafPetalAxisSample>(samples.Count);
            foreach (var sample in samples)
            {
                remapped.Add(sample with
                {
                    AxisPosition = 1d - sample.AxisPosition,
                });
            }
            remapped.Sort((a, b) => a.AxisPosition.CompareTo(b.AxisPosition));
            samples = remapped;
            (firstWidth, lastWidth) = (lastWidth, firstWidth);
        }

        model = new LeafPetalArcModel(
            candidate,
            samples,
            reversed,
            firstWidth,
            lastWidth,
            coverage);
        return true;
    }
}
