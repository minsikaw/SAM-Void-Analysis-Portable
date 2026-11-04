using System.Globalization;
using System.Text;
using System.Text.Json;

namespace RFGAN.Batch;

public static class BatchExporter
{
    public static void Export(string folder, IReadOnlyList<BatchSample> samples, BatchSettings settings)
    {
        if (samples.Count == 0) throw new InvalidOperationException("No samples to export.");
        // A result must never be exported as completed after any inputs have changed.
        foreach (BatchSample sample in samples.Where(s => s.State == SampleState.Completed && s.Result != null))
        {
            Measurement result = sample.Result!;
            if (result.ImageHash != FileIdentity.Sha256File(sample.OriginalPath) ||
                result.CscanHash != FileIdentity.Sha256File(sample.ImagePath) ||
                result.MaskHash != FileIdentity.Sha256File(sample.MaskPath) ||
                result.ValidityHash != FileIdentity.Sha256File(sample.ValidityMaskPath))
                throw new InvalidDataException($"Input changed since analysis: {sample.Identity}. Re-run the sample before export.");
        }
        Directory.CreateDirectory(folder);
        var recipe = DetectionRecipeFactory.Create(settings);
        var records = samples.Select(s => new
        {
            SampleId = s.Identity,
            s.IdentityConfirmed,
            Source = s.OriginalPath,
            Cscan = s.ImagePath,
            DefectMask = s.MaskPath,
            AcquisitionValidityMask = s.ValidityMaskPath,
            State = s.State.ToString(),
            s.Message,
            Result = s.Result,
            Detector = s.Detection == null ? null : new
            {
                s.Detection.Success, s.Detection.Confidence,
                s.Detection.ConfidenceLevel, s.Detection.FailureReason,
                s.Detection.RecipeHashSha256,
                SampleBox = new { s.Detection.SampleRect.X, s.Detection.SampleRect.Y,
                    s.Detection.SampleRect.Width, s.Detection.SampleRect.Height },
                SamplePolygon = s.Detection.SamplePolygon.Select(p => new { p.X, p.Y }).ToArray()
            },
            ManualRoi = s.ManualRoi.HasValue ? new
            {
                s.ManualRoi.Value.X, s.ManualRoi.Value.Y,
                s.ManualRoi.Value.Width, s.ManualRoi.Value.Height
            } : null,
            s.RoiApproved
        }).ToList();

        var manifest = new
        {
            Schema = "RFGAN.BatchQuantifier.v1",
            ExportedAtUtc = DateTimeOffset.UtcNow,
            RecipeHashSha256 = recipe.RecipeHashSha256,
            Settings = settings,
            Summary = new { Total = samples.Count,
                Completed = samples.Count(s => s.State == SampleState.Completed),
                NeedsReview = samples.Count(s => s.State == SampleState.NeedsReview),
                Blocked = samples.Count(s => s.State == SampleState.Blocked),
                Errors = samples.Count(s => s.State == SampleState.Error) },
            Samples = records
        };
        var options = new JsonSerializerOptions { WriteIndented = true };
        WriteAtomic(Path.Combine(folder, "results.json"), JsonSerializer.Serialize(manifest, options));

        var csv = new StringBuilder();
        csv.AppendLine("SampleID,IdentityConfirmed,Source,Image,Mask,State,Message,RoiPixels,MeasuredPixels,CoveragePercent,DefectPixels,DelamPercent,TsvDelamPercent,Confidence,ManualRoi,Exploratory,RecipeHash,ImageHash,CscanHash,MaskHash");
        foreach (BatchSample s in samples)
        {
            Measurement? r = s.Result;
            string[] cols =
            {
                s.Identity, s.IdentityConfirmed.ToString(), s.OriginalPath, s.ImagePath, s.MaskPath ?? "",
                s.State.ToString(), s.Message,
                r?.RoiPixels.ToString(CultureInfo.InvariantCulture) ?? "",
                r?.ValidRoiPixels.ToString(CultureInfo.InvariantCulture) ?? "",
                r?.CoveragePercent.ToString("F4", CultureInfo.InvariantCulture) ?? "",
                r?.DelaminationPixels.ToString(CultureInfo.InvariantCulture) ?? "",
                r?.DelaminationPercent.ToString("F4", CultureInfo.InvariantCulture) ?? "",
                r?.TsvDelaminationPercent?.ToString("F4", CultureInfo.InvariantCulture) ?? "",
                r?.RoiConfidence.ToString("F4", CultureInfo.InvariantCulture) ?? "",
                r?.ManualRoi.ToString() ?? "", r?.Exploratory.ToString() ?? "",
                r?.RecipeHash ?? "", r?.ImageHash ?? "", r?.CscanHash ?? "", r?.MaskHash ?? ""
            };
            csv.AppendLine(string.Join(",", cols.Select(FileIdentity.Csv)));
        }
        WriteAtomic(Path.Combine(folder, "results.csv"), csv.ToString());
    }

    private static void WriteAtomic(string path, string text)
    {
        string temp = path + ".tmp";
        File.WriteAllText(temp, text, new UTF8Encoding(false));
        File.Move(temp, path, overwrite: true);
    }
}
