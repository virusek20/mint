using System.CommandLine;
using System.Text.Json;

namespace MetalMintSolid.Stg;

public static class StgCommand
{
    public static Command AddStgCommand(this Command command)
    {
        var stgCommand = new Command("stg", "Manipulate PCX stage files\n" +
            "Making entirely new STG archives is NOT supported, use patching to modify existing entries");

        var stageExtractCommand = new Command("extract", "Extract stage files (textures, models, game data)");
        var stageExtractSourceArgument = new Argument<FileInfo>("source", "Source stage");
        var stageExtractTargetArgument = new Argument<DirectoryInfo?>("target", "Output directory");
        stageExtractCommand.AddArgument(stageExtractSourceArgument);
        stageExtractCommand.AddArgument(stageExtractTargetArgument);
        stageExtractCommand.SetHandler(ExtractHandler, stageExtractSourceArgument, stageExtractTargetArgument);

        var stagePatchCommand = new Command("patch", "Replaces a file within a stage (regular OR cached)");
        var stagePatchDarArgument = new Argument<FileInfo>("patch", "Replacement file");
        var stagePatchStageArgument = new Argument<FileInfo>("stage", "Original stage file");
        var stagePatchIndexArgument = new Argument<int>("index", "Replaced archive index");
        var stagePatchOutputArgument = new Argument<FileInfo?>("output", "Patched stage file");
        stagePatchCommand.AddArgument(stagePatchDarArgument);
        stagePatchCommand.AddArgument(stagePatchStageArgument);
        stagePatchCommand.AddArgument(stagePatchIndexArgument);
        stagePatchCommand.AddArgument(stagePatchOutputArgument);
        stagePatchCommand.SetHandler(PatchHandler, stagePatchDarArgument, stagePatchStageArgument, stagePatchIndexArgument, stagePatchOutputArgument);

        var stageListCommand = new Command("list", "List entries in a stage");
        var stageListStageArgument = new Argument<FileInfo>("stage", "Stage file");
        stageListCommand.AddArgument(stageListStageArgument);
        stageListCommand.SetHandler(ListHandler, stageListStageArgument);

        var stagePackCommand = new Command("pack", "Pack stage file");
        var stagePackTargetArgument = new Argument<DirectoryInfo>("source", "Source directory");
        var stagePackSourceArgument = new Argument<FileInfo?>("target", "Output stage file");
        stagePackCommand.AddArgument(stagePackTargetArgument);
        stagePackCommand.AddArgument(stagePackSourceArgument);
        stagePackCommand.SetHandler(PackHandler, stagePackTargetArgument, stagePackSourceArgument);

        stgCommand.Add(stageExtractCommand);
        stgCommand.Add(stagePatchCommand);
        stgCommand.Add(stageListCommand);
        stgCommand.Add(stagePackCommand);

        command.Add(stgCommand);
        return stgCommand;
    }

    /// <summary>
    /// Computes the stage header size (in sectors) for a config list whose Size
    /// fields have already been finalized (cached -> cumulative start offset,
    /// 0xFF -> cumulative end, regular -> byte length). Cached members are NOT
    /// counted individually; the whole cached run is accounted for by the 0xFF
    /// end marker. +1 is the header sector itself.
    /// </summary>
    private static short ComputeHeaderSizeSectors(IEnumerable<StgConfig> configs)
    {
        return (short)(configs.Where(c => c.Mode != 'c' || c.Extension == 0xFF).Sum(c => c.SizeSectors) + 1);
    }

    /// <summary>
    /// Writes the stage body (header sector already reserved) at the standard
    /// 2048 origin: regular members are sector-padded, cached members are written
    /// back-to-back, and the 0xFF marker pads the end of the cached run.
    /// </summary>
    private static void WriteStageBody(BinaryWriter writer, IReadOnlyList<StgConfig> configs, IReadOnlyList<byte[]> data)
    {
        writer.BaseStream.Position = 2048;
        for (int i = 0; i < configs.Count; i++)
        {
            var config = configs[i];
            if (config.Extension == 0xFF)
            {
                var pad = 2048 - writer.BaseStream.Position % 2048;
                if (pad == 2048) pad = 0;
                writer.BaseStream.Position += pad;
                continue;
            }

            writer.Write(data[i]);

            if (config.Mode != 'c')
            {
                var pad = 2048 - writer.BaseStream.Position % 2048;
                if (pad == 2048) pad = 0;
                writer.BaseStream.Position += pad;
            }
        }

        // Make sure trailing zeroes get written
        writer.BaseStream.SetLength(writer.BaseStream.Position);
    }

    private static void PackHandler(DirectoryInfo source, FileInfo? target)
    {
        if (!source.Exists) throw new FileNotFoundException("Specified source directory does not exist", source.FullName);
        target ??= new FileInfo($"{Path.GetFileNameWithoutExtension(source.FullName)}.stg");

        var rebuildJsonPath = Path.Combine(source.FullName, "rebuild.json");
        if (!File.Exists(rebuildJsonPath)) throw new FileNotFoundException("Could not find rebuild data (rebuild.json)", rebuildJsonPath); ;
        var rebuildJson = File.ReadAllText(rebuildJsonPath);
        var rebuildInfo = JsonSerializer.Deserialize(rebuildJson, MintJsonSerializerContext.Default.StgRebuildInfo) ?? throw new InvalidDataException("Failed to deserialize rebuild info");

        using var file = target.Create();
        using var writer = new BinaryWriter(file);

        // Load member payloads in order (the 0xFF marker carries no data).
        var data = rebuildInfo.Configs
            .Select(c => c.Extension == 0xFF ? Array.Empty<byte>() : File.ReadAllBytes(Path.Combine(source.FullName, c.Filename)))
            .ToList();

        // Finalize config sizes from the payloads.
        var packedSize = 0;
        var configs = rebuildInfo.Configs.Select((config, i) =>
        {
            int fileSize;
            var entrySize = data[i].Length;
            if (config.Mode == 'c')
            {
                fileSize = packedSize;
                packedSize += entrySize;
            }
            else fileSize = entrySize;

            if (config.Extension == 0xFF) fileSize = packedSize;

            return new StgConfig
            {
                Extension = config.Extension,
                Hash = config.Hash,
                Mode = config.Mode,
                Size = fileSize
            };
        }).ToList();

        var computed = ComputeHeaderSizeSectors(configs);
        // Preserve an over-declared original header (e.g. the "order" stage) so we
        // never silently drop a load-time reservation; never shrink below content.
        var headerSize = rebuildInfo.OriginalSize is int orig && orig > computed ? (short)orig : computed;

        writer.Write(new StgHeader
        {
            Field0 = rebuildInfo.Field0,
            Field1 = rebuildInfo.Field1,
            Size = headerSize
        });

        foreach (var config in configs) writer.Write(config);

        WriteStageBody(writer, configs, data);
    }

    private static void PatchHandler(FileInfo patch, FileInfo stage, int index, FileInfo? output)
    {
        if (!patch.Exists) throw new FileNotFoundException("Specified replacement file not found", patch.FullName);
        if (!stage.Exists) throw new FileNotFoundException("Specified original stage not found", stage.FullName);
        output ??= new FileInfo($"{Path.GetFileNameWithoutExtension(stage.FullName)}.stg");

        var stageBytes = File.ReadAllBytes(stage.FullName);
        using var reader = new BinaryReader(new MemoryStream(stageBytes));
        var header = reader.ReadStgHeader();
        var configs = reader.ReadStgConfigList();

        if (index < 0 || index >= configs.Count) throw new ArgumentOutOfRangeException(nameof(index), $"Index is outside of the valid range of this stage file (0..{configs.Count - 1})");
        if (configs[index].Extension == 0xFF) throw new NotSupportedException("Index refers to the cached-section end marker, which holds no data");

        // Extract every member's raw bytes. Cached members are addressed by a
        // cumulative start offset, so a member's real length is next.Size - this.Size;
        // regular members store their own byte length directly.
        var data = new byte[configs.Count][];
        reader.BaseStream.Position = 2048;
        for (int i = 0; i < configs.Count; i++)
        {
            var c = configs[i];
            if (c.Extension == 0xFF)
            {
                var skip = 2048 - reader.BaseStream.Position % 2048;
                if (skip == 2048) skip = 0;
                reader.BaseStream.Position += skip;
                data[i] = Array.Empty<byte>();
            }
            else if (c.Mode == 'c')
            {
                var len = configs[i + 1].Size - c.Size;
                data[i] = reader.ReadBytes(len);
            }
            else
            {
                data[i] = reader.ReadBytes(c.Size);
                var skip = 2048 - reader.BaseStream.Position % 2048;
                if (skip == 2048) skip = 0;
                reader.BaseStream.Position += skip;
            }
        }

        // Swap in the replacement payload.
        data[index] = File.ReadAllBytes(patch.FullName);

        // Re-finalize sizes: cached -> cumulative start offset (shifting every
        // cached member after the edit), 0xFF -> new cumulative end, regular ->
        // byte length. This is the bookkeeping the old code couldn't do, which is
        // why cached ('c') entries were previously refused.
        var packed = 0;
        for (int i = 0; i < configs.Count; i++)
        {
            var c = configs[i];
            if (c.Mode == 'c') { c.Size = packed; packed += data[i].Length; }
            else if (c.Extension == 0xFF) c.Size = packed;
            else c.Size = data[i].Length;
        }

        var computed = ComputeHeaderSizeSectors(configs);
        // header.Size was read from the original stage; keep an over-declaration
        // (reservation) if it still exceeds the recomputed content size.
        header.Size = header.Size > computed ? header.Size : computed;

        using var writer = new BinaryWriter(output.Create());
        writer.Write(header);
        foreach (var config in configs) writer.Write(config);

        WriteStageBody(writer, configs, data);
    }

    private static void ExtractHandler(FileInfo source, DirectoryInfo? target)
    {
        if (!source.Exists) throw new FileNotFoundException("Specified stage was not found", source.FullName);
        target ??= new DirectoryInfo($"{Path.GetFileNameWithoutExtension(source.FullName)}");
        target.Create();

        using var stageFile = source.OpenRead();
        using var reader = new BinaryReader(stageFile);

        var header = reader.ReadStgHeader();
        var configs = reader.ReadStgConfigList();

        reader.BaseStream.Position = 2048;
        for (int i = 0; i < configs.Count; i++)
        {
            StgConfig? config = configs[i];

            if (config.Extension == 0xFF) // End of cached section
            {
                var pad = 2048 - reader.BaseStream.Position % 2048;
                if (pad == 2048) pad = 0;
                reader.BaseStream.Position += pad;
            }
            else if (config.Mode == 'c') // Cached entry
            {
                var nextConfig = configs[i + 1];
                var cachedSize = nextConfig.Size - config.Size; // These are technically offsets, not sizes

                var filePath = Path.Combine(target.FullName, $"{i}_{config.Hash}.{config.ExtensionName}");
                File.WriteAllBytes(filePath, reader.ReadBytes(cachedSize));
            }
            else // Regular file
            {
                var filePath = Path.Combine(target.FullName, $"{i}_{config.Hash}.{config.ExtensionName}");
                File.WriteAllBytes(filePath, reader.ReadBytes(config.Size));
                var pad = 2048 - reader.BaseStream.Position % 2048;
                if (pad == 2048) pad = 0;
                reader.BaseStream.Position += pad;
            }
        }

        var rebuildInfo = new StgRebuildInfo
        {
            Field0 = header.Field0,
            Field1 = header.Field1,
            OriginalSize = header.Size, // capture declared size so pack can preserve reservations
            Configs = configs.Select((c, i) => new StgConfigRebuildInfo
            {
                Extension = c.Extension,
                Mode = c.Mode,
                Hash = c.Hash,
                Filename = c.Extension == 0xFF ? "" : $"{i}_{c.Hash}.{c.ExtensionName}"
            }).ToList()
        };

        var rebuildJson = JsonSerializer.Serialize(rebuildInfo, MintJsonSerializerContext.Default.StgRebuildInfo);
        var rebuildJsonPath = Path.Combine(target.FullName, "rebuild.json");
        File.WriteAllText(rebuildJsonPath, rebuildJson);
    }

    private static void ListHandler(FileInfo source)
    {
        if (!source.Exists) throw new FileNotFoundException("Specified stage was not found", source.FullName);

        using var stageFile = source.OpenRead();
        using var reader = new BinaryReader(stageFile);

        var header = reader.ReadStgHeader();
        var configs = reader.ReadStgConfigList();

        Console.WriteLine($"Field 0, 1 (unknown): {header.Field0}, {header.Field1}");
        Console.WriteLine($"Total size: {header.Size * 2048} bytes ({header.Size} sectors)");

        for (int i = 0; i < configs.Count; i++)
        {
            StgConfig? entry = configs[i];

            Console.WriteLine($"{i}: {entry.Hash}.{entry.ExtensionName}");
            Console.WriteLine($"    Extension: {entry.Extension} '{(char)entry.Extension}'");
            Console.WriteLine($"    Mode: {entry.Mode} '{(char)entry.Mode}'");
            Console.WriteLine($"    Size: {entry.Size} bytes ({entry.SizeSectors} sectors)");
        }
    }
}
