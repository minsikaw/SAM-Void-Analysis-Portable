using OpenCvSharp;

namespace RFGAN.Batch;

public static class BatchInputValidator
{
    // This verifies structural input validity, not whether a given acoustic pixel
    // is actually a delamination defect. That requires a validated quantification method.
    public static void ValidateImport(BatchSample sample, BatchSettings settings)
    {
        sample.Result = null;
        if (!string.IsNullOrEmpty(sample.ImportError))
        {
            sample.State = SampleState.Blocked; sample.Message = sample.ImportError;
            return;
        }
        try
        {
            if (string.IsNullOrWhiteSpace(sample.ImagePath) || !File.Exists(sample.ImagePath))
                throw new InvalidDataException("Image missing.");
            using Mat scan = Cv2.ImRead(sample.ImagePath, ImreadModes.Grayscale);
            if (scan.Empty()) throw new InvalidDataException("Image cannot be decoded.");
            if (settings.MaskMode == MaskMode.ExternalBinaryMask)
            {
                if (!sample.MaskPresent)
                {
                    sample.State = SampleState.NeedsReview;
                    sample.Message = "Image valid; missing defect mask (assign one).";
                    return;
                }
                using Mat mask = Cv2.ImRead(sample.MaskPath!, ImreadModes.Grayscale);
                if (mask.Empty()) throw new InvalidDataException("Defect mask cannot be decoded.");
                if (mask.Width != scan.Width || mask.Height != scan.Height)
                    throw new InvalidDataException("Defect mask dimensions differ from C-scan image.");
            }
            if (!string.IsNullOrWhiteSpace(sample.ValidityMaskPath))
            {
                using Mat validity = Cv2.ImRead(sample.ValidityMaskPath, ImreadModes.Grayscale);
                if (validity.Empty() || validity.Width != scan.Width || validity.Height != scan.Height)
                    throw new InvalidDataException("Acquisition-validity mask missing or has mismatched dimensions.");
            }
            sample.State = SampleState.Ready;
            sample.Message = sample.IdentityConfirmed
                ? "Input structure valid; ready for sample detection"
                : "Image/mask valid; sample identity still needs confirmation";
        }
        catch (Exception ex)
        {
            sample.State = SampleState.Blocked;
            sample.Message = "Invalid input: " + ex.Message;
        }
    }
}
