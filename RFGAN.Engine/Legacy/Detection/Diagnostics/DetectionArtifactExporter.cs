using OpenCvSharp;
using RFGAN.SampleMapper.Services;

namespace RFGAN.SampleMapper.Detection.Diagnostics;

/// <summary>
/// Writes human-reviewable artifacts derived from a completed detection result.
/// This is deliberately separate from the mathematical detection strategy so application callers
/// do not need a second detection facade merely to save the final ROI overlay.
/// </summary>
internal static class DetectionArtifactExporter
{
    public static void SaveFinalRoiReviewOverlay(
        string imagePath,
        SamDetectionResult result,
        string debugOutputDirectory,
        string? debugBaseName)
    {
        try
        {
            Directory.CreateDirectory(debugOutputDirectory);

            using Mat src = Cv2.ImRead(imagePath, ImreadModes.Color);
            if (src.Empty())
                return;

            using Mat vis = src.Clone();

            if (result.SamplePolygon is { Length: >= 3 })
            {
                using Mat overlay = vis.Clone();
                Cv2.FillPoly(overlay, new[] { result.SamplePolygon }, new Scalar(255, 255, 0), LineTypes.AntiAlias);
                Cv2.AddWeighted(overlay, 0.18, vis, 0.82, 0, vis);
                Cv2.Polylines(vis, new[] { result.SamplePolygon }, true, new Scalar(255, 255, 0), 2, LineTypes.AntiAlias);
            }

            if (result.SampleRect.Width > 0 && result.SampleRect.Height > 0)
                Cv2.Rectangle(vis, result.SampleRect, new Scalar(0, 255, 0), 2, LineTypes.AntiAlias);

            if (result.TsvPolygon is { Length: >= 3 })
                Cv2.Polylines(vis, new[] { result.TsvPolygon }, true, new Scalar(0, 0, 255), 2, LineTypes.AntiAlias);
            else if (result.TsvRect.HasValue && result.TsvRect.Value.Width > 0 && result.TsvRect.Value.Height > 0)
                Cv2.Rectangle(vis, result.TsvRect.Value, new Scalar(0, 0, 255), 2, LineTypes.AntiAlias);

            string title = $"Final unified ROI | {result.ConfidenceLevel} | {result.Confidence:P0}";
            int labelY = Math.Max(22, result.SampleRect.Y - 10);
            int labelX = Math.Clamp(result.SampleRect.X, 4, Math.Max(4, vis.Cols - 360));
            int labelWidth = Math.Max(1, Math.Min(350, vis.Cols - labelX));

            Cv2.Rectangle(
                vis,
                new Rect(Math.Max(0, labelX - 3), Math.Max(0, labelY - 18), labelWidth, 23),
                new Scalar(0, 0, 0),
                -1);
            Cv2.PutText(
                vis,
                title,
                new OpenCvSharp.Point(labelX, labelY),
                HersheyFonts.HersheySimplex,
                0.48,
                new Scalar(255, 255, 255),
                1,
                LineTypes.AntiAlias);
            Cv2.PutText(
                vis,
                "green=final rect, cyan=final polygon, red=TSV ROI",
                new OpenCvSharp.Point(5, Math.Min(vis.Rows - 8, 18)),
                HersheyFonts.HersheySimplex,
                0.42,
                new Scalar(255, 255, 255),
                1,
                LineTypes.AntiAlias);

            string baseName = string.IsNullOrWhiteSpace(debugBaseName)
                ? Path.GetFileNameWithoutExtension(imagePath)
                : debugBaseName;
            string outputPath = Path.Combine(
                debugOutputDirectory,
                $"{baseName}_00_final_unified_roi_overlay.png");

            vis.SaveImage(outputPath);
        }
        catch (Exception ex)
        {
            LoggerHelper.Warning(
                "SAMPLE DETECTION ENGINE: Could not save final ROI review overlay. Image={Image}, Error={Error}",
                imagePath,
                ex.Message);
        }
    }
}
