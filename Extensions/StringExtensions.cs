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
}
