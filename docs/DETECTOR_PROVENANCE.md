# Provenance: original Phase 4.3 detector

Source: `RFGAN_SamVoidQuantifier_Phase4_3_UnifiedEngine.zip` supplied in this conversation.
All 10 source files under `RFGAN.Engine/Legacy/Detection/` and 9 selected C# files under `Legacy/Services` and `Legacy/Models` except the new `LoggerHelper.cs` are byte-for-byte copies of their original Phase 4.3 sources. The `SamAdvancedTsvDetectorAdapter.RunPipeline` algorithm is **not rewritten**.

Preserved components:
- `Detection/Contracts/ISampleDetectionEngine`, `SampleDetectionRequest`, options and diagnostics level
- `Detection/Engine/SampleDetectionEngine` / `DetectionContext`
- `Detection/Algorithms/AdvancedClassicalDetectionStrategy` / strategy interface
- `Detection/Diagnostics/DetectionArtifactExporter`
- Original advanced OpenCV adapter and model types, calibration settings and original ZGT reader
- Recipe snapshot/parameter/geometry models

A minimal new `Legacy/Services/LoggerHelper.cs` implements the original logging methods without needing the original Serilog dependency. This is a compatibility seam, *not* the original logging implementation.

A new `BatchProcessor` is the only production batch caller of `ISampleDetectionEngine`. It creates the per-run recipe snapshot, handles review/coverage checks and delegates to the original adapter through the preserved engine contract.

The original detector's calibration/accuracy has not been independently verified on this v1; keeping its implementation unchanged does not guarantee correctness. Build and regression-test with representative annotated SAM scans.
