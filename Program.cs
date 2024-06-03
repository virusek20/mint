using MetalMintSolid.Dar;
using MetalMintSolid.Kmd;
using MetalMintSolid.Pcx;
using System.CommandLine;
using System.CommandLine.Builder;
using System.CommandLine.Parsing;

var rootCommand = new RootCommand("MGS import .NET Tool (MINT)");
rootCommand.AddDarCommand();
rootCommand.AddPcxCommand();
rootCommand.AddKmdCommand();

var parser = new CommandLineBuilder(rootCommand)
    .UseVersionOption()
    .UseHelp()
    .UseEnvironmentVariableDirective()
    .UseParseDirective()
    .UseSuggestDirective()
    .RegisterWithDotnetSuggest()
    .UseTypoCorrections()
    .UseParseErrorReporting()
    .Build();

await parser.InvokeAsync(args);