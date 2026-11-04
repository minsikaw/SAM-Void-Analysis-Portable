namespace RFGAN.SampleMapper.Detection.Contracts;

/// <summary>
/// Per-execution behavior for a sample-detection request.
/// These options describe how the engine should execute; they are not detector tuning parameters.
/// Detector tuning remains part of the immutable AnalysisRecipeSnapshot.
/// </summary>
public sealed class DetectionExecutionOptions
{
    /// <summary>
    /// Amount of explainability information requested by the caller.
    /// </summary>
    public DetectionDiagnosticsLevel DiagnosticsLevel { get; init; } = DetectionDiagnosticsLevel.Summary;

    /// <summary>
    /// Reserved engine policy flag for a future fallback strategy.
    /// Phase 4.2 intentionally does not add or change fallback behavior.
    /// </summary>
    public bool AllowFallback { get; init; } = true;

    /// <summary>
    /// Optional directory used by the existing advanced detector for debug artifacts.
    /// Artifact ownership can be extracted into a dedicated exporter in a later phase.
    /// </summary>
    public string? DebugOutputDirectory { get; init; }

    /// <summary>
    /// Optional base name for debug artifacts. When omitted, the image file name is used.
    /// </summary>
    public string? DebugBaseName { get; init; }

    /// <summary>
    /// Preserves the current advanced detector's ability to save intermediate debug stages.
    /// </summary>
    public bool SaveDebugStages { get; init; }

    /// <summary>
    /// Requests the engine-owned final ROI review overlay when a debug output directory is supplied.
    /// The underlying classical strategy may also retain its existing stage-export behavior.
    /// </summary>
    public bool SaveFinalReviewOverlay { get; init; }

    internal DetectionExecutionOptions Clone() => new()
    {
        DiagnosticsLevel = DiagnosticsLevel,
        AllowFallback = AllowFallback,
        DebugOutputDirectory = DebugOutputDirectory,
        DebugBaseName = DebugBaseName,
        SaveDebugStages = SaveDebugStages,
        SaveFinalReviewOverlay = SaveFinalReviewOverlay
    };
}
