namespace RugScale.Core.Drawing;

internal sealed record LeafPetalRegion(
    byte Color,
    IReadOnlyList<int> Pixels,
    IReadOnlyList<int> BoundaryPixels,
    int MinX,
    int MinY,
    int MaxX,
    int MaxY,
    bool IsSubLobe = false)
{
    public int Width =>
        MaxX -
        MinX +
        1;

    public int Height =>
        MaxY -
        MinY +
        1;

    public int Area =>
        Pixels.Count;
}

internal sealed record LeafPetalArcCandidate(
    LeafPetalRegion Region,
    double CenterX,
    double CenterY,
    double AxisX,
    double AxisY,
    double NormalX,
    double NormalY,
    double MajorExtent,
    double MinorExtent,
    double Elongation,
    double BoundaryRatio);

internal readonly record struct LeafPetalAxisSample(
    double X,
    double Y,
    double HalfWidth,
    double AxisPosition);

internal sealed record LeafPetalArcModel(
    LeafPetalArcCandidate Candidate,
    IReadOnlyList<LeafPetalAxisSample> Samples,
    bool ReversedForApex,
    double BaseWidth,
    double ApexWidth,
    double SkeletonCoverage);

internal readonly record struct ElegantArcPoint(
    double X,
    double Y,
    double HalfWidth);

internal sealed record ElegantArcFit(
    IReadOnlyList<ElegantArcPoint> Points,
    bool IsSafe,
    bool IsMonotonic,
    int CurvatureSignFlips,
    double MaximumCenterlineDeviation);

internal sealed record LeafPetalBoundaryCurveModel(
    LeafPetalArcModel ArcModel,
    byte OutlineColor,
    double OutlineCoverage,
    IReadOnlyList<(int X, int Y)> LeftSourcePath,
    IReadOnlyList<(int X, int Y)> RightSourcePath,
    IReadOnlySet<(int X, int Y)> SourceOuterPath,
    ToolFaithfulCurveStyleFit LeftFit,
    ToolFaithfulCurveStyleFit RightFit,
    bool DrawBaseCap,
    bool DrawApexCap);

internal sealed record LeafPetalCandidateStage(
    byte Color,
    int MinX,
    int MinY,
    int MaxX,
    int MaxY,
    int Area,
    bool IsSubLobe,
    double Elongation,
    bool AxisBuilt,
    bool ElegantFitSafe,
    bool BoundaryCurveBuilt,
    double OutlineCoverage,
    int LeftControls,
    int RightControls,
    int LeftSourcePathPixels,
    int RightSourcePathPixels,
    double LeftRoundness,
    double RightRoundness,
    double TargetLeftRoundness,
    double TargetRightRoundness,
    bool TargetPairAdjusted);

internal readonly record struct LeafPetalArcDiagnostics(
    int Regions,
    int Candidates,
    int Refined,
    int RejectedByAxis,
    int RejectedByFit,
    int BoundaryPixelsChanged,
    int BoundaryCurveRefined = 0,
    int CenterlineRefined = 0,
    int BoundaryCurvePixelsChanged = 0,
    int CenterlinePixelsChanged = 0);
