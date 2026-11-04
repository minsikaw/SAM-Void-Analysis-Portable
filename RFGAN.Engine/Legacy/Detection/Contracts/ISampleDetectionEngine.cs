using RFGAN.SampleMapper.Services;

namespace RFGAN.SampleMapper.Detection.Contracts;

/// <summary>
/// The single public contract for automatic sample detection.
/// UI, batch-processing and calibration callers should converge on this interface.
/// </summary>
public interface ISampleDetectionEngine
{
    SamDetectionResult Detect(
        SampleDetectionRequest request,
        CancellationToken cancellationToken = default);
}
