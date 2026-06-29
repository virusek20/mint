using SharpGLTF.Schema2;
using System.CommandLine;
using System.CommandLine.Invocation;
using System.Globalization;

namespace MetalMintSolid.Kmd;

public static class KmdCommand
{
    public static Command AddKmdCommand(this Command command)
    {
        var kmdCommand = new Command("kmd", "Manipulate MGS KMD models (encode/decode to glTF, weld joint seams, repair legacy export defects)");

        // ---------------- encode ----------------
        var kmdEncodeCommand = new Command("encode", "Encode a KMD model from a glTF (.glb) file, copying MGS-specific metadata from an existing KMD");
        var kmdEncodeSourceArgument = new Argument<FileInfo>("source", "Source glTF (.glb) model to convert into a KMD");
        var kmdEncodeTargetArgument = new Argument<FileInfo?>("target", "Output KMD path (defaults to <source>.kmd next to the input)")
        {
            Arity = ArgumentArity.ZeroOrOne
        };
        var kmdEncodeMetadataSourceOption = new Option<FileInfo?>("--metadata", "Existing KMD whose MGS-specific metadata (bounding boxes, flags, bone layout) is copied onto the new model. Required.")
        {
            Arity = ArgumentArity.ExactlyOne
        };
        kmdEncodeCommand.AddArgument(kmdEncodeSourceArgument);
        kmdEncodeCommand.AddArgument(kmdEncodeTargetArgument);
        kmdEncodeCommand.AddOption(kmdEncodeMetadataSourceOption);
        kmdEncodeCommand.SetHandler(EncodeHandler, kmdEncodeSourceArgument, kmdEncodeTargetArgument, kmdEncodeMetadataSourceOption);

        // ---------------- decode ----------------
        var kmdDecodeCommand = new Command("decode", "Decode a KMD model to a glTF (.glb) file, optionally embedding textures");
        var kmdDecodeCommandSourceArgument = new Argument<FileInfo>("source", "Source KMD model to convert to glTF");
        var kmdDecodeCommandTargetArgument = new Argument<FileInfo?>("target", "Output glTF path (defaults to <source>.glb next to the input)")
        {
            Arity = ArgumentArity.ZeroOrOne
        };
        var kmdDecodeCommandTexturePathOption = new Option<DirectoryInfo?>("--textures", "Folder of decoded PNG textures to embed. Each face's texture is matched by name-hash, so run `pcx batchdecode` on the model's .pcc/.pcx files into this folder first.")
        {
            Arity = ArgumentArity.ZeroOrOne
        };
        var kmdDecodeCommandRawOption = new Option<bool>("--raw", "Keep original un-welded vertex positions (do NOT close paired joint seams). By default decode welds paired seam vertices so the glTF has no joint gaps.");
        kmdDecodeCommand.AddArgument(kmdDecodeCommandSourceArgument);
        kmdDecodeCommand.AddArgument(kmdDecodeCommandTargetArgument);
        kmdDecodeCommand.AddOption(kmdDecodeCommandTexturePathOption);
        kmdDecodeCommand.AddOption(kmdDecodeCommandRawOption);
        kmdDecodeCommand.SetHandler(DecodeHandler, kmdDecodeCommandSourceArgument, kmdDecodeCommandTargetArgument, kmdDecodeCommandTexturePathOption, kmdDecodeCommandRawOption);

        // ---------------- weld ----------------
        var kmdWeldCommand = new Command("weld", "Bake vertex pairing into a KMD, closing joint seam gaps (ankle/knee/hip) without altering anything else");
        var kmdWeldSourceArgument = new Argument<FileInfo>("source", "Source KMD model to weld");
        var kmdWeldTargetArgument = new Argument<FileInfo?>("target", "Output KMD path (defaults to <source>_welded.kmd next to the input)")
        {
            Arity = ArgumentArity.ZeroOrOne
        };
        kmdWeldCommand.AddArgument(kmdWeldSourceArgument);
        kmdWeldCommand.AddArgument(kmdWeldTargetArgument);
        kmdWeldCommand.SetHandler(WeldHandler, kmdWeldSourceArgument, kmdWeldTargetArgument);

        // ---------------- repair ----------------
        var kmdRepairCommand = new Command("repair",
            "Retrofit-fix a KMD made with the OLD MetalMintSolid. Closes 'missing mesh node' holes (e.g. the rear-pelvis/butt gap) by sealing open boundary loops, splits concave quads that the PSX renderer tears open, and re-welds joint seams. Use this on models that show holes in Noesis/in-game that you did not author.");
        var kmdRepairSourceArgument = new Argument<FileInfo>("source", "Source (broken) KMD model to repair");
        var kmdRepairTargetArgument = new Argument<FileInfo?>("target", "Output KMD path (defaults to <source>_fixed.kmd next to the input)")
        {
            Arity = ArgumentArity.ZeroOrOne
        };
        var kmdRepairToleranceOption = new Option<float>("--tolerance", () => 2.0f,
            "Distance (model units) within which two vertices count as the same point, used both to detect open holes and to re-pair joint seams. Raise if seams are authored further apart; lower to be more conservative.");
        var kmdRepairNoSealOption = new Option<bool>("--no-seal", "Do NOT seal open boundary-loop holes (skip the missing-mesh-node / butt-gap fix).");
        var kmdRepairNoSplitOption = new Option<bool>("--no-split", "Do NOT split concave quads (skip the concave-quad-tear fix).");
        var kmdRepairNoWeldOption = new Option<bool>("--no-weld", "Do NOT re-pair and bake joint seam vertices (skip the seam-weld fix).");
        var kmdRepairSealMaxVertsOption = new Option<int>("--seal-max-verts", () => 5,
            "Largest boundary loop (in vertices) the sealer will fill. Small loops are stray holes; large loops are usually intentional openings (neck, cuffs), so they are left alone. Increase only if a genuine hole is bigger than this.");
        var kmdRepairSealAnywhereOption = new Option<bool>("--seal-anywhere",
            "Seal small holes ANYWHERE on the model, not just the lower/central body. Advanced: this can fill legitimate openings, so review the printed list of sealed loops afterwards.");
        kmdRepairCommand.AddArgument(kmdRepairSourceArgument);
        kmdRepairCommand.AddArgument(kmdRepairTargetArgument);
        kmdRepairCommand.AddOption(kmdRepairToleranceOption);
        kmdRepairCommand.AddOption(kmdRepairNoSealOption);
        kmdRepairCommand.AddOption(kmdRepairNoSplitOption);
        kmdRepairCommand.AddOption(kmdRepairNoWeldOption);
        kmdRepairCommand.AddOption(kmdRepairSealMaxVertsOption);
        kmdRepairCommand.AddOption(kmdRepairSealAnywhereOption);
        // 8 bound values exceeds the typed SetHandler overloads, so read them off the parse result.
        kmdRepairCommand.SetHandler((InvocationContext ctx) =>
        {
            var pr = ctx.ParseResult;
            RepairHandler(
                pr.GetValueForArgument(kmdRepairSourceArgument),
                pr.GetValueForArgument(kmdRepairTargetArgument),
                pr.GetValueForOption(kmdRepairToleranceOption),
                pr.GetValueForOption(kmdRepairNoSealOption),
                pr.GetValueForOption(kmdRepairNoSplitOption),
                pr.GetValueForOption(kmdRepairNoWeldOption),
                pr.GetValueForOption(kmdRepairSealMaxVertsOption),
                pr.GetValueForOption(kmdRepairSealAnywhereOption));
        });

        kmdCommand.AddCommand(kmdDecodeCommand);
        kmdCommand.AddCommand(kmdEncodeCommand);
        kmdCommand.AddCommand(kmdWeldCommand);
        kmdCommand.AddCommand(kmdRepairCommand);

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
        using var kmdFile = target.Create();
        using var writer = new BinaryWriter(kmdFile);
        writer.Write(kmd);
    }

    private static void DecodeHandler(FileInfo source, FileInfo? target, DirectoryInfo? texturePath, bool raw)
    {
        if (!source.Exists) throw new FileNotFoundException("Specified model file does not exist", source.FullName);
        target ??= new FileInfo($"{Path.GetFileNameWithoutExtension(source.FullName)}.glb");

        using var file = File.Open(source.FullName, FileMode.Open);
        using var reader = new BinaryReader(file);

        var kmd = reader.ReadKmdModel();
        if (!raw) kmd.BakeVertexPairs();
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;

        var textures = texturePath != null && texturePath.Exists ? texturePath.FullName : "";
        var modelRoot = kmd.ToGltf(textures);
        modelRoot.SaveGLB(target.FullName);
    }

    private static void WeldHandler(FileInfo source, FileInfo? target)
    {
        if (!source.Exists) throw new FileNotFoundException("Specified model file does not exist", source.FullName);
        target ??= new FileInfo($"{Path.GetFileNameWithoutExtension(source.FullName)}_welded.kmd");

        KmdModel kmd;
        using (var file = File.Open(source.FullName, FileMode.Open))
        using (var reader = new BinaryReader(file))
        {
            kmd = reader.ReadKmdModel();
        }

        var moved = kmd.BakeVertexPairs();
        Console.WriteLine($"Welded {moved} paired seam vertices onto their parent vertices.");

        using var outFile = target.Create();
        using var writer = new BinaryWriter(outFile);
        writer.Write(kmd);
    }

    private static void RepairHandler(FileInfo source, FileInfo? target, float tolerance,
        bool noSeal, bool noSplit, bool noWeld, int sealMaxVerts, bool sealAnywhere)
    {
        if (!source.Exists) throw new FileNotFoundException("Specified model file does not exist", source.FullName);
        target ??= new FileInfo($"{Path.GetFileNameWithoutExtension(source.FullName)}_fixed.kmd");

        KmdModel kmd;
        using (var file = File.Open(source.FullName, FileMode.Open))
        using (var reader = new BinaryReader(file))
        {
            kmd = reader.ReadKmdModel();
        }

        int facesBefore = kmd.Objects.Sum(o => (int)o.FaceCount);
        Console.WriteLine($"Loaded {source.Name}: {kmd.Objects.Count} objects, {facesBefore} faces.");

        var (sealedCount, split, paired, baked) = kmd.Repair(
            pairingTolerance: tolerance,
            sealCracks: !noSeal,
            splitConcave: !noSplit,
            weldSeams: !noWeld,
            sealMaxLoopVerts: sealMaxVerts,
            sealAnywhere: sealAnywhere,
            log: Console.WriteLine);

        Console.WriteLine($"Sealed {sealedCount} hole triangle(s); split {split} concave quad(s); " +
                          $"re-paired {paired} seam vertex/vertices; baked {baked}.");

        using var outFile = target.Create();
        using var writer = new BinaryWriter(outFile);
        writer.Write(kmd);

        int facesAfter = kmd.Objects.Sum(o => (int)o.FaceCount);
        Console.WriteLine($"Wrote {target.Name}: {facesAfter} faces.");
    }
}
