namespace RFGAN.SampleMapper.Detection.Contracts;

/// <summary>
/// Controls how much diagnostic information a caller requests from sample detection.
/// Phase 4.2 preserves the diagnostics produced by the existing advanced detector;
/// richer calibration-stage ownership will be unified in a later migration phase.
/// </summary>
public enum DetectionDiagnosticsLevel
{
    None = 0,
    Summary = 1,
    Full = 2
}
