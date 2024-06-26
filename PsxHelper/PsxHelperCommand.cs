using MetalMintSolid.Dar.Psx;
using MetalMintSolid.Dir;
using MetalMintSolid.Stg;
using MetalMintSolid.Util;
using System.CommandLine;

namespace MetalMintSolid.PsxHelper;

public static class PsxHelperCommand
{
    public static Command AddPsxHelpers(this Command command)
    {
        var ps1Helper = new PsxHelper();
        var pc = new Command("pcxsnake", "Swap Snake's model and textures (texture and model DARs of init) on PSX, probably refer to source code for this, requires setup, experimental");
        pc.SetHandler(() =>
        {
            ps1Helper.RepackDar("stage/init0", "stg_mdl1.dar", "order.mdl.txt");
            ps1Helper.RepackDar("stage/init1", "stg_tex1.dar", "order.tex.txt");
            ps1Helper.InjectStage();
            ps1Helper.RepackDir();
        });

        command.AddCommand(pc);
        return pc;
    }
}

public class PsxHelper
{
    private int _sector = 1;
    private int _fileNum = 0;

    public void InjectStage()
    {
        using var reader = new BinaryReader(File.OpenRead("init.stg"));
        var header = reader.ReadStgHeader();
        var configs = ReadConfigs(reader, 0);

        var modMdl = File.ReadAllBytes("stg_mdl1.dar");
        var modTex = File.ReadAllBytes("stg_tex1.dar");
        using var writer = new BinaryWriter(File.Open("stage/init.stg", FileMode.Create));

        void Resize(StgConfig config, int newLength)
        {
            var oldSizeSectors = config.SizeSectors;
            var sizeSectors = (int)Math.Ceiling(newLength / 2048f);
            var sizeDiff = sizeSectors - oldSizeSectors;

            config.Size = newLength;
            header.Size += (short)sizeDiff;
        }

        var oldSizeSectors = configs[0].SizeSectors + configs[1].SizeSectors;
        Resize(configs[0], modMdl.Length);
        Resize(configs[1], modTex.Length);

        // Header
        writer.Write(header);
        foreach (var config in configs) writer.Write(config);

        // Modded data
        writer.BaseStream.Position = 2048;

        writer.Write(modMdl);
        var pad = 2048 - writer.BaseStream.Position % 2048;
        if (pad == 2048) pad = 0;
        writer.BaseStream.Position += pad;

        writer.Write(modTex);
        pad = 2048 - writer.BaseStream.Position % 2048;
        if (pad == 2048) pad = 0;
        writer.BaseStream.Position += pad;

        // Rest of file
        reader.BaseStream.Position = 2048 + oldSizeSectors * 2048;
        writer.Write(reader.ReadBytes((int)reader.BaseStream.Length));
    }

    private List<StgConfig> ReadConfigs(BinaryReader reader, int offset)
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
        }

        throw new Exception("Failed to load archive");
    }

    private void Loader_helper_8002336C(BinaryReader reader, StgConfig conf)
    {
        Console.WriteLine("subarchive?");
    }

    private void Loader_helper2_80023460(BinaryReader reader, StgConfig conf, int offset)
    {
        var isResidentCache = conf.Mode == 114;
        Console.WriteLine("rando file");

        var origPos = reader.BaseStream.Position;

        reader.BaseStream.Position = offset + _sector * 2048;
        //var c2f = ReadConfigs(reader);
        var data = reader.ReadBytes(conf.Size);
        var r2 = new BinaryReader(new MemoryStream(data));

        //if (fileNum == 1) File.WriteAllBytes("stg_tex1_orig.dar", r2.ReadBytes(99999999));

        var file = new List<DarFile>();
        while (r2.BaseStream.Position != r2.BaseStream.Length)
        {
            var a = BinaryReaderDarFileExtensions.ReadDarFile(r2);
            //Console.WriteLine($"{a.Hash}.{ExtensionNames.Extensions[a.Extension]}");
            //File.WriteAllBytes($"stage/init{fileNum}/{a.Hash}.{ExtensionNames.Extensions[a.Extension]}", a.Data);
            file.Add(a);
        }

        reader.BaseStream.Position = origPos;
        _sector += (int)Math.Ceiling(conf.Size / 2048f);
        _fileNum++;
    }

    public void RepackDar(string src, string dest, string orderFile)
    {

        var order = File.ReadAllLines(orderFile);
        var darfiles = Directory.GetFiles(src).Select(f =>
        {
            return new DarFile
            {
                Hash = ushort.Parse(Path.GetFileNameWithoutExtension(f)),
                Extension = ExtensionNames.Extensions.First(e => e.Value == Path.GetExtension(f)[1..]).Key,
                Data = File.ReadAllBytes(f)
            };
        });

        using var w = new BinaryWriter(File.Open(dest, FileMode.Create));
        foreach (var d in order)
        {
            var file = darfiles.First(a => $"{a.Hash}.{ExtensionNames.Extensions[a.Extension]}" == d);
            w.Write(file);
        }
        w.Close();

    }

    public void RepackDir()
    {
        var offset = 2048;
        var files = Directory.GetFiles("stage").Select(f =>
        {
            var dir = new DirFile
            {
                Name = Path.GetFileNameWithoutExtension(f),
                Offset = offset,
            };

            var fi = new FileInfo(f).Length;
            offset += (int)(Math.Ceiling(fi / 2048f) * 2048);

            return (dir, f);
        }).ToList();

        var archive = new DirArchive
        {
            Files = files.Select(f => f.dir).ToList()
        };

        using var dw = new BinaryWriter(File.Open(@"STAGE.DIR", FileMode.Create));
        dw.Write(archive);
        foreach (var file in files)
        {
            dw.BaseStream.Position = file.dir.Offset;
            dw.Write(File.ReadAllBytes(file.f));
        }
    }
}
