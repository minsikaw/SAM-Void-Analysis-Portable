using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using RFGAN.SampleMapper.Models;

namespace RFGAN.SampleMapper.Services;

/// <summary>
/// C# port of DetectionParams from sam_sample_tsv_detector_advanced_geometry.py.
/// Geometry is deliberately kept separate from these non-geometry CV knobs.
/// </summary>
public sealed class SamAdvancedDetectionParams
{
    public double ClaheClipLimit { get; set; } = 2.5;
    public int ClaheTileSize { get; set; } = 8;
    public double BlurDivisor { get; set; } = 120.0;

    public double BrightMaskPercentile { get; set; } = 55.0;
    public int BorderMargin { get; set; } = 2;
    public double MorphDivisor { get; set; } = 150.0;

    public double ContrastWeight { get; set; } = 2.80;
    public double BoundaryEdgeWeight { get; set; } = 0.85;
    public double InternalStructureWeight { get; set; } = 0.35;
    public double InsideBrightnessWeight { get; set; } = 0.18;
    public double CentralityWeight { get; set; } = 0.12;
    public double AspectQualityWeight { get; set; } = 0.08;

    public bool LocalEdgeRefinement { get; set; } = true;
    public int ShowTopCandidates { get; set; } = 12;

    public SamAdvancedDetectionParams Clone() => (SamAdvancedDetectionParams)MemberwiseClone();

    public string ToSummary()
        => $"CLAHE={ClaheClipLimit:0.###}/{ClaheTileSize}, blurDiv={BlurDivisor:0.#}, brightPct={BrightMaskPercentile:0.#}, border={BorderMargin}, morphDiv={MorphDivisor:0.#}, weights(C={ContrastWeight:0.##}, E={BoundaryEdgeWeight:0.##}, I={InternalStructureWeight:0.##}, B={InsideBrightnessWeight:0.##}, center={CentralityWeight:0.##}, aspect={AspectQualityWeight:0.##}), edgeRefine={LocalEdgeRefinement}, topN={ShowTopCandidates}";
}

public sealed class SamAdvancedRelativePoint
{
    public double X { get; set; }
    public double Y { get; set; }

    public SamAdvancedRelativePoint() { }
    public SamAdvancedRelativePoint(double x, double y)
    {
        X = x;
        Y = y;
    }
}

/// <summary>
/// C# port of LearnedGeometry from the Python detector.
/// Sample polygon is normalized to full image size.
/// TSV polygon is normalized relative to the detected sample rectangle.
/// </summary>
public sealed class SamAdvancedLearnedGeometry
{
    public List<SamAdvancedRelativePoint>? SamplePolygonNorm { get; set; }
    public double? SampleWidthFrac { get; set; }
    public double? SampleHeightFrac { get; set; }
    public double? SampleAspect { get; set; }
    public List<SamAdvancedRelativePoint>? TsvPolygonRel { get; set; }

    public bool HasSampleGeometry()
        => SampleWidthFrac.HasValue && SampleHeightFrac.HasValue && SampleAspect.HasValue;

    public bool HasTsvGeometry()
        => TsvPolygonRel is { Count: >= 3 };

    public SamAdvancedLearnedGeometry Clone() => new()
    {
        SamplePolygonNorm = SamplePolygonNorm?.Select(p => new SamAdvancedRelativePoint(p.X, p.Y)).ToList(),
        SampleWidthFrac = SampleWidthFrac,
        SampleHeightFrac = SampleHeightFrac,
        SampleAspect = SampleAspect,
        TsvPolygonRel = TsvPolygonRel?.Select(p => new SamAdvancedRelativePoint(p.X, p.Y)).ToList()
    };

    public static SamAdvancedLearnedGeometry FromRectangles(Rectangle sampleRect, System.Drawing.Size imageSize, Rectangle? tsvRect)
    {
        int iw = Math.Max(1, imageSize.Width);
        int ih = Math.Max(1, imageSize.Height);
        int sw = Math.Max(1, sampleRect.Width);
        int sh = Math.Max(1, sampleRect.Height);

        var geom = new SamAdvancedLearnedGeometry
        {
            SampleWidthFrac = sw / (double)iw,
            SampleHeightFrac = sh / (double)ih,
            SampleAspect = sh / (double)sw,
            SamplePolygonNorm = new List<SamAdvancedRelativePoint>
            {
                new(sampleRect.Left / (double)iw, sampleRect.Top / (double)ih),
                new(sampleRect.Right / (double)iw, sampleRect.Top / (double)ih),
                new(sampleRect.Right / (double)iw, sampleRect.Bottom / (double)ih),
                new(sampleRect.Left / (double)iw, sampleRect.Bottom / (double)ih)
            }
        };

        if (tsvRect.HasValue && tsvRect.Value.Width > 1 && tsvRect.Value.Height > 1)
        {
            Rectangle t = tsvRect.Value;
            geom.TsvPolygonRel = new List<SamAdvancedRelativePoint>
            {
                new((t.Left - sampleRect.Left) / (double)sw, (t.Top - sampleRect.Top) / (double)sh),
                new((t.Right - sampleRect.Left) / (double)sw, (t.Top - sampleRect.Top) / (double)sh),
                new((t.Right - sampleRect.Left) / (double)sw, (t.Bottom - sampleRect.Top) / (double)sh),
                new((t.Left - sampleRect.Left) / (double)sw, (t.Bottom - sampleRect.Top) / (double)sh)
            };
        }

        return geom;
    }
}

public sealed class SamAdvancedDetectorConfig
{
    public SamAdvancedDetectionParams Params { get; set; } = new();
    public SamAdvancedLearnedGeometry Geometry { get; set; } = new();
    public DateTime UpdatedAt { get; set; } = DateTime.Now;
    public string UpdatedBy { get; set; } = "default";
}

public sealed class SamAdvancedCandidate
{
    public string Source { get; set; } = string.Empty;
    public double Score { get; set; }
    public Rect BBox { get; set; }
    public Dictionary<string, double> Details { get; set; } = new();
}

internal sealed class SamAdvancedPipelineResult : IDisposable
{
    public bool Success { get; set; }
    public string FailureReason { get; set; } = string.Empty;
    public Rect? SampleBBox { get; set; }
    public OpenCvSharp.Point[] SamplePolygon { get; set; } = Array.Empty<OpenCvSharp.Point>();
    public Rect? TsvBBox { get; set; }
    public OpenCvSharp.Point[] TsvPolygon { get; set; } = Array.Empty<OpenCvSharp.Point>();
    public List<SamAdvancedCandidate> Candidates { get; set; } = new();
    public List<DetectionStageImage> Stages { get; set; } = new();
    public List<string> Log { get; set; } = new();
    public long ProcessingTimeMs { get; set; }

    public void Dispose()
    {
        foreach (var stage in Stages)
            stage.Dispose();
        Stages.Clear();
    }
}

internal sealed class SamCvDetectorRequest
{
    public SamAdvancedDetectionParams? Parameters { get; set; }
    public SamAdvancedLearnedGeometry? Geometry { get; set; }
    public UnifiedSampleDetectionPreset? ActivePreset { get; set; }
    public AnalysisRecipeSnapshot? RecipeSnapshot { get; set; }
    public Rect? ReferenceRect { get; set; }
    public string? DebugOutputDir { get; set; }
    public string? DebugBaseName { get; set; }
    public bool SaveDebugStages { get; set; }
    public bool SaveFinalReviewOverlay { get; set; }

    // Internal bridge used by the Phase 4 unified engine. Full calibration diagnostics are
    // requested through the normal Detect() operation rather than a second detection API.
    public bool CaptureCalibrationDiagnostics { get; set; }
}

internal interface ISamCvDetectorAdapter
{
    string Name { get; }
    SamDetectionResult Detect(string imagePath, SampleType sampleType, SamCvDetectorRequest? request = null);
}
