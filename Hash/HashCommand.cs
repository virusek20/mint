using MetalMintSolid.Extensions;
using System.CommandLine;

namespace MetalMintSolid.Hash;

public static class HashCommand
{
    public static Command AddHashCommand(this Command command)
    {
        var hashCommand = new Command("hash", "Calculate MGS hash of a string");
        var hashInputArgument = new Argument<string>("input", "Input string");
        hashCommand.AddArgument(hashInputArgument);
        hashCommand.SetHandler(HashHandler, hashInputArgument);

        command.AddCommand(hashCommand);
        return hashCommand;
    }

    private static void HashHandler(string input)
    {
        Console.WriteLine(StringExtensions.GV_StrCode_80016CCC(input));
    }
}
