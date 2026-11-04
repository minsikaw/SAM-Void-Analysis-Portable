using RFGAN.SampleMapper.Services;

namespace RFGAN.SampleMapper.Models;

/// <summary>
/// Product/recipe-specific disposition limits. The defaults preserve legacy behaviour,
/// but the values are now versioned with each published recipe instead of being hard-coded
/// inside the analysis service.
/// </summary>
public sealed class DelamDecisionThresholds
{
    public double WholeSampleFailThresholdPercent { get; set; } = 5.0;
    public double? TsvFailThresholdPercent { get; set; }

    public DelamDecisionThresholds Clone() => new()
    {
        WholeSampleFailThresholdPercent = WholeSampleFailThresholdPercent,
        TsvFailThresholdPercent = TsvFailThresholdPercent
    };

    public void Normalize()
    {
        WholeSampleFailThresholdPercent = Math.Clamp(WholeSampleFailThresholdPercent, 0.0, 100.0);
        if (TsvFailThresholdPercent.HasValue)
            TsvFailThresholdPercent = Math.Clamp(TsvFailThresholdPercent.Value, 0.0, 100.0);
    }
}

/// <summary>
/// Immutable runtime snapshot used by detection, quantification, persistence and export.
/// It deliberately contains complete cloned settings so later preset edits cannot change
/// the meaning of an already-completed analysis run.
/// </summary>
public sealed class AnalysisRecipeSnapshot
{
    public int SchemaVersion { get; set; } = 1;
    public string SnapshotId { get; set; } = Guid.NewGuid().ToString("N");
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public string SampleType { get; set; } = RFGAN.SampleMapper.Services.SampleType.RfGanWithTsv.ToString();

    public string PresetId { get; set; } = string.Empty;
    public string PresetName { get; set; } = "Detector-only runtime recipe";
    public int PresetVersion { get; set; }
    public UnifiedSampleDetectionPreset? UnifiedPreset { get; set; }

    public SamAdvancedDetectionParams DetectorParameters { get; set; } = new();
    public SamAdvancedLearnedGeometry DetectorGeometry { get; set; } = new();
    public DelamDecisionThresholds DecisionThresholds { get; set; } = new();

    public string RecipeHashSha256 { get; set; } = string.Empty;

    public RFGAN.SampleMapper.Services.SampleType ParsedSampleType
        => Enum.TryParse<RFGAN.SampleMapper.Services.SampleType>(SampleType, true, out var value)
            ? value
            : RFGAN.SampleMapper.Services.SampleType.RfGanWithTsv;

    public AnalysisRecipeSnapshot Clone() => new()
    {
        SchemaVersion = SchemaVersion,
        SnapshotId = SnapshotId,
        CreatedAtUtc = CreatedAtUtc,
        SampleType = SampleType,
        PresetId = PresetId,
        PresetName = PresetName,
        PresetVersion = PresetVersion,
        UnifiedPreset = UnifiedPreset?.Clone(),
        DetectorParameters = (DetectorParameters ?? new SamAdvancedDetectionParams()).Clone(),
        DetectorGeometry = (DetectorGeometry ?? new SamAdvancedLearnedGeometry()).Clone(),
        DecisionThresholds = (DecisionThresholds ?? new DelamDecisionThresholds()).Clone(),
        RecipeHashSha256 = RecipeHashSha256
    };
}
