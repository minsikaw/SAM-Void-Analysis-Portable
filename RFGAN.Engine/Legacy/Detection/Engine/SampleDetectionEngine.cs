using RFGAN.SampleMapper.Detection.Algorithms;
using RFGAN.SampleMapper.Detection.Contracts;
using RFGAN.SampleMapper.Detection.Diagnostics;
using RFGAN.SampleMapper.Models;
using RFGAN.SampleMapper.Services;

namespace RFGAN.SampleMapper.Detection.Engine;

/// <summary>
/// Production-facing sample-detection orchestrator introduced in Phase 4.2.
///
/// Responsibilities in this phase:
/// - validate the explicit request boundary,
/// - clone/normalize runtime state into a DetectionContext,
/// - execute the current advanced classical strategy,
/// - guarantee recipe provenance on the returned result,
/// - convert unexpected engine-boundary failures into a normal failed detection result.
///
/// It deliberately does NOT resolve active presets, access WinForms controls, or implement
/// fallback selection. Those concerns remain outside / are introduced in later phases.
/// </summary>
public sealed class SampleDetectionEngine : ISampleDetectionEngine
{
    private readonly IDetectionStrategy _primaryStrategy;

    public SampleDetectionEngine()
        : this(new AdvancedClassicalDetectionStrategy())
    {
    }

    internal SampleDetectionEngine(IDetectionStrategy primaryStrategy)
    {
        _primaryStrategy = primaryStrategy ?? throw new ArgumentNullException(nameof(primaryStrategy));
    }

    public SamDetectionResult Detect(
        SampleDetectionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Recipe);

        cancellationToken.ThrowIfCancellationRequested();

        DetectionContext context;
        try
        {
            context = DetectionContext.Create(request);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return CreateFailureResult(request, ex.Message);
        }

        if (!File.Exists(context.ImagePath))
        {
            return CreateFailureResult(
                request,
                $"Image file was not found: {context.ImagePath}");
        }

        try
        {
            SamDetectionResult result = _primaryStrategy.Detect(context, cancellationToken);

            // Preserve explicit engine provenance even if the delegated legacy adapter changes.
            result.SampleType = context.SampleType;
            result.RecipeSnapshot = context.Recipe.Clone();
            result.RecipeSnapshotId = context.Recipe.SnapshotId;
            result.RecipeHashSha256 = context.Recipe.RecipeHashSha256;

            if (result.Success &&
                context.Options.SaveFinalReviewOverlay &&
                !string.IsNullOrWhiteSpace(context.Options.DebugOutputDirectory))
            {
                DetectionArtifactExporter.SaveFinalRoiReviewOverlay(
                    context.ImagePath,
                    result,
                    context.Options.DebugOutputDirectory!,
                    context.Options.DebugBaseName);
            }

            return result;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            AppLogger.Error(
                "SampleDetectionEngine",
                ex,
                "Sample detection failed at engine boundary. Image={Image}, SampleType={SampleType}, Strategy={Strategy}",
                context.ImagePath,
                context.SampleType,
                _primaryStrategy.Name);

            return CreateFailureResult(request, ex.Message);
        }
    }

    private static SamDetectionResult CreateFailureResult(
        SampleDetectionRequest request,
        string failureReason)
    {
        AnalysisRecipeSnapshot recipe = request.Recipe.Clone();

        return new SamDetectionResult
        {
            Success = false,
            FailureReason = failureReason ?? string.Empty,
            SampleType = request.SampleType,
            Confidence = 0,
            ConfidenceLevel = "Failed",
            RecipeSnapshot = recipe,
            RecipeSnapshotId = recipe.SnapshotId,
            RecipeHashSha256 = recipe.RecipeHashSha256
        };
    }
}
