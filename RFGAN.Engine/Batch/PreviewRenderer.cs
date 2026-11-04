using System.Drawing;
using OpenCvSharp;

namespace RFGAN.Batch;

public static class PreviewRenderer
{
    public static Bitmap Render(BatchSample sample, BatchSettings settings, bool showMask)
    {
        using Mat src = Cv2.ImRead(sample.ImagePath, ImreadModes.Color);
        if (src.Empty()) throw new InvalidDataException("Cannot render image.");
        using Mat canvas = src.Clone();
        if (showMask)
        {
            using Mat gray = Cv2.ImRead(sample.ImagePath, ImreadModes.Grayscale);
            using Mat mask = RoiQuantifier.CreateDefectMask(sample, settings, gray, out _);
            using Mat roi = RoiQuantifier.MakeSampleRoi(src.Size(), sample.ManualRoi, sample.Detection);
            using Mat validity = RoiQuantifier.ReadBinaryOrFull(sample.ValidityMaskPath, src.Size());
            using Mat active = new();
            using Mat overlap = new();
            Cv2.BitwiseAnd(mask, roi, active);
            Cv2.BitwiseAnd(active, validity, overlap);
            using Mat tint = canvas.Clone();
            tint.SetTo(new Scalar(0, 35, 255), overlap);
            Cv2.AddWeighted(tint, 0.55, canvas, 0.45, 0, canvas);
        }
        if (sample.ManualRoi.HasValue)
            Cv2.Rectangle(canvas, sample.ManualRoi.Value, new Scalar(0, 215, 255), 2);
        else if (sample.Detection?.Success == true)
        {
            if (sample.Detection.SamplePolygon.Length >= 3)
                Cv2.Polylines(canvas, new[] { sample.Detection.SamplePolygon }, true, new Scalar(255, 220, 0), 2);
            else Cv2.Rectangle(canvas, sample.Detection.SampleRect, new Scalar(255, 220, 0), 2);
            if (sample.Detection.TsvPolygon.Length >= 3)
                Cv2.Polylines(canvas, new[] { sample.Detection.TsvPolygon }, true, new Scalar(80, 255, 80), 2);
        }
        byte[] bytes = Cv2.ImEncode(".png", canvas);
        using var ms = new MemoryStream(bytes);
        using var intermediate = new Bitmap(ms);
        return new Bitmap(intermediate);
    }
}
