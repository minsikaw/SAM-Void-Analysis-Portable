using OpenCvSharp;
using RFGAN.SampleMapper.Detection.Contracts;
using RFGAN.SampleMapper.Models;
using RFGAN.SampleMapper.Services;

namespace RFGAN.SampleMapper.Detection.Engine;

/// <summary>
/// Normalized, engine-owned runtime state for one detection execution.
/// Mutable application/preset objects are cloned at the engine boundary so strategies cannot
/// accidentally observe later UI/configuration changes during the same detection run.
/// </summary>
internal sealed class DetectionContext
{
    private DetectionContext(
        string imagePath,
        SampleType sampleType,
        AnalysisRecipeSnapshot recipe,
        Rect? referenceRect,
        DetectionExecutionOptions options)
    {
        ImagePath = imagePath;
        SampleType = sampleType;
        Recipe = recipe;
        ReferenceRect = referenceRect;
        Options = options;
    }

    public string ImagePath { get; }
    public SampleType SampleType { get; }
    public AnalysisRecipeSnapshot Recipe { get; }
    public Rect? ReferenceRect { get; }
    public DetectionExecutionOptions Options { get; }

    public static DetectionContext Create(SampleDetectionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Recipe);

        if (string.IsNullOrWhiteSpace(request.ImagePath))
            throw new ArgumentException("A sample-detection image path is required.", nameof(request));

        return new DetectionContext(
            request.ImagePath,
            request.SampleType,
            request.Recipe.Clone(),
            request.ReferenceRect,
            (request.Options ?? new DetectionExecutionOptions()).Clone());
    }
}
