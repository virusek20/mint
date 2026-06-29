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
        var packOrderOption = new Option<FileInfo?>("--order", "File order file, orders the archive in a specific way (useful for modding)")
        {
            Arity = ArgumentArity.ZeroOrOne
        };
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

        var convertCommand = new Command("convert", "Convert a .dar between PC (name-indexed) and PSX (hash-indexed) formats.\n" +
            "PC format = u32 count + named entries; PSX format = flat hash-indexed entries (as found inside .stg).\n" +
            "Bridges external 'psx_XXXX.pcx' texture dumps into a PSX archive that can be spliced into a stage.");
        var convertSourceArgument = new Argument<FileInfo>("source", "Source .dar archive");
        var convertTargetArgument = new Argument<FileInfo?>("target", "Output .dar archive")
        {
            Arity = ArgumentArity.ZeroOrOne
        };
        var convertFromOption = new Option<PlatformEnum>("--from", "Source format (pc = name-indexed, psx = hash-indexed)") { IsRequired = true };
        var convertToOption = new Option<PlatformEnum>("--to", "Target format (pc = name-indexed, psx = hash-indexed)") { IsRequired = true };
        convertCommand.AddArgument(convertSourceArgument);
        convertCommand.AddArgument(convertTargetArgument);
        convertCommand.AddOption(convertFromOption);
        convertCommand.AddOption(convertToOption);
        convertCommand.SetHandler(ConvertHandler, convertSourceArgument, convertTargetArgument, convertFromOption, convertToOption);

        darCommand.AddCommand(extractCommand);
        darCommand.AddCommand(packCommand);
        darCommand.AddCommand(listCommand);
        darCommand.AddCommand(convertCommand);

        command.AddCommand(darCommand);
        return darCommand;
    }

    private static void ExtractHandler(FileInfo file, DirectoryInfo? target, PlatformEnum platform)
    {
        if (!file.Exists) throw new FileNotFoundException("Specified archive cannot be found", file.FullName);

        if (target == null) target = Directory.CreateDirectory(Path.GetFileNameWithoutExtension(file.Name));
        else if (!target.Exists) target = Directory.CreateDirectory(target.FullName);

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
                // Accept decimal ("23400"), hex ("psx_5b68") and named files, and
                // treat .pcx as .pcc, so a PC-format texture dump repacks to PSX.
                Hash = StringExtensions.HashFromFileName(Path.GetFileNameWithoutExtension(file)),
                Extension = ExtensionNames.ByteFromExtension(Path.GetExtension(file)),
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

    private static void ConvertHandler(FileInfo source, FileInfo? target, PlatformEnum from, PlatformEnum to)
    {
        if (!source.Exists) throw new FileNotFoundException("Specified archive cannot be found", source.FullName);
        if (from == to) throw new ArgumentException("--from and --to are the same format; nothing to convert");
        target ??= new FileInfo($"{Path.GetFileNameWithoutExtension(source.FullName)}.{to.ToString().ToLowerInvariant()}.dar");

        // Read the source into a common representation: hash + extension byte +
        // a round-trippable name + payload. Order is preserved (it is significant
        // for PSX hash-indexed archives, which the engine reads positionally).
        var entries = new List<(ushort Hash, byte Ext, string Name, byte[] Data)>();

        using (var inStream = source.OpenRead())
        using (var reader = new BinaryReader(inStream))
        {
            switch (from)
            {
                case PlatformEnum.Pc:
                    var archive = reader.ReadDarArchive();
                    foreach (var pcEntry in archive.Files)
                    {
                        var nameNoExt = Path.GetFileNameWithoutExtension(pcEntry.Name);
                        entries.Add((StringExtensions.HashFromFileName(nameNoExt), ExtensionNames.ByteFromExtension(Path.GetExtension(pcEntry.Name)), pcEntry.Name, pcEntry.Data));
                    }
                    break;
                case PlatformEnum.Psx:
                    while (reader.BaseStream.Position != reader.BaseStream.Length)
                    {
                        var psxEntry = Psx.BinaryReaderDarFileExtensions.ReadDarFile(reader);
                        // Hex 'psx_XXXX' name round-trips back through HashFromFileName.
                        entries.Add((psxEntry.Hash, psxEntry.Extension, $"psx_{psxEntry.Hash:x4}.{psxEntry.ExtensionName}", psxEntry.Data));
                    }
                    break;
                default:
                    throw new NotImplementedException("Source platform is not supported");
            }
        }

        using var outStream = target.Create();
        using var writer = new BinaryWriter(outStream);

        switch (to)
        {
            case PlatformEnum.Pc:
                var outArchive = new DarArchive
                {
                    Files = entries.Select(e => new DarFile { Name = e.Name, Data = e.Data }).ToList()
                };
                writer.Write(outArchive);
                break;
            case PlatformEnum.Psx:
                foreach (var e in entries) writer.Write(new Psx.DarFile { Hash = e.Hash, Extension = e.Ext, Data = e.Data });
                break;
            default:
                throw new NotImplementedException("Target platform is not supported");
        }

        Console.WriteLine($"Converted {entries.Count} entries ({from} -> {to}) -> {target.FullName}");
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
