using OpenCvSharp;

namespace RFGAN.SampleMapper.Models;

public enum SampleDetectionNormalizeMode
{
    MinMax = 0,
    Percentile = 1
}

public enum SampleDetectionBlurType
{
    None = 0,
    Gaussian = 1,
    Median = 2,
    Bilateral = 3
}

public enum SampleDetectionThresholdMode
{
    Otsu = 0,
    AdaptiveMean = 1,
    AdaptiveGaussian = 2,
    Manual = 3
}

public enum SampleDetectionKernelShape
{
    Rect = 0,
    Ellipse = 1,
    Cross = 2
}

public enum SampleDetectionGradientMethod
{
    Sobel = 0,
    Scharr = 1,
    Canny = 2
}

public enum SampleDetectionMaskType
{
    AxisAlignedRectangle = 0,
    RotatedRectangle = 1
}

public sealed class SampleDetectionParameters
{
    public string Name { get; set; } = "Balanced";

    // Normalization
    public SampleDetectionNormalizeMode NormalizeMode { get; set; } = SampleDetectionNormalizeMode.MinMax;
    public double LowerPercentile { get; set; } = 1.0;
    public double UpperPercentile { get; set; } = 99.0;
    public bool InvertImage { get; set; }

    // CLAHE
    public bool EnableClahe { get; set; } = true;
    public double ClaheClipLimit { get; set; } = 2.0;
    public int ClaheTileGridSize { get; set; } = 8;

    // Blur / denoising
    public SampleDetectionBlurType BlurType { get; set; } = SampleDetectionBlurType.Gaussian;
    public int BlurKernelSize { get; set; } = 5;
    public double BlurSigma { get; set; } = 0.0;

    // Thresholding
    public SampleDetectionThresholdMode ThresholdMode { get; set; } = SampleDetectionThresholdMode.Otsu;
    public double ThresholdOffset { get; set; } = 0.0;
    public int ManualThresholdValue { get; set; } = 128;
    public int AdaptiveBlockSize { get; set; } = 31;
    public double AdaptiveC { get; set; } = 2.0;
    public bool InvertThreshold { get; set; }
    public bool AutoInvertThreshold { get; set; } = true;

    // Morphology
    public int OpenKernelSize { get; set; } = 3;
    public int CloseKernelSize { get; set; } = 9;
    public int OpenIterations { get; set; } = 1;
    public int CloseIterations { get; set; } = 1;
    public SampleDetectionKernelShape KernelShape { get; set; } = SampleDetectionKernelShape.Rect;
    public bool FillHoles { get; set; } = true;

    // Contour filtering
    public double MinAreaFraction { get; set; } = 0.01;
    public double MaxAreaFraction { get; set; } = 0.95;
    public int MinWidth { get; set; } = 12;
    public int MinHeight { get; set; } = 12;
    public double MinSolidity { get; set; } = 0.40;
    public double MinRectangularity { get; set; } = 0.25;
    public double AspectRatioMin { get; set; } = 0.20;
    public double AspectRatioMax { get; set; } = 5.00;
    public bool RejectBorderTouching { get; set; } = true;
    public int BorderMargin { get; set; } = 3;

    // Candidate scoring
    public double AreaWeight { get; set; } = 0.70;
    public double AspectRatioWeight { get; set; } = 0.45;
    public double RectangularityWeight { get; set; } = 1.05;
    public double SolidityWeight { get; set; } = 1.15;
    public double CenterWeight { get; set; } = 0.75;
    public double EdgeStrengthWeight { get; set; } = 0.55;
    public double PresetFitWeight { get; set; } = 1.15;
    public double TsvStructureWeight { get; set; } = 0.30;
    public double OversizePenalty { get; set; } = 2.20;
    public double BorderPenalty { get; set; } = 0.28;
    public double TopBandPenalty { get; set; } = 0.28;

    // Projection refinement
    public bool EnableProjectionRefinement { get; set; } = true;
    public double OccupancyThreshold { get; set; } = 0.35;
    public int ProfileSmoothingWindow { get; set; } = 3;
    public int MinimumRunLength { get; set; } = 5;
    public double MaxShrinkFractionPerSide { get; set; } = 0.35;
    public int PaddingAfterProjection { get; set; } = 0;

    // Preset-window snap
    public bool EnablePresetSnap { get; set; } = true;
    public int ExpectedWidth { get; set; } = 0;
    public int ExpectedHeight { get; set; } = 0;
    public double WidthTolerance { get; set; } = 0.22;
    public double HeightTolerance { get; set; } = 0.22;
    public int SearchRadius { get; set; } = 35;
    public double SnapStrength { get; set; } = 1.0;

    // Edge snapping
    public bool EnableEdgeSnap { get; set; } = true;
    public SampleDetectionGradientMethod GradientMethod { get; set; } = SampleDetectionGradientMethod.Sobel;
    public int SearchBandWidth { get; set; } = 14;
    public int MaxSnapDistance { get; set; } = 18;
    public double EdgeThreshold { get; set; } = 0.0;
    public int EdgeProfileSmoothing { get; set; } = 3;
    public int PaddingAfterSnap { get; set; } = 0;
    public bool InwardOnly { get; set; } = true;

    // Final mask
    public int FinalPadding { get; set; } = 0;
    public bool ClipToImageBounds { get; set; } = true;
    public SampleDetectionMaskType MaskType { get; set; } = SampleDetectionMaskType.AxisAlignedRectangle;

    public static SampleDetectionParameters CreateBalanced() => new();

    public static SampleDetectionParameters CreateDefault() => CreateBalanced();

    public SampleDetectionParameters Clone() => (SampleDetectionParameters)MemberwiseClone();
}

public sealed class SampleDetectionPreset
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "Custom preset";
    public string Description { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime UpdatedAt { get; set; } = DateTime.Now;
    public bool IsBuiltIn { get; set; }
    public SampleDetectionParameters Parameters { get; set; } = SampleDetectionParameters.CreateBalanced();
    public override string ToString() => Name;
}

public sealed class DetectionStageImage : IDisposable
{
    public int Order { get; set; }
    public string Key { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Caption { get; set; } = string.Empty;
    public Mat Image { get; set; } = new();

    public void Dispose()
    {
        Image?.Dispose();
        Image = new Mat();
    }
}

public sealed class DetectionConfidencePoint
{
    public int Stage { get; set; }
    public string Name { get; set; } = string.Empty;
    public double Confidence { get; set; }
    public int CandidateCount { get; set; }
    public Rect Roi { get; set; }
}

public sealed class DetectionCalibrationResult : IDisposable
{
    public bool Success { get; set; }
    public string FailureReason { get; set; } = string.Empty;
    public string SourceImagePath { get; set; } = string.Empty;
    public Rect InitialCandidateRect { get; set; }
    public Rect ProjectionRect { get; set; }
    public Rect EdgeSnappedRect { get; set; }
    public Rect FinalRect { get; set; }
    public Rect? TsvRect { get; set; }
    public List<SpecialAbsoluteRoiResult> SpecialRois { get; set; } = new();
    public string ActivePresetName { get; set; } = string.Empty;
    public UnifiedSampleDetectionPreset? ActivePresetSnapshot { get; set; }
    public SampleDetectionParameters? EffectiveDetectionParameters { get; set; }
    public OpenCvSharp.Point[] FinalPolygon { get; set; } = Array.Empty<OpenCvSharp.Point>();
    public int CandidateCount { get; set; }
    public double SelectedCandidateScore { get; set; }
    public double StructuralConfidence { get; set; }
    public double Rectangularity { get; set; }
    public double Solidity { get; set; }
    public double EdgeStrength { get; set; }
    public int MaskPixelCount { get; set; }
    public double AspectRatio => FinalRect.Height <= 0 ? 0 : FinalRect.Width / (double)FinalRect.Height;
    public double RoiArea => FinalRect.Width * FinalRect.Height;
    public bool TsvAvailable => TsvRect.HasValue && TsvRect.Value.Width > 0 && TsvRect.Value.Height > 0;
    public long ProcessingTimeMs { get; set; }
    public List<string> Warnings { get; set; } = new();
    public List<DetectionStageImage> Stages { get; set; } = new();
    public List<DetectionConfidencePoint> ConfidenceEvolution { get; set; } = new();
    public RoiComparisonMetrics? Comparison { get; set; }

    public void Dispose()
    {
        foreach (DetectionStageImage stage in Stages)
            stage.Dispose();
        Stages.Clear();
    }
}

public sealed class RoiComparisonMetrics
{
    public bool HasReference { get; set; }
    public double IoU { get; set; }
    public double CenterErrorPixels { get; set; }
    public double WidthErrorPixels { get; set; }
    public double HeightErrorPixels { get; set; }
    public double BoundaryErrorPixels { get; set; }
    public Rect DetectedRect { get; set; }
    public Rect ReferenceRect { get; set; }

    public static RoiComparisonMetrics Empty(Rect detected) => new()
    {
        HasReference = false,
        DetectedRect = detected
    };

    public static RoiComparisonMetrics FromRects(Rect detected, Rect reference)
    {
        double interLeft = Math.Max(detected.Left, reference.Left);
        double interTop = Math.Max(detected.Top, reference.Top);
        double interRight = Math.Min(detected.Right, reference.Right);
        double interBottom = Math.Min(detected.Bottom, reference.Bottom);
        double interArea = Math.Max(0, interRight - interLeft) * Math.Max(0, interBottom - interTop);
        double unionArea = Math.Max(1, detected.Width * detected.Height + reference.Width * reference.Height - interArea);
        double dcx = detected.X + detected.Width / 2.0;
        double dcy = detected.Y + detected.Height / 2.0;
        double rcx = reference.X + reference.Width / 2.0;
        double rcy = reference.Y + reference.Height / 2.0;
        double boundary = (Math.Abs(detected.Left - reference.Left) +
                           Math.Abs(detected.Top - reference.Top) +
                           Math.Abs(detected.Right - reference.Right) +
                           Math.Abs(detected.Bottom - reference.Bottom)) / 4.0;

        return new RoiComparisonMetrics
        {
            HasReference = true,
            DetectedRect = detected,
            ReferenceRect = reference,
            IoU = interArea / unionArea,
            CenterErrorPixels = Math.Sqrt((dcx - rcx) * (dcx - rcx) + (dcy - rcy) * (dcy - rcy)),
            WidthErrorPixels = detected.Width - reference.Width,
            HeightErrorPixels = detected.Height - reference.Height,
            BoundaryErrorPixels = boundary
        };
    }
}
