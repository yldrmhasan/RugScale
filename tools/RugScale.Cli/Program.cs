using RugScale.Core.Drawing;
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
