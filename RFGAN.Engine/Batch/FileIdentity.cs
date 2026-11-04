using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace RFGAN.Batch;

public static class FileIdentity
{
    private static readonly Regex Pos = new(@"(?<![A-Za-z0-9])X\s*0*(\d+)\s*[_\s-]*Y\s*0*(\d+)(?!\d)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static string? ParseSampleId(string path)
    {
        string stem = Path.GetFileNameWithoutExtension(path);
        Match m = Pos.Match(stem);
        return m.Success ? $"X{int.Parse(m.Groups[1].Value)}Y{int.Parse(m.Groups[2].Value)}" : null;
    }

    public static string Sha256File(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return "";
        using FileStream stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    public static string HashJson(object value)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value)))).ToLowerInvariant();

    public static bool IsSupportedImage(string path)
        => new[] { ".png", ".jpg", ".jpeg", ".bmp", ".tif", ".tiff" }
            .Contains(Path.GetExtension(path).ToLowerInvariant());

    public static bool IsMaskFile(string path)
    {
        string name = Path.GetFileNameWithoutExtension(path).ToLowerInvariant();
        return Regex.IsMatch(name, @"(?:^|[_\s~.\-])(?:delam[_\s\-]?)?mask(?:$|[_\s~.\-])", RegexOptions.IgnoreCase) ||
               Regex.IsMatch(name, @"(?:^|[_\s~.\-])validity(?:$|[_\s~.\-])", RegexOptions.IgnoreCase) ||
               path.Split(Path.DirectorySeparatorChar).Any(p => p.Equals("Masks", StringComparison.OrdinalIgnoreCase));
    }

    public static string Csv(string? value)
    {
        string text = value ?? "";
        // Prevent spreadsheet formula execution when users open CSV exports.
        if (text.Length > 0 && "=+-@".Contains(text[0])) text = "'" + text;
        return "\"" + text.Replace("\"", "\"\"") + "\"";
    }
}
