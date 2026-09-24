using System.IO.Compression;
using System.Text;
using RugScale.Core.Drawing;

namespace RugScale.Core.Services;

/// <summary>
/// Persistent + portable RugScale motif knowledge.
///
/// Runtime copy lives in %AppData%\RugScale\rugscale-motif-memory.json. The same JSON can be
/// exported/imported so learned motif families remain independent from any desktop host.
/// </summary>
public static class MotifMemoryStore
{
    private static readonly string Folder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "RugScale");

    public static readonly string StorePath = Path.Combine(
        Folder,
        "rugscale-motif-memory.json");

    private const string SeedResourceName =
        "RugScale.Core.Assets.rugscale-motif-memory.seed.gz.b64";

    private static MotifMemoryFile _memory = LoadInternal();

    public static MotifMemoryFile Memory => _memory;

    public static event EventHandler? Changed;

    public static void Save()
    {
        Directory.CreateDirectory(Folder);
        File.WriteAllText(
            StorePath,
            MotifMemorySerializer.Save(_memory));
        Changed?.Invoke(
            null,
            EventArgs.Empty);
    }

    public static void Replace(
        MotifMemoryFile memory)
    {
        _memory =
            memory ??
            new MotifMemoryFile();
        Save();
    }

    public static void Import(
        string path,
        bool merge)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var incoming =
            MotifMemorySerializer.Load(
                File.ReadAllText(path));

        if (!merge)
        {
            Replace(incoming);
            return;
        }

        MergeInto(
            _memory,
            incoming);
        Save();
    }

    public static void Export(
        string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        File.WriteAllText(
            path,
            MotifMemorySerializer.Save(_memory));
    }

    public static void MergeInto(
        MotifMemoryFile target,
        MotifMemoryFile incoming)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(incoming);

        foreach (var incomingFamily in incoming.Families)
        {
            var descriptor =
                new MotifDescriptor
                {
                    Roles =
                        incomingFamily.CanonicalRoles,
                    Edges =
                        incomingFamily.CanonicalEdges,
                    RoleCount =
                        incomingFamily.RoleCount,
                    PhysicalAspect =
                        incomingFamily.PhysicalAspect,
                    CanonicalTransform =
                        MotifTransform.Identity,
                };

            var existing =
                MotifMemorySerializer.FindBestFamily(
                    target,
                    descriptor,
                    0.94,
                    out _);

            if (existing is null)
            {
                target.Families.Add(
                    incomingFamily);
                continue;
            }

            // Importing the same memory/seed repeatedly must be idempotent. Summing counters here
            // would make confidence grow merely because the application restarted.
            existing.PositiveFeedback =
                Math.Max(
                    existing.PositiveFeedback,
                    incomingFamily.PositiveFeedback);
            existing.NegativeFeedback =
                Math.Max(
                    existing.NegativeFeedback,
                    incomingFamily.NegativeFeedback);

            MergeStyleFeedback(
                existing.RepairStylePositive,
                incomingFamily.RepairStylePositive);
            MergeStyleFeedback(
                existing.RepairStyleNegative,
                incomingFamily.RepairStyleNegative);

            foreach (var sample in incomingFamily.Samples)
            {
                var duplicate =
                    existing.Samples.Any(current =>
                        current.SourceTag ==
                        sample.SourceTag &&
                        current.SourceWidth ==
                        sample.SourceWidth &&
                        current.SourceHeight ==
                        sample.SourceHeight &&
                        current.Transform ==
                        sample.Transform);

                if (!duplicate)
                    existing.Samples.Add(sample);
            }
        }

        target.UpdatedUtc =
            DateTime.UtcNow;
    }

    private static void MergeStyleFeedback(
        Dictionary<string, int> target,
        IReadOnlyDictionary<string, int> incoming)
    {
        foreach (var pair in incoming)
        {
            target[pair.Key] =
                Math.Max(
                    target.GetValueOrDefault(
                        pair.Key),
                    pair.Value);
        }
    }

    private static MotifMemoryFile LoadInternal()
    {
        MotifMemoryFile memory;

        try
        {
            memory =
                File.Exists(StorePath)
                    ? MotifMemorySerializer.Load(
                        File.ReadAllText(StorePath))
                    : new MotifMemoryFile();
        }
        catch
        {
            // Corrupt user knowledge must never prevent a RugScale host from starting.
            memory =
                new MotifMemoryFile();
        }

        try
        {
            var assembly =
                typeof(MotifMemoryStore)
                    .Assembly;
            var resourceName =
                assembly
                    .GetManifestResourceNames()
                    .FirstOrDefault(name =>
                        string.Equals(
                            name,
                            SeedResourceName,
                            StringComparison.Ordinal))
                ??
                assembly
                    .GetManifestResourceNames()
                    .FirstOrDefault(name =>
                        name.EndsWith(
                            "rugscale-motif-memory.seed.gz.b64",
                            StringComparison.OrdinalIgnoreCase));

            using var seedStream =
                resourceName is null
                    ? null
                    : assembly.GetManifestResourceStream(
                        resourceName);

            if (seedStream is not null)
            {
                using var seedReader =
                    new StreamReader(
                        seedStream,
                        Encoding.UTF8,
                        detectEncodingFromByteOrderMarks: true,
                        leaveOpen: false);

                var base64 =
                    seedReader
                        .ReadToEnd()
                        .Trim();
                var compressed =
                    Convert.FromBase64String(
                        base64);

                using var source =
                    new MemoryStream(
                        compressed,
                        writable: false);
                using var gzip =
                    new GZipStream(
                        source,
                        CompressionMode.Decompress);
                using var reader =
                    new StreamReader(
                        gzip,
                        Encoding.UTF8);

                var seed =
                    MotifMemorySerializer.Load(
                        reader.ReadToEnd());

                // Seed knowledge is only the baseline. User feedback always survives.
                MergeInto(
                    memory,
                    seed);
            }
        }
        catch
        {
            // A damaged packaged seed must not invalidate the user's own learned memory.
        }

        return memory;
    }
}
