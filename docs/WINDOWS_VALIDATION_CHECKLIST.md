# Checklist before any lab trial

## Build

- [ ] Open `.sln` on Windows with Visual Studio 2022 and .NET 8 desktop workload.
- [ ] Restore NuGet dependencies with OpenCvSharp runtime.
- [ ] Build Release x64 with zero errors.
- [ ] Run `build.ps1` and verify all smoke tests pass.
- [ ] Run with and without a saved detector calibration file in LocalAppData.

## Input and UX

- [ ] Import `DemoData` and verify two image/mask pairs are found.
- [ ] Confirm identities and try editing one ID in the grid.
- [ ] Import an actual ZGT batch (including incomplete scans) with at least two gate settings.
- [ ] Confirm acquired-validity masks are separate from defect masks.
- [ ] Verify missing, multiple, nonbinary and wrong-size defect masks are surfaced.
- [ ] Check preview + parameter form and confidence warnings.
- [ ] Review a sample with a wrongly detected ROI by drawing a manual rectangle.

## Analysis and export

- [ ] Verify known 25% synthetic defect example via smoke test.
- [ ] Ensure invalid acquisition pixels are excluded from numerator and denominator.
- [ ] Verify partial batch cancellation preserves already completed rows.
- [ ] Change the defect mask after analysis; completed export must refuse stale inputs.
- [ ] Change detector settings; previous measurements must be invalidated.
- [ ] Export CSV/JSON and compare all rows with the in-app statuses.
- [ ] Verify experimental intensity-threshold results remain flagged exploratory.

## Scientific validation (required before production)

- [ ] Establish annotation standards for whole-die/TSV ROIs and defects.
- [ ] Benchmark original detector by sample category, wafer and acquisition mode.
- [ ] Evaluate delamination percentage error vs expert masks or independent reference.
- [ ] Document gating, normalization, calibration and uncertainty.
- [ ] Define acceptance criteria with the microscopy/lab supervisor.
