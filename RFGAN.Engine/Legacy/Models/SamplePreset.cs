using System.Text.Json.Serialization;
using RFGAN.SampleMapper.Services; // for SampleType

namespace RFGAN.SampleMapper.Models;

public class SamplePreset
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = string.Empty;
    public SampleType SampleType { get; set; }

    // Normalized (0-1) relative to the reference image dimensions
    public double NormLeft { get; set; }
    public double NormTop { get; set; }
    public double NormWidth { get; set; }
    public double NormHeight { get; set; }

    [JsonIgnore]
    public double AreaFraction => NormWidth * NormHeight;

    [JsonIgnore]
    public double AspectRatio => NormHeight > 0 && NormWidth > 0 ? NormWidth / NormHeight : 1.0;

    // Reference-image metadata and tolerance for confidence scoring.
    // ToleranceFraction is relative. Example: 0.10 = ±10% normal tolerance.
    public int ReferenceImageWidth { get; set; }
    public int ReferenceImageHeight { get; set; }
    public int ReferenceRectWidth { get; set; }
    public int ReferenceRectHeight { get; set; }
    public double ToleranceFraction { get; set; } = 0.10;

    // Optional: original reference image path for debugging
    public string ReferenceImagePath { get; set; } = string.Empty;

    // Non-empty when this legacy-shaped item is a compatibility facade over a
    // UnifiedSampleDetectionPreset. MainForm can still consume SamplePreset safely,
    // while calibration/test mode uses the unified object directly.
    public string UnifiedPresetId { get; set; } = string.Empty;

    [JsonIgnore]
    public bool IsUnifiedPresetBridge => !string.IsNullOrWhiteSpace(UnifiedPresetId);
}