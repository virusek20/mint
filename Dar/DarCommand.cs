using MetalMintSolid.Extensions;
using System.CommandLine;

namespace MetalMintSolid.Dar;

public static class DarCommand
{
    public static Command AddDarCommand(this Command command)
    {
        var darCommand = new Command("dar", "Manipulate DAR archives");

        var extractCommand = new Command("extract", "Extracts a .dar archive");
        var extractFileArgument = new Argument<FileInfo>("file", "The .dar archive to be extracted");
        var extractTargetArgument = new Argument<DirectoryInfo?>("output", "Output directory")
        {
            Arity = ArgumentArity.ZeroOrOne
        };
        extractCommand.AddArgument(extractFileArgument);
        extractCommand.AddArgument(extractTargetArgument);
        extractCommand.SetHandler(ExtractHandler, extractFileArgument, extractTargetArgument);

        var packCommand = new Command("pack", "Creates a new .dar archive");
        var packFileArgument = new Argument<FileInfo?>("file", "Output filename");
        var packTargetArgument = new Argument<DirectoryInfo>("input", "Archive source directory");
        var packOrderOption = new Option<FileInfo?>("--order", () => new FileInfo("order.txt"), "File order file, orders the archive in a specific way (useful for modding)");
        packCommand.AddArgument(packTargetArgument);
        packCommand.AddArgument(packFileArgument);
        packCommand.AddOption(packOrderOption);
        packCommand.SetHandler(PackHandler, packFileArgument, packTargetArgument, packOrderOption);

        var listCommand = new Command("list", "List contents of a .dar archive");
        var listFileArgument = new Argument<FileInfo>("file", "The .dar archive to list contents of");
        listCommand.AddArgument(listFileArgument);
        listCommand.SetHandler(ListHandler, listFileArgument);

        darCommand.AddCommand(extractCommand);
        darCommand.AddCommand(packCommand);
        darCommand.AddCommand(listCommand);

        command.AddCommand(darCommand);
        return darCommand;
    }

    private static void ExtractHandler(FileInfo file, DirectoryInfo? target)
    {
        if (!file.Exists) throw new FileNotFoundException("Specified archive cannot be found", file.FullName);

        if (target == null) target = Directory.CreateDirectory(Path.GetFileNameWithoutExtension(file.Name));
        else if (!target.Exists) target = Directory.CreateDirectory(Path.GetFileNameWithoutExtension(target.FullName));

        using var fileStream = File.Open(file.FullName, FileMode.Open);
        using var reader = new BinaryReader(fileStream);

        var archive = reader.ReadDarArchive();

        foreach (var entry in archive.Files)
        {
            var entryPath = Path.Combine(target.FullName, entry.Name);
            File.WriteAllBytes(entryPath, entry.Data);
        }

        var orderPath = Path.Combine(target.FullName, "order.txt");
        File.WriteAllLines(orderPath, archive.Files.Select(f => f.Name));
    }

    private static void PackHandler(FileInfo? file, DirectoryInfo input, FileInfo? order) 
    {
        if (!input.Exists) throw new FileNotFoundException("Archive source does not exist", input.FullName);
        file ??= new FileInfo($"{Path.GetFileNameWithoutExtension(input.FullName)}.dar");

        order ??= new FileInfo(Path.Combine(input.FullName, "order.txt"));
        if (!order.Exists) throw new FileNotFoundException("Failed to find order.txt required for keeping references intact", order.FullName);
        var fileOrder = File.ReadAllLines(order.FullName);

        var archive = new DarArchive
        {
            Files = fileOrder.Select(file => new DarFile
            {
                Name = file,
                Data = File.ReadAllBytes(Path.Combine(input.FullName, file))
            }).ToList()
        };

        using var archiveFile = File.Open(file.FullName, FileMode.Create);
        using var writer = new BinaryWriter(archiveFile);
        writer.Write(archive);
    }

    private static void ListHandler(FileInfo file)
    {
        if (!file.Exists) throw new FileNotFoundException("Specified archive cannot be found", file.FullName);

        using var fileStream = File.Open(file.FullName, FileMode.Open);
        using var reader = new BinaryReader(fileStream);

        var archive = reader.ReadDarArchive();
        foreach (var entry in archive.Files)
        {
            Console.WriteLine($"{entry.Name} ({entry.Data.Length} bytes) | Hash: {StringExtensions.GV_StrCode_80016CCC(entry.Name[0..^4])}");
        }
    }
}
