using RFGAN.SampleMapper.Detection.Engine;
using RFGAN.SampleMapper.Detection.Contracts;
using RFGAN.SampleMapper.Services;

namespace RFGAN.SampleMapper.Detection.Algorithms;

/// <summary>
/// Phase 4.2 adapter strategy around the current production advanced classical CV pipeline.
///
/// No OpenCV algorithm has been rewritten here. Delegating to SamAdvancedTsvDetectorAdapter
/// preserves current detector behavior while placing it behind the new engine boundary.
/// A later phase can move RunPipeline implementation details into this class without changing
/// any ISampleDetectionEngine caller.
/// </summary>
internal sealed class AdvancedClassicalDetectionStrategy : IDetectionStrategy
{
    private readonly SamAdvancedTsvDetectorAdapter _adapter;

    public AdvancedClassicalDetectionStrategy()
        : this(SamAdvancedTsvDetectorAdapter.Shared)
    {
    }

    internal AdvancedClassicalDetectionStrategy(SamAdvancedTsvDetectorAdapter adapter)
    {
        _adapter = adapter ?? throw new ArgumentNullException(nameof(adapter));
    }

    public string Name => "Advanced classical SAM sample + TSV detector";

    public SamDetectionResult Detect(
        DetectionContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        // Translate the engine-owned immutable context to the existing adapter request.
        // Every detector tuning value comes from the supplied recipe snapshot; this strategy
        // never resolves an active preset or reads UI state.
        var adapterRequest = new SamCvDetectorRequest
        {
            RecipeSnapshot = context.Recipe.Clone(),
            ActivePreset = context.Recipe.UnifiedPreset?.Clone(),
            Parameters = context.Recipe.DetectorParameters.Clone(),
            Geometry = context.Recipe.DetectorGeometry.Clone(),
            ReferenceRect = context.ReferenceRect,
            DebugOutputDir = context.Options.DebugOutputDirectory,
            DebugBaseName = context.Options.DebugBaseName,
            SaveDebugStages = context.Options.SaveDebugStages,
            SaveFinalReviewOverlay = context.Options.SaveFinalReviewOverlay,
            CaptureCalibrationDiagnostics = context.Options.DiagnosticsLevel == DetectionDiagnosticsLevel.Full
        };

        SamDetectionResult result = _adapter.Detect(
            context.ImagePath,
            context.SampleType,
            adapterRequest);

        cancellationToken.ThrowIfCancellationRequested();
        return result;
    }
}
