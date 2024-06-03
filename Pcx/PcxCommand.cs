using System.CommandLine;
using System.Drawing;

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
        pcxEncodeCommand.AddArgument(pcxEncodeSourceArgument);
        pcxEncodeCommand.AddArgument(pcxEncodeTargetArgument);
        pcxEncodeCommand.AddOption(pcxEncodeMetadataSourceOption);
        pcxEncodeCommand.SetHandler(EncodeHandler, pcxEncodeSourceArgument, pcxEncodeTargetArgument, pcxEncodeMetadataSourceOption);

        var pcxDecodeCommand = new Command("decode", "Decodes a PCX image to a btimap");
        var pcxDecodeCommandSourceArgument = new Argument<FileInfo>("source", "Source PCX image");
        var pcxDecodeCommandTargetArgument = new Argument<FileInfo?>("target", "Output filename");
        pcxDecodeCommand.AddArgument(pcxDecodeCommandSourceArgument);
        pcxDecodeCommand.AddArgument(pcxDecodeCommandTargetArgument);
        pcxDecodeCommand.SetHandler(DecodeHandler, pcxDecodeCommandSourceArgument, pcxDecodeCommandTargetArgument);

        var pcxBatchDecodeCommand = new Command("batchdecode", "Decodes a PCX image to a btimap");
        var pcxBatchDecodeCommandSourceArgument = new Argument<DirectoryInfo>("source", "Source directory");
        var pcxBatchDecodeCommandTargetArgument = new Argument<DirectoryInfo>("target", "Output directoy");
        pcxBatchDecodeCommand.AddArgument(pcxBatchDecodeCommandSourceArgument);
        pcxBatchDecodeCommand.AddArgument(pcxBatchDecodeCommandTargetArgument);
        pcxBatchDecodeCommand.SetHandler(BatchDecodeHandler, pcxBatchDecodeCommandSourceArgument, pcxBatchDecodeCommandTargetArgument);

        pcxCommand.AddCommand(pcxDecodeCommand);
        pcxCommand.AddCommand(pcxEncodeCommand);
        pcxCommand.AddCommand(pcxBatchDecodeCommand);

        command.Add(pcxCommand);
        return pcxCommand;
    }

    private static void EncodeHandler(FileInfo source, FileInfo? target, FileInfo? metadata)
    {
        if (!source.Exists) throw new FileNotFoundException("Specified bitmap was not found", source.FullName);
        target ??= new FileInfo($"{Path.GetFileNameWithoutExtension(source.FullName)}.pcx");

        var bitmap = new Bitmap(source.FullName);
        var pcx = PcxImage.FromBitmap(bitmap);

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
}
