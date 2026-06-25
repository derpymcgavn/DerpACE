using System.Buffers.Binary;
using System.Globalization;
using System.IO.Compression;
using System.Text;

using ACE.DatLoader;
using ACE.DatLoader.FileTypes;

Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

const int LandblocksPerAxis = 256;
const int CellsPerLandblock = 8;
const int VerticesPerLandblock = 9;

var options = Options.Parse(args);

if (!File.Exists(options.CellDatPath))
    Fail($"Cell DAT not found: {options.CellDatPath}");

if (!File.Exists(options.PortalDatPath))
    Fail($"Portal DAT not found: {options.PortalDatPath}");

Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(options.OutputPath)) ?? ".");

var workingCellDatPath = PrepareDatPath(options.CellDatPath, options.CopyToTemp);
var workingPortalDatPath = PrepareDatPath(options.PortalDatPath, options.CopyToTemp);

Console.WriteLine($"Reading portal DAT: {workingPortalDatPath}");
var portalDat = new PortalDatDatabase(workingPortalDatPath);
var region = portalDat.RegionDesc;
var terrainColors = region.TerrainInfo.TerrainTypes
    .Select(t => DecodeTerrainColor(t.TerrainColor, options.ColorOrder))
    .ToArray();

Console.WriteLine($"Reading cell DAT: {workingCellDatPath}");
var cellDat = new CellDatDatabase(workingCellDatPath, true);

var pixelsPerCell = Math.Max(1, options.PixelsPerCell);
var imageSize = LandblocksPerAxis * CellsPerLandblock * pixelsPerCell;
var pixels = new byte[imageSize * imageSize * 4];
Fill(pixels, 11, 18, 22, 255);

var landblocks = 0;
var drawnCells = 0;

foreach (var entry in cellDat.AllFiles.Values.OrderBy(f => f.ObjectId))
{
    var fileId = entry.ObjectId;
    if ((fileId & 0xFFFF) != 0xFFFF)
        continue;

    var landblockId = fileId >> 16;
    var lbx = (int)((landblockId >> 8) & 0xFF);
    var lby = (int)(landblockId & 0xFF);

    CellLandblock landblock;
    try
    {
        landblock = cellDat.ReadFromDat<CellLandblock>(fileId);
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Skipping 0x{fileId:X8}: {ex.Message}");
        continue;
    }

    landblocks++;

    for (var cx = 0; cx < CellsPerLandblock; cx++)
    {
        for (var cy = 0; cy < CellsPerLandblock; cy++)
        {
            var terrain = GetDominantTerrain(landblock, cx, cy);
            var color = terrain < terrainColors.Length ? terrainColors[terrain] : new Rgba(60, 66, 62, 255);
            color = ApplyHeightShade(color, landblock, cx, cy, region.LandDefs.LandHeightTable);

            if (HasRoad(landblock, cx, cy))
                color = Blend(color, new Rgba(92, 88, 78, 255), 0.62);

            var px = (lbx * CellsPerLandblock + cx) * pixelsPerCell;
            var py = ((LandblocksPerAxis - 1 - lby) * CellsPerLandblock + (CellsPerLandblock - 1 - cy)) * pixelsPerCell;
            DrawBlock(pixels, imageSize, px, py, pixelsPerCell, color);
            drawnCells++;
        }
    }
}

Console.WriteLine($"Writing {imageSize}x{imageSize} PNG: {options.OutputPath}");
PngWriter.WriteRgba(options.OutputPath, imageSize, imageSize, pixels);
Console.WriteLine($"Done. Landblocks: {landblocks:N0}, terrain cells: {drawnCells:N0}");

static ushort GetDominantTerrain(CellLandblock landblock, int x, int y)
{
    Span<ushort> terrains =
    [
        CellLandblock.GetType(landblock.Terrain[x * VerticesPerLandblock + y]),
        CellLandblock.GetType(landblock.Terrain[(x + 1) * VerticesPerLandblock + y]),
        CellLandblock.GetType(landblock.Terrain[(x + 1) * VerticesPerLandblock + y + 1]),
        CellLandblock.GetType(landblock.Terrain[x * VerticesPerLandblock + y + 1])
    ];

    var best = terrains[0];
    var bestCount = 0;
    foreach (var terrain in terrains)
    {
        var count = 0;
        foreach (var compare in terrains)
        {
            if (terrain == compare)
                count++;
        }

        if (count > bestCount)
        {
            best = terrain;
            bestCount = count;
        }
    }

    return best;
}

static bool HasRoad(CellLandblock landblock, int x, int y)
{
    return CellLandblock.GetRoad(landblock.Terrain[x * VerticesPerLandblock + y]) != 0
        || CellLandblock.GetRoad(landblock.Terrain[(x + 1) * VerticesPerLandblock + y]) != 0
        || CellLandblock.GetRoad(landblock.Terrain[(x + 1) * VerticesPerLandblock + y + 1]) != 0
        || CellLandblock.GetRoad(landblock.Terrain[x * VerticesPerLandblock + y + 1]) != 0;
}

static Rgba ApplyHeightShade(Rgba color, CellLandblock landblock, int x, int y, IReadOnlyList<float> heightTable)
{
    var h1 = heightTable[landblock.Height[x * VerticesPerLandblock + y]];
    var h2 = heightTable[landblock.Height[(x + 1) * VerticesPerLandblock + y]];
    var h3 = heightTable[landblock.Height[(x + 1) * VerticesPerLandblock + y + 1]];
    var h4 = heightTable[landblock.Height[x * VerticesPerLandblock + y + 1]];
    var eastSlope = ((h2 + h3) - (h1 + h4)) * 0.006f;
    var northSlope = ((h3 + h4) - (h1 + h2)) * 0.004f;
    var shade = Math.Clamp(1.0f + eastSlope + northSlope, 0.72f, 1.28f);

    return new Rgba(
        (byte)Math.Clamp((int)MathF.Round(color.R * shade), 0, 255),
        (byte)Math.Clamp((int)MathF.Round(color.G * shade), 0, 255),
        (byte)Math.Clamp((int)MathF.Round(color.B * shade), 0, 255),
        color.A);
}

static Rgba DecodeTerrainColor(uint value, string order)
{
    var b0 = (byte)((value >> 24) & 0xFF);
    var b1 = (byte)((value >> 16) & 0xFF);
    var b2 = (byte)((value >> 8) & 0xFF);
    var b3 = (byte)(value & 0xFF);

    return order.ToLowerInvariant() switch
    {
        "rgba" => new Rgba(b0, b1, b2, b3 == 0 ? (byte)255 : b3),
        "abgr" => new Rgba(b3, b2, b1, b0 == 0 ? (byte)255 : b0),
        "bgra" => new Rgba(b2, b1, b0, b3 == 0 ? (byte)255 : b3),
        _ => new Rgba(b1, b2, b3, b0 == 0 ? (byte)255 : b0)
    };
}

static Rgba Blend(Rgba a, Rgba b, double amount)
{
    amount = Math.Clamp(amount, 0.0, 1.0);
    return new Rgba(
        (byte)Math.Round(a.R + (b.R - a.R) * amount),
        (byte)Math.Round(a.G + (b.G - a.G) * amount),
        (byte)Math.Round(a.B + (b.B - a.B) * amount),
        255);
}

static void DrawBlock(byte[] pixels, int imageSize, int x, int y, int size, Rgba color)
{
    for (var yy = 0; yy < size; yy++)
    {
        var row = ((y + yy) * imageSize + x) * 4;
        for (var xx = 0; xx < size; xx++)
        {
            var offset = row + xx * 4;
            pixels[offset] = color.R;
            pixels[offset + 1] = color.G;
            pixels[offset + 2] = color.B;
            pixels[offset + 3] = color.A;
        }
    }
}

static void Fill(byte[] pixels, byte r, byte g, byte b, byte a)
{
    for (var i = 0; i < pixels.Length; i += 4)
    {
        pixels[i] = r;
        pixels[i + 1] = g;
        pixels[i + 2] = b;
        pixels[i + 3] = a;
    }
}

static void Fail(string message)
{
    Console.Error.WriteLine(message);
    System.Environment.Exit(1);
}

static string PrepareDatPath(string sourcePath, bool copyToTemp)
{
    if (!copyToTemp)
        return sourcePath;

    var tempRoot = Path.Combine(Path.GetTempPath(), "DerpACE.AdminMapGen");
    Directory.CreateDirectory(tempRoot);

    var destPath = Path.Combine(tempRoot, $"{Path.GetFileNameWithoutExtension(sourcePath)}-{File.GetLastWriteTimeUtc(sourcePath).Ticks:X}{Path.GetExtension(sourcePath)}");
    if (File.Exists(destPath) && new FileInfo(destPath).Length == new FileInfo(sourcePath).Length)
        return destPath;

    Console.WriteLine($"Copying DAT for shared read: {sourcePath}");
    using var source = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
    using var dest = new FileStream(destPath, FileMode.Create, FileAccess.Write, FileShare.None);
    source.CopyTo(dest);
    return destPath;
}

readonly record struct Rgba(byte R, byte G, byte B, byte A);

sealed record Options
{
    public string CellDatPath { get; init; } = @"C:\Turbine\Asheron's Call\client_cell_1.dat";
    public string PortalDatPath { get; init; } = @"C:\Turbine\Asheron's Call\client_portal.dat";
    public string OutputPath { get; init; } = Path.Combine("Source", "ACE.Server", "Data", "AdminMap", "dereth-map.png");
    public int PixelsPerCell { get; init; } = 4;
    public string ColorOrder { get; init; } = "argb";
    public bool CopyToTemp { get; init; } = true;

    public static Options Parse(string[] args)
    {
        var options = new Options();

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            var value = i + 1 < args.Length ? args[i + 1] : null;

            switch (arg.ToLowerInvariant())
            {
                case "--cell":
                case "--cell-dat":
                    options = options with { CellDatPath = RequireValue(arg, value) };
                    i++;
                    break;
                case "--portal":
                case "--portal-dat":
                    options = options with { PortalDatPath = RequireValue(arg, value) };
                    i++;
                    break;
                case "--out":
                case "--output":
                    options = options with { OutputPath = RequireValue(arg, value) };
                    i++;
                    break;
                case "--scale":
                case "--pixels-per-cell":
                    options = options with { PixelsPerCell = int.Parse(RequireValue(arg, value), CultureInfo.InvariantCulture) };
                    i++;
                    break;
                case "--color-order":
                    options = options with { ColorOrder = RequireValue(arg, value) };
                    i++;
                    break;
                case "--no-copy":
                    options = options with { CopyToTemp = false };
                    break;
                case "-h":
                case "--help":
                    PrintHelp();
                    System.Environment.Exit(0);
                    break;
            }
        }

        return options;
    }

    private static string RequireValue(string arg, string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException($"{arg} requires a value.");

        return value;
    }

    private static void PrintHelp()
    {
        Console.WriteLine("""
        Generates an overworld admin map PNG from Asheron's Call DAT files.

        Options:
          --cell <path>          Path to client_cell_1.dat
          --portal <path>        Path to client_portal.dat
          --out <path>           Output PNG path
          --scale <n>            Pixels per terrain cell. 1 = 2048x2048, 2 = 4096x4096, 4 = 8192x8192
          --color-order <order>  argb, rgba, abgr, or bgra for TerrainColor decoding
          --no-copy              Read DATs in place instead of copying to temp first
        """);
    }
}

static class PngWriter
{
    private static readonly byte[] Signature = [137, 80, 78, 71, 13, 10, 26, 10];

    public static void WriteRgba(string path, int width, int height, byte[] rgba)
    {
        using var output = File.Create(path);
        output.Write(Signature);

        Span<byte> ihdr = stackalloc byte[13];
        BinaryPrimitives.WriteInt32BigEndian(ihdr, width);
        BinaryPrimitives.WriteInt32BigEndian(ihdr[4..], height);
        ihdr[8] = 8;
        ihdr[9] = 6;
        ihdr[10] = 0;
        ihdr[11] = 0;
        ihdr[12] = 0;
        WriteChunk(output, "IHDR", ihdr);

        using var compressed = new MemoryStream();
        using (var z = new ZLibStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
        {
            var stride = width * 4;
            for (var y = 0; y < height; y++)
            {
                z.WriteByte(0);
                z.Write(rgba, y * stride, stride);
            }
        }

        WriteChunk(output, "IDAT", compressed.ToArray());
        WriteChunk(output, "IEND", ReadOnlySpan<byte>.Empty);
    }

    private static void WriteChunk(Stream output, string type, ReadOnlySpan<byte> data)
    {
        Span<byte> length = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(length, data.Length);
        output.Write(length);

        var typeBytes = Encoding.ASCII.GetBytes(type);
        output.Write(typeBytes);
        output.Write(data);

        var crc = Crc32(typeBytes, data);
        Span<byte> crcBytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(crcBytes, crc);
        output.Write(crcBytes);
    }

    private static uint Crc32(ReadOnlySpan<byte> type, ReadOnlySpan<byte> data)
    {
        var crc = 0xFFFFFFFFu;
        Update(type);
        Update(data);
        return crc ^ 0xFFFFFFFFu;

        void Update(ReadOnlySpan<byte> bytes)
        {
            foreach (var b in bytes)
            {
                crc ^= b;
                for (var i = 0; i < 8; i++)
                    crc = (crc & 1) != 0 ? 0xEDB88320u ^ (crc >> 1) : crc >> 1;
            }
        }
    }
}
