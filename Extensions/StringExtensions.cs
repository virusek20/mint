namespace MetalMintSolid.Extensions;

public static class StringExtensions
{
    public static ushort GV_StrCode_80016CCC(string input)
    {
        ushort id = 0;

        foreach (char c in input)
        {
            id = (ushort)((id >> 11) | (id << 5));
            id += c;
        }

        return id;
    }

    /// <summary>
    /// Resolves the MGS 16-bit texture/asset hash that a file name refers to,
    /// across the naming conventions used by the different toolchains:
    /// <list type="bullet">
    /// <item>PC: an arbitrary asset name hashed with <see cref="GV_StrCode_80016CCC"/> (e.g. "sna_face").</item>
    /// <item>PSX (MINT dar extract): the hash as a decimal string (e.g. "23400").</item>
    /// <item>PSX (external repackers): "psx_XXXX" with the hash in hex (e.g. "psx_5b68").</item>
    /// </list>
    /// A purely numeric name is always treated as the literal hash; MGS PC asset
    /// names are never purely numeric, so this stays unambiguous in practice.
    /// </summary>
    public static ushort HashFromFileName(string nameWithoutExtension)
    {
        // PSX "psx_5b68" -> low16 hash (hex)
        if (nameWithoutExtension.StartsWith("psx_", StringComparison.OrdinalIgnoreCase) &&
            ushort.TryParse(nameWithoutExtension.AsSpan(4), System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out var hexHash))
            return hexHash;

        // PSX decimal hash "23400"
        if (ushort.TryParse(nameWithoutExtension, out var decHash))
            return decHash;

        // PC: name -> GV_StrCode
        return GV_StrCode_80016CCC(nameWithoutExtension);
    }
}
