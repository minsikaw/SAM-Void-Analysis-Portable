# V1 architecture and data contracts

```
MainForm (WinForms)                 RFGAN.Desktop
  ├── DetectorSettingsForm
  └── ReviewForm
          │
          ▼
BatchImporter / BatchInputValidator RFGAN.Engine/Batch
          │
          ▼
BatchProcessor                     Sequential, cancel between samples
   │                         ┌─────────────────────────────┐
   ├── ISampleDetectionEngine├──→ Phase 4.3 classical engine
   │                         └─────────────────────────────┘
   └── RoiQuantifier                Pixel masks, valid coverage, TSV optional
          │
          ▼
BatchSample + Measurement           Per-sample state and hashes
          │
          ▼
BatchExporter                       Atomic file updates, JSON + CSV
```

## Invariants

1. Raw ZGT **validity** is never used as a delamination mask.
2. No defect-mask file in external-mask mode means `Blocked`.
3. A detected sample ROI is required (polygon or rectangle); if detection fails the operator may draw a manual ROI.
4. Exact image dimensions for defect and validity masks are required.
5. Zero measured ROI pixels are fatal to quantification.
6. Unconfirmed identities, low confidence, incomplete valid-data coverage and nonbinary masks need review.
7. Manual ROI approval is tied to hashes of the image and recipe. Edits invalidate previous approvals.
8. A completed result cannot be exported if the source, C-scan, defect mask or validity-mask bytes have changed.
9. All export rows, including Blocked/Error/NeedsReview, retain their true status. A value appearing in a NeedsReview row is provisional.
10. Experimental threshold results are identified as exploratory in CSV/JSON even after review.

## Production follow-ups

- True independent xUnit/NUnit CI tests with Windows build agents.
- Labeled-scan detector regression suite with IoU, confidence calibration and per-chip accuracy.
- Native (non-normalized) floating-point C-scan amplitude as measurement representation.
- More comprehensive ZGT variants and gate-calibration metadata; explicit limits and memory-use checks.
- Manual polygon edit and separate TSV ROI review.
- Persisted session/project store, durable recovery from partial batches, audit trail.
- Quantifier validation against expert defect masks, CT, or independent experimental measurements.
- Per-channel disambiguation, tray mapping and large-dataset paging.
- Signed Windows installer / packaging, update mechanism, threat modelling, release pipeline.
