using MetalMintSolid.Util;
using System.Text.Json.Serialization;

namespace MetalMintSolid.Stg;

public class StgConfig
{
    public ushort Hash { get; set; }
    public byte Mode { get; set; }
    public byte Extension { get; set; }
    [JsonIgnore]
    public string ExtensionName => ExtensionNames.Extensions[Extension];
    public int Size { get; set; }
    [JsonIgnore]
    public int SizeSectors
    {
        get => (int)Math.Ceiling(Size / 2048f);
        set => Size = value * 2048;
    }
}

public static class BinaryReaderStgConfigExtensions
{
    public static StgConfig ReadStgConfig(this BinaryReader reader)
    {
        return new StgConfig
        {
            Hash = reader.ReadUInt16(),
            Mode = reader.ReadByte(),
            Extension = reader.ReadByte(),
            Size = reader.ReadInt32(),
        };
    }

    public static List<StgConfig> ReadStgConfigList(this BinaryReader reader)
    {
        var configs = new List<StgConfig>();
        while (true)
        {
            var conf = reader.ReadStgConfig();
            if (conf.Mode == 0) return configs;

            configs.Add(conf);
        }

        throw new Exception("Failed to load archive");
    }
}

public static class BinaryWriterStgConfigExtensions
{
    public static void Write(this BinaryWriter writer, StgConfig header)
    {
        writer.Write(header.Hash);
        writer.Write(header.Mode);
        writer.Write(header.Extension);
        writer.Write(header.Size);
    }
}