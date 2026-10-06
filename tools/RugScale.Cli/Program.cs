using RugScale.Core.Drawing;
using RugScale.Core.Drawing.WallToWall;
using RugScale.Core.IO;

static int Usage(
    string? error = null)
{
    if (!string.IsNullOrWhiteSpace(error))
        Console.Error.WriteLine(error);

    Console.WriteLine(
        """
        RugScale CLI

        Usage:
          dotnet run --project tools/RugScale.Cli --             --input source.bmp             --output target.bmp             --width 800             --height 1320             --mode leaf-petal             [--source-warp 40] [--source-weft 50]             [--target-warp 40] [--target-weft 50]

        Modes:
          nearest
          edge-smooth
          dominant
          preserve-detail
          motif
          curve-fill
          curve          (neutral-first curve redraw)
          texture        (abstract / distressed designs: grain kept 1:1)
          wall-to-wall   (roll designs: rapport repeated, not scaled)
                         [--repeat both|width|length] [--rapport x,y,w,h] [--drop px]
                         [--seamless false] [--seam-band px]
                         [--expand WxH] [--expanded-output rapport.bmp]   (rapor açma)
          leaf-petal
          smooth
          area-average
        """);

    return string.IsNullOrWhiteSpace(error)
        ? 0
        : 2;
}

static Dictionary<string, string> ParseArgs(
    string[] values)
{
    var result =
        new Dictionary<string, string>(
            StringComparer.OrdinalIgnoreCase);

    for (var i = 0;
         i < values.Length;
         i++)
    {
        var token =
            values[i];

        if (!token.StartsWith("--"))
            continue;

        if (string.Equals(
                token,
                "--help",
                StringComparison.OrdinalIgnoreCase))
        {
            result["help"] =
                "true";
            continue;
        }

        if (i + 1 >=
            values.Length)
        {
            throw new ArgumentException(
                $"Missing value for {token}.");
        }

        result[
            token[2..]] =
            values[++i];
    }

    return result;
}

static int PositiveInt(
    IReadOnlyDictionary<string, string> args,
    string name,
    int? fallback = null)
{
    if (!args.TryGetValue(
            name,
            out var raw))
    {
        if (fallback.HasValue)
            return fallback.Value;

        throw new ArgumentException(
            $"Missing --{name}.");
    }

    if (!int.TryParse(
            raw,
            out var value) ||
        value <= 0)
    {
        throw new ArgumentException(
            $"--{name} must be a positive integer.");
    }

    return value;
}

static ScaleMode ParseMode(
    string raw) =>
    raw.Trim()
        .ToLowerInvariant() switch
    {
        "nearest" or
        "nearest-neighbor" =>
            ScaleMode.NearestNeighbor,

        "edge-smooth" =>
            ScaleMode.EdgeSmooth,

        "dominant" =>
            ScaleMode.Dominant,

        "preserve-detail" =>
            ScaleMode.PreserveDetail,

        "motif" or
        "rugscale" =>
            ScaleMode.RugScale,

        "curve-fill" =>
            ScaleMode.CurveFill,

        "curve" or
        "curve-neutral" =>
            ScaleMode.CurveNeutral,

        "texture" =>
            ScaleMode.Texture,

        "wall-to-wall" or
        "w2w" or
        "roll" =>
            ScaleMode.WallToWall,

        "leaf-petal" or
        "leaf-petal-arcs" =>
            ScaleMode.LeafPetalArcs,

        "smooth" =>
            ScaleMode.Smooth,

        "area-average" =>
            ScaleMode.AreaAverage,

        _ =>
            throw new ArgumentException(
                $"Unknown mode '{raw}'."),
    };

try
{
    var options =
        ParseArgs(args);

    if (options.ContainsKey("help"))
        return Usage();

    if (!options.TryGetValue(
            "input",
            out var input) ||
        string.IsNullOrWhiteSpace(
            input))
    {
        return Usage(
            "Missing --input.");
    }

    if (!options.TryGetValue(
            "output",
            out var output) ||
        string.IsNullOrWhiteSpace(
            output))
    {
        return Usage(
            "Missing --output.");
    }

    var width =
        PositiveInt(
            options,
            "width");
    var height =
        PositiveInt(
            options,
            "height");

    if (!options.TryGetValue(
            "mode",
            out var modeRaw))
    {
        return Usage(
            "Missing --mode.");
    }

    var mode =
        ParseMode(
            modeRaw);
    var sourceWarp =
        PositiveInt(
            options,
            "source-warp",
            1);
    var sourceWeft =
        PositiveInt(
            options,
            "source-weft",
            1);
    var targetWarp =
        PositiveInt(
            options,
            "target-warp",
            sourceWarp);
    var targetWeft =
        PositiveInt(
            options,
            "target-weft",
            sourceWeft);

    var loaded =
        IndexedBmpCodec.Read(
            input);

    if (mode == ScaleMode.WallToWall)
    {
        var detection =
            RapportDetector.Detect(
                loaded.Document);
        var tile =
            detection.Tile;

        if (options.TryGetValue("rapport", out var rapportRaw))
        {
            var parts =
                rapportRaw
                    .Split(',')
                    .Select(int.Parse)
                    .ToArray();
            tile = new RapportTile(parts[0], parts[1], parts[2], parts[3], tile.Drop);
        }

        if (options.TryGetValue("drop", out var dropRaw))
            tile = tile with { Drop = int.Parse(dropRaw) };

        var direction =
            (options.TryGetValue("repeat", out var repeatRaw) ? repeatRaw : "both").ToLowerInvariant() switch
            {
                "width" or "en" or "enden" => RapportDirection.Width,
                "length" or "boy" or "boydan" => RapportDirection.Length,
                _ => RapportDirection.Both,
            };
        var seamless =
            !options.TryGetValue("seamless", out var seamlessRaw) ||
            !string.Equals(seamlessRaw, "false", StringComparison.OrdinalIgnoreCase);
        var repeatSource =
            loaded.Document;
        EdgeMarkers? originalMarkers = null;

        // Rapor açma: grow the rapport with the design's own texture, then repeat the grown one.
        if (options.TryGetValue("expand", out var expandRaw))
        {
            var size =
                expandRaw
                    .ToLowerInvariant()
                    .Split('x')
                    .Select(int.Parse)
                    .ToArray();
            var markers =
                RapportDetector.FindEdgeMarkers(loaded.Document);
            repeatSource =
                RapportExpander.Expand(
                    loaded.Document,
                    tile,
                    size[0],
                    size[1],
                    options.TryGetValue("seed", out var seedRaw) ? int.Parse(seedRaw) : 1,
                    options.TryGetValue("expand-block", out var blockRaw) ? int.Parse(blockRaw) : 0);
            Console.WriteLine(
                $"Rapport opened: {tile.Width}x{tile.Height} -> {size[0]}x{size[1]}");

            if (options.TryGetValue("expanded-output", out var expandedPath))
            {
                IndexedBmpCodec.Write(
                    expandedPath,
                    repeatSource,
                    loaded.XPixelsPerMeter,
                    loaded.YPixelsPerMeter);
            }

            tile =
                new RapportTile(0, 0, size[0], size[1], tile.Drop);
            originalMarkers =
                markers;

            // The opened rapport is already built to wrap around: no further join.
            seamless = false;
        }

        var rendered =
            WallToWallRepeat.Render(
                repeatSource,
                tile,
                width,
                height,
                new RapportOptions(
                    direction,
                    seamless,
                    options.TryGetValue("seam-band", out var bandRaw) ? int.Parse(bandRaw) : new RapportOptions().SeamBand,
                    Markers: originalMarkers));

        Console.WriteLine(
            $"Rapport detected: {detection.Tile.Width}x{detection.Tile.Height} @ {detection.Tile.X},{detection.Tile.Y}, drop {detection.Tile.Drop} " +
            $"(across {(detection.HorizontalFound ? "repeat" : "whole width")} {detection.HorizontalScore:P1}, " +
            $"along {(detection.VerticalFound ? "repeat" : "whole height")} {detection.VerticalScore:P1}; " +
            $"markers {(detection.Markers is null ? "none" : $"{detection.Markers.Left}+{detection.Markers.Right}")})");
        Console.WriteLine(
            $"Rapport used: {tile.Width}x{tile.Height} @ {tile.X},{tile.Y}, drop {tile.Drop}, repeat {direction}, seamless {seamless}; " +
            $"period {rendered.PeriodWidth}x{rendered.PeriodHeight}; seam across {rendered.SeamAcross:F2}, along {rendered.SeamAlong:F2}" +
            (rendered.SeamAcross > WallToWallRepeat.VisibleSeam ? " (repeat across leaves a visible join)" : "") +
            (rendered.SeamAlong > WallToWallRepeat.VisibleSeam ? " (repeat along leaves a visible join)" : ""));

        IndexedBmpCodec.Write(
            output,
            rendered.Design,
            loaded.XPixelsPerMeter,
            loaded.YPixelsPerMeter);
        Console.WriteLine($"Output: {output}");
        return 0;
    }

    var resized =
        DesignResizer.Scale(
            loaded.Document,
            width,
            height,
            mode,
            sourceWarp,
            sourceWeft,
            targetWarp,
            targetWeft);

    IndexedBmpCodec.Write(
        output,
        resized,
        loaded.XPixelsPerMeter,
        loaded.YPixelsPerMeter);

    Console.WriteLine(
        $"RugScale complete: {loaded.Document.Width}x{loaded.Document.Height} -> {width}x{height}");
    Console.WriteLine(
        $"Mode: {mode}");
    Console.WriteLine(
        $"Quality: {sourceWarp}x{sourceWeft} -> {targetWarp}x{targetWeft}");
    Console.WriteLine(
        $"Output: {Path.GetFullPath(output)}");

    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine(
        $"RugScale failed: {ex.Message}");
    return 1;
}
