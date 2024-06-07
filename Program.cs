using MetalMintSolid.Dar;
using MetalMintSolid.Kmd;
using MetalMintSolid.Pcx;
using System.CommandLine;
using System.CommandLine.Builder;
using System.CommandLine.Parsing;
using System.Drawing;


// TODO: Cleanup this temp mess
/*
var images = Directory.GetFiles(@"C:\Users\virus\Documents\Visual Studio 2022\Projects\MetalMintSolid\bin\Debug\net8.0-windows\available_textures");
var imageNames = images.Select(Path.GetFileNameWithoutExtension);
var pixels = images.Select(i =>
{
    var b = Image.FromFile(i);
    return b.Width * b.Height;
}).Sum();

var textures = Directory.GetFiles(@"E:\temp\metal mod gog\stage\init_jim\res_tex1").Where(f => imageNames.Contains(Path.GetFileNameWithoutExtension(f)));
var positions = textures.Select(i =>
{
    using var a = File.OpenRead(i);
    using var r = new BinaryReader(a);
    var header = r.ReadPcxImage().Header;

    return (Path.GetFileNameWithoutExtension(i), px: header.Px, py: header.Py, w: header.WindowMax.X + 1, h: header.WindowMax.Y + 1);
}).ToList();

var vramMap = new Bitmap(1024, 512);
var allocMap = new int[1024, 512];

for (int i = 0; i < positions.Count; i++)
{
    var region = positions[i];
    var color = Color.FromArgb(255, Random.Shared.Next(255), Random.Shared.Next(255), Random.Shared.Next(255));

    for (int x = region.px; x < region.px + region.w / 4; x++)
    {
        for (int y = region.py; y < region.py + region.h / 4; y++)
        {
            if (allocMap[x, y] != 0)
            {
                Console.WriteLine($"Overlap at {x}:{y} with {positions[allocMap[x, y]].Item1} while writing {region.Item1}");
            }
            else allocMap[x, y] = i;

            vramMap.SetPixel(x, y, color);
        }
    }
}

vramMap.Save("vram.png");

var colors = new Bitmap(@"C:\Users\virus\Documents\Visual Studio 2022\Projects\MetalMintSolid\bin\Debug\net8.0-windows\textures\Layer.001.png");
var set = new HashSet<Color>();
for (int i = 0; i < colors.Width; i++)
{
    for (int j = 0; j < colors.Height; j++)
    {
        set.Add(colors.GetPixel(i, j));
    }
}
*/


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