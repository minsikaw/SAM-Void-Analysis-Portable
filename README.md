# RF GaN SAM Batch Quantifier — Reworked v1.0.1 (WinForms)

A deliberately small Windows desktop prototype for batch quantification of delamination masks inside SAM sample ROIs. The **original Phase 4.3 sample-detection algorithm and engine are copied unchanged**, while ingestion, analysis, ROI review, and export live in a separate workflow. This is an engineering/research prototype, **not validated for semiconductor release decisions**.

## v1.0.1 compiler hotfix

Resolved the `CS0104` ambiguity between `System.Drawing.Size` and `OpenCvSharp.Size` in `RFGAN.Engine/Batch/RoiQuantifier.cs` by adding a local type alias. A missing `RFGAN.Engine.dll` metadata error can be a downstream symptom of this engine compile failure. The Phase 4.3 sample detector has not been modified.

After extracting this ZIP, close any prior Visual Studio solution, open this version’s `.sln`, run **Build → Clean Solution**, followed by **Build → Rebuild Solution**. If the engine still fails to compile, check the **first** error reported under the `RFGAN.Engine` project.

## Requirements and launch

- Windows 10/11, x64 recommended.
- Visual Studio 2022 with .NET desktop development workload, or .NET 8 SDK.
- Internet access to restore the two OpenCvSharp NuGet packages on the first build.

1. Extract the ZIP to a short, writable folder.
2. Open `RFGAN_BatchQuantifier_V1.sln` in Visual Studio.
3. Set **RFGAN.Desktop** as the startup project and build for **x64** / Debug.
4. Press **F5**. Alternatively run `build.ps1` in PowerShell to build and execute smoke tests.

The archive contains source code, not a compiled signed `.exe`. **No build or runtime test was possible in the generation environment**, which lacks a Windows/.NET SDK. Test on Windows before relying on results.

## Workflow

1. **Import images:** select a folder containing C-scan images (PNG, TIF, TIFF, JPG, BMP) and binary defect masks. Identities are parsed from `X1Y1`, `X01 Y02`, etc. Duplicate IDs must be resolved; unmatched or ambiguous masks can be assigned with **Assign mask**. Scan names containing `DelamDie` are treated as images, not automatically as binary defect masks.
2. **Import ZGT:** select a folder containing `.zgt` files, choose an A-scan gate and C-scan metric. The original ZGT parser converts each scan to a display PNG in a local application workspace and produces **a separate acquisition-validity mask**. This mask marks measured pixels, *not* delamination.
3. **Confirm IDs:** inspect the grid/paths and click **Confirm IDs** after correcting any sample IDs; this is an explicit operator action.
4. **Detector parameters:** edit the original Phase 4.3 `SamAdvancedDetectionParams` and optional normalized `SamAdvancedLearnedGeometry` in a property grid. This form is opened before the first ROI preview or batch run. Settings changes invalidate previous results.
5. **Preview ROI:** run automatic sample detection on one sample; display the detected die outline and TSV region. **Show mask overlay** visually highlights defect pixels only inside the sample ROI and valid measured region.
6. **Run batch:** process sequentially with progress/cancel. Missing or mismatched binary masks and invalid ROIs do not become zero-delamination successes. Low confidence/poor coverage/nonbinary masks trigger review.
7. **Review selected:** inspect the ROI and defect overlay. Drag a manual die rectangle if the auto ROI is wrong, or approve the current detected ROI; reanalyse. Manual ROI takes precedence for quantification, and approvals are bound to image and recipe hashes.
8. **Export results:** choose an output folder. `results.csv` and `results.json` include sample statuses, measured ROI sizes, percentages, confidence, masks, input hashes, detector settings, and recipe hash. Export blocks changed inputs for completed results.

## Filename conventions

Recommended simple input folder:

```
Scans/
  X1Y1.png
  X2Y1.png
  Masks/
    X1Y1_delam_mask.png
    X2Y1_delam_mask.png
```

Mask positivity: grayscale > 127 means **delamination**. A binary mask should have only pixel values 0 and 255; nonbinary values are explicitly flagged for review. Images/masks must have exactly matching width and height. This v1 requires one unique sample ID per imported row; multi-channel datasets need separate imports or explicit per-channel IDs in a future version.

## ZGT and experimental threshold mode

The ZGT reader is reused as-is from Phase 4.3. Gate boundaries are in *A-scan sample indices* (`end` exclusive); each exported C-scan is normalized per-scan for visualization. Thus PNG grayscale levels **are not calibrated absolute acoustic amplitudes**.

The default mode requires a *separate defect mask* associated with each image. ZGT files do not inherently provide a delamination ground-truth mask. For a demo only, select `ExperimentalImageThreshold`, choose a grayscale threshold and bright/dark polarity, run, and explicitly review flagged results. These percentages are exploratory image-segmentation measurements; **do not interpret them as validated physical delamination** without calibration and reference measurements.

## Status meanings

- **Ready**: required file structure is valid, but the sample has not been analysed.
- **Blocked**: missing defect mask, bad format, incorrect dimensions, or zero valid measured ROI.
- **NeedsReview**: identity not confirmed, low ROI confidence, low measured-area coverage, nonbinary defect-mask pixels, experimental threshold without review, or a failed automatic ROI.
- **Completed**: the selected method returned a measurement and review constraints are satisfied. `Exploratory=true` still means research-only, not physical validation.
- **Error**: unhandled per-sample processing failure, explicitly logged in the row.

## Calculations

For sample ROI `R`, acquisition-validity mask `V`, and delamination mask `M`, let `D = R ∩ V`. Then:

```
valid coverage (%) = 100 * |D| / |R|
delamination (%)   = 100 * |D ∩ M| / |D|
```

If `|R| == 0` or `|D| == 0`, the sample is blocked. TSV percentage is reported only if the automatic detector returns usable TSV geometry; manual rectangle ROI review in v1 does not automatically reconstruct a TSV ROI.

## Project organization

```
RFGAN.Engine/
  Legacy/Detection/        Original Phase 4.3 detection contracts, strategy and engine
  Legacy/Services/         Original CV adapter, models, settings loader, ZGT parser
  Legacy/Models/           Original detector recipe/geometry models
  Batch/                   New input matching, validation, quantified ROI pipeline, exports
RFGAN.Desktop/             New compact WinForms UI + detector-parameter and ROI-review forms
RFGAN.SmokeTests/          Small headless tests with synthetic pixel masks
DemoData/                  Artificial PNGs for checking UI import and mask pairing
```

The original Phase 4.3 `AppLogger` is retained; a minimal `LoggerHelper` compatibility implementation replaces Serilog only within this new solution to avoid unnecessary dependencies. This is not a detector algorithm modification.

## Tests and known limitations

Run `dotnet run --project RFGAN.SmokeTests` on Windows. Smoke tests check sample ID parsing, mask pairing, exact ROI/defect pixel arithmetic, exclusion of invalid acquisition pixels, blocked missing masks, and refusing stale exports.

Known limitations: no detector accuracy regression benchmark, no paired physical CT/inspection validation, no built installer, no formal release pipeline, no 16-bit calibrated acoustic-amplitude quantification, no saved/restorable analysis sessions, manual ROI only supports rectangles, exports do not include annotated PNGs, one sample identity per row, and a limited ZGT importer based on the previously reverse-engineered file variant. ZGT conversion of large batches may consume substantial disk space and processing time. Do not mistake a successful smoke test for validated metrology.

## Preserved source and provenance

See `docs/DETECTOR_PROVENANCE.md` and `updates.txt`. Previous Phase 4.3 `updates.txt` history was preserved and extended with the new v1 change entry.
