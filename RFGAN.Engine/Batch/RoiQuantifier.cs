using OpenCvSharp;
// This file uses OpenCV image geometry, not System.Drawing.Size (which WinForms also imports).
using Size = OpenCvSharp.Size;
using RFGAN.SampleMapper.Services;

namespace RFGAN.Batch;

public static class RoiQuantifier
{
    public static Measurement Calculate(
        BatchSample sample, BatchSettings settings, SamDetectionResult? detection,
        string recipeHash, CancellationToken token = default)
    {
        using Mat image = Cv2.ImRead(sample.ImagePath, ImreadModes.Grayscale);
        if (image.Empty()) throw new InvalidDataException("Cannot read the C-scan image.");
        using Mat roi = MakeSampleRoi(image.Size(), sample.ManualRoi, detection);
        using Mat validity = ReadBinaryOrFull(sample.ValidityMaskPath, image.Size());
        using Mat measured = new();
        Cv2.BitwiseAnd(roi, validity, measured);
        int roiCount = Cv2.CountNonZero(roi);
        int validCount = Cv2.CountNonZero(measured);
        if (roiCount <= 0) throw new InvalidDataException("Sample ROI has zero pixels.");
        if (validCount <= 0) throw new InvalidDataException("No valid acoustic measurements inside the sample ROI.");
        token.ThrowIfCancellationRequested();

        using Mat defect = CreateDefectMask(sample, settings, image, out bool nonBinary);
        using Mat delamInRoi = new();
        Cv2.BitwiseAnd(defect, measured, delamInRoi);
        int defectCount = Cv2.CountNonZero(delamInRoi);
        if (defectCount > validCount) throw new InvalidDataException("Defect pixels exceed measured ROI pixels.");

        var result = new Measurement
        {
            SampleId = sample.Identity,
            RoiPixels = roiCount,
            ValidRoiPixels = validCount,
            DelaminationPixels = defectCount,
            CoveragePercent = 100.0 * validCount / roiCount,
            DelaminationPercent = 100.0 * defectCount / validCount,
            Exploratory = settings.MaskMode == MaskMode.ExperimentalImageThreshold,
            NonBinaryMask = nonBinary,
            RoiConfidence = detection?.Confidence ?? 0,
            ManualRoi = sample.ManualRoi.HasValue,
            RecipeHash = recipeHash,
            ImageHash = FileIdentity.Sha256File(sample.OriginalPath),
            CscanHash = FileIdentity.Sha256File(sample.ImagePath),
            MaskHash = FileIdentity.Sha256File(sample.MaskPath),
            ValidityHash = FileIdentity.Sha256File(sample.ValidityMaskPath)
        };

        // TSV is optional; only report it if the detector returned real geometry.
        if (!sample.ManualRoi.HasValue && detection?.Success == true)
        {
            using Mat tsv = new(image.Size(), MatType.CV_8UC1, Scalar.Black);
            if (detection.TsvPolygon.Length >= 3)
                Cv2.FillPoly(tsv, new[] { detection.TsvPolygon }, Scalar.White);
            else if (detection.TsvRect is { Width: > 0, Height: > 0 } r && RectWithin(r, image.Size()))
                Cv2.Rectangle(tsv, r, Scalar.White, -1);
            using Mat tsvInsideRoi = new();
            Cv2.BitwiseAnd(tsv, measured, tsvInsideRoi);
            result.TsvValidPixels = Cv2.CountNonZero(tsvInsideRoi);
            if (result.TsvValidPixels > 0)
            {
                using Mat defectsInTsv = new();
                Cv2.BitwiseAnd(tsvInsideRoi, defect, defectsInTsv);
                result.TsvDelaminationPixels = Cv2.CountNonZero(defectsInTsv);
                result.TsvDelaminationPercent = 100.0 * result.TsvDelaminationPixels / result.TsvValidPixels;
            }
        }
        return result;
    }

    public static Mat MakeSampleRoi(Size imageSize, Rect? manualRoi, SamDetectionResult? detection)
    {
        var roi = new Mat(imageSize, MatType.CV_8UC1, Scalar.Black);
        if (manualRoi.HasValue)
        {
            Rect rect = manualRoi.Value;
            if (!RectWithin(rect, imageSize))
            {
                roi.Dispose();
                throw new InvalidDataException("Manual ROI is outside the image or has zero area.");
            }
            Cv2.Rectangle(roi, rect, Scalar.White, -1);
            return roi;
        }
        if (detection?.Success != true)
        {
            roi.Dispose();
            throw new InvalidDataException("No successful sample detection. Review or draw a manual ROI.");
        }
        if (detection.SamplePolygon is { Length: >= 3 })
        {
            if (detection.SamplePolygon.Any(p => p.X < 0 || p.Y < 0 || p.X >= imageSize.Width || p.Y >= imageSize.Height))
            {
                roi.Dispose();
                throw new InvalidDataException("Detected polygon contains out-of-image coordinates.");
            }
            Cv2.FillPoly(roi, new[] { detection.SamplePolygon }, Scalar.White);
        }
        else if (RectWithin(detection.SampleRect, imageSize))
            Cv2.Rectangle(roi, detection.SampleRect, Scalar.White, -1);
        else
        {
            roi.Dispose();
            throw new InvalidDataException("Automatic sample ROI is empty or outside image.");
        }
        return roi;
    }

    public static Mat ReadBinaryOrFull(string? path, Size size)
    {
        if (string.IsNullOrWhiteSpace(path)) return new Mat(size, MatType.CV_8UC1, Scalar.White);
        if (!File.Exists(path)) throw new FileNotFoundException("Validity mask is missing.", path);
        using Mat raw = Cv2.ImRead(path, ImreadModes.Grayscale);
        CheckDimensions(raw, size, "Acquisition-validity mask");
        var binary = new Mat();
        Cv2.Threshold(raw, binary, 127, 255, ThresholdTypes.Binary);
        return binary;
    }

    public static Mat CreateDefectMask(BatchSample sample, BatchSettings settings, Mat grayImage, out bool nonBinary)
    {
        nonBinary = false;
        if (settings.MaskMode == MaskMode.ExperimentalImageThreshold)
        {
            var threshold = new Mat();
            Cv2.Threshold(grayImage, threshold, settings.ExploratoryThreshold, 255,
                settings.BrightPixelsAreDelamination ? ThresholdTypes.Binary : ThresholdTypes.BinaryInv);
            return threshold;
        }
        if (string.IsNullOrWhiteSpace(sample.MaskPath) || !File.Exists(sample.MaskPath))
            throw new FileNotFoundException("Delamination mask missing. Assign a binary mask before running.", sample.MaskPath);
        using Mat raw = Cv2.ImRead(sample.MaskPath, ImreadModes.Grayscale);
        CheckDimensions(raw, grayImage.Size(), "Delamination mask");
        using Mat mid = new();
        Cv2.InRange(raw, new Scalar(1), new Scalar(254), mid);
        nonBinary = Cv2.CountNonZero(mid) != 0;
        var mask = new Mat();
        Cv2.Threshold(raw, mask, 127, 255, ThresholdTypes.Binary);
        return mask;
    }

    private static bool RectWithin(Rect r, Size s)
        => r.X >= 0 && r.Y >= 0 && r.Width > 0 && r.Height > 0 &&
           r.Right <= s.Width && r.Bottom <= s.Height;

    private static void CheckDimensions(Mat image, Size expected, string label)
    {
        if (image.Empty()) throw new InvalidDataException($"Cannot decode {label}.");
        if (image.Width != expected.Width || image.Height != expected.Height)
            throw new InvalidDataException($"{label} dimensions {image.Width}×{image.Height} do not match image {expected.Width}×{expected.Height}.");
    }
}
