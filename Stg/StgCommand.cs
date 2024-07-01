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
        var stagePatchDarArgument = new Argument<FileInfo>("dar", "Replacement DAR archive");
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

    private static void PatchHandler(FileInfo dar, FileInfo stage, int index, FileInfo? output)
    {
        throw new NotImplementedException();
    }

    private static void ExtractHandler(FileInfo source, DirectoryInfo? target)
    {
        if (!source.Exists) throw new FileNotFoundException("Specified stage was not found", source.FullName);
        target ??= new DirectoryInfo($"{Path.GetFileNameWithoutExtension(source.FullName)}");
        target.Create();

        using var stageFile = source.OpenRead();
        using var reader = new BinaryReader(stageFile);

        var header = reader.ReadStgHeader();
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
