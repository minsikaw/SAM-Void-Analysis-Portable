using OpenCvSharp;
using RFGAN.SampleMapper.Models;
using RFGAN.SampleMapper.Services;

namespace RFGAN.Batch;

public enum SourceFormat { Image, Zgt }
public enum MaskMode { ExternalBinaryMask, ExperimentalImageThreshold }
public enum SampleState { Imported, Ready, Blocked, NeedsReview, Completed, Error }

public sealed class BatchSettings
{
    public SampleType SampleType { get; set; } = SampleType.RfGanWithTsv;
    public SamAdvancedDetectionParams Detector { get; set; } = new();
    public SamAdvancedLearnedGeometry Geometry { get; set; } = new();
    public double MinimumConfidence { get; set; } = 0.60;
    public double MinimumValidCoverage { get; set; } = 0.95;
    public MaskMode MaskMode { get; set; } = MaskMode.ExternalBinaryMask;
    public byte ExploratoryThreshold { get; set; } = 180;
    public bool BrightPixelsAreDelamination { get; set; } = true;
    public ZgtCscanMode ZgtMode { get; set; } = ZgtCscanMode.GateAbsPeak;
    public int GateStart { get; set; } = 0;
    public int GateEnd { get; set; } = 750;
    public BatchSettings Clone() => new()
    {
        SampleType = SampleType,
        Detector = Detector.Clone(),
        Geometry = Geometry.Clone(),
        MinimumConfidence = MinimumConfidence,
        MinimumValidCoverage = MinimumValidCoverage,
        MaskMode = MaskMode,
        ExploratoryThreshold = ExploratoryThreshold,
        BrightPixelsAreDelamination = BrightPixelsAreDelamination,
        ZgtMode = ZgtMode,
        GateStart = GateStart,
        GateEnd = GateEnd
    };
}

public sealed class BatchSample
{
    public string Identity { get; set; } = "";
    public string OriginalPath { get; set; } = "";
    public string ImagePath { get; set; } = "";
    public string? MaskPath { get; set; }
    public string? ValidityMaskPath { get; set; }
    public SourceFormat SourceFormat { get; set; }
    public SampleState State { get; set; } = SampleState.Imported;
    public string Message { get; set; } = "Not processed";
    public bool IdentityConfirmed { get; set; }
    public bool RoiApproved { get; set; }
    public string ApprovedCscanHash { get; set; } = "";
    public string ApprovedRecipeHash { get; set; } = "";
    public Rect? ManualRoi { get; set; }
    public SamDetectionResult? Detection { get; set; }
    public Measurement? Result { get; set; }
    public string? ImportError { get; set; }

    public bool MaskPresent => !string.IsNullOrWhiteSpace(MaskPath) && File.Exists(MaskPath);
    public override string ToString() => Identity;
}

public sealed class Measurement
{
    public DateTimeOffset CalculatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public string SampleId { get; set; } = "";
    public int RoiPixels { get; set; }
    public int ValidRoiPixels { get; set; }
    public int DelaminationPixels { get; set; }
    public double CoveragePercent { get; set; }
    public double DelaminationPercent { get; set; }
    public int TsvValidPixels { get; set; }
    public int TsvDelaminationPixels { get; set; }
    public double? TsvDelaminationPercent { get; set; }
    public bool Exploratory { get; set; }
    public bool NonBinaryMask { get; set; }
    public double RoiConfidence { get; set; }
    public bool ManualRoi { get; set; }
    public string RecipeHash { get; set; } = "";
    public string ImageHash { get; set; } = "";
    public string CscanHash { get; set; } = "";
    public string MaskHash { get; set; } = "";
    public string ValidityHash { get; set; } = "";
}

public static class DetectionRecipeFactory
{
    public static AnalysisRecipeSnapshot Create(BatchSettings settings)
    {
        var recipe = new AnalysisRecipeSnapshot
        {
            SampleType = settings.SampleType.ToString(),
            PresetName = "Batch v1 detector parameters",
            DetectorParameters = settings.Detector.Clone(),
            DetectorGeometry = settings.Geometry.Clone(),
            UnifiedPreset = null
        };
        recipe.RecipeHashSha256 = FileIdentity.HashJson(new
        {
            recipe.SampleType, recipe.DetectorParameters, recipe.DetectorGeometry,
            settings.MaskMode, settings.ExploratoryThreshold,
            settings.BrightPixelsAreDelamination,
            settings.MinimumConfidence, settings.MinimumValidCoverage,
            settings.ZgtMode, settings.GateStart, settings.GateEnd
        });
        return recipe;
    }
}
