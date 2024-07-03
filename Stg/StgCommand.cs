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

        var stagePatchCommand = new Command("patch", "Replaces a DAR file within a stage");
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

        var packedSize = 0;
        var configs = rebuildInfo.Configs.Select(config =>
        {
            var fileSize = 0;
            if (config.Filename != "")
            {
                var fileInfo = new FileInfo(Path.Combine(source.FullName, config.Filename));
                if (fileInfo.Exists == false) throw new FileNotFoundException("Failed to find file referenced in rebuild info", fileInfo.FullName);

                var entrySize = (int)fileInfo.Length;
                if (config.Mode == 'c')
                {
                    fileSize = packedSize;
                    packedSize += entrySize;
                }
                else fileSize = entrySize;
            }

            if (config.Extension == 0xFF) fileSize = packedSize;

            return new StgConfig
            {
                Extension = config.Extension,
                Hash = config.Hash,
                Mode = config.Mode,
                Size = fileSize
            };
        }).ToList();

        writer.Write(new StgHeader
        {
            Field0 = rebuildInfo.Field0,
            Field1 = rebuildInfo.Field1,
            Size = (short)(configs.Where(c => c.Mode != 'c' || c.Extension == 0xFF).Sum(c => c.SizeSectors) + 1) // Extra one for header
        });

        foreach (var config in configs) writer.Write(config);

        writer.BaseStream.Position = 2048;
        foreach (var config in rebuildInfo.Configs)
        {
            if (config.Extension == 0xFF)
            {
                var pad = 2048 - writer.BaseStream.Position % 2048;
                if (pad == 2048) pad = 0;
                writer.BaseStream.Position += pad;
                continue;
            }

            var data = File.ReadAllBytes(Path.Combine(source.FullName, config.Filename));

            writer.Write(data);

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

    private static void PatchHandler(FileInfo patch, FileInfo stage, int index, FileInfo? output)
    {
        if (!patch.Exists) throw new FileNotFoundException("Specified replacement file not found", patch.FullName);
        if (!stage.Exists) throw new FileNotFoundException("Specified original stage not found", stage.FullName);
        output ??= new FileInfo($"{Path.GetFileNameWithoutExtension(stage.FullName)}.stg");

        using var reader = new BinaryReader(stage.OpenRead());
        var header = reader.ReadStgHeader();
        var configs = reader.ReadStgConfigList();

        if (index < 0 || index >= configs.Count) throw new ArgumentOutOfRangeException(nameof(index), $"Index is outside of the valid range of this stage file (0..{configs.Count - 1})");
        if (configs[index].Mode == 'c') throw new NotSupportedException("Cannot replace packed files yet"); // TODO: Add this

        var mod = File.ReadAllBytes(patch.FullName);
        using var writer = new BinaryWriter(output.Create());

        void Resize(StgConfig config, int newLength)
        {
            var oldSizeSectors = config.SizeSectors;
            var sizeSectors = (int)Math.Ceiling(newLength / 2048f);
            var sizeDiff = sizeSectors - oldSizeSectors;

            config.Size = newLength;
            header.Size += (short)sizeDiff;
        }

        var oldSizeSectors = configs[index].SizeSectors;
        Resize(configs[index], mod.Length);

        // Header
        writer.Write(header);
        foreach (var config in configs) writer.Write(config);

        // Start of file
        var skippedSectors = configs.Take(index)
            .Where(c => c.Mode != 'c')
            .Sum(c => c.SizeSectors);
        reader.BaseStream.Position = 2048;
        writer.BaseStream.Position = 2048;
        writer.Write(reader.ReadBytes(skippedSectors * 2048));

        // Modded data
        writer.Write(mod);
        var pad = 2048 - writer.BaseStream.Position % 2048;
        if (pad == 2048) pad = 0;
        writer.BaseStream.Position += pad;

        // Rest of file
        reader.BaseStream.Position = (1 + skippedSectors + oldSizeSectors) * 2048;
        writer.Write(reader.ReadBytes((int)reader.BaseStream.Length));
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
