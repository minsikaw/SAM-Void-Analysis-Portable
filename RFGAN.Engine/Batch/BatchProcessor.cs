using OpenCvSharp;
using RFGAN.SampleMapper.Detection.Contracts;
using RFGAN.SampleMapper.Detection.Engine;
using RFGAN.SampleMapper.Models;
using RFGAN.SampleMapper.Services;

namespace RFGAN.Batch;

public sealed class BatchProcessor
{
    private readonly ISampleDetectionEngine _detector;
    public BatchProcessor(ISampleDetectionEngine? detector = null)
        => _detector = detector ?? new SampleDetectionEngine();

    public SamDetectionResult DetectOnly(BatchSample sample, BatchSettings settings, CancellationToken token = default)
    {
        var recipe = DetectionRecipeFactory.Create(settings);
        return _detector.Detect(new SampleDetectionRequest
        {
            ImagePath = sample.ImagePath, SampleType = settings.SampleType, Recipe = recipe,
            Options = new DetectionExecutionOptions { DiagnosticsLevel = DetectionDiagnosticsLevel.Summary }
        }, token);
    }

    public void ProcessOne(BatchSample sample, BatchSettings settings, CancellationToken token = default)
    {
        sample.Result = null;
        sample.State = SampleState.Imported;
        try
        {
            token.ThrowIfCancellationRequested();
            if (!string.IsNullOrEmpty(sample.ImportError)) throw new InvalidDataException(sample.ImportError);
            if (string.IsNullOrWhiteSpace(sample.ImagePath) || !File.Exists(sample.ImagePath))
                throw new FileNotFoundException("No readable C-scan image is associated with this sample.", sample.ImagePath);
            if (settings.MaskMode == MaskMode.ExternalBinaryMask && !sample.MaskPresent)
                throw new FileNotFoundException("No delamination mask assigned. Choose Assign Mask or experimental threshold mode.");

            AnalysisRecipeSnapshot recipe = DetectionRecipeFactory.Create(settings);
            string cscanHash = FileIdentity.Sha256File(sample.ImagePath);
            bool matchingApprovedRoi = sample.RoiApproved &&
                sample.ApprovedCscanHash == cscanHash &&
                sample.ApprovedRecipeHash == recipe.RecipeHashSha256;
            if (sample.RoiApproved && !matchingApprovedRoi)
            {
                sample.RoiApproved = false;
                sample.State = SampleState.NeedsReview;
                sample.Message = "Approved ROI is stale: image or recipe changed. Review geometry again.";
                return;
            }
            // Preserve an approved automatic ROI only when both image and recipe match.
            // Otherwise use the original Phase 4.3 detection engine, unchanged.
            if (!sample.ManualRoi.HasValue && !(matchingApprovedRoi && sample.Detection?.Success == true))
            {
                sample.Detection = DetectOnly(sample, settings, token);
                if (!sample.Detection.Success)
                {
                    sample.State = SampleState.NeedsReview;
                    sample.Message = $"Sample detector: {sample.Detection.FailureReason}. Draw a manual ROI to proceed.";
                    return;
                }
            }

            Measurement result = RoiQuantifier.Calculate(sample, settings, sample.Detection,
                recipe.RecipeHashSha256, token);
            sample.Result = result;
            var reasons = new List<string>();
            if (!sample.IdentityConfirmed) reasons.Add("Sample identity not confirmed");
            if (sample.ManualRoi.HasValue && !matchingApprovedRoi) reasons.Add("Manual ROI not yet approved");
            if (!sample.ManualRoi.HasValue && sample.Detection != null &&
                sample.Detection.Confidence < settings.MinimumConfidence && !sample.RoiApproved)
                reasons.Add($"Low ROI confidence {sample.Detection.Confidence:P0}");
            if (result.CoveragePercent < 100 * settings.MinimumValidCoverage)
                reasons.Add($"Low valid-data coverage {result.CoveragePercent:F1}%");
            if (result.NonBinaryMask)
                reasons.Add("Defect mask has grayscale/antialiased pixels (thresholded at 127)");
            if (settings.MaskMode == MaskMode.ExperimentalImageThreshold && !sample.RoiApproved)
                reasons.Add("Experimental intensity segmentation requires explicit review");
            sample.State = reasons.Count == 0 ? SampleState.Completed : SampleState.NeedsReview;
            sample.Message = reasons.Count == 0
                ? (result.Exploratory ? "Experimental, operator reviewed; not validated as delamination" : "Measurement complete")
                : string.Join("; ", reasons);
        }
        catch (OperationCanceledException) { throw; }
        catch (FileNotFoundException ex)
        {
            sample.State = SampleState.Blocked;
            sample.Message = ex.Message;
        }
        catch (InvalidDataException ex)
        {
            sample.State = SampleState.Blocked;
            sample.Message = ex.Message;
        }
        catch (Exception ex)
        {
            sample.State = SampleState.Error;
            sample.Message = ex.Message;
        }
    }
}
