using MetalMintSolid.Dar;
using MetalMintSolid.Dir;
using MetalMintSolid.Hash;
using MetalMintSolid.Kmd;
using MetalMintSolid.Pcx;
using MetalMintSolid.PsxHelper;
using MetalMintSolid.Stg;
using System.CommandLine;
using System.CommandLine.Builder;
using System.CommandLine.Parsing;

var rootCommand = new RootCommand("MGS import .NET Tool (MINT)");
rootCommand.AddDarCommand();
rootCommand.AddPcxCommand();
rootCommand.AddKmdCommand();
rootCommand.AddDirCommand();
rootCommand.AddHashCommand();
rootCommand.AddStgCommand();
rootCommand.AddPsxHelpers();

var parser = new CommandLineBuilder(rootCommand)
    .UseVersionOption()
    .UseHelp()
    .UseEnvironmentVariableDirective()
    .UseParseDirective()
    .UseSuggestDirective()
    .RegisterWithDotnetSuggest()
    .UseTypoCorrections()
    .UseParseErrorReporting()
#if RELEASE
    .UseExceptionHandler()
#endif
    .Build();

await parser.InvokeAsync(args);