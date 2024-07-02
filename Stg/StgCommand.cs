using System.CommandLine;

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

        stgCommand.Add(stageExtractCommand);
        stgCommand.Add(stagePatchCommand);
        stgCommand.Add(stageListCommand);

        command.Add(stgCommand);
        return stgCommand;
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
            if (config.Mode == 'c') continue; // TODO: Packed cached files

            if (config.Mode == 0xFF)  // TODO: End of packed files
            {
                reader.BaseStream.Position += config.SizeSectors * 2048;
            }
            else
            {
                var filePath = Path.Combine(target.FullName, $"{i}_{config.Hash}.{config.ExtensionName}");
                File.WriteAllBytes(filePath, reader.ReadBytes(config.Size));
                var pad = 2048 - reader.BaseStream.Position % 2048;
                if (pad == 2048) pad = 0;
                reader.BaseStream.Position += pad;
            }
        }
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

        Console.WriteLine(configs.Sum(c => c.SizeSectors));
    }
}
