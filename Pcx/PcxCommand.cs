using MetalMintSolid.Extensions;
using SkiaSharp;
using System.CommandLine;
using System.Text.Json;

namespace MetalMintSolid.Pcx;

public static class PcxCommand
{
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

        PaletteData? colors = null;
        if (palette != null)
        {
            if (!palette.Exists) throw new FileNotFoundException("Specified palette file was not found", palette.FullName);
            using var paletteFile = palette.OpenRead();

            colors = JsonSerializer.Deserialize(paletteFile, MintJsonSerializerContext.Default.PaletteData) ?? throw new NotSupportedException("Failed to deserialize palette contents");
            if (colors.Palette.Count > 16) throw new NotSupportedException($"Palette contains {colors.Palette.Count} colors, only up to 16 are supported");
        }

        var bitmap = SKBitmap.Decode(source.FullName);
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

            if (pcx.Header.WindowMax != metaPcx.Header.WindowMax) Console.WriteLine($"Texture sizes differ ('{pcx.Header.WindowMax}' vs '{metaPcx.Header.WindowMax}'), this will most likely not work!");
        }
        else
        {
            Console.WriteLine("Creating basic PCX file with no MGS specific metadata");
            Console.WriteLine("Specify a --metadata original pcx file for texture swapping");
        }

        if (colors != null)
        {
            pcx.Header.Cx = colors.Cx;
            pcx.Header.Cy = colors.Cy;
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
        var bitmap = pcx.AsBitmap();
        using var targetFile = target.Create();
        bitmap.Encode(targetFile, SKEncodedImageFormat.Png, 100);
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
            var bitmap = pcx.AsBitmap();
            using var targetFile = File.Open(outputPath, FileMode.Create);
            bitmap.Encode(targetFile, SKEncodedImageFormat.Png, 100);
        }
    }

    private static void AnalyzeHandler(string source, bool verbose)
    {
        if (!Path.Exists(source)) throw new DirectoryNotFoundException("Specified source was not found");

        var texturesFiles = Directory.Exists(source) ? new DirectoryInfo(source).GetFiles() : [ new FileInfo(source) ];
        IEnumerable<(string, PcxHeader?)> textures = texturesFiles.Select(file =>
        {
            var name = Path.GetFileNameWithoutExtension(file.FullName);
            PcxHeader? header = null;
            try
            {
                using var stream = file.OpenRead();
                using var reader = new BinaryReader(stream);
                header = reader.ReadPcxImage().Header;
            }
            catch { }

            return (name, header);
        });
        if (verbose == false) Console.WriteLine("Name (hash) width x height @ pX x pY cX x cY (n colors)");

        var names = new string[] {
            "sna_arm1",
            "sna_arm2",
            "sna_boot",
            "sna_chest1",
            "sna_chest2",
            "sna_chest3",
            "sna_chest4",
            "sna_collar1",
            "sna_collar2",
            "sna_ear1",
            "sna_ear2",
            "sna_face",
            "sna_face2",
            "sna_face3",
            "sna_fin",
            "sna_fin2",
            "sna_fin3",
            "sna_hand",
            "sna_hand2",
            "sna_hed",
            "sna_hip1",
            "sna_hip2",
            "sna_leg1",
            "sna_leg2",
            "sna_leg3",
            "sna_leg4",
            "sna_neck",
            "sna_neck2"
        };

        foreach (var (name, header) in textures)
        {
            // Probably not a PCX image 
            if (header == null) continue;

            if (verbose)
            {
                Console.WriteLine($"{name}:");
                Console.WriteLine($"  Hash: {StringExtensions.GV_StrCode_80016CCC(name)}");
                Console.WriteLine($"  Size: {header.WindowMax.X + 1}x{header.WindowMax.Y + 1}");
                Console.WriteLine($"  VRAM Position: {header.Px}x{header.Py}");
                Console.WriteLine($"  CLUT Position: {header.Cx}x{header.Cy}");
                Console.WriteLine($"  Flags: {header.Flags}");
                Console.WriteLine($"  Color count: {header.NColors}");

                Console.WriteLine("  Palette:");
                for (int i = 0; i< header.NColors; i++)
                {
                    var color = header.Palette[i];
                    Console.Write($"    #{color.Red:X2}{color.Green:X2}{color.Blue:X2} ({color.Red}, {color.Green}, {color.Blue})");
                    
                    if (color.Red == 0 && color.Green == 0 && color.Blue == 0) Console.WriteLine(" | Transparent");
                    else Console.WriteLine();
                }
            }
            else
            {
                if (ushort.TryParse(name, out var hash))
                {
                    var readableName = names.FirstOrDefault(n => StringExtensions.GV_StrCode_80016CCC(n) == hash);
                    Console.WriteLine($"{readableName} ({name}): {header.WindowMax.X + 1}x{header.WindowMax.Y + 1} @ {header.Px}x{header.Py} {header.Cx}x{header.Cy} ({header.NColors} colors)");
                }
                else Console.WriteLine($"{name} ({StringExtensions.GV_StrCode_80016CCC(name)}): {header.WindowMax.X + 1}x{header.WindowMax.Y + 1} @ {header.Px}x{header.Py} {header.Cx}x{header.Cy} ({header.NColors} colors)");
            }
        }
    }
}
