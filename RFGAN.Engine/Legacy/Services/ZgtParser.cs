using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;

namespace RFGAN.SampleMapper.Services;

public enum ZgtCscanMode
{
    AbsPeak,
    PeakToPeak,
    Rms,
    MeanAbs,
    RuntimeMap,
    GateAbsPeak,
    GatePeakToPeak,
    GateRms,
    GateMeanAbs,
    GateSignedPeak,
    GatePeakIndex
}

public sealed class ZgtParseException : Exception
{
    public ZgtParseException(string message) : base(message) { }
}

public sealed class ZgtRowInfo
{
    public int RowIndex { get; init; }
    public int RowLength { get; init; }
    public long RowLengthOffset { get; init; }
    public long RowBlockStart { get; init; }
    public long TableOffset { get; init; }
    public long WaveformsOffset { get; init; }
    public int ValidCount { get; init; }
    public bool RowFormulaMatches { get; init; }
}

public sealed class ZgtIndex
{
    public const int InvalidMarker = -1;

    public string FilePath { get; }
    public long FileSize { get; }
    public int Width { get; }
    public int Height { get; }
    public int TraceLength { get; }
    public uint[] RawHeader { get; }
    public int[,] RowTables { get; }
    public bool[,] Mask { get; }
    public IReadOnlyList<ZgtRowInfo> Rows { get; }

    public ZgtIndex(
        string filePath,
        long fileSize,
        int width,
        int height,
        int traceLength,
        uint[] rawHeader,
        int[,] rowTables,
        bool[,] mask,
        List<ZgtRowInfo> rows)
    {
        FilePath = filePath;
        FileSize = fileSize;
        Width = width;
        Height = height;
        TraceLength = traceLength;
        RawHeader = rawHeader;
        RowTables = rowTables;
        Mask = mask;
        Rows = rows;
    }

    public int GetRuntimeValue(int x, int y)
    {
        return RowTables[y, x];
    }

    public bool IsValid(int x, int y)
    {
        return Mask[y, x];
    }

    public double? SampleIntervalNsIfHeader2IsMsps
    {
        get
        {
            if (RawHeader.Length < 3) return null;
            int sr = unchecked((int)RawHeader[2]);
            if (sr <= 0) return null;
            return 1000.0 / sr;
        }
    }

    public (int SampleStart, int SampleEnd) ResolveGateSamples(int gateStart, int gateLength)
    {
        if (gateLength <= 0)
            throw new InvalidOperationException("Gate length must be > 0.");

        int sampleStart = Math.Max(0, gateStart);
        int sampleEnd = Math.Min(TraceLength, gateStart + gateLength);

        if (sampleStart >= sampleEnd)
            throw new InvalidOperationException($"Resolved gate is empty. start={sampleStart}, end={sampleEnd}");

        return (sampleStart, sampleEnd);
    }

    public short[] GetTrace(int x, int y)
    {
        if (x < 0 || x >= Width || y < 0 || y >= Height)
            throw new ArgumentOutOfRangeException($"Pixel ({x}, {y}) out of range.");

        if (!IsValid(x, y))
            throw new InvalidOperationException($"Pixel ({x}, {y}) is invalid.");

        ZgtRowInfo row = Rows[y];

        int validBefore = 0;
        for (int i = 0; i < x; i++)
        {
            if (RowTables[y, i] != InvalidMarker)
                validBefore++;
        }

        long traceOffset = row.WaveformsOffset + (long)validBefore * TraceLength;

        using var fs = new FileStream(FilePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        fs.Seek(traceOffset, SeekOrigin.Begin);

        byte[] raw = new byte[TraceLength];
        int read = fs.Read(raw, 0, raw.Length);

        if (read != raw.Length)
            throw new ZgtParseException($"Could not read full trace for pixel ({x}, {y}).");

        short[] trace = new short[TraceLength];
        for (int i = 0; i < TraceLength; i++)
            trace[i] = unchecked((sbyte)raw[i]);

        return trace;
    }

    public float[,] BuildCscan(ZgtCscanMode mode, int? gateStart = null, int? gateEnd = null)
    {
        float[,] image = new float[Height, Width];

        for (int y = 0; y < Height; y++)
        {
            for (int x = 0; x < Width; x++)
                image[y, x] = float.NaN;
        }

        if (mode == ZgtCscanMode.RuntimeMap)
        {
            for (int y = 0; y < Height; y++)
            {
                for (int x = 0; x < Width; x++)
                {
                    if (Mask[y, x])
                        image[y, x] = RowTables[y, x];
                }
            }
            return image;
        }

        int segStart = gateStart ?? 0;
        int segEnd = gateEnd ?? TraceLength;

        segStart = Math.Clamp(segStart, 0, TraceLength - 1);
        segEnd = Math.Clamp(segEnd, segStart + 1, TraceLength);

        using var fs = new FileStream(FilePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var br = new BinaryReader(fs);

        for (int y = 0; y < Height; y++)
        {
            ZgtRowInfo row = Rows[y];
            int validCount = row.ValidCount;
            int blockBytes = validCount * TraceLength;

            fs.Seek(row.WaveformsOffset, SeekOrigin.Begin);

            byte[] waveformBlock = br.ReadBytes(blockBytes);
            if (waveformBlock.Length != blockBytes)
                throw new ZgtParseException($"Unexpected EOF while reading waveform block for row {y}.");

            int traceOrdinal = 0;

            for (int x = 0; x < Width; x++)
            {
                if (!Mask[y, x])
                    continue;

                int traceOffset = traceOrdinal * TraceLength;
                traceOrdinal++;

                image[y, x] = ComputeMetric(waveformBlock, traceOffset, segStart, segEnd, mode);
            }
        }

        return image;
    }

    private static float ComputeMetric(byte[] rowBlock, int traceOffset, int segStart, int segEnd, ZgtCscanMode mode)
    {
        int len = segEnd - segStart;
        if (len <= 0)
            throw new InvalidOperationException("Metric segment is empty.");

        int firstValue = unchecked((sbyte)rowBlock[traceOffset + segStart]);
        int minVal = firstValue;
        int maxVal = firstValue;
        int signedPeakValue = firstValue;
        int signedPeakAbs = Math.Abs(firstValue);
        int peakIndexRel = 0;

        double sumAbs = 0.0;
        double sumSq = 0.0;

        for (int i = 0; i < len; i++)
        {
            int v = unchecked((sbyte)rowBlock[traceOffset + segStart + i]);
            int absV = Math.Abs(v);

            if (v < minVal) minVal = v;
            if (v > maxVal) maxVal = v;

            if (absV > signedPeakAbs)
            {
                signedPeakAbs = absV;
                signedPeakValue = v;
                peakIndexRel = i;
            }

            sumAbs += absV;
            sumSq += (double)v * v;
        }

        return mode switch
        {
            ZgtCscanMode.AbsPeak => signedPeakAbs,
            ZgtCscanMode.GateAbsPeak => signedPeakAbs,
            ZgtCscanMode.PeakToPeak => maxVal - minVal,
            ZgtCscanMode.GatePeakToPeak => maxVal - minVal,
            ZgtCscanMode.Rms => (float)Math.Sqrt(sumSq / len),
            ZgtCscanMode.GateRms => (float)Math.Sqrt(sumSq / len),
            ZgtCscanMode.MeanAbs => (float)(sumAbs / len),
            ZgtCscanMode.GateMeanAbs => (float)(sumAbs / len),
            ZgtCscanMode.GateSignedPeak => signedPeakValue,
            ZgtCscanMode.GatePeakIndex => peakIndexRel,
            _ => throw new InvalidOperationException($"Unsupported mode: {mode}")
        };
    }

    public Bitmap BuildMaskBitmap(int scale = 1)
    {
        scale = Math.Max(1, scale);
        int w = Math.Max(1, Width * scale);
        int h = Math.Max(1, Height * scale);

        Bitmap bmp = new Bitmap(w, h, PixelFormat.Format24bppRgb);
        BitmapData data = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, PixelFormat.Format24bppRgb);

        try
        {
            int stride = data.Stride;
            byte[] buffer = new byte[stride * h];

            for (int y = 0; y < Height; y++)
            {
                for (int sy = 0; sy < scale; sy++)
                {
                    int destY = y * scale + sy;
                    int rowBase = destY * stride;

                    for (int x = 0; x < Width; x++)
                    {
                        byte v = Mask[y, x] ? (byte)255 : (byte)0;
                        int destX = x * scale;

                        for (int sx = 0; sx < scale; sx++)
                        {
                            int offset = rowBase + (destX + sx) * 3;
                            buffer[offset + 0] = v;
                            buffer[offset + 1] = v;
                            buffer[offset + 2] = v;
                        }
                    }
                }
            }

            Marshal.Copy(buffer, 0, data.Scan0, buffer.Length);
        }
        finally
        {
            bmp.UnlockBits(data);
        }

        return bmp;
    }
}

public static class ZgtParser
{
    private const int ExpectedHeaderU32 = 6;

    public static ZgtIndex Parse(string filePath)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException("ZGT file not found.", filePath);

        long fileSize = new FileInfo(filePath).Length;

        using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var br = new BinaryReader(fs);

        uint[] rawHeader = new uint[ExpectedHeaderU32];
        for (int i = 0; i < ExpectedHeaderU32; i++)
            rawHeader[i] = br.ReadUInt32();

        int width = checked((int)rawHeader[0]);
        int height = checked((int)rawHeader[1]);
        int traceLength = checked((int)rawHeader[5]);

        if (width <= 0 || width > 100000)
            throw new ZgtParseException($"Unreasonable width: {width}");

        if (height <= 0 || height > 100000)
            throw new ZgtParseException($"Unreasonable height: {height}");

        if (traceLength <= 0 || traceLength > 10_000_000)
            throw new ZgtParseException($"Unreasonable trace length: {traceLength}");

        int[,] rowTables = new int[height, width];
        bool[,] mask = new bool[height, width];
        List<ZgtRowInfo> rows = new List<ZgtRowInfo>(height);

        for (int y = 0; y < height; y++)
        {
            long rowLengthOffset = fs.Position;

            if (fs.Position + 4 > fileSize)
                throw new ZgtParseException($"Unexpected EOF while reading row length for row {y}.");

            int rowLength = unchecked((int)br.ReadUInt32());
            long rowBlockStart = fs.Position;
            long tableOffset = rowBlockStart;

            int validCount = 0;

            for (int x = 0; x < width; x++)
            {
                if (fs.Position + 4 > fileSize)
                    throw new ZgtParseException($"Unexpected EOF while reading row table at row {y}, col {x}.");

                int v = br.ReadInt32();
                rowTables[y, x] = v;

                bool isValid = v != ZgtIndex.InvalidMarker;
                mask[y, x] = isValid;
                if (isValid) validCount++;
            }

            long waveformsOffset = fs.Position;
            int expectedRowLength = width * 4 + validCount * traceLength;
            bool formulaMatches = rowLength == expectedRowLength;

            long rowEnd = rowBlockStart + rowLength;
            if (rowEnd > fileSize)
                throw new ZgtParseException($"Row {y} exceeds file size.");

            rows.Add(new ZgtRowInfo
            {
                RowIndex = y,
                RowLength = rowLength,
                RowLengthOffset = rowLengthOffset,
                RowBlockStart = rowBlockStart,
                TableOffset = tableOffset,
                WaveformsOffset = waveformsOffset,
                ValidCount = validCount,
                RowFormulaMatches = formulaMatches
            });

            fs.Seek(rowEnd, SeekOrigin.Begin);
        }

        return new ZgtIndex(filePath, fileSize, width, height, traceLength, rawHeader, rowTables, mask, rows);
    }
}

public static class ZgtImageExporter
{
    public static Bitmap CreateBitmapFromFloatImage(float[,] image)
    {
        int height = image.GetLength(0);
        int width = image.GetLength(1);

        float min = float.PositiveInfinity;
        float max = float.NegativeInfinity;

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                float v = image[y, x];
                if (float.IsNaN(v) || float.IsInfinity(v))
                    continue;

                if (v < min) min = v;
                if (v > max) max = v;
            }
        }

        if (float.IsInfinity(min) || float.IsInfinity(max))
            throw new InvalidOperationException("Image contains no finite values.");

        if (Math.Abs(max - min) < 1e-12f)
            max = min + 1f;

        Bitmap bmp = new Bitmap(width, height, PixelFormat.Format24bppRgb);
        BitmapData data = bmp.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.WriteOnly, PixelFormat.Format24bppRgb);

        try
        {
            int stride = data.Stride;
            byte[] buffer = new byte[stride * height];

            for (int y = 0; y < height; y++)
            {
                int rowBase = y * stride;

                for (int x = 0; x < width; x++)
                {
                    float v = image[y, x];
                    byte g = 0;

                    if (!float.IsNaN(v) && !float.IsInfinity(v))
                    {
                        float norm = (v - min) / (max - min);
                        norm = Math.Clamp(norm, 0f, 1f);
                        g = (byte)Math.Round(norm * 255f);
                    }

                    int offset = rowBase + x * 3;
                    buffer[offset + 0] = g;
                    buffer[offset + 1] = g;
                    buffer[offset + 2] = g;
                }
            }

            Marshal.Copy(buffer, 0, data.Scan0, buffer.Length);
        }
        finally
        {
            bmp.UnlockBits(data);
        }

        return bmp;
    }

    public static void SaveBitmap(Bitmap bitmap, string outputPath)
    {
        string? dir = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrWhiteSpace(dir))
            Directory.CreateDirectory(dir);

        bitmap.Save(outputPath, ImageFormat.Png);
    }
}