using OpenCvSharp;
using RFGAN.SampleMapper.Models;
using RFGAN.SampleMapper.Services;

namespace RFGAN.SampleMapper.Detection.Contracts;

/// <summary>
/// Complete description of one automatic sample-detection job.
///
/// The caller is responsible for resolving the active preset into an AnalysisRecipeSnapshot
/// before constructing this request. The detection engine deliberately does not read preset
/// stores or other application-global configuration.
/// </summary>
public sealed class SampleDetectionRequest
{
    public required string ImagePath { get; init; }

    public required SampleType SampleType { get; init; }

    /// <summary>
    /// Exact, cloned-at-execution recipe used for reproducible detection.
    /// </summary>
    public required AnalysisRecipeSnapshot Recipe { get; init; }

    /// <summary>
    /// Optional manually supplied reference rectangle used for evaluation/calibration diagnostics.
    /// It is not an instruction to override the automatic detector result.
    /// </summary>
    public Rect? ReferenceRect { get; init; }

    public DetectionExecutionOptions Options { get; init; } = new();
}
