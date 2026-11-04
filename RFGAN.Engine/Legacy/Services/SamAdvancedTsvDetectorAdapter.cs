using OpenCvSharp;
using RFGAN.SampleMapper.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;

namespace RFGAN.SampleMapper.Services;

/// <summary>
/// OpenCvSharp port of sam_sample_tsv_detector_advanced_geometry.py.
/// Phase 4.3 keeps this as an internal algorithm implementation detail behind
/// AdvancedClassicalDetectionStrategy. Application/UI code must enter through ISampleDetectionEngine.
/// </summary>
internal sealed class SamAdvancedTsvDetectorAdapter : ISamCvDetectorAdapter
{
    public static readonly SamAdvancedTsvDetectorAdapter Shared = new();
    public string Name => "SAM advanced sample + TSV detector";

    public SamDetectionResult Detect(string imagePath, SampleType sampleType, SamCvDetectorRequest? request = null)
    {
        SamAdvancedPipelineResult pipeline = RunPipeline(imagePath, sampleType, request);
        try
        {
            SamDetectionResult result = ConvertToSamDetectionResult(imagePath, sampleType, pipeline, request);

            if (request?.CaptureCalibrationDiagnostics == true)
            {
                result.CalibrationDiagnostics = CreateCalibrationDiagnostics(imagePath, pipeline, request);

                // Full diagnostics own the stage Mats. Clear them from the pipeline so the
                // pipeline dispose in finally does not dispose images still shown by the UI.
                pipeline.Stages = new List<DetectionStageImage>();
            }

            return result;
        }
        finally
        {
            pipeline.Dispose();
        }
    }

    private static DetectionCalibrationResult CreateCalibrationDiagnostics(
        string imagePath,
        SamAdvancedPipelineResult pipeline,
        SamCvDetectorRequest? request)
    {
        var result = new DetectionCalibrationResult
        {
            SourceImagePath = imagePath,
            Success = pipeline.Success,
            FailureReason = pipeline.FailureReason,
            Stages = pipeline.Stages,
            ProcessingTimeMs = pipeline.ProcessingTimeMs,
            ActivePresetName = request?.ActivePreset?.PresetName ?? "Advanced CV runtime settings",
            ActivePresetSnapshot = request?.ActivePreset?.Clone(),
            EffectiveDetectionParameters = MapAdvancedToLegacyParameters(request?.RecipeSnapshot?.DetectorParameters ?? request?.Parameters ?? SamCvCalibrationSettingsStore.Load().Params),
            CandidateCount = pipeline.Candidates.Count,
            SelectedCandidateScore = pipeline.Candidates.FirstOrDefault()?.Score ?? 0
        };

        if (pipeline.Success && pipeline.SampleBBox.HasValue)
        {
            Rect r = pipeline.SampleBBox.Value;
            result.InitialCandidateRect = pipeline.Candidates.FirstOrDefault()?.BBox ?? r;
            result.ProjectionRect = r;
            result.EdgeSnappedRect = r;
            result.FinalRect = r;
            result.FinalPolygon = pipeline.SamplePolygon is { Length: >= 3 }
                ? pipeline.SamplePolygon.ToArray()
                : RectToPolygon(r);
            result.MaskPixelCount = result.FinalPolygon.Length >= 3
                ? Math.Max(1, (int)Math.Round(Math.Abs(Cv2.ContourArea(result.FinalPolygon))))
                : Math.Max(1, r.Width * r.Height);
            result.TsvRect = pipeline.TsvBBox;
            result.StructuralConfidence = EstimateConfidence(pipeline);
            result.Rectangularity = 1.0;
            result.Solidity = 1.0;
            result.EdgeStrength = pipeline.Candidates.FirstOrDefault()?.Details.GetValueOrDefault("boundary_edge") ?? 0;
            result.Comparison = request?.ReferenceRect.HasValue == true
                ? RoiComparisonMetrics.FromRects(r, request.ReferenceRect.Value)
                : RoiComparisonMetrics.Empty(r);
            result.ConfidenceEvolution.Add(new DetectionConfidencePoint { Stage = 1, Name = "Advanced contrast scan", CandidateCount = pipeline.Candidates.Count, Confidence = Math.Clamp(result.StructuralConfidence * 0.70, 0, 1), Roi = result.InitialCandidateRect });
            result.ConfidenceEvolution.Add(new DetectionConfidencePoint { Stage = 2, Name = "Local edge refinement", CandidateCount = pipeline.Candidates.Count, Confidence = result.StructuralConfidence, Roi = result.FinalRect });
            if (pipeline.TsvBBox.HasValue)
            {
                result.SpecialRois.Add(new SpecialAbsoluteRoiResult { Name = "TSV", Type = "TSV", Rect = pipeline.TsvBBox.Value });
            }
        }
        else
        {
            result.Warnings.Add(string.IsNullOrWhiteSpace(pipeline.FailureReason)
                ? "Advanced CV detector did not find a valid sample candidate."
                : pipeline.FailureReason);
        }

        foreach (string line in pipeline.Log)
        {
            if (line.Contains("warning", StringComparison.OrdinalIgnoreCase))
                result.Warnings.Add(line);
        }

        return result;
    }

    internal SamAdvancedPipelineResult RunPipeline(string imagePath, SampleType sampleType, SamCvDetectorRequest? request = null)
    {
        var sw = Stopwatch.StartNew();
        var output = new SamAdvancedPipelineResult();
        string baseName = string.IsNullOrWhiteSpace(request?.DebugBaseName) ? Path.GetFileNameWithoutExtension(imagePath) : request!.DebugBaseName!;

        try
        {
            if (string.IsNullOrWhiteSpace(imagePath) || !File.Exists(imagePath))
                throw new FileNotFoundException("Image file was not found.", imagePath);

            SamAdvancedDetectorConfig persisted = SamCvCalibrationSettingsStore.Load();
            AnalysisRecipeSnapshot? recipe = request?.RecipeSnapshot;
            SamAdvancedDetectionParams parameters = recipe?.DetectorParameters?.Clone()
                ?? request?.Parameters?.Clone()
                ?? persisted.Params.Clone();
            SamAdvancedLearnedGeometry geometry = recipe?.DetectorGeometry?.Clone()
                ?? request?.Geometry?.Clone()
                ?? persisted.Geometry.Clone();
            UnifiedSampleDetectionPreset? activePreset = recipe?.UnifiedPreset ?? request?.ActivePreset;
            MergePresetGeometry(activePreset, geometry);

            AppLogger.Info("SamAdvancedTsvDetectorAdapter", "Detection started. Image={Image}, SampleType={SampleType}, Params={Params}, HasSampleGeom={HasSampleGeom}, HasTsvGeom={HasTsvGeom}",
                imagePath, sampleType, parameters.ToSummary(), geometry.HasSampleGeometry(), geometry.HasTsvGeometry());

            using Mat src = LoadImage(imagePath);
            using Mat gray = ToGray(src);
            AddStage(output, 1, "01_raw_image", "Raw Image", "Loaded SAM C-scan image.", src);

            Preprocess(gray, parameters, out Mat clahe, out Mat smoothed, out Mat grad01);
            using (clahe)
            using (smoothed)
            using (grad01)
            {
                AddStage(output, 2, "02_clahe_contrast", "CLAHE Contrast", "CLAHE contrast-enhanced image.", clahe);
                AddStage(output, 3, "03_smoothed_image", "Smoothed Image", "Gaussian smoothing using blur divisor.", smoothed);
                using Mat gradVis = NormalizeFloatToByte(grad01);
                AddStage(output, 4, "04_gradient_magnitude", "Gradient Magnitude", "Sobel gradient magnitude used for boundary scoring.", gradVis);

                using Mat mask = MakeBorderClearedMask(smoothed, parameters);
                AddStage(output, 5, "05_border_cleared_bright_mask", "Border-Cleared Bright Mask", "Bright mask after border component removal and morphology.", mask);

                List<SamAdvancedCandidate> scanCandidates = ScanContrastRectangles(smoothed, grad01, parameters, geometry, out Mat scoreVis);
                using (scoreVis)
                {
                    List<SamAdvancedCandidate> contourCandidates = ContourCandidates(mask, smoothed, grad01, parameters, geometry);
                    List<SamAdvancedCandidate> allCandidates = scanCandidates.Concat(contourCandidates)
                        .OrderByDescending(c => c.Score)
                        .ToList();

                    output.Candidates = allCandidates;
                    output.Log.Add(geometry.HasSampleGeometry() ? "Geometry source: user-taught/calibration geometry" : "Geometry source: generic fallback");
                    output.Log.Add($"Geometry constraints: {FormatConstraints(GeometryConstraints(gray.Cols, gray.Rows, geometry))}");
                    output.Log.Add($"Contrast-scan candidates: {scanCandidates.Count}");
                    output.Log.Add($"Contour candidates: {contourCandidates.Count}");
                    output.Log.Add($"Total ranked candidates: {allCandidates.Count}");

                    AddStage(output, 6, "06_contrast_score_map", "Contrast Score Map", "Higher values indicate likely sample rectangle centers.", scoreVis);
                    using Mat candidateVis = DrawCandidates(src, allCandidates, parameters.ShowTopCandidates);
                    AddStage(output, 7, "07_candidate_boxes", "Candidate Boxes", "Ranked sample candidates before final selection.", candidateVis);

                    if (allCandidates.Count == 0)
                    {
                        output.Success = false;
                        output.FailureReason = "No valid sample candidate found by the advanced detector.";
                        output.Log.Add(output.FailureReason);
                        AddStage(output, 8, "08_final_sample_tsv", "Final Sample + TSV", "No final ROI because no valid candidate was found.", src);
                        return Finish(output, sw, request, baseName);
                    }

                    SamAdvancedCandidate best = allCandidates[0];
                    Rect sample = best.BBox;
                    output.Log.Add($"Selected candidate source: {best.Source}");
                    output.Log.Add($"Selected candidate score: {best.Score:0.####}");
                    foreach (var kv in best.Details.OrderBy(k => k.Key))
                        output.Log.Add($"  {kv.Key}: {kv.Value:0.####}");

                    if (parameters.LocalEdgeRefinement)
                    {
                        Rect refined = RefineBBoxByLocalEdges(gray, sample);
                        if (!RectsEqual(refined, sample))
                            output.Log.Add($"Local edge refinement: {RectText(sample)} -> {RectText(refined)}");
                        else
                            output.Log.Add("Local edge refinement kept original bbox.");
                        sample = refined;
                    }
                    else
                    {
                        output.Log.Add("Local edge refinement disabled.");
                    }

                    OpenCvSharp.Point[] samplePoly = ProjectSamplePolygon(sample, geometry);
                    ProjectTsvPolygon(sample, geometry, out OpenCvSharp.Point[] tsvPoly, out Rect? tsvRect);
                    if (sampleType != SampleType.RfGanWithTsv)
                    {
                        tsvPoly = Array.Empty<OpenCvSharp.Point>();
                        tsvRect = null;
                    }

                    output.SampleBBox = sample;
                    output.SamplePolygon = samplePoly;
                    output.TsvPolygon = tsvPoly;
                    output.TsvBBox = tsvRect;
                    output.Success = true;
                    output.Log.Add($"Final sample bbox: {RectText(sample)}");
                    output.Log.Add(tsvRect.HasValue ? $"Final TSV bbox: {RectText(tsvRect.Value)}" : "Final TSV bbox: none");
                    output.Log.Add(geometry.HasTsvGeometry() ? "TSV source: user-taught/calibration relative polygon" : "TSV source: fallback relative ROI");

                    using Mat final = DrawFinal(src, sample, samplePoly, tsvPoly, tsvRect);
                    AddStage(output, 8, "08_final_sample_tsv", "Final Sample + TSV", "Final sample rectangle and TSV ROI produced by the classical strategy.", final);
                    return Finish(output, sw, request, baseName);
                }
            }
        }
        catch (Exception ex)
        {
            output.Success = false;
            output.FailureReason = ex.Message;
            output.Log.Add(ex.ToString());
            AppLogger.Error("SamAdvancedTsvDetectorAdapter", ex, "Advanced detector failed. Image={Image}", imagePath);
            return Finish(output, sw, request, baseName);
        }
    }

    private static SamAdvancedPipelineResult Finish(SamAdvancedPipelineResult output, Stopwatch sw, SamCvDetectorRequest? request, string baseName)
    {
        sw.Stop();
        output.ProcessingTimeMs = sw.ElapsedMilliseconds;
        output.Log.Add($"Processing time: {output.ProcessingTimeMs} ms");

        if (!string.IsNullOrWhiteSpace(request?.DebugOutputDir) && (request.SaveDebugStages || request.SaveFinalReviewOverlay))
            SaveDebugStages(output, request.DebugOutputDir!, baseName);

        AppLogger.Info("SamAdvancedTsvDetectorAdapter", "Detection completed. Success={Success}, Failure={Failure}, Candidates={Candidates}, Sample={Sample}, TSV={Tsv}, TimeMs={TimeMs}",
            output.Success, output.FailureReason, output.Candidates.Count,
            output.SampleBBox.HasValue ? RectText(output.SampleBBox.Value) : "none",
            output.TsvBBox.HasValue ? RectText(output.TsvBBox.Value) : "none",
            output.ProcessingTimeMs);
        return output;
    }

    private static void SaveDebugStages(SamAdvancedPipelineResult result, string debugDir, string baseName)
    {
        try
        {
            Directory.CreateDirectory(debugDir);
            foreach (DetectionStageImage stage in result.Stages)
            {
                string safe = string.Join("_", stage.Key.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries));
                string path = Path.Combine(debugDir, $"{baseName}_{stage.Order:00}_{safe}.png");
                stage.Image.SaveImage(path);
            }
        }
        catch (Exception ex)
        {
            AppLogger.Warning("SamAdvancedTsvDetectorAdapter", "Could not save debug stages. Error={Error}", ex.Message);
        }
    }

    private static SamDetectionResult ConvertToSamDetectionResult(string imagePath, SampleType sampleType, SamAdvancedPipelineResult pipeline, SamCvDetectorRequest? request)
    {
        var diagnostics = new SamDetectionDiagnostics { ProfileName = "SAM advanced sample + TSV detector" };
        try
        {
            using Mat img = LoadImage(imagePath);
            diagnostics.ImageWidth = img.Cols;
            diagnostics.ImageHeight = img.Rows;
        }
        catch { }

        diagnostics.Steps.Add(new SamDetectionStep
        {
            Index = 1,
            Name = "Advanced Python-model CV pipeline",
            Description = "CLAHE -> smoothing -> gradient -> border-cleared bright mask -> contrast rectangle scan -> contour candidates -> local edge refinement -> TSV projection.",
            Candidates = pipeline.Candidates.Take(20).Select((c, i) => new SamDetectionCandidateSnapshot
            {
                CandidateId = $"A{i + 1:000}",
                Rank = i + 1,
                Source = c.Source,
                Status = i == 0 && pipeline.Success ? "Selected" : "Candidate",
                Score = c.Score,
                Rect = c.BBox,
                Polygon = RectToPolygon(c.BBox),
                Metrics = c.Details,
                Notes = i == 0 ? "Top ranked candidate from advanced detector." : string.Empty
            }).ToList()
        });

        if (!pipeline.Success || !pipeline.SampleBBox.HasValue)
        {
            return new SamDetectionResult
            {
                Success = false,
                FailureReason = pipeline.FailureReason,
                SampleType = sampleType,
                ProfileName = "SAM advanced sample + TSV detector",
                Confidence = 0,
                ConfidenceLevel = "Failed",
                Diagnostics = diagnostics,
                RecipeSnapshot = request?.RecipeSnapshot?.Clone(),
                RecipeSnapshotId = request?.RecipeSnapshot?.SnapshotId ?? string.Empty,
                RecipeHashSha256 = request?.RecipeSnapshot?.RecipeHashSha256 ?? string.Empty
            };
        }

        Rect sample = pipeline.SampleBBox.Value;
        double confidence = EstimateConfidence(pipeline);
        return new SamDetectionResult
        {
            Success = true,
            SampleRect = sample,
            SamplePolygon = pipeline.SamplePolygon is { Length: >= 3 } ? pipeline.SamplePolygon : RectToPolygon(sample),
            SampleMaskAreaPixels = pipeline.SamplePolygon is { Length: >= 3 }
                ? Math.Max(1, (int)Math.Round(Math.Abs(Cv2.ContourArea(pipeline.SamplePolygon))))
                : Math.Max(1, sample.Width * sample.Height),
            TsvRect = pipeline.TsvBBox,
            TsvPolygon = pipeline.TsvPolygon,
            Confidence = confidence,
            ConfidenceLevel = confidence >= 0.78 ? "High" : confidence >= 0.50 ? "Medium" : "Low",
            FailureReason = string.Empty,
            SampleType = sampleType,
            ProfileName = "SAM advanced sample + TSV detector",
            UsedFallbackDetector = false,
            Warnings = confidence < 0.50 ? new List<string> { "Low confidence; manual ROI review is recommended." } : new List<string>(),
            Diagnostics = diagnostics,
            RecipeSnapshot = request?.RecipeSnapshot?.Clone(),
            RecipeSnapshotId = request?.RecipeSnapshot?.SnapshotId ?? string.Empty,
            RecipeHashSha256 = request?.RecipeSnapshot?.RecipeHashSha256 ?? string.Empty
        };
    }

    private static double EstimateConfidence(SamAdvancedPipelineResult pipeline)
    {
        if (!pipeline.Success || pipeline.Candidates.Count == 0)
            return 0;

        double best = pipeline.Candidates[0].Score;
        double second = pipeline.Candidates.Count > 1 ? pipeline.Candidates[1].Score : best - 0.25;
        double margin = Math.Max(0, best - second);
        double normalizedScore = 1.0 / (1.0 + Math.Exp(-best));
        double marginScore = Math.Clamp(margin / 0.35, 0, 1);
        return Math.Clamp(0.65 * normalizedScore + 0.35 * marginScore, 0.05, 0.98);
    }

    private static SampleDetectionParameters MapAdvancedToLegacyParameters(SamAdvancedDetectionParams p) => new()
    {
        EnableClahe = true,
        ClaheClipLimit = p.ClaheClipLimit,
        ClaheTileGridSize = p.ClaheTileSize,
        BlurType = SampleDetectionBlurType.Gaussian,
        BorderMargin = p.BorderMargin,
        AreaWeight = p.ContrastWeight,
        EdgeStrengthWeight = p.BoundaryEdgeWeight,
        TsvStructureWeight = p.InternalStructureWeight,
        CenterWeight = p.CentralityWeight,
        AspectRatioWeight = p.AspectQualityWeight,
        EnableEdgeSnap = p.LocalEdgeRefinement
    };

    private static Mat LoadImage(string path)
    {
        byte[] data = File.ReadAllBytes(path);
        Mat img = Cv2.ImDecode(data, ImreadModes.Unchanged);
        if (img.Empty())
            throw new InvalidOperationException($"Could not read image: {path}");
        if (img.Channels() == 4)
        {
            Mat bgr = new();
            Cv2.CvtColor(img, bgr, ColorConversionCodes.BGRA2BGR);
            img.Dispose();
            return NormalizeColorUint8(bgr);
        }
        return img.Channels() == 1 ? NormalizeUint8(img) : NormalizeColorUint8(img);
    }

    private static Mat ToGray(Mat img)
    {
        if (img.Channels() == 1)
            return NormalizeUint8(img);
        Mat gray = new();
        Cv2.CvtColor(img, gray, ColorConversionCodes.BGR2GRAY);
        return NormalizeUint8(gray);
    }

    private static Mat NormalizeColorUint8(Mat img)
    {
        if (img.Type() == MatType.CV_8UC1 || img.Type() == MatType.CV_8UC3 || img.Type() == MatType.CV_8UC4)
            return img.Clone();
        Mat outMat = new();
        Cv2.Normalize(img, outMat, 0, 255, NormTypes.MinMax, MatType.CV_8U);
        return outMat;
    }

    private static Mat NormalizeUint8(Mat img)
    {
        Mat gray = img.Channels() == 1 ? img : ToGray(img);
        if (gray.Type() == MatType.CV_8UC1)
            return gray.Clone();
        Mat outMat = new();
        Cv2.Normalize(gray, outMat, 0, 255, NormTypes.MinMax, MatType.CV_8U);
        return outMat;
    }

    private static void Preprocess(Mat gray, SamAdvancedDetectionParams p, out Mat clahe, out Mat smoothed, out Mat grad01)
    {
        using Mat gray8 = NormalizeUint8(gray);
        int tile = Math.Max(2, p.ClaheTileSize);
        using var claheObj = Cv2.CreateCLAHE(p.ClaheClipLimit, new OpenCvSharp.Size(tile, tile));
        clahe = new Mat();
        claheObj.Apply(gray8, clahe);

        double blurDiv = Math.Max(20.0, p.BlurDivisor);
        int k = Math.Max(3, (int)Math.Round(Math.Min(gray8.Rows, gray8.Cols) / blurDiv));
        if (k % 2 == 0) k++;
        smoothed = new Mat();
        Cv2.GaussianBlur(clahe, smoothed, new OpenCvSharp.Size(k, k), 0);

        using Mat gx = new();
        using Mat gy = new();
        Cv2.Sobel(smoothed, gx, MatType.CV_32F, 1, 0, 3);
        Cv2.Sobel(smoothed, gy, MatType.CV_32F, 0, 1, 3);
        using Mat mag = new();
        Cv2.Magnitude(gx, gy, mag);
        grad01 = new Mat();
        Cv2.Normalize(mag, grad01, 0, 1, NormTypes.MinMax, MatType.CV_32F);
    }

    private static Mat NormalizeFloatToByte(Mat mat)
    {
        Mat outMat = new();
        Cv2.Normalize(mat, outMat, 0, 255, NormTypes.MinMax, MatType.CV_8U);
        return outMat;
    }

    private static Mat MakeBorderClearedMask(Mat smoothed, SamAdvancedDetectionParams p)
    {
        using Mat gray = NormalizeUint8(smoothed);
        double percentile = Math.Clamp(p.BrightMaskPercentile, 1.0, 99.0);
        double threshold = Percentile(gray, percentile, ignoreNearZero: true);
        Mat mask = new Mat(gray.Rows, gray.Cols, MatType.CV_8UC1, Scalar.Black);
        Cv2.Threshold(gray, mask, threshold, 255, ThresholdTypes.Binary);
        using Mat cleared = ClearBorderComponents(mask, p);
        mask.Dispose();

        double morphDiv = Math.Max(25.0, p.MorphDivisor);
        int k = Math.Max(3, (int)Math.Round(Math.Min(gray.Rows, gray.Cols) / morphDiv));
        if (k % 2 == 0) k++;
        using Mat kernel = Cv2.GetStructuringElement(MorphShapes.Rect, new OpenCvSharp.Size(k, k));
        Mat opened = new();
        Mat closed = new();
        Cv2.MorphologyEx(cleared, opened, MorphTypes.Open, kernel, iterations: 1);
        Cv2.MorphologyEx(opened, closed, MorphTypes.Close, kernel, iterations: 2);
        opened.Dispose();
        return closed;
    }

    private static double Percentile(Mat gray8, double percentile, bool ignoreNearZero)
    {
        var values = new List<byte>(gray8.Rows * gray8.Cols);
        for (int y = 0; y < gray8.Rows; y++)
        {
            for (int x = 0; x < gray8.Cols; x++)
            {
                byte v = gray8.At<byte>(y, x);
                if (ignoreNearZero && v <= 3)
                    continue;
                values.Add(v);
            }
        }
        if (values.Count < 20 && ignoreNearZero)
            return Percentile(gray8, percentile, ignoreNearZero: false);
        if (values.Count == 0)
            return 0;
        values.Sort();
        double idx = Math.Clamp(percentile / 100.0, 0, 1) * (values.Count - 1);
        int i0 = (int)Math.Floor(idx);
        int i1 = Math.Min(values.Count - 1, i0 + 1);
        double t = idx - i0;
        return values[i0] * (1 - t) + values[i1] * t;
    }

    private static Mat ClearBorderComponents(Mat mask, SamAdvancedDetectionParams p)
    {
        using Mat labels = new();
        using Mat stats = new();
        using Mat centroids = new();
        int num = Cv2.ConnectedComponentsWithStats(mask, labels, stats, centroids);
        Mat output = new Mat(mask.Rows, mask.Cols, MatType.CV_8UC1, Scalar.Black);
        for (int i = 1; i < num; i++)
        {
            int x = stats.At<int>(i, 0);
            int y = stats.At<int>(i, 1);
            int w = stats.At<int>(i, 2);
            int h = stats.At<int>(i, 3);
            if (TouchesBorder(x, y, w, h, mask.Cols, mask.Rows, p.BorderMargin))
                continue;
            using Mat componentMask = new();
            Cv2.InRange(labels, new Scalar(i), new Scalar(i), componentMask);
            output.SetTo(Scalar.White, componentMask);
        }
        return output;
    }

    private static Dictionary<string, double> GeometryConstraints(int imageW, int imageH, SamAdvancedLearnedGeometry geom)
    {
        if (geom.HasSampleGeometry())
        {
            double wf = geom.SampleWidthFrac!.Value;
            double hf = geom.SampleHeightFrac!.Value;
            double aspect = geom.SampleAspect!.Value;
            return new Dictionary<string, double>
            {
                ["min_width_frac"] = Math.Max(0.04, wf * 0.55),
                ["max_width_frac"] = Math.Min(0.80, wf * 1.55),
                ["min_height_frac"] = Math.Max(0.04, hf * 0.55),
                ["max_height_frac"] = Math.Min(0.80, hf * 1.55),
                ["min_aspect"] = Math.Max(0.50, aspect * 0.55),
                ["max_aspect"] = Math.Min(5.00, aspect * 1.65),
                ["preferred_aspect"] = aspect
            };
        }

        return new Dictionary<string, double>
        {
            ["min_width_frac"] = 0.14,
            ["max_width_frac"] = 0.42,
            ["min_height_frac"] = 0.10,
            ["max_height_frac"] = 0.42,
            ["min_aspect"] = 1.10,
            ["max_aspect"] = 3.20,
            ["preferred_aspect"] = 1.80
        };
    }

    private static string FormatConstraints(Dictionary<string, double> d)
        => string.Join(", ", d.Select(kv => $"{kv.Key}={kv.Value:0.###}"));

    private static List<SamAdvancedCandidate> ScanContrastRectangles(Mat smoothed, Mat grad01, SamAdvancedDetectionParams p, SamAdvancedLearnedGeometry geom, out Mat scoreVis)
    {
        using Mat gray01 = new();
        smoothed.ConvertTo(gray01, MatType.CV_32F, 1.0 / 255.0);
        int h = gray01.Rows;
        int w = gray01.Cols;
        Dictionary<string, double> constraints = GeometryConstraints(w, h, geom);
        using Mat iiGray = new();
        using Mat iiGrad = new();
        Cv2.Integral(gray01, iiGray, MatType.CV_64F);
        Cv2.Integral(grad01, iiGrad, MatType.CV_64F);
        using Mat scoreMap = new Mat(h, w, MatType.CV_32F, Scalar.Black);
        var candidates = new List<SamAdvancedCandidate>();
        SamAdvancedCandidate? best = null;

        int minBw = Math.Max(8, (int)Math.Round(w * constraints["min_width_frac"]));
        int maxBw = Math.Max(minBw + 2, (int)Math.Round(w * constraints["max_width_frac"]));
        double minAspect = constraints["min_aspect"];
        double maxAspect = constraints["max_aspect"];
        int widthStep = Math.Max(2, (int)Math.Round(w * 0.018));

        for (int bw = minBw; bw <= maxBw; bw += widthStep)
        {
            for (int ai = 0; ai < 13; ai++)
            {
                double aspect = minAspect + (maxAspect - minAspect) * ai / 12.0;
                int bh = (int)Math.Round(bw * aspect);
                if (bh < (int)(h * constraints["min_height_frac"]) || bh > (int)(h * constraints["max_height_frac"]))
                    continue;
                int step = Math.Max(2, (int)Math.Round(Math.Min(bw, bh) / 10.0));
                int ring = Math.Max(4, (int)Math.Round(bw * 0.22));
                int yStart = ring;
                int yEnd = h - bh - ring;
                int xStart = ring;
                int xEnd = w - bw - ring;
                if (yEnd <= yStart || xEnd <= xStart)
                    continue;

                for (int y = yStart; y < yEnd; y += step)
                {
                    for (int x = xStart; x < xEnd; x += step)
                    {
                        if (!TryScoreRectangle(gray01, grad01, iiGray, iiGrad, x, y, bw, bh, w, h, p, constraints, out double score, out Dictionary<string, double> details))
                            continue;
                        int cx = Math.Clamp(x + bw / 2, 0, w - 1);
                        int cy = Math.Clamp(y + bh / 2, 0, h - 1);
                        float previous = scoreMap.At<float>(cy, cx);
                        if (score > previous)
                            scoreMap.Set<float>(cy, cx, (float)score);
                        var cand = new SamAdvancedCandidate { Source = "contrast_scan_coarse", Score = score, BBox = new Rect(x, y, bw, bh), Details = details };
                        if (best is null || cand.Score > best.Score)
                            best = cand;
                    }
                }
            }
        }

        if (best is not null)
        {
            candidates.Add(best);
            Rect br = best.BBox;
            int dx = Math.Max(6, br.Width / 4);
            int dy = Math.Max(6, br.Height / 4);
            SamAdvancedCandidate fineBest = best;
            for (int nbw = Math.Max(6, (int)(br.Width * 0.82)); nbw <= (int)(br.Width * 1.18); nbw += 2)
            {
                for (int nbh = Math.Max(6, (int)(br.Height * 0.82)); nbh <= (int)(br.Height * 1.18); nbh += 2)
                {
                    int y0 = Math.Max(2, br.Y - dy);
                    int y1 = Math.Min(h - nbh - 2, br.Y + dy);
                    int x0 = Math.Max(2, br.X - dx);
                    int x1 = Math.Min(w - nbw - 2, br.X + dx);
                    if (y1 <= y0 || x1 <= x0)
                        continue;
                    for (int y = y0; y <= y1; y += 2)
                    {
                        for (int x = x0; x <= x1; x += 2)
                        {
                            if (!TryScoreRectangle(gray01, grad01, iiGray, iiGrad, x, y, nbw, nbh, w, h, p, constraints, out double score, out Dictionary<string, double> details))
                                continue;
                            var cand = new SamAdvancedCandidate { Source = "contrast_scan_fine", Score = score, BBox = new Rect(x, y, nbw, nbh), Details = details };
                            if (cand.Score > fineBest.Score)
                                fineBest = cand;
                        }
                    }
                }
            }
            candidates.Add(fineBest);
        }

        using Mat score8 = NormalizeFloatToByte(scoreMap);
        scoreVis = new Mat();
        Cv2.ApplyColorMap(score8, scoreVis, ColormapTypes.Jet);
        return candidates.OrderByDescending(c => c.Score).ToList();
    }

    private static List<SamAdvancedCandidate> ContourCandidates(Mat mask, Mat smoothed, Mat grad01, SamAdvancedDetectionParams p, SamAdvancedLearnedGeometry geom)
    {
        int h = mask.Rows;
        int w = mask.Cols;
        using Mat gray01 = new();
        smoothed.ConvertTo(gray01, MatType.CV_32F, 1.0 / 255.0);
        Dictionary<string, double> constraints = GeometryConstraints(w, h, geom);
        using Mat iiGray = new();
        using Mat iiGrad = new();
        Cv2.Integral(gray01, iiGray, MatType.CV_64F);
        Cv2.Integral(grad01, iiGrad, MatType.CV_64F);

        Cv2.FindContours(mask, out OpenCvSharp.Point[][] contours, out _, RetrievalModes.External, ContourApproximationModes.ApproxSimple);
        var output = new List<SamAdvancedCandidate>();
        foreach (OpenCvSharp.Point[] contour in contours)
        {
            double area = Cv2.ContourArea(contour);
            if (area < Math.Max(20, 0.0008 * w * h))
                continue;
            Rect r = Cv2.BoundingRect(contour);
            if (TouchesBorder(r.X, r.Y, r.Width, r.Height, w, h, p.BorderMargin))
                continue;
            if (!TryScoreRectangle(gray01, grad01, iiGray, iiGrad, r.X, r.Y, r.Width, r.Height, w, h, p, constraints, out double score, out Dictionary<string, double> details))
                continue;
            double fill = area / Math.Max(1.0, r.Width * r.Height);
            score += 0.10 * fill;
            details["contour_area"] = area;
            details["contour_fill"] = fill;
            output.Add(new SamAdvancedCandidate { Source = "border_cleared_contour", Score = score, BBox = r, Details = details });
        }
        return output.OrderByDescending(c => c.Score).ToList();
    }

    private static bool TryScoreRectangle(Mat gray01, Mat grad01, Mat iiGray, Mat iiGrad, int x, int y, int bw, int bh, int imageW, int imageH, SamAdvancedDetectionParams p, Dictionary<string, double> constraints, out double score, out Dictionary<string, double> details)
    {
        score = double.NegativeInfinity;
        details = new Dictionary<string, double>();
        if (bw <= 4 || bh <= 4)
            return false;
        if (TouchesBorder(x, y, bw, bh, imageW, imageH, p.BorderMargin))
            return false;
        double aspect = bh / (double)Math.Max(bw, 1);
        if (aspect < constraints["min_aspect"] || aspect > constraints["max_aspect"])
            return false;
        double widthFrac = bw / (double)Math.Max(imageW, 1);
        double heightFrac = bh / (double)Math.Max(imageH, 1);
        if (widthFrac < constraints["min_width_frac"] || widthFrac > constraints["max_width_frac"])
            return false;
        if (heightFrac < constraints["min_height_frac"] || heightFrac > constraints["max_height_frac"])
            return false;
        double areaFrac = (bw * bh) / (double)Math.Max(imageW * imageH, 1);
        if (areaFrac < 0.003 || areaFrac > 0.40)
            return false;
        int ring = Math.Max(4, (int)Math.Round(bw * 0.22));
        if (x - ring < 0 || y - ring < 0 || x + bw + ring >= imageW || y + bh + ring >= imageH)
            return false;

        double insideSum = IntegralSum(iiGray, x, y, bw, bh);
        double insideMean = insideSum / Math.Max(bw * bh, 1);
        int rx = x - ring;
        int ry = y - ring;
        int rw = bw + 2 * ring;
        int rh = bh + 2 * ring;
        double outerSum = IntegralSum(iiGray, rx, ry, rw, rh);
        double ringArea = Math.Max(rw * rh - bw * bh, 1);
        double ringMean = (outerSum - insideSum) / ringArea;
        double contrast = insideMean - ringMean;
        int band = Math.Max(2, (int)Math.Round(Math.Min(bw, bh) * 0.05));
        double top = IntegralSum(iiGrad, x, y, bw, band) / Math.Max(bw * band, 1);
        double bottom = IntegralSum(iiGrad, x, y + bh - band, bw, band) / Math.Max(bw * band, 1);
        double left = IntegralSum(iiGrad, x, y, band, bh) / Math.Max(band * bh, 1);
        double right = IntegralSum(iiGrad, x + bw - band, y, band, bh) / Math.Max(band * bh, 1);
        double boundaryEdge = top + bottom + left + right;
        int innerX = x + band;
        int innerY = y + band;
        int innerW = Math.Max(1, bw - 2 * band);
        int innerH = Math.Max(1, bh - 2 * band);
        double internalGrad = IntegralSum(iiGrad, innerX, innerY, innerW, innerH) / Math.Max(innerW * innerH, 1);
        double cx = x + bw / 2.0;
        double cy = y + bh / 2.0;
        double centrality = 1.0 - Math.Min(1.0, Math.Sqrt(Math.Pow((cx - imageW / 2.0) / imageW, 2) + Math.Pow((cy - imageH * 0.55) / imageH, 2)) * 1.25);
        double preferredAspect = Math.Max(0.1, constraints["preferred_aspect"]);
        double aspectQuality = 1.0 - Math.Min(1.0, Math.Abs(aspect - preferredAspect) / preferredAspect);
        score = p.ContrastWeight * contrast
            + p.BoundaryEdgeWeight * boundaryEdge
            + p.InternalStructureWeight * internalGrad
            + p.InsideBrightnessWeight * insideMean
            + p.CentralityWeight * centrality
            + p.AspectQualityWeight * aspectQuality;
        details["inside_mean"] = insideMean;
        details["ring_mean"] = ringMean;
        details["contrast"] = contrast;
        details["boundary_edge"] = boundaryEdge;
        details["internal_grad"] = internalGrad;
        details["centrality"] = centrality;
        details["aspect"] = aspect;
        details["aspect_quality"] = aspectQuality;
        details["width_frac"] = widthFrac;
        details["height_frac"] = heightFrac;
        details["area_frac"] = areaFrac;
        return true;
    }

    private static Rect RefineBBoxByLocalEdges(Mat gray, Rect bbox)
    {
        int h = gray.Rows;
        int w = gray.Cols;
        int padX = Math.Max(5, bbox.Width / 5);
        int padY = Math.Max(5, bbox.Height / 5);
        int x0 = Math.Max(0, bbox.X - padX);
        int y0 = Math.Max(0, bbox.Y - padY);
        int x1 = Math.Min(w, bbox.X + bbox.Width + padX);
        int y1 = Math.Min(h, bbox.Y + bbox.Height + padY);
        Rect cropRect = new(x0, y0, Math.Max(1, x1 - x0), Math.Max(1, y1 - y0));
        using Mat crop = new(gray, cropRect);
        using Mat cropBlur = new();
        Cv2.GaussianBlur(crop, cropBlur, new OpenCvSharp.Size(3, 3), 0);
        using Mat gx = new();
        using Mat gy = new();
        Cv2.Sobel(cropBlur, gx, MatType.CV_32F, 1, 0, 3);
        Cv2.Sobel(cropBlur, gy, MatType.CV_32F, 0, 1, 3);
        using Mat absGx = new();
        using Mat absGy = new();
        Cv2.ConvertScaleAbs(gx, absGx);
        Cv2.ConvertScaleAbs(gy, absGy);
        double[] colScore = new double[cropRect.Width];
        double[] rowScore = new double[cropRect.Height];
        for (int yy = 0; yy < cropRect.Height; yy++)
        {
            for (int xx = 0; xx < cropRect.Width; xx++)
            {
                colScore[xx] += absGx.At<byte>(yy, xx);
                rowScore[yy] += absGy.At<byte>(yy, xx);
            }
        }
        for (int i = 0; i < colScore.Length; i++) colScore[i] /= Math.Max(1, cropRect.Height);
        for (int i = 0; i < rowScore.Length; i++) rowScore[i] /= Math.Max(1, cropRect.Width);

        int leftExpected = bbox.X - x0;
        int rightExpected = bbox.X + bbox.Width - x0;
        int topExpected = bbox.Y - y0;
        int bottomExpected = bbox.Y + bbox.Height - y0;
        int radiusX = Math.Max(4, bbox.Width / 8);
        int radiusY = Math.Max(4, bbox.Height / 8);
        int left = LocalPeak(colScore, leftExpected, radiusX);
        int right = LocalPeak(colScore, rightExpected, radiusX);
        int top = LocalPeak(rowScore, topExpected, radiusY);
        int bottom = LocalPeak(rowScore, bottomExpected, radiusY);
        int nx = x0 + Math.Min(left, right);
        int ny = y0 + Math.Min(top, bottom);
        int nr = x0 + Math.Max(left, right);
        int nb = y0 + Math.Max(top, bottom);
        int nw = nr - nx;
        int nh = nb - ny;
        if (nw < bbox.Width * 0.65 || nh < bbox.Height * 0.65 || nw > bbox.Width * 1.35 || nh > bbox.Height * 1.35)
            return bbox;
        return new Rect(nx, ny, nw, nh);
    }

    private static int LocalPeak(double[] scores, int expected, int radius)
    {
        int a = Math.Max(0, expected - radius);
        int b = Math.Min(scores.Length, expected + radius + 1);
        if (b <= a)
            return Math.Clamp(expected, 0, Math.Max(0, scores.Length - 1));
        int best = a;
        double bestVal = scores[a];
        for (int i = a + 1; i < b; i++)
        {
            if (scores[i] > bestVal)
            {
                bestVal = scores[i];
                best = i;
            }
        }
        return best;
    }

    private static OpenCvSharp.Point[] ProjectSamplePolygon(Rect sample, SamAdvancedLearnedGeometry geom)
    {
        if (geom.SamplePolygonNorm is not { Count: >= 3 })
            return RectToPolygon(sample);

        double minX = geom.SamplePolygonNorm.Min(p => p.X);
        double maxX = geom.SamplePolygonNorm.Max(p => p.X);
        double minY = geom.SamplePolygonNorm.Min(p => p.Y);
        double maxY = geom.SamplePolygonNorm.Max(p => p.Y);
        double spanX = Math.Max(1e-9, maxX - minX);
        double spanY = Math.Max(1e-9, maxY - minY);

        int maxXPixel = Math.Max(sample.X, sample.Right - 1);
        int maxYPixel = Math.Max(sample.Y, sample.Bottom - 1);
        OpenCvSharp.Point[] polygon = geom.SamplePolygonNorm
            .Select(p => new OpenCvSharp.Point(
                Math.Clamp(
                    (int)Math.Round(sample.X + ((p.X - minX) / spanX) * Math.Max(0, sample.Width - 1)),
                    sample.X,
                    maxXPixel),
                Math.Clamp(
                    (int)Math.Round(sample.Y + ((p.Y - minY) / spanY) * Math.Max(0, sample.Height - 1)),
                    sample.Y,
                    maxYPixel)))
            .ToArray();

        return polygon.Length >= 3 ? polygon : RectToPolygon(sample);
    }

    private static void ProjectTsvPolygon(Rect sample, SamAdvancedLearnedGeometry geom, out OpenCvSharp.Point[] poly, out Rect? bbox)
    {
        if (!geom.HasTsvGeometry())
        {
            int bx = (int)Math.Round(sample.X + 0.28 * sample.Width);
            int by = (int)Math.Round(sample.Y + 0.12 * sample.Height);
            int bw = (int)Math.Round(0.44 * sample.Width);
            int bh = (int)Math.Round(0.78 * sample.Height);
            poly = RectToPolygon(new Rect(bx, by, bw, bh));
            bbox = new Rect(bx, by, bw, bh);
            return;
        }

        int maxX = Math.Max(sample.X, sample.Right - 1);
        int maxY = Math.Max(sample.Y, sample.Bottom - 1);
        poly = geom.TsvPolygonRel!
            .Select(p => new OpenCvSharp.Point(
                Math.Clamp((int)Math.Round(sample.X + p.X * Math.Max(0, sample.Width - 1)), sample.X, maxX),
                Math.Clamp((int)Math.Round(sample.Y + p.Y * Math.Max(0, sample.Height - 1)), sample.Y, maxY)))
            .ToArray();
        bbox = poly.Length >= 3 ? Cv2.BoundingRect(poly) : null;
    }

    private static Mat DrawCandidates(Mat baseImg, List<SamAdvancedCandidate> candidates, int topN)
    {
        Mat vis = baseImg.Channels() == 1 ? baseImg.CvtColor(ColorConversionCodes.GRAY2BGR) : baseImg.Clone();
        int n = Math.Max(1, topN);
        for (int i = 0; i < candidates.Count && i < n; i++)
        {
            Rect r = candidates[i].BBox;
            Scalar color = i == 0 ? new Scalar(0, 255, 0) : new Scalar(0, 255, 255);
            Cv2.Rectangle(vis, r, color, i == 0 ? 2 : 1);
            Cv2.PutText(vis, $"{i + 1}:{candidates[i].Score:0.00}", new OpenCvSharp.Point(r.X, Math.Max(12, r.Y - 4)), HersheyFonts.HersheySimplex, 0.38, color, 1, LineTypes.AntiAlias);
        }
        return vis;
    }

    private static Mat DrawFinal(Mat img, Rect sample, OpenCvSharp.Point[] samplePoly, OpenCvSharp.Point[] tsvPoly, Rect? tsvRect)
    {
        Mat vis = img.Channels() == 1 ? img.CvtColor(ColorConversionCodes.GRAY2BGR) : img.Clone();
        Cv2.Rectangle(vis, sample, new Scalar(0, 255, 0), 2);
        if (samplePoly.Length >= 3)
            Cv2.Polylines(vis, new[] { samplePoly }, true, new Scalar(255, 255, 0), 2, LineTypes.AntiAlias);
        Cv2.PutText(vis, $"Sample bbox: x={sample.X}, y={sample.Y}, w={sample.Width}, h={sample.Height}", new OpenCvSharp.Point(Math.Max(5, sample.X), Math.Max(18, sample.Y - 8)), HersheyFonts.HersheySimplex, 0.48, new Scalar(0, 255, 0), 1, LineTypes.AntiAlias);
        if (tsvPoly.Length >= 3)
            Cv2.Polylines(vis, new[] { tsvPoly }, true, new Scalar(255, 0, 255), 2, LineTypes.AntiAlias);
        if (tsvRect.HasValue)
        {
            Rect r = tsvRect.Value;
            Cv2.Rectangle(vis, r, new Scalar(255, 0, 255), 1);
            Cv2.PutText(vis, "TSV ROI", new OpenCvSharp.Point(Math.Max(5, r.X), Math.Min(vis.Rows - 5, r.Y + r.Height + 14)), HersheyFonts.HersheySimplex, 0.45, new Scalar(255, 0, 255), 1, LineTypes.AntiAlias);
        }
        return vis;
    }

    private static void AddStage(SamAdvancedPipelineResult result, int order, string key, string title, string caption, Mat image)
    {
        result.Stages.Add(new DetectionStageImage
        {
            Order = order,
            Key = key,
            Title = title,
            Caption = caption,
            Image = image.Clone()
        });
    }

    private static double IntegralSum(Mat ii, int x, int y, int w, int h)
        => ii.At<double>(y + h, x + w) - ii.At<double>(y, x + w) - ii.At<double>(y + h, x) + ii.At<double>(y, x);

    private static bool TouchesBorder(int x, int y, int w, int h, int imageW, int imageH, int margin)
        => x <= margin || y <= margin || x + w >= imageW - margin || y + h >= imageH - margin;

    private static bool RectsEqual(Rect a, Rect b) => a.X == b.X && a.Y == b.Y && a.Width == b.Width && a.Height == b.Height;

    private static OpenCvSharp.Point[] RectToPolygon(Rect r)
        => new[] { new OpenCvSharp.Point(r.Left, r.Top), new OpenCvSharp.Point(r.Right, r.Top), new OpenCvSharp.Point(r.Right, r.Bottom), new OpenCvSharp.Point(r.Left, r.Bottom) };

    private static string RectText(Rect r) => $"{r.X},{r.Y},{r.Width},{r.Height}";

    private static void MergePresetGeometry(UnifiedSampleDetectionPreset? preset, SamAdvancedLearnedGeometry geometry)
    {
        if (preset?.Geometry is null)
            return;

        SampleGeometrySettings g = preset.Geometry;
        if (!geometry.HasSampleGeometry())
        {
            int refW = Math.Max(1, g.ReferenceImageWidth);
            int refH = Math.Max(1, g.ReferenceImageHeight);
            if (g.OuterRoiPixels is { Width: > 1, Height: > 1 } outer)
            {
                geometry.SampleWidthFrac = outer.Width / (double)refW;
                geometry.SampleHeightFrac = outer.Height / (double)refH;
                geometry.SampleAspect = outer.Height / (double)Math.Max(1, outer.Width);
                geometry.SamplePolygonNorm = new List<SamAdvancedRelativePoint>
                {
                    new(outer.Left / (double)refW, outer.Top / (double)refH),
                    new(outer.Right / (double)refW, outer.Top / (double)refH),
                    new(outer.Right / (double)refW, outer.Bottom / (double)refH),
                    new(outer.Left / (double)refW, outer.Bottom / (double)refH)
                };
            }
            else if (g.ExpectedWidth > 0 && g.ExpectedHeight > 0 && refW > 1 && refH > 1)
            {
                geometry.SampleWidthFrac = g.ExpectedWidth / (double)refW;
                geometry.SampleHeightFrac = g.ExpectedHeight / (double)refH;
                geometry.SampleAspect = g.ExpectedHeight / (double)Math.Max(1, g.ExpectedWidth);
            }
        }

        if (!geometry.HasTsvGeometry())
        {
            SpecialRelativeRoi? tsv = g.SpecialRois.FirstOrDefault(r => r.Enabled && r.Type.Equals("TSV", StringComparison.OrdinalIgnoreCase));
            if (tsv is not null && tsv.RelativeWidth > 0 && tsv.RelativeHeight > 0)
            {
                geometry.TsvPolygonRel = new List<SamAdvancedRelativePoint>
                {
                    new(tsv.RelativeX, tsv.RelativeY),
                    new(tsv.RelativeX + tsv.RelativeWidth, tsv.RelativeY),
                    new(tsv.RelativeX + tsv.RelativeWidth, tsv.RelativeY + tsv.RelativeHeight),
                    new(tsv.RelativeX, tsv.RelativeY + tsv.RelativeHeight)
                };
            }
        }
    }
}
