using MetalMintSolid;
using MetalMintSolid.Dar.Psx;
using MetalMintSolid.Extensions;
using MetalMintSolid.Util;
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
        var extractPlatformOption = new Option<PlatformEnum>("--platform", () => PlatformEnum.Pc, "Target platform");
        extractCommand.AddArgument(extractFileArgument);
        extractCommand.AddArgument(extractTargetArgument);
        extractCommand.AddOption(extractPlatformOption);
        extractCommand.SetHandler(ExtractHandler, extractFileArgument, extractTargetArgument, extractPlatformOption);

        var packCommand = new Command("pack", "Creates a new .dar archive");
        var packFileArgument = new Argument<FileInfo?>("file", "Output filename");
        var packTargetArgument = new Argument<DirectoryInfo>("input", "Archive source directory");
        var packOrderOption = new Option<FileInfo?>("--order", () => new FileInfo("order.txt"), "File order file, orders the archive in a specific way (useful for modding)");
        var packPlatformOption = new Option<PlatformEnum>("--platform", () => PlatformEnum.Pc, "Target platform");
        packCommand.AddArgument(packTargetArgument);
        packCommand.AddArgument(packFileArgument);
        packCommand.AddOption(packOrderOption);
        packCommand.AddOption(packPlatformOption);
        packCommand.SetHandler(PackHandler, packFileArgument, packTargetArgument, packOrderOption, packPlatformOption);

        var listCommand = new Command("list", "List contents of a .dar archive");
        var listFileArgument = new Argument<FileInfo>("file", "The .dar archive to list contents of");
        var listPlatformOption = new Option<PlatformEnum>("--platform", () => PlatformEnum.Pc, "Target platform");
        listCommand.AddArgument(listFileArgument);
        listCommand.AddOption(listPlatformOption);
        listCommand.SetHandler(ListHandler, listFileArgument, listPlatformOption);

        darCommand.AddCommand(extractCommand);
        darCommand.AddCommand(packCommand);
        darCommand.AddCommand(listCommand);

        command.AddCommand(darCommand);
        return darCommand;
    }

    private static void ExtractHandler(FileInfo file, DirectoryInfo? target, PlatformEnum platform)
    {
        if (!file.Exists) throw new FileNotFoundException("Specified archive cannot be found", file.FullName);

        if (target == null) target = Directory.CreateDirectory(Path.GetFileNameWithoutExtension(file.Name));
        else if (!target.Exists) target = Directory.CreateDirectory(Path.GetFileNameWithoutExtension(target.FullName));

        using var fileStream = File.Open(file.FullName, FileMode.Open);
        using var reader = new BinaryReader(fileStream);

        void ExtractPc()
        {
            var archive = reader.ReadDarArchive();

            foreach (var entry in archive.Files)
            {
                var entryPath = Path.Combine(target.FullName, entry.Name);
                File.WriteAllBytes(entryPath, entry.Data);
            }

            var orderPath = Path.Combine(target.FullName, "order.txt");
            File.WriteAllLines(orderPath, archive.Files.Select(f => f.Name));
        }

        void ExtractPsx()
        {
            var filesNames = new List<string>();
            while (reader.BaseStream.Position != reader.BaseStream.Length)
            {
                var file = Psx.BinaryReaderDarFileExtensions.ReadDarFile(reader);
                var fileName = $"{file.Hash}.{file.ExtensionName}";
                filesNames.Add(fileName);

                var entryPath = Path.Combine(target.FullName, fileName);
                File.WriteAllBytes(entryPath, file.Data);
            }

            var orderPath = Path.Combine(target.FullName, "order.txt");
            File.WriteAllLines(orderPath, filesNames);
        }

        switch (platform)
        {
            case PlatformEnum.Pc:
                ExtractPc();
                break;
            case PlatformEnum.Psx:
                ExtractPsx();
                break;
            default:
                throw new NotImplementedException("Platform is not supported");
        }
    }

    private static void PackHandler(FileInfo? file, DirectoryInfo input, FileInfo? order, PlatformEnum platform) 
    {
        if (!input.Exists) throw new FileNotFoundException("Archive source does not exist", input.FullName);
        file ??= new FileInfo($"{Path.GetFileNameWithoutExtension(input.FullName)}.dar");

        order ??= new FileInfo(Path.Combine(input.FullName, "order.txt"));
        string[]? fileOrder;

        if (!order.Exists) fileOrder = Directory.GetFiles(input.FullName).Select(f => Path.GetFileName(f)).ToArray();
        else fileOrder = File.ReadAllLines(order.FullName);

        using var archiveFile = File.Open(file.FullName, FileMode.Create);
        using var writer = new BinaryWriter(archiveFile);

        void SavePc()
        {
            var archive = new DarArchive
            {
                Files = fileOrder.Select(file => new DarFile
                {
                    Name = file,
                    Data = File.ReadAllBytes(Path.Combine(input.FullName, file))
                }).ToList()
            };

            writer.Write(archive);
        }

        void SavePsx()
        {
            var psxFiles = fileOrder.Select(file => new Psx.DarFile
            {
                Hash = ushort.Parse(Path.GetFileNameWithoutExtension(file)),
                Extension = ExtensionNames.Extensions.First(e => e.Value == Path.GetExtension(file)[1..]).Key,
                Data = File.ReadAllBytes(Path.Combine(input.FullName, file))
            }).ToList();

            foreach (var psxFile in psxFiles) writer.Write(psxFile);
        }

        switch (platform)
        {
            case PlatformEnum.Pc:
                SavePc();
                break;
            case PlatformEnum.Psx:
                SavePsx();
                break;
            default:
                throw new NotImplementedException("Platform is not supported");
        }
    }

    private static void ListHandler(FileInfo file, PlatformEnum platform)
    {
        if (!file.Exists) throw new FileNotFoundException("Specified archive cannot be found", file.FullName);

        using var fileStream = File.Open(file.FullName, FileMode.Open);
        using var reader = new BinaryReader(fileStream);

        void ListPc()
        {
            var archive = reader.ReadDarArchive();
            foreach (var entry in archive.Files)
            {
                Console.WriteLine($"{entry.Name} ({entry.Data.Length} bytes) | Hash: {StringExtensions.GV_StrCode_80016CCC(entry.Name[0..^4])}");
            }
        }

        void ListPsx()
        {
            while (reader.BaseStream.Position != reader.BaseStream.Length)
            {
                var file = Psx.BinaryReaderDarFileExtensions.ReadDarFile(reader);
                Console.WriteLine($"{file.Hash}.{file.ExtensionName} ({file.Data.Length} bytes)");
            }
        }

        switch (platform)
        {
            case PlatformEnum.Pc:
                ListPc();
                break;
            case PlatformEnum.Psx:
                ListPsx();
                break;
            default:
                throw new NotImplementedException("Platform is not supported");
        }
    }
}
