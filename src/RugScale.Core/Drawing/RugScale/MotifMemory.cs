using System.Text.Json;
using System.Text.Json.Serialization;
using RugScale.Core.Models;

namespace RugScale.Core.Drawing;

public enum MotifTransform : byte
{
    Identity,
    Rotate90,
    Rotate180,
    Rotate270,
    MirrorX,
    MirrorXRotate90,
    MirrorXRotate180,
    MirrorXRotate270,
}

/// <summary>
/// Portable RugScale motif knowledge. It intentionally stores shape/color-role structure instead
/// of document palette indexes, so the same leaf/branch/medallion part can be recognized when it
/// is recolored, mirrored or rotated in another rug.
/// </summary>
public sealed class MotifMemoryFile
{
    public int Version { get; set; } = 2;
    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
    public List<MotifFamilyMemory> Families { get; set; } = [];
}

public sealed class MotifFamilyMemory
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "Motif";
    public int SignatureWidth { get; set; } = MotifDescriptor.SignatureSize;
    public int SignatureHeight { get; set; } = MotifDescriptor.SignatureSize;

    /// <summary>One byte per normalized cell. 255 means outside the selected motif.</summary>
    public byte[] CanonicalRoles { get; set; } = [];

    /// <summary>Structural boundary bitmap, one byte per normalized cell (0/1).</summary>
    public byte[] CanonicalEdges { get; set; } = [];

    public int RoleCount { get; set; }
    public double PhysicalAspect { get; set; } = 1d;
    public int PositiveFeedback { get; set; }
    public int NegativeFeedback { get; set; }

    /// <summary>
    /// User feedback about HOW this motif family should be redrawn after shrink. Keys are
    /// MotifRepairStyle names so the portable JSON stays forward-compatible with enum additions.
    /// </summary>
    public Dictionary<string, int> RepairStylePositive { get; set; } = [];
    public Dictionary<string, int> RepairStyleNegative { get; set; } = [];

    public List<MotifSampleMemory> Samples { get; set; } = [];

    [JsonIgnore]
    public double Confidence =>
        (PositiveFeedback + 1d) /
        (PositiveFeedback + NegativeFeedback + 2d);
}

public sealed class MotifSampleMemory
{
    public string SourceTag { get; set; } = "";
    public int SourceWidth { get; set; }
    public int SourceHeight { get; set; }
    public int WarpDensity { get; set; }
    public int WeftDensity { get; set; }
    public MotifTransform Transform { get; set; }
    public DateTime LearnedUtc { get; set; } = DateTime.UtcNow;
    public bool UserApproved { get; set; }
}

public sealed class MotifDescriptor
{
    public const int SignatureSize = 24;
    public required byte[] Roles { get; init; }
    public required byte[] Edges { get; init; }
    public required int RoleCount { get; init; }
    public required double PhysicalAspect { get; init; }
    public required MotifTransform CanonicalTransform { get; init; }

    /// <summary>
    /// Palette-role grid in the patch's actual orientation. Canonical Roles are rotate/mirror
    /// invariant; RawRoles lets repair matching recover which transform maps source to target.
    /// </summary>
    public byte[] RawRoles { get; init; } = [];

    /// <summary>Physical width/height before canonical rotation.</summary>
    public double RawPhysicalAspect { get; init; } = 1d;

    public static MotifDescriptor FromPatch(
        DesignDocument document,
        int left,
        int top,
        int width,
        int height,
        bool[]? selectionMask,
        int warpDensity,
        int weftDensity)
    {
        if (width <= 0 || height <= 0)
            throw new ArgumentOutOfRangeException(nameof(width));

        var roles = NormalizeRoles(
            BuildRoleGrid(
                document,
                left,
                top,
                width,
                height,
                selectionMask));

        var bestRoles = roles;
        var bestTransform = MotifTransform.Identity;

        foreach (var transform in Enum.GetValues<MotifTransform>())
        {
            var transformed = NormalizeRoles(
                TransformGrid(
                    roles,
                    SignatureSize,
                    SignatureSize,
                    transform));

            if (LexicographicCompare(transformed, bestRoles) < 0)
            {
                bestRoles = transformed;
                bestTransform = transform;
            }
        }

        var edges = BuildEdges(bestRoles, SignatureSize, SignatureSize);
        var maxRole = bestRoles
            .Where(static value => value != byte.MaxValue)
            .DefaultIfEmpty((byte)0)
            .Max();

        var physicalWidth =
            width / (double)Math.Max(1, warpDensity);
        var physicalHeight =
            height / (double)Math.Max(1, weftDensity);
        var rawPhysicalAspect =
            physicalWidth /
            Math.Max(1e-9, physicalHeight);
        var canonicalPhysicalAspect =
            SwapsAxes(bestTransform)
                ? 1d / Math.Max(1e-9, rawPhysicalAspect)
                : rawPhysicalAspect;

        return new MotifDescriptor
        {
            Roles = bestRoles,
            Edges = edges,
            RoleCount = bestRoles.Any(static value => value != byte.MaxValue)
                ? maxRole + 1
                : 0,
            PhysicalAspect = canonicalPhysicalAspect,
            CanonicalTransform = bestTransform,
            RawRoles = roles,
            RawPhysicalAspect = rawPhysicalAspect,
        };
    }

    public static double Similarity(
        MotifDescriptor a,
        MotifDescriptor b)
    {
        if (a.Roles.Length != b.Roles.Length ||
            a.Edges.Length != b.Edges.Length)
        {
            return 0d;
        }

        var occupancySame = 0;
        var edgeSame = 0;
        var roleBoundarySame = 0;

        for (var i = 0; i < a.Roles.Length; i++)
        {
            var aInside = a.Roles[i] != byte.MaxValue;
            var bInside = b.Roles[i] != byte.MaxValue;
            if (aInside == bInside)
                occupancySame++;

            if (a.Edges[i] == b.Edges[i])
                edgeSame++;

            if (aInside && bInside)
            {
                // Local role numbers are canonicalized by first occurrence, so this remains
                // palette-index independent while still describing multicolor motif structure.
                if (a.Roles[i] == b.Roles[i])
                    roleBoundarySame++;
            }
        }

        var occupancy =
            occupancySame / (double)a.Roles.Length;
        var edges =
            edgeSame / (double)a.Edges.Length;
        var roles =
            roleBoundarySame /
            (double)Math.Max(
                1,
                a.Roles.Count(static value =>
                    value != byte.MaxValue));

        var aspectDelta =
            Math.Abs(
                Math.Log(
                    Math.Max(1e-6, a.PhysicalAspect) /
                    Math.Max(1e-6, b.PhysicalAspect)));
        var aspectScore = Math.Exp(-aspectDelta * 1.6);

        return Math.Clamp(
            occupancy * 0.25 +
            edges * 0.40 +
            roles * 0.20 +
            aspectScore * 0.15,
            0d,
            1d);
    }

    /// <summary>
    /// Finds the transform that maps the source patch's actual orientation to the target patch's
    /// actual orientation. This is deliberately separate from CanonicalTransform: canonicalization
    /// tells us two motifs belong to the same family; this method tells the repair renderer how to
    /// rotate/mirror the source instance on screen.
    /// </summary>
    public static MotifTransform FindBestRelativeTransform(
        MotifDescriptor source,
        MotifDescriptor target,
        out double similarity)
    {
        var sourceRaw =
            source.RawRoles.Length == SignatureSize * SignatureSize
                ? source.RawRoles
                : source.Roles;
        var targetRaw =
            target.RawRoles.Length == SignatureSize * SignatureSize
                ? target.RawRoles
                : target.Roles;

        var targetRawDescriptor = DescriptorFromOrientedRoles(
            targetRaw,
            target.RawRoles.Length > 0
                ? target.RawPhysicalAspect
                : target.PhysicalAspect);

        var best = MotifTransform.Identity;
        similarity = double.NegativeInfinity;

        foreach (var transform in Enum.GetValues<MotifTransform>())
        {
            var transformed = NormalizeRoles(
                TransformGrid(
                    sourceRaw,
                    SignatureSize,
                    SignatureSize,
                    transform));

            var sourceAspect =
                source.RawRoles.Length > 0
                    ? source.RawPhysicalAspect
                    : source.PhysicalAspect;
            if (SwapsAxes(transform))
                sourceAspect =
                    1d / Math.Max(1e-9, sourceAspect);

            var candidate = DescriptorFromOrientedRoles(
                transformed,
                sourceAspect);
            var score = Similarity(
                candidate,
                targetRawDescriptor);

            if (score <= similarity)
                continue;

            similarity = score;
            best = transform;
        }

        similarity = Math.Clamp(similarity, 0d, 1d);
        return best;
    }

    private static MotifDescriptor DescriptorFromOrientedRoles(
        byte[] roles,
        double physicalAspect)
    {
        var normalized = NormalizeRoles(roles);
        var edges = BuildEdges(
            normalized,
            SignatureSize,
            SignatureSize);
        var maxRole = normalized
            .Where(static value => value != byte.MaxValue)
            .DefaultIfEmpty((byte)0)
            .Max();

        return new MotifDescriptor
        {
            Roles = normalized,
            Edges = edges,
            RoleCount = normalized.Any(static value => value != byte.MaxValue)
                ? maxRole + 1
                : 0,
            PhysicalAspect = physicalAspect,
            CanonicalTransform = MotifTransform.Identity,
            RawRoles = normalized,
            RawPhysicalAspect = physicalAspect,
        };
    }

    private static byte[] NormalizeRoles(byte[] source)
    {
        var result = new byte[source.Length];
        var map = new Dictionary<byte, byte>();
        byte next = 0;

        for (var i = 0; i < source.Length; i++)
        {
            var value = source[i];
            if (value == byte.MaxValue)
            {
                result[i] = byte.MaxValue;
                continue;
            }

            if (!map.TryGetValue(value, out var role))
            {
                role = next++;
                map[value] = role;
            }

            result[i] = role;
        }

        return result;
    }

    internal static bool SwapsAxes(MotifTransform transform) =>
        transform is
            MotifTransform.Rotate90 or
            MotifTransform.Rotate270 or
            MotifTransform.MirrorXRotate90 or
            MotifTransform.MirrorXRotate270;

    internal static void MapOutputToSource(
        double outputX,
        double outputY,
        MotifTransform transform,
        out double sourceX,
        out double sourceY)
    {
        sourceX = transform >= MotifTransform.MirrorX
            ? 1d - outputX
            : outputX;
        sourceY = outputY;

        var rotations = transform switch
        {
            MotifTransform.Rotate90 or
            MotifTransform.MirrorXRotate90 => 1,
            MotifTransform.Rotate180 or
            MotifTransform.MirrorXRotate180 => 2,
            MotifTransform.Rotate270 or
            MotifTransform.MirrorXRotate270 => 3,
            _ => 0,
        };

        for (var r = 0; r < rotations; r++)
        {
            var previousX = sourceX;
            sourceX = 1d - sourceY;
            sourceY = previousX;
        }

        sourceX = Math.Clamp(sourceX, 0d, 1d);
        sourceY = Math.Clamp(sourceY, 0d, 1d);
    }

    private static byte[] BuildRoleGrid(
        DesignDocument document,
        int left,
        int top,
        int width,
        int height,
        bool[]? selectionMask)
    {
        var result = new byte[SignatureSize * SignatureSize];
        Array.Fill(result, byte.MaxValue);

        var roleMap = new Dictionary<byte, byte>();
        byte nextRole = 0;

        for (var gy = 0; gy < SignatureSize; gy++)
        {
            var sy = Math.Clamp(
                top + (int)Math.Floor(
                    (gy + 0.5) * height /
                    SignatureSize),
                0,
                document.Height - 1);

            for (var gx = 0; gx < SignatureSize; gx++)
            {
                var sx = Math.Clamp(
                    left + (int)Math.Floor(
                        (gx + 0.5) * width /
                        SignatureSize),
                    0,
                    document.Width - 1);

                if (selectionMask is not null)
                {
                    var localX = Math.Clamp(
                        sx - left,
                        0,
                        width - 1);
                    var localY = Math.Clamp(
                        sy - top,
                        0,
                        height - 1);
                    var maskIndex =
                        localY * width + localX;

                    if (maskIndex < 0 ||
                        maskIndex >= selectionMask.Length ||
                        !selectionMask[maskIndex])
                    {
                        continue;
                    }
                }

                var color = document.GetPixel(sx, sy);
                if (!roleMap.TryGetValue(color, out var role))
                {
                    role = nextRole++;
                    roleMap[color] = role;
                }

                result[gy * SignatureSize + gx] = role;
            }
        }

        return result;
    }

    private static byte[] BuildEdges(
        byte[] roles,
        int width,
        int height)
    {
        var edges = new byte[roles.Length];

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var index = y * width + x;
                var role = roles[index];
                if (role == byte.MaxValue)
                    continue;

                var edge =
                    x == 0 ||
                    x == width - 1 ||
                    y == 0 ||
                    y == height - 1;

                if (!edge)
                {
                    edge =
                        roles[index - 1] != role ||
                        roles[index + 1] != role ||
                        roles[index - width] != role ||
                        roles[index + width] != role;
                }

                edges[index] =
                    edge ? (byte)1 : (byte)0;
            }
        }

        return edges;
    }

    private static byte[] TransformGrid(
        byte[] source,
        int width,
        int height,
        MotifTransform transform)
    {
        var result = new byte[source.Length];

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var outputX =
                    (x + 0.5) / width;
                var outputY =
                    (y + 0.5) / height;

                MapOutputToSource(
                    outputX,
                    outputY,
                    transform,
                    out var sourceX,
                    out var sourceY);

                var sx = Math.Clamp(
                    (int)Math.Floor(sourceX * width),
                    0,
                    width - 1);
                var sy = Math.Clamp(
                    (int)Math.Floor(sourceY * height),
                    0,
                    height - 1);

                result[y * width + x] =
                    source[sy * width + sx];
            }
        }

        return result;
    }

    private static int LexicographicCompare(
        byte[] first,
        byte[] second)
    {
        for (var i = 0; i < first.Length; i++)
        {
            var cmp = first[i].CompareTo(second[i]);
            if (cmp != 0)
                return cmp;
        }

        return 0;
    }
}

public static class MotifMemorySerializer
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    public static MotifMemoryFile Load(string json) =>
        JsonSerializer.Deserialize<MotifMemoryFile>(
            json,
            Options) ??
        new MotifMemoryFile();

    public static string Save(MotifMemoryFile memory)
    {
        memory.UpdatedUtc = DateTime.UtcNow;
        return JsonSerializer.Serialize(memory, Options);
    }

    public static MotifFamilyMemory? FindBestFamily(
        MotifMemoryFile memory,
        MotifDescriptor descriptor,
        double minimumSimilarity,
        out double similarity)
    {
        MotifFamilyMemory? best = null;
        similarity = 0d;

        foreach (var family in memory.Families)
        {
            if (family.CanonicalRoles.Length !=
                    descriptor.Roles.Length ||
                family.CanonicalEdges.Length !=
                    descriptor.Edges.Length)
            {
                continue;
            }

            var familyDescriptor = new MotifDescriptor
            {
                Roles = family.CanonicalRoles,
                Edges = family.CanonicalEdges,
                RoleCount = family.RoleCount,
                PhysicalAspect = family.PhysicalAspect,
                CanonicalTransform = MotifTransform.Identity,
                RawRoles = family.CanonicalRoles,
                RawPhysicalAspect = family.PhysicalAspect,
            };

            var score = MotifDescriptor.Similarity(
                descriptor,
                familyDescriptor) *
                (0.80 + 0.20 * family.Confidence);

            if (score <= similarity)
                continue;

            similarity = score;
            best = family;
        }

        return similarity >= minimumSimilarity
            ? best
            : null;
    }

    public static MotifFamilyMemory LearnPositive(
        MotifMemoryFile memory,
        MotifDescriptor descriptor,
        string sourceTag,
        int sourceWidth,
        int sourceHeight,
        int warpDensity,
        int weftDensity)
    {
        var family = FindBestFamily(
            memory,
            descriptor,
            0.90,
            out _) ??
            CreateFamily(memory, descriptor);

        family.PositiveFeedback++;
        family.Samples.Add(new MotifSampleMemory
        {
            SourceTag = sourceTag,
            SourceWidth = sourceWidth,
            SourceHeight = sourceHeight,
            WarpDensity = warpDensity,
            WeftDensity = weftDensity,
            Transform = descriptor.CanonicalTransform,
            UserApproved = true,
        });

        memory.UpdatedUtc = DateTime.UtcNow;
        return family;
    }

    public static void LearnRepairStyle(
        MotifMemoryFile memory,
        string? familyId,
        MotifRepairStyle style,
        bool success)
    {
        if (string.IsNullOrWhiteSpace(familyId))
            return;

        var family = memory.Families.FirstOrDefault(
            item => item.Id == familyId);
        if (family is null)
            return;

        var key = style.ToString();
        var dictionary = success
            ? family.RepairStylePositive
            : family.RepairStyleNegative;

        dictionary[key] =
            dictionary.GetValueOrDefault(key) + 1;
        memory.UpdatedUtc = DateTime.UtcNow;
    }

    public static double RepairStyleConfidence(
        MotifMemoryFile memory,
        string? familyId,
        MotifRepairStyle style)
    {
        if (string.IsNullOrWhiteSpace(familyId))
            return 0.5d;

        var family = memory.Families.FirstOrDefault(
            item => item.Id == familyId);
        if (family is null)
            return 0.5d;

        var key = style.ToString();
        var positive =
            family.RepairStylePositive.GetValueOrDefault(key);
        var negative =
            family.RepairStyleNegative.GetValueOrDefault(key);

        // Beta(1,1) prior: unknown style starts neutral at 0.5 and gradually moves with feedback.
        return (positive + 1d) /
               (positive + negative + 2d);
    }

    public static void LearnNegative(
        MotifMemoryFile memory,
        string? familyId)
    {
        if (string.IsNullOrWhiteSpace(familyId))
            return;

        var family = memory.Families.FirstOrDefault(
            item => item.Id == familyId);
        if (family is null)
            return;

        family.NegativeFeedback++;
        memory.UpdatedUtc = DateTime.UtcNow;
    }

    private static MotifFamilyMemory CreateFamily(
        MotifMemoryFile memory,
        MotifDescriptor descriptor)
    {
        var family = new MotifFamilyMemory
        {
            Name =
                $"Motif {memory.Families.Count + 1}",
            CanonicalRoles =
                descriptor.Roles.ToArray(),
            CanonicalEdges =
                descriptor.Edges.ToArray(),
            RoleCount = descriptor.RoleCount,
            PhysicalAspect =
                descriptor.PhysicalAspect,
        };
        memory.Families.Add(family);
        return family;
    }
}
