using RFGAN.SampleMapper.Detection.Engine;
using RFGAN.SampleMapper.Services;

namespace RFGAN.SampleMapper.Detection.Algorithms;

/// <summary>
/// Internal algorithm contract. The engine owns workflow/policy; a strategy owns the
/// mathematical/computer-vision implementation used to produce a detection result.
/// </summary>
internal interface IDetectionStrategy
{
    string Name { get; }

    SamDetectionResult Detect(
        DetectionContext context,
        CancellationToken cancellationToken);
}
