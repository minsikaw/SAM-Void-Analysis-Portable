using System.Drawing;
using System.Drawing.Imaging;
using OpenCvSharp;
using RFGAN.SampleMapper.Services;

namespace RFGAN.Batch;

public sealed class BatchImporter
{
    public List<BatchSample> ImportImages(string folder, CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(folder)) throw new DirectoryNotFoundException(folder);
        string[] images = Directory.EnumerateFiles(folder, "*.*", SearchOption.AllDirectories)
            .Where(FileIdentity.IsSupportedImage)
            .Where(p => !p.Split(Path.DirectorySeparatorChar).Any(s =>
                s.Equals("Summary", StringComparison.OrdinalIgnoreCase) ||
                s.Equals(".RFGAN-v1", StringComparison.OrdinalIgnoreCase)))
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToArray();
        string[] masks = images.Where(FileIdentity.IsMaskFile)
            .Where(p => !Path.GetFileName(p).Contains("validity", StringComparison.OrdinalIgnoreCase)).ToArray();
        var masksByIdentity = masks.Select(m => (Path: m, Id: FileIdentity.ParseSampleId(m)))
            .Where(m => m.Id != null)
            .GroupBy(m => m.Id!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Select(m => m.Path).ToArray(), StringComparer.OrdinalIgnoreCase);

        var result = new List<BatchSample>();
        foreach (string image in images.Where(p => !FileIdentity.IsMaskFile(p)))
        {
            cancellationToken.ThrowIfCancellationRequested();
            string? id = FileIdentity.ParseSampleId(image);
            masksByIdentity.TryGetValue(id ?? "", out string[]? candidates);
            var sample = new BatchSample
            {
                OriginalPath = image, ImagePath = image, Identity = id ?? "UNIDENTIFIED",
                IdentityConfirmed = false, SourceFormat = SourceFormat.Image,
                MaskPath = candidates?.Length == 1 ? candidates[0] : null,
                ImportError = candidates?.Length > 1 ? "Multiple masks match the same sample; assign one manually." : null
            };
            try
            {
                using Mat mat = Cv2.ImRead(image, ImreadModes.Grayscale);
                if (mat.Empty()) sample.ImportError = "Cannot decode image.";
            }
            catch (Exception ex) { sample.ImportError = $"Cannot open image: {ex.Message}"; }
            result.Add(sample);
        }
        return result;
    }

    public List<BatchSample> ImportZgt(
        string folder, string workspace, BatchSettings settings,
        IProgress<(int current, int total, string name)>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(folder)) throw new DirectoryNotFoundException(folder);
        string[] paths = Directory.GetFiles(folder, "*.*", SearchOption.TopDirectoryOnly)
            .Where(p => Path.GetExtension(p).Equals(".zgt", StringComparison.OrdinalIgnoreCase))
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToArray();
        Directory.CreateDirectory(workspace);
        var samples = new List<BatchSample>();
        for (int i = 0; i < paths.Length; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string path = paths[i];
            progress?.Report((i + 1, paths.Length, Path.GetFileName(path)));
            string id = FileIdentity.ParseSampleId(path) ?? "UNIDENTIFIED";
            var sample = new BatchSample
            {
                Identity = id, SourceFormat = SourceFormat.Zgt,
                OriginalPath = path, IdentityConfirmed = false
            };
            samples.Add(sample);
            try
            {
                ZgtIndex index = ZgtParser.Parse(path);
                if (index.Rows.Any(r => !r.RowFormulaMatches))
                    throw new ZgtParseException("ZGT row-length validation failed.");
                // Never call the acquisition-validity bitmap a delamination mask.
                float[,] cscan = index.BuildCscan(settings.ZgtMode, settings.GateStart, settings.GateEnd);
                string safeStem = $"{i + 1:D4}_{id}";
                sample.ImagePath = Path.Combine(workspace, safeStem + "_cscan.png");
                sample.ValidityMaskPath = Path.Combine(workspace, safeStem + "_validity.png");
                using (Bitmap png = ZgtImageExporter.CreateBitmapFromFloatImage(cscan))
                    ZgtImageExporter.SaveBitmap(png, sample.ImagePath);
                using (Bitmap validity = index.BuildMaskBitmap())
                    ZgtImageExporter.SaveBitmap(validity, sample.ValidityMaskPath);
                if (!index.Mask.Cast<bool>().Any(v => v))
                    sample.ImportError = "ZGT has no valid acquisition pixels.";
            }
            catch (Exception ex)
            {
                sample.ImportError = "ZGT conversion failed: " + ex.Message;
            }
        }
        return samples;
    }
}
