using OpenCvSharp;
using RFGAN.Batch;

static void Expect(bool condition, string description)
{
    if (!condition) throw new Exception("FAILED: " + description);
    Console.WriteLine("PASS: " + description);
}

string root = Path.Combine(Path.GetTempPath(), "rfgan-v1-tests-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
try
{
    Expect(FileIdentity.ParseSampleId("RawImageAll X01 Y002 ZGate1.png") == "X1Y2", "SAM sample filename parsing");
    Expect(FileIdentity.IsMaskFile("X1Y1_delam_mask.png"), "Explicit mask naming");
    Expect(!FileIdentity.IsMaskFile("X1Y1_DelamDie.png"), "DelamDie C-scan not mistaken for mask");
    string image = Path.Combine(root, "X1Y1.png");
    string mask = Path.Combine(root, "X1Y1_delam_mask.png");
    string validity = Path.Combine(root, "X1Y1_validity.png");

    using (var gray = new Mat(new Size(20, 20), MatType.CV_8UC1, Scalar.Black))
        gray.SaveImage(image);
    using (var binary = new Mat(new Size(20, 20), MatType.CV_8UC1, Scalar.Black))
    {
        Cv2.Rectangle(binary, new Rect(5, 5, 5, 5), Scalar.White, -1);
        binary.SaveImage(mask);
    }
    var found = new BatchImporter().ImportImages(root);
    Expect(found.Count == 1 && found[0].MaskPath == mask, "Exact 1:1 image-mask mapping");
    var sample = found.Single();
    sample.IdentityConfirmed = true;
    sample.ManualRoi = new Rect(5, 5, 10, 10);
    sample.RoiApproved = true;
    var settings = new BatchSettings();
    var processor = new BatchProcessor();
    processor.ProcessOne(sample, settings);
    Expect(sample.State == SampleState.Completed, "Manual ROI batch processing completes");
    Expect(sample.Result?.RoiPixels == 100, "Correct denominator from 10x10 ROI");
    Expect(sample.Result?.DelaminationPixels == 25, "Count defect mask only inside ROI");
    Expect(Math.Abs(sample.Result!.DelaminationPercent - 25.0) < 1e-9, "Known defect percentage 25.0%");

    using (var valid = new Mat(new Size(20, 20), MatType.CV_8UC1, Scalar.White))
    {
        Cv2.Rectangle(valid, new Rect(5, 5, 5, 5), Scalar.Black, -1);
        valid.SaveImage(validity);
    }
    sample.ValidityMaskPath = validity;
    processor.ProcessOne(sample, settings);
    Expect(sample.Result?.ValidRoiPixels == 75, "Invalid acquisition pixels removed from denominator");
    Expect(sample.Result?.DelaminationPixels == 0, "Invalid acoustic pixels not counted as defect");
    Expect(sample.State == SampleState.NeedsReview, "Low coverage requires review");

    sample.ValidityMaskPath = null;
    sample.MaskPath = null;
    processor.ProcessOne(sample, settings);
    Expect(sample.State == SampleState.Blocked && sample.Result == null, "Missing defect mask blocks measurement");

    sample.MaskPath = mask;
    processor.ProcessOne(sample, settings);
    Expect(sample.State == SampleState.Completed, "Can reprocess after restoring valid mask");
    using (var modified = new Mat(new Size(20, 20), MatType.CV_8UC1, Scalar.White))
        modified.SaveImage(mask);
    bool blockedStale = false;
    try { BatchExporter.Export(Path.Combine(root, "export"), new[] { sample }, settings); }
    catch (InvalidDataException) { blockedStale = true; }
    Expect(blockedStale, "Changed input mask blocked at export");
    Console.WriteLine("All smoke tests passed. Note: automatic detector accuracy requires a labeled SAM regression dataset.");
}
finally
{
    try { Directory.Delete(root, true); } catch { /* diagnostic assets can be cleaned manually */ }
}
