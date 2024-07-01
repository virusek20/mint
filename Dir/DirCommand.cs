using System.CommandLine;

namespace MetalMintSolid.Dir;

public static class DirCommand
{
    public static Command AddDirCommand(this Command command)
    {
        var darCommand = new Command("dir", "Manipulate DIR archives");

        var extractCommand = new Command("extract", "Extracts a .dir archive");
        var extractFileArgument = new Argument<FileInfo>("file", "The .dir archive to be extracted");
        var extractTargetArgument = new Argument<DirectoryInfo?>("output", "Output directory")
        {
            Arity = ArgumentArity.ZeroOrOne
        };
        extractCommand.AddArgument(extractFileArgument);
        extractCommand.AddArgument(extractTargetArgument);
        extractCommand.SetHandler(ExtractHandler, extractFileArgument, extractTargetArgument);

        var packCommand = new Command("pack", "Creates a new .dir archive");
        var packTargetArgument = new Argument<DirectoryInfo>("input", "Archive source directory");
        var packFileArgument = new Argument<FileInfo?>("file", "Output filename")
        {
            Arity = ArgumentArity.ZeroOrOne
        };
        packCommand.AddArgument(packTargetArgument);
        packCommand.AddArgument(packFileArgument);
        packCommand.SetHandler(PackHandler, packTargetArgument, packFileArgument);

        var listCommand = new Command("list", "List contents of a .dir archive");
        var listFileArgument = new Argument<FileInfo>("file", "The .dir archive to list contents of");
        listCommand.AddArgument(listFileArgument);
        listCommand.SetHandler(ListHandler, listFileArgument);

        darCommand.AddCommand(extractCommand);
        darCommand.AddCommand(packCommand);
        darCommand.AddCommand(listCommand);

        command.AddCommand(darCommand);
        return darCommand;
    }

    private static void ListHandler(FileInfo file)
    {
        if (!file.Exists) throw new FileNotFoundException("Specified archive cannot be found", file.FullName);

        using var fileStream = File.Open(file.FullName, FileMode.Open);
        using var reader = new BinaryReader(fileStream);

        var archive = reader.ReadDirArchive();
        foreach (var entry in archive.Files)
        {
            Console.WriteLine($"{entry.Name} @ 0x{entry.Offset:X4}");
        }
    }

    private static void PackHandler(DirectoryInfo input, FileInfo? target)
    {
        if (!input.Exists) throw new FileNotFoundException("Archive source does not exist", input.FullName);
        target ??= new FileInfo($"{Path.GetFileNameWithoutExtension(input.FullName)}.dir");

        var offset = 2048;
        var files = input.GetFiles().Select(file =>
        {
            var dir = new DirFile
            {
                Name = Path.GetFileNameWithoutExtension(file.Name),
                Offset = offset,
            };

            offset += (int)(Math.Ceiling(file.Length / 2048f) * 2048);

            return (DirEntry: dir, Filename: file.FullName);
        }).ToList();

        var archive = new DirArchive
        {
            Files = files.Select(f => f.DirEntry).ToList()
        };

        using var writer = new BinaryWriter(target.Create());
        writer.Write(archive);

        foreach (var file in files)
        {
            writer.BaseStream.Position = file.DirEntry.Offset;
            writer.Write(File.ReadAllBytes(file.Filename));
        }
    }

    private static void ExtractHandler(FileInfo file, DirectoryInfo? target)
    {
        if (!file.Exists) throw new FileNotFoundException("Specified archive cannot be found", file.FullName);

        if (target == null) target = Directory.CreateDirectory(Path.GetFileNameWithoutExtension(file.Name));
        else if (!target.Exists) target = Directory.CreateDirectory(Path.GetFileNameWithoutExtension(target.FullName));

        using var fileStream = File.Open(file.FullName, FileMode.Open);
        using var reader = new BinaryReader(fileStream);

        var archive = reader.ReadDirArchive();
        Console.WriteLine("DIR archives do not contain file sizes, extracted files might have extra padding");

        for (int i = 0; i < archive.Files.Count; i++)
        {
            DirFile? entry = archive.Files[i];

            int len = 0;
            if (i == archive.Files.Count - 1) len = (int)reader.BaseStream.Length - entry.Offset;
            else len = archive.Files[i + 1].Offset - entry.Offset;

            var origPosition = reader.BaseStream.Position;
            reader.BaseStream.Position = entry.Offset;
            var entryPath = Path.Combine(target.FullName, $"{entry.SanitizedName}.stg");
            File.WriteAllBytes(entryPath, reader.ReadBytes(len));
            reader.BaseStream.Position = origPosition;
        }

        var orderPath = Path.Combine(target.FullName, "order.txt");
        File.WriteAllLines(orderPath, archive.Files.Select(f => f.Name));
    }
}
