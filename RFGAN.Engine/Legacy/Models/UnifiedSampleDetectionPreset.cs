using System.Drawing;
using System.Text.Json.Serialization;
using RFGAN.SampleMapper.Services;

namespace RFGAN.SampleMapper.Models;

public sealed class UnifiedSampleDetectionPreset
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string PresetName { get; set; } = "Balanced";
    public string SampleType { get; set; } = RFGAN.SampleMapper.Services.SampleType.RfGanWithTsv.ToString();
    public string Description { get; set; } = string.Empty;
    public int Version { get; set; } = 2;
    public bool IsBuiltIn { get; set; }
    public bool IsLegacyWrapped { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime UpdatedAt { get; set; } = DateTime.Now;

    public SampleGeometrySettings Geometry { get; set; } = new();
    public SampleDetectionParameters DetectionParameters { get; set; } = SampleDetectionParameters.CreateDefault();

    // Published advanced CV settings. Older presets deserialize with null and are
    // explicitly migrated from the last published calibration configuration.
    public SamAdvancedDetectionParams? AdvancedDetectionParameters { get; set; }
    public SamAdvancedLearnedGeometry? AdvancedGeometry { get; set; }

    // Product-specific decision limits travel with the recipe and analysis result.
    public DelamDecisionThresholds DecisionThresholds { get; set; } = new();
    public string ContentHashSha256 { get; set; } = string.Empty;

    public PresetVisualizationSettings Visualization { get; set; } = new();

    [JsonIgnore]
    public RFGAN.SampleMapper.Services.SampleType ParsedSampleType
        => Enum.TryParse(SampleType, ignoreCase: true, out RFGAN.SampleMapper.Services.SampleType type)
            ? type
            : RFGAN.SampleMapper.Services.SampleType.RfGanWithTsv;

    public override string ToString() => PresetName;

    public UnifiedSampleDetectionPreset Clone() => new()
    {
        Id = Id,
        PresetName = PresetName,
        SampleType = SampleType,
        Description = Description,
        Version = Version,
        IsBuiltIn = IsBuiltIn,
        IsLegacyWrapped = IsLegacyWrapped,
        CreatedAt = CreatedAt,
        UpdatedAt = UpdatedAt,
        Geometry = (Geometry ?? new SampleGeometrySettings()).Clone(),
        DetectionParameters = (DetectionParameters ?? SampleDetectionParameters.CreateDefault()).Clone(),
        AdvancedDetectionParameters = AdvancedDetectionParameters?.Clone(),
        AdvancedGeometry = AdvancedGeometry?.Clone(),
        DecisionThresholds = (DecisionThresholds ?? new DelamDecisionThresholds()).Clone(),
        ContentHashSha256 = ContentHashSha256,
        Visualization = (Visualization ?? new PresetVisualizationSettings()).Clone()
    };

    public SamplePreset ToLegacySamplePresetBridge()
    {
        Rectangle? outer = Geometry.OuterRoiPixels;
        int imageW = Math.Max(1, Geometry.ReferenceImageWidth > 0 ? Geometry.ReferenceImageWidth : (outer?.Right ?? Geometry.ExpectedWidth));
        int imageH = Math.Max(1, Geometry.ReferenceImageHeight > 0 ? Geometry.ReferenceImageHeight : (outer?.Bottom ?? Geometry.ExpectedHeight));
        int expectedW = Math.Max(1, Geometry.ExpectedWidth > 0 ? Geometry.ExpectedWidth : (outer?.Width ?? 1));
        int expectedH = Math.Max(1, Geometry.ExpectedHeight > 0 ? Geometry.ExpectedHeight : (outer?.Height ?? 1));

        double normLeft = outer.HasValue ? outer.Value.X / (double)imageW : 0.0;
        double normTop = outer.HasValue ? outer.Value.Y / (double)imageH : 0.0;
        double normWidth = outer.HasValue ? outer.Value.Width / (double)imageW : expectedW / (double)imageW;
        double normHeight = outer.HasValue ? outer.Value.Height / (double)imageH : expectedH / (double)imageH;

        double tolW = Geometry.WidthTolerance > 0 && expectedW > 0 ? Geometry.WidthTolerance / (double)expectedW : DetectionParameters.WidthTolerance;
        double tolH = Geometry.HeightTolerance > 0 && expectedH > 0 ? Geometry.HeightTolerance / (double)expectedH : DetectionParameters.HeightTolerance;

        return new SamplePreset
        {
            Id = Id,
            Name = PresetName,
            SampleType = ParsedSampleType,
            ReferenceImagePath = Geometry.ReferenceImagePath,
            ReferenceImageWidth = imageW,
            ReferenceImageHeight = imageH,
            ReferenceRectWidth = expectedW,
            ReferenceRectHeight = expectedH,
            ToleranceFraction = Math.Clamp(Math.Max(tolW, tolH), 0.02, 0.50),
            NormLeft = Math.Clamp(normLeft, 0, 1),
            NormTop = Math.Clamp(normTop, 0, 1),
            NormWidth = Math.Clamp(normWidth, 0.001, 1),
            NormHeight = Math.Clamp(normHeight, 0.001, 1),
            UnifiedPresetId = Id
        };
    }
}

public sealed class SampleGeometrySettings
{
    public Rectangle? OuterRoiPixels { get; set; }

    public int ExpectedWidth { get; set; }
    public int ExpectedHeight { get; set; }
    public int WidthTolerance { get; set; }
    public int HeightTolerance { get; set; }
    public double ExpectedAspectRatio { get; set; }
    public string Orientation { get; set; } = "Unknown";

    public int ReferenceImageWidth { get; set; }
    public int ReferenceImageHeight { get; set; }
    public string ReferenceImagePath { get; set; } = string.Empty;

    public List<SpecialRelativeRoi> SpecialRois { get; set; } = new();

    /// <summary>
    /// Outline of the sample in reference-image pixel coordinates.
    ///
    /// The polygon is the precise shape; <see cref="OuterRoiPixels"/> remains its axis-aligned
    /// bounding box so that every existing consumer (tolerance scoring, crop logic, combiner,
    /// detector geometry) keeps working unchanged. Rectangle-only presets saved before polygon
    /// support are auto-converted to a 4-point polygon on load, so this list is never empty for
    /// a preset that has geometry at all.
    /// </summary>
    public List<Point> OutlinePolygonPixels { get; set; } = new();

    /// <summary>True when the outline is a real traced shape rather than a converted rectangle.</summary>
    public bool HasTracedOutline { get; set; }

    /// <summary>
    /// Path to the reference image copy stored beside the preset, used by the welcome tiles.
    /// </summary>
    public string ReferenceImageCopyPath { get; set; } = string.Empty;

    /// <summary>Path to the small cached thumbnail rendered for the welcome tiles.</summary>
    public string ThumbnailPath { get; set; } = string.Empty;

    /// <summary>
    /// Ensures the polygon is populated, converting a rectangle-only preset into the four
    /// corners of its bounding box. Idempotent.
    /// </summary>
    public void EnsureOutlinePolygon()
    {
        if (OutlinePolygonPixels.Count >= 3)
            return;

        if (OuterRoiPixels is not { Width: > 1, Height: > 1 } rect)
            return;

        OutlinePolygonPixels = new List<Point>
        {
            new(rect.Left, rect.Top),
            new(rect.Right, rect.Top),
            new(rect.Right, rect.Bottom),
            new(rect.Left, rect.Bottom)
        };
        HasTracedOutline = false;
    }

    /// <summary>
    /// Recomputes <see cref="OuterRoiPixels"/> from the polygon so the rectangle-based
    /// pipeline always agrees with the traced shape.
    /// </summary>
    public void SyncBoundingRectFromPolygon()
    {
        if (OutlinePolygonPixels.Count < 3)
            return;

        int minX = OutlinePolygonPixels.Min(p => p.X);
        int minY = OutlinePolygonPixels.Min(p => p.Y);
        int maxX = OutlinePolygonPixels.Max(p => p.X);
        int maxY = OutlinePolygonPixels.Max(p => p.Y);

        OuterRoiPixels = new Rectangle(minX, minY, Math.Max(1, maxX - minX), Math.Max(1, maxY - minY));
        ExpectedWidth = OuterRoiPixels.Value.Width;
        ExpectedHeight = OuterRoiPixels.Value.Height;
        ExpectedAspectRatio = ExpectedHeight > 0 ? ExpectedWidth / (double)ExpectedHeight : ExpectedAspectRatio;
        Orientation = ExpectedWidth >= ExpectedHeight ? "Horizontal" : "Vertical";
    }

    public SampleGeometrySettings Clone() => new()
    {
        OutlinePolygonPixels = OutlinePolygonPixels.ToList(),
        HasTracedOutline = HasTracedOutline,
        ReferenceImageCopyPath = ReferenceImageCopyPath,
        ThumbnailPath = ThumbnailPath,
        OuterRoiPixels = OuterRoiPixels,
        ExpectedWidth = ExpectedWidth,
        ExpectedHeight = ExpectedHeight,
        WidthTolerance = WidthTolerance,
        HeightTolerance = HeightTolerance,
        ExpectedAspectRatio = ExpectedAspectRatio,
        Orientation = Orientation,
        ReferenceImageWidth = ReferenceImageWidth,
        ReferenceImageHeight = ReferenceImageHeight,
        ReferenceImagePath = ReferenceImagePath,
        SpecialRois = SpecialRois.Select(r => r.Clone()).ToList()
    };
}

public sealed class SpecialRelativeRoi
{
    public string Name { get; set; } = "TSV";
    public string Type { get; set; } = "TSV";

    public double RelativeX { get; set; }
    public double RelativeY { get; set; }
    public double RelativeWidth { get; set; }
    public double RelativeHeight { get; set; }

    public bool Enabled { get; set; } = true;

    public SpecialRelativeRoi Clone() => new()
    {
        Name = Name,
        Type = Type,
        RelativeX = RelativeX,
        RelativeY = RelativeY,
        RelativeWidth = RelativeWidth,
        RelativeHeight = RelativeHeight,
        Enabled = Enabled
    };
}

public sealed class PresetVisualizationSettings
{
    public bool ShowOuterRoiOverlay { get; set; } = true;
    public bool ShowSpecialRoiOverlay { get; set; } = true;
    public bool ShowMaskOverlay { get; set; } = true;
    public string PreferredColorMap { get; set; } = "Default";
    public double OverlayOpacity { get; set; } = 0.35;

    public PresetVisualizationSettings Clone() => new()
    {
        ShowOuterRoiOverlay = ShowOuterRoiOverlay,
        ShowSpecialRoiOverlay = ShowSpecialRoiOverlay,
        ShowMaskOverlay = ShowMaskOverlay,
        PreferredColorMap = PreferredColorMap,
        OverlayOpacity = OverlayOpacity
    };
}

public sealed class SpecialAbsoluteRoiResult
{
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public OpenCvSharp.Rect Rect { get; set; }
}
