namespace MetalMintSolid.Util;

public static class ExtensionNames
{
    public static readonly Dictionary<byte, string> Extensions = new()
    {
        { 0x61, "azm" },
        { 0x62, "bin" },
        { 0x63, "con" },
        { 0x64, "dar" },
        { 0x65, "efx" },
        { 0x67, "gcx" },
        { 0x68, "hzm" },
        { 0x69, "img" },
        { 0x6b, "kmd" },
        { 0x6c, "lit" },
        { 0x6d, "mt3" },
        { 0x6f, "oar" },
        { 0x70, "pcc" },
        { 0x72, "rar" },
        { 0x73, "sgt" },
        { 0x77, "wvx" },
        { 0x7a, "zmd" },
        { 0xff, "dar" }
    };

    /// <summary>
    /// Resolves a file extension (with or without a leading dot) to its MGS DAR
    /// extension byte. The PC ".pcx" and PSX ".pcc" extensions are the same MGS
    /// PCX payload, so ".pcx" is accepted as an alias for the "pcc" byte (0x70) --
    /// this is what lets a PC-format (name-indexed) archive of .pcx textures be
    /// repacked into a PSX (hash-indexed) archive.
    /// </summary>
    public static byte ByteFromExtension(string extension)
    {
        var ext = extension.TrimStart('.').ToLowerInvariant();
        if (ext == "pcx") ext = "pcc";

        foreach (var pair in Extensions)
            if (pair.Value == ext) return pair.Key;

        throw new NotSupportedException($"Unknown DAR entry extension '.{ext}'");
    }
}
