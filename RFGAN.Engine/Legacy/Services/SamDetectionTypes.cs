using OpenCvSharp;
using System.Collections.Generic;
using RFGAN.SampleMapper.Models;

namespace RFGAN.SampleMapper.Services;

public enum SampleType
{
    GenericSam = 0,
    RfGanWithTsv = 1
}

public sealed class SamDetectionProfile
{
    public string Name { get; set; } = string.Empty;

    public double MinAreaFraction { get; set; } = 0.01;
    public double MaxAreaFraction { get; set; } = 0.95;

    public double OpenKernelFraction { get; set; } = 0.010;
    public double CloseKernelFraction { get; set; } = 0.030;

    // Final padding applied to the detected sample rectangle.
    // For percentage calculations this should be small; for RFGaN it is set to 0 by default.
    public double PaddingFraction { get; set; } = 0.018;

    public double AreaWeight { get; set; } = 1.40;
    public double SolidityWeight { get; set; } = 1.10;
    public double ExtentWeight { get; set; } = 0.80;
    public double CenterWeight { get; set; } = 0.70;
    public double BorderPenaltyWeight { get; set; } = 0.22;
    public double TopBandPenaltyWeight { get; set; } = 0.18;
    public double AspectPenaltyWeight { get; set; } = 0.35;

    public double MaxAspectRatio { get; set; } = 5.0;
    public double TopBandFraction { get; set; } = 0.12;

    // ──────────────────────────────────────────────────────────────
    // New: preset-size validation.
    // The old code only used a size preset as a weak score multiplier.
    // These settings make the preset act as a real guard against oversized detections.
    // ──────────────────────────────────────────────────────────────
    public bool UsePresetHardSizeFilter { get; set; } = true;
    public double PresetMaxWidthOversizeFraction { get; set; } = 0.25;
    public double PresetMaxHeightOversizeFraction { get; set; } = 0.25;
    public double PresetMaxAreaOversizeFraction { get; set; } = 0.35;
    public double PresetFitWeight { get; set; } = 0.90;
    public double PresetOversizePenaltyWeight { get; set; } = 1.80;

    // ──────────────────────────────────────────────────────────────
    // New: projection refinement.
    // After a candidate is selected, the detector shrinks the rectangle using
    // row/column foreground occupancy. This removes sparse acoustic halos/ringing
    // that inflate the bounding box.
    // ──────────────────────────────────────────────────────────────
    public bool UseProjectionRefinement { get; set; } = true;
    public double ProjectionMarginFraction { get; set; } = 0.035;
    public double ProjectionOccupancyFraction { get; set; } = 0.35;
    public double ProjectionMinAreaKeepFraction { get; set; } = 0.45;
    public double ProjectionMaxAreaGrowFraction { get; set; } = 1.10;

    // ──────────────────────────────────────────────────────────────
    // New: nuisance-zone and border-connected rejection.
    // This prevents top/right acoustic background regions and bottom label areas from becoming
    // the selected sample when they are connected to a real die through noise.
    // ──────────────────────────────────────────────────────────────
    public bool UseNuisanceZoneSuppression { get; set; } = false;
    public double IgnoreTopFraction { get; set; } = 0.00;
    public double IgnoreBottomFraction { get; set; } = 0.00;
    public bool RejectLargeBorderConnectedCandidates { get; set; } = true;
    public double BorderConnectedMaxAreaFraction { get; set; } = 0.22;
    public int BorderConnectedMinTouchCount { get; set; } = 2;

    // ──────────────────────────────────────────────────────────────
    // New: preset-guided window snapping.
    // When the coarse contour is much larger than the taught sample size, the detector searches
    // inside that coarse region for the most rectangle-like window matching the preset size.
    // ──────────────────────────────────────────────────────────────
    public bool UsePresetWindowSnap { get; set; } = true;
    public double PresetWindowSnapSearchMarginFraction { get; set; } = 0.22;
    public double PresetWindowSnapScaleMin { get; set; } = 0.88;
    public double PresetWindowSnapScaleMax { get; set; } = 1.12;
    public double PresetWindowSnapScaleStep { get; set; } = 0.06;
    public int PresetWindowSnapPixelStep { get; set; } = 3;
    public double PresetWindowSnapMinScore { get; set; } = 0.12;
    public double PresetWindowSnapOversizeTriggerFraction { get; set; } = 0.12;

    // ──────────────────────────────────────────────────────────────
    // New: inward edge snapping.
    // After preset-window snapping, each side is pulled inward to the strongest local edge.
    // This removes small acoustic halo margins without depending on a single large contour.
    // ──────────────────────────────────────────────────────────────
    public bool UseEdgeSnapRefinement { get; set; } = true;
    public double EdgeSnapSearchFraction { get; set; } = 0.18;
    public double EdgeSnapMaxShrinkFraction { get; set; } = 0.28;
    public double EdgeSnapMinAreaKeepFraction { get; set; } = 0.72;


    // ──────────────────────────────────────────────────────────────
    // Robustness upgrades for noisy SAM edge cases.
    // These keep the classical CV pipeline explainable while adding
    // stronger artifact rejection and fallback behavior.
    // ──────────────────────────────────────────────────────────────
    public bool UseLocalContrastNormalization { get; set; } = true;
    public double ClaheClipLimit { get; set; } = 2.0;
    public int ClaheTileGridSize { get; set; } = 8;

    public bool RemoveBorderConnectedForeground { get; set; } = true;

    public bool UseGlobalPresetWindowFallback { get; set; } = true;
    public double GlobalPresetWindowMinScore { get; set; } = 0.30;
    public double LowConfidenceFallbackTrigger { get; set; } = 0.62;

    public bool UseInternalStructureScore { get; set; } = true;
    public double InternalStructureWeight { get; set; } = 0.55;

    public double HighConfidenceThreshold { get; set; } = 0.78;
    public double MediumConfidenceThreshold { get; set; } = 0.50;


    // RFGaN TSV fractions inside sample rect
    public double TsvFracX1 { get; set; } = 0.30;
    public double TsvFracX2 { get; set; } = 0.68;
    public double TsvFracY1 { get; set; } = 0.14;
    public double TsvFracY2 { get; set; } = 0.82;

    public bool HasTsv => TsvFracX2 > TsvFracX1 && TsvFracY2 > TsvFracY1;
}

public static class SamProfiles
{
    public static SamDetectionProfile Get(SampleType sampleType)
    {
        return sampleType switch
        {
            SampleType.GenericSam => new SamDetectionProfile
            {
                Name = "Generic SAM",
                MinAreaFraction = 0.01,
                MaxAreaFraction = 0.95,
                OpenKernelFraction = 0.010,
                CloseKernelFraction = 0.030,
                PaddingFraction = 0.006,
                AreaWeight = 1.25,
                SolidityWeight = 1.10,
                ExtentWeight = 0.85,
                CenterWeight = 0.70,
                BorderPenaltyWeight = 0.20,
                TopBandPenaltyWeight = 0.16,
                AspectPenaltyWeight = 0.32,
                MaxAspectRatio = 5.5,
                TopBandFraction = 0.12,
                UsePresetHardSizeFilter = true,
                PresetMaxWidthOversizeFraction = 0.30,
                PresetMaxHeightOversizeFraction = 0.30,
                PresetMaxAreaOversizeFraction = 0.45,
                PresetFitWeight = 0.75,
                PresetOversizePenaltyWeight = 1.40,
                UseProjectionRefinement = true,
                ProjectionMarginFraction = 0.035,
                ProjectionOccupancyFraction = 0.32,
                ProjectionMinAreaKeepFraction = 0.40,
                ProjectionMaxAreaGrowFraction = 1.12,
                UseNuisanceZoneSuppression = false,
                IgnoreTopFraction = 0.00,
                IgnoreBottomFraction = 0.00,
                RejectLargeBorderConnectedCandidates = true,
                BorderConnectedMaxAreaFraction = 0.28,
                BorderConnectedMinTouchCount = 2,
                UsePresetWindowSnap = false,
                PresetWindowSnapSearchMarginFraction = 0.22,
                PresetWindowSnapScaleMin = 0.90,
                PresetWindowSnapScaleMax = 1.10,
                PresetWindowSnapScaleStep = 0.05,
                PresetWindowSnapPixelStep = 4,
                PresetWindowSnapMinScore = 0.12,
                PresetWindowSnapOversizeTriggerFraction = 0.15,
                UseEdgeSnapRefinement = false,
                EdgeSnapSearchFraction = 0.16,
                EdgeSnapMaxShrinkFraction = 0.24,
                EdgeSnapMinAreaKeepFraction = 0.70,
                TsvFracX1 = 0,
                TsvFracX2 = 0,
                TsvFracY1 = 0,
                TsvFracY2 = 0
            },

            SampleType.RfGanWithTsv => new SamDetectionProfile
            {
                Name = "RFGaN (with TSV)",
                MinAreaFraction = 0.01,
                MaxAreaFraction = 0.95,
                OpenKernelFraction = 0.010,
                CloseKernelFraction = 0.030,

                // Important: padding was one reason the green sample box became too large.
                // For delamination % the sample ROI should be tight, not padded.
                PaddingFraction = 0.000,

                // The old detector rewarded large areas quite strongly. For a known RFGaN die,
                // the expected-size/preset score should dominate more than raw contour size.
                AreaWeight = 0.70,
                SolidityWeight = 1.15,
                ExtentWeight = 1.05,
                CenterWeight = 0.75,
                BorderPenaltyWeight = 0.28,
                TopBandPenaltyWeight = 0.28,
                AspectPenaltyWeight = 0.45,
                MaxAspectRatio = 5.0,
                TopBandFraction = 0.12,

                UsePresetHardSizeFilter = true,
                PresetMaxWidthOversizeFraction = 0.22,
                PresetMaxHeightOversizeFraction = 0.22,
                PresetMaxAreaOversizeFraction = 0.32,
                PresetFitWeight = 1.15,
                PresetOversizePenaltyWeight = 2.20,

                UseProjectionRefinement = true,
                ProjectionMarginFraction = 0.030,
                ProjectionOccupancyFraction = 0.35,
                ProjectionMinAreaKeepFraction = 0.45,
                ProjectionMaxAreaGrowFraction = 1.08,

                UseNuisanceZoneSuppression = true,
                IgnoreTopFraction = 0.035,
                IgnoreBottomFraction = 0.12,
                RejectLargeBorderConnectedCandidates = true,
                BorderConnectedMaxAreaFraction = 0.20,
                BorderConnectedMinTouchCount = 2,

                UsePresetWindowSnap = true,
                PresetWindowSnapSearchMarginFraction = 0.28,
                PresetWindowSnapScaleMin = 0.88,
                PresetWindowSnapScaleMax = 1.12,
                PresetWindowSnapScaleStep = 0.06,
                PresetWindowSnapPixelStep = 3,
                PresetWindowSnapMinScore = 0.12,
                PresetWindowSnapOversizeTriggerFraction = 0.10,

                UseEdgeSnapRefinement = true,
                EdgeSnapSearchFraction = 0.18,
                EdgeSnapMaxShrinkFraction = 0.28,
                EdgeSnapMinAreaKeepFraction = 0.72,

                TsvFracX1 = 0.30,
                TsvFracX2 = 0.68,
                TsvFracY1 = 0.14,
                TsvFracY2 = 0.82
            },

            _ => throw new ArgumentOutOfRangeException(nameof(sampleType), sampleType, null)
        };
    }
}

public sealed class SamDetectionResult
{
    public bool Success { get; set; }

    // Axis-aligned rectangle kept for compatibility and for deriving the TSV ROI.
    // Delamination percentages should prefer SamplePolygon/SampleMaskAreaPixels when available.
    public Rect SampleRect { get; set; }
    public Rect? TsvRect { get; set; }
    public OpenCvSharp.Point[] TsvPolygon { get; set; } = Array.Empty<OpenCvSharp.Point>();

    // Pixel-accurate sample boundary in full-image coordinates.
    // This is filled into a mask when calculating delamination percentage, so the denominator is
    // the actual detected sample silhouette rather than SampleRect.Width * SampleRect.Height.
    public OpenCvSharp.Point[] SamplePolygon { get; set; } = Array.Empty<OpenCvSharp.Point>();
    public int SampleMaskAreaPixels { get; set; }
    public bool HasSampleMask => SamplePolygon.Length >= 3 && SampleMaskAreaPixels > 0;

    public double Confidence { get; set; }
    public string ConfidenceLevel { get; set; } = "Low";
    public List<string> Warnings { get; set; } = new();
    public bool UsedFallbackDetector { get; set; }
    public string FailureReason { get; set; } = string.Empty;
    public SampleType SampleType { get; set; }
    public string ProfileName { get; set; } = string.Empty;

    // Exact runtime recipe used for this detection. Stored as a clone to prevent
    // later preset edits from changing the interpretation of the result.
    public AnalysisRecipeSnapshot? RecipeSnapshot { get; set; }
    public string RecipeSnapshotId { get; set; } = string.Empty;
    public string RecipeHashSha256 { get; set; } = string.Empty;

    // Structured trace used by review/debug views.
    // It records what the CV detector considered at each major stage.
    public SamDetectionDiagnostics Diagnostics { get; set; } = new();

    // Optional full diagnostics payload requested through DetectionDiagnosticsLevel.Full.
    // Production callers normally leave this null. CV Calibration Studio owns/disposes this
    // payload when it explicitly requests full stage images and reference-ROI metrics.
    public DetectionCalibrationResult? CalibrationDiagnostics { get; set; }
}

public sealed class SamDetectionDiagnostics
{
    public string ProfileName { get; set; } = string.Empty;
    public int ImageWidth { get; set; }
    public int ImageHeight { get; set; }
    public List<SamDetectionStep> Steps { get; set; } = new();
}

public sealed class SamDetectionStep
{
    public int Index { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public List<SamDetectionCandidateSnapshot> Candidates { get; set; } = new();
}

public sealed class SamDetectionCandidateSnapshot
{
    public string CandidateId { get; set; } = string.Empty;
    public int Rank { get; set; }
    public string Source { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public double Score { get; set; }
    public Rect Rect { get; set; }
    public OpenCvSharp.Point[] Polygon { get; set; } = Array.Empty<OpenCvSharp.Point>();
    public Dictionary<string, double> Metrics { get; set; } = new();
    public string Notes { get; set; } = string.Empty;
}
