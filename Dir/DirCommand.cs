using MetalMintSolid.Dar.Psx;
using MetalMintSolid.Stg;
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
        var packFileArgument = new Argument<FileInfo?>("file", "Output filename");
        var packTargetArgument = new Argument<DirectoryInfo>("input", "Archive source directory");
        var packOrderOption = new Option<FileInfo?>("--order", () => new FileInfo("order.txt"), "File order file, orders the archive in a specific way (useful for modding)");
        packCommand.AddArgument(packTargetArgument);
        packCommand.AddArgument(packFileArgument);
        packCommand.AddOption(packOrderOption);
        packCommand.SetHandler(PackHandler, packFileArgument, packTargetArgument, packOrderOption);

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

        /*
        // TEST ONLY
        var expanded = new DirArchive
        {
            Files = archive.Files.Select(f => new DirFile { Name = f.Name, Offset = f.Offset }).ToList(),
        };
        foreach (var entry in expanded.Files.Skip(1)) entry.Offset += 2048 * 128; // Mint do be sizeable

        var w = new BinaryWriter(File.OpenWrite("STAGE.DIR"));
        w.Write(expanded);

        for (int i = 0; i < expanded.Files.Count; i++)
        {
            DirFile? entry = expanded.Files[i];
            DirFile? entryOriginal = archive.Files[i];

            w.BaseStream.Position = entry.Offset;
            reader.BaseStream.Position = entryOriginal.Offset;

            int len = 0;
            if (i == expanded.Files.Count - 1) len = (int)reader.BaseStream.Length - archive.Files[i].Offset;
            else len = archive.Files[i + 1].Offset - archive.Files[i].Offset;

            using var w2 = new BinaryWriter(File.OpenWrite($"stage/{entry.SanitizedName}.stg"));

            var data = reader.ReadBytes(len);
            w.Write(data);
            w2.Write(data);
        }

        w.Close();
        
        // TEST ONLY

        reader.BaseStream.Position = archive.Files[8].Offset;
        var header = reader.ReadStgHeader();

        var configs = ReadConfigs(reader, archive.Files[8].Offset);
        */
    }

    private static void PackHandler(FileInfo? file, DirectoryInfo input, FileInfo? order)
    {
        throw new NotImplementedException();
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

    private static int sector = 1;

    private static List<StgConfig> ReadConfigs(BinaryReader reader, int offset)
    {
        var configs = new List<StgConfig>();
        while (true)
        {
            var conf = reader.ReadStgConfig();
            switch (conf.Mode)
            {
                case 99:
                    //Loader_helper_8002336C(reader, conf);
                    break;
                case 115: // s = skip?
                    break;
                case 0:
                    return configs;
                default:
                    Loader_helper2_80023460(reader, conf, offset);
                    break;
            }
            if (conf.Mode == 0) break;
            else configs.Add(conf);

            if (conf.Extension == 0xFF) // DAR
            {
                if (conf.Mode == 0x6E) Console.WriteLine("Texture pack");
                else if (conf.Mode == 0x63) Console.WriteLine("Model pack");
            }
        }

        throw new Exception("Failed to load archive");
    }

    private static void Loader_helper_8002336C(BinaryReader reader, StgConfig conf)
    {
        Console.WriteLine("subarchive?");
    }

    private static void Loader_helper2_80023460(BinaryReader reader, StgConfig conf, int offset)
    {
        var isResidentCache = conf.Mode == 114;
        Console.WriteLine("rando file");

        var origPos = reader.BaseStream.Position;

        reader.BaseStream.Position = offset + sector * 2048;
        //var c2f = ReadConfigs(reader);
        var data = reader.ReadBytes(conf.Size);
        var r2 = new BinaryReader(new MemoryStream(data));

        var file = new List<DarFile>();
        while (r2.BaseStream.Position != r2.BaseStream.Length) 
        {
            file.Add(r2.ReadDarFile());
        }

        reader.BaseStream.Position = origPos;
        sector += (int)Math.Ceiling(conf.Size / 2048f);
    }
}
