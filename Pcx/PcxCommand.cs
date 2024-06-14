using MetalMintSolid.Extensions;
using MetalMintSolid.Util;
using System.CommandLine;
using System.Drawing;
using System.Reflection.PortableExecutable;
using System.Text.Json;

namespace MetalMintSolid.Pcx;

public static class PcxCommand
{
    private static readonly JsonSerializerOptions _serializerOptions = new()
    {
        Converters = 
        {
            new ColorJsonConverter()
        }
    };

    public static Command AddPcxCommand(this Command command)
    {
        var pcxCommand = new Command("pcx", "Manipulate PCX images");

        var pcxEncodeCommand = new Command("encode", "Encodes a PCX image from a source bitmap");
        var pcxEncodeSourceArgument = new Argument<FileInfo>("source", "Source image");
        var pcxEncodeTargetArgument = new Argument<FileInfo?>("target", "Output filename");
        var pcxEncodeMetadataSourceOption = new Option<FileInfo?>("--metadata", "Copy metadata from supplied file, properly sets MGS specific data (useful for texture swapping)");
        var pcxEncodePaletteOption = new Option<FileInfo?>("--palette", "Uses a predefined palette instead of generating one (useful for when multiple textures share the same CLUT)");
        pcxEncodeCommand.AddArgument(pcxEncodeSourceArgument);
        pcxEncodeCommand.AddArgument(pcxEncodeTargetArgument);
        pcxEncodeCommand.AddOption(pcxEncodeMetadataSourceOption);
        pcxEncodeCommand.AddOption(pcxEncodePaletteOption);
        pcxEncodeCommand.SetHandler(EncodeHandler, pcxEncodeSourceArgument, pcxEncodeTargetArgument, pcxEncodeMetadataSourceOption, pcxEncodePaletteOption);

        var pcxDecodeCommand = new Command("decode", "Decodes a PCX image to a btimap");
        var pcxDecodeCommandSourceArgument = new Argument<FileInfo>("source", "Source PCX image");
        var pcxDecodeCommandTargetArgument = new Argument<FileInfo?>("target", "Output filename");
        pcxDecodeCommand.AddArgument(pcxDecodeCommandSourceArgument);
        pcxDecodeCommand.AddArgument(pcxDecodeCommandTargetArgument);
        pcxDecodeCommand.SetHandler(DecodeHandler, pcxDecodeCommandSourceArgument, pcxDecodeCommandTargetArgument);

        var pcxBatchDecodeCommand = new Command("batchdecode", "Decodes PCX images to a bitmaps");
        var pcxBatchDecodeCommandSourceArgument = new Argument<DirectoryInfo>("source", "Source directory");
        var pcxBatchDecodeCommandTargetArgument = new Argument<DirectoryInfo>("target", "Output directoy");
        pcxBatchDecodeCommand.AddArgument(pcxBatchDecodeCommandSourceArgument);
        pcxBatchDecodeCommand.AddArgument(pcxBatchDecodeCommandTargetArgument);
        pcxBatchDecodeCommand.SetHandler(BatchDecodeHandler, pcxBatchDecodeCommandSourceArgument, pcxBatchDecodeCommandTargetArgument);

        var pcxAnalyzeCommand = new Command("analyze", "Analyzes PCX image(s)");
        var pcxAnalyzeCommandSourceArgument = new Argument<string>("source", "Source file / directory");
        var pcxAnalyzeVerboseOption = new Option<bool>("--verbose", () => false, "Display verbose image info");
        pcxAnalyzeCommand.AddArgument(pcxAnalyzeCommandSourceArgument);
        pcxAnalyzeCommand.AddOption(pcxAnalyzeVerboseOption);
        pcxAnalyzeCommand.SetHandler(AnalyzeHandler, pcxAnalyzeCommandSourceArgument, pcxAnalyzeVerboseOption);

        pcxCommand.AddCommand(pcxDecodeCommand);
        pcxCommand.AddCommand(pcxEncodeCommand);
        pcxCommand.AddCommand(pcxBatchDecodeCommand);
        pcxCommand.AddCommand(pcxAnalyzeCommand);

        command.Add(pcxCommand);
        return pcxCommand;
    }

    private static void EncodeHandler(FileInfo source, FileInfo? target, FileInfo? metadata, FileInfo? palette)
    {
        if (!source.Exists) throw new FileNotFoundException("Specified bitmap was not found", source.FullName);
        target ??= new FileInfo($"{Path.GetFileNameWithoutExtension(source.FullName)}.pcx");

        List<Color>? colors = null;
        if (palette != null)
        {
            if (!palette.Exists) throw new FileNotFoundException("Specified palette file was not found", palette.FullName);
            using var paletteFile = palette.OpenRead();

            colors = JsonSerializer.Deserialize<List<Color>>(paletteFile, _serializerOptions) ?? throw new NotSupportedException("Failed to deserialize palette contents");
            if (colors.Count > 16) throw new NotSupportedException($"Palette contains {colors.Count} colors, only up to 16 are supported");
        }

        var bitmap = new Bitmap(source.FullName);
        var pcx = PcxImage.FromBitmap(bitmap, colors);

        if (metadata != null)
        {
            if (!metadata.Exists) throw new FileNotFoundException("Specified metadata source PCX file was not found", metadata.FullName);
            using var metadataFile = metadata.OpenRead();
            using var reader = new BinaryReader(metadataFile);
            var metaPcx = reader.ReadPcxImage();

            pcx.Header.Flags = metaPcx.Header.Flags;
            pcx.Header.Px = metaPcx.Header.Px;
            pcx.Header.Py = metaPcx.Header.Py;
            pcx.Header.Cx = metaPcx.Header.Cx;
            pcx.Header.Cy = metaPcx.Header.Cy;

            // Just to be sure
            pcx.Header.Padding = metaPcx.Header.Padding;
            pcx.Header.Reserved = metaPcx.Header.Reserved;
        }
        else
        {
            Console.WriteLine("Creating basic PCX file with no MGS specific metadata");
            Console.WriteLine("Specify a --metadata original pcx file for texture swapping");
        }

        using var file = target.Open(FileMode.Create);
        using var writer = new BinaryWriter(file);
        writer.Write(pcx);
    }

    private static void DecodeHandler(FileInfo source, FileInfo? target)
    {
        if (!source.Exists) throw new FileNotFoundException("Specified PCX image was not found", source.FullName);
        target ??= new FileInfo($"{Path.GetFileNameWithoutExtension(source.FullName)}.png");

        using var file = File.Open(source.FullName, FileMode.Open);
        using var reader = new BinaryReader(file);

        var pcx = reader.ReadPcxImage();
        pcx.AsBitmap().Save(target.FullName);
    }

    private static void BatchDecodeHandler(DirectoryInfo source, DirectoryInfo target)
    {
        if (!source.Exists) throw new DirectoryNotFoundException("Specified source directory was not found");
        if (!target.Exists) throw new DirectoryNotFoundException("Specified target directory was not found");

        var files = source.EnumerateFiles("*.pcx");
        foreach (var file in files)
        {
            var fileName = Path.GetFileNameWithoutExtension(file.FullName);
            var outputPath = $"{Path.Combine(target.FullName, fileName)}{".png"}";

            using var stream = file.OpenRead();
            using var reader = new BinaryReader(stream);

            var pcx = reader.ReadPcxImage();
            pcx.AsBitmap().Save(outputPath);
        }
    }

    private static void AnalyzeHandler(string source, bool verbose)
    {
        if (!Path.Exists(source)) throw new DirectoryNotFoundException("Specified source was not found");

        var texturesFiles = Directory.Exists(source) ? new DirectoryInfo(source).GetFiles() : [ new FileInfo(source) ];
        var textures = texturesFiles.Select(file =>
        {
            try
            {
                using var stream = file.OpenRead();
                using var reader = new BinaryReader(stream);
                var header = reader.ReadPcxImage().Header;

                return (name: Path.GetFileNameWithoutExtension(file.FullName), header, w: header.WindowMax.X + 1, h: header.WindowMax.Y + 1);
            }
            catch
            {
                return (name: Path.GetFileNameWithoutExtension(file.FullName), header: null, w: -1, h: -1);
            }
        }).ToList();

        if (verbose == false) Console.WriteLine("Name (hash) width x height @ pX x pY cX x cY");

        foreach (var (name, header, w, h) in textures)
        {
            // Probably not a PCX image 
            if (header == null) continue;

            if (verbose)
            {
                Console.WriteLine($"{name}:");
                Console.WriteLine($"  Hash: {StringExtensions.GV_StrCode_80016CCC(name)}");
                Console.WriteLine($"  Size: {w}x{h}");
                Console.WriteLine($"  VRAM Position: {header.Px}x{header.Py}");
                Console.WriteLine($"  CLUT Position: {header.Cx}x{header.Cy}");
                Console.WriteLine($"  Flags: {header.Flags}");
                Console.WriteLine($"  Color count: {header.NColors}");

                Console.WriteLine("  Palette:");
                for (int i = 0; i< header.NColors; i++)
                {
                    var color = header.Palette[i];
                    Console.Write($"    #{color.R:X2}{color.G:X2}{color.B:X2} ({color.R}, {color.G}, {color.B})");
                    
                    if (color.R == 0 && color.G == 0 && color.B == 0) Console.WriteLine(" | Transparent");
                    else Console.WriteLine();
                }
            }
            else
            {
                Console.WriteLine($"{name} ({StringExtensions.GV_StrCode_80016CCC(name)}): {w}x{h} @ {header.Px}x{header.Py} {header.Cx}x{header.Cy}");
            }
        }
    }
}
