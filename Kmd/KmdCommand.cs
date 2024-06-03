using SharpGLTF.Schema2;
using System.CommandLine;
using System.Globalization;

namespace MetalMintSolid.Kmd;

public static class KmdCommand
{
    public static Command AddKmdCommand(this Command command)
    {
        var kmdCommand = new Command("kmd", "Manipulate KMD models");

        var kmdEncodeCommand = new Command("encode", "Encodes a KMD model from a GLTF file");
        var kmdEncodeSourceArgument = new Argument<FileInfo>("source", "Source GLTF model");
        var kmdEncodeTargetArgument = new Argument<FileInfo?>("target", "Output filename")
        {
            Arity = ArgumentArity.ZeroOrOne
        };
        var kmdEncodeMetadataSourceOption = new Option<FileInfo?>("--metadata", "Copy metadata from supplied file, properly sets MGS specific data")
        {
            Arity = ArgumentArity.ExactlyOne
        };
        kmdEncodeCommand.AddArgument(kmdEncodeSourceArgument);
        kmdEncodeCommand.AddArgument(kmdEncodeTargetArgument);
        kmdEncodeCommand.AddOption(kmdEncodeMetadataSourceOption);
        kmdEncodeCommand.SetHandler(EncodeHandler, kmdEncodeSourceArgument, kmdEncodeTargetArgument, kmdEncodeMetadataSourceOption);

        var kmdDecodeCommand = new Command("decode", "Decodes a KMD model to a GLTF file");
        var kmdDecodeCommandSourceArgument = new Argument<FileInfo>("source", "Source KMD model");
        var kmdDecodeCommandTargetArgument = new Argument<FileInfo?>("target", "Output filename")
        {
            Arity = ArgumentArity.ZeroOrOne
        };
        var kmdDecodeCommandTexturePathOption = new Option<DirectoryInfo?>("--textures", "Path to textures")
        {
            Arity = ArgumentArity.ZeroOrOne
        };
        kmdDecodeCommand.AddArgument(kmdDecodeCommandSourceArgument);
        kmdDecodeCommand.AddArgument(kmdDecodeCommandTargetArgument);
        kmdDecodeCommand.AddOption(kmdDecodeCommandTexturePathOption);
        kmdDecodeCommand.SetHandler(DecodeHandler, kmdDecodeCommandSourceArgument, kmdDecodeCommandTargetArgument, kmdDecodeCommandTexturePathOption);

        kmdCommand.AddCommand(kmdDecodeCommand);
        kmdCommand.AddCommand(kmdEncodeCommand);

        command.AddCommand(kmdCommand);
        return kmdCommand;
    }

    private static void EncodeHandler(FileInfo source, FileInfo? target, FileInfo? metadata)
    {
        if (!source.Exists) throw new FileNotFoundException("Specified model file does not exist", source.FullName);
        if (metadata == null || !metadata.Exists) throw new FileNotFoundException("Specified original model file does not exist", source.FullName);
        target ??= new FileInfo($"{Path.GetFileNameWithoutExtension(source.FullName)}.kmd");

        using var reader = source.OpenRead();
        var gltf = ModelRoot.ReadGLB(reader);

        using var kmdSourceFile = metadata.OpenRead();
        using var reader2 = new BinaryReader(kmdSourceFile);
        var kmdSource = reader2.ReadKmdModel();

        var kmd = KmdImporter.FromGltf(gltf, kmdSource);
        using var kmdFile = target.OpenWrite();
        using var writer = new BinaryWriter(kmdFile);
        writer.Write(kmd);
    }

    private static void DecodeHandler(FileInfo source, FileInfo? target, DirectoryInfo? texturePath)
    {
        if (!source.Exists) throw new FileNotFoundException("Specified model file does not exist", source.FullName);
        target ??= new FileInfo($"{Path.GetFileNameWithoutExtension(source.FullName)}.glb");

        using var file = File.Open(source.FullName, FileMode.Open);
        using var reader = new BinaryReader(file);

        var kmd = reader.ReadKmdModel();
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;

        var textures = texturePath != null && texturePath.Exists ? texturePath.FullName : "";
        var thing = kmd.ToGltf(textures);
        thing.SaveGLB(target.FullName);
    }
}
