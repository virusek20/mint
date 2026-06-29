using MetalMintSolid.Extensions;
using SkiaSharp;
using System.CommandLine;
using System.Text.Json;

namespace MetalMintSolid.Pcx;

public static class PcxCommand
{
    public static Command AddPcxCommand(this Command command)
    {
        var pcxCommand = new Command("pcx", "Manipulate PCX (PC) / PCC (PSX) images");

        // PSX textures use the .pcc extension but are the same MGS PCX format, so
        // expose 'pcc' as an alias of 'pcx': `metalmintsolid pcc <subcommand>` is
        // identical to `metalmintsolid pcx <subcommand>` for every subcommand
        // below. 'pcx' is unchanged; the alias is listed next to it in help.
        pcxCommand.AddAlias("pcc");

        var pcxEncodeCommand = new Command("encode", "Encodes a PCX image from a source bitmap");
        var pcxEncodeSourceArgument = new Argument<FileInfo>("source", "Source image");
        var pcxEncodeTargetArgument = new Argument<FileInfo?>("target", "Output filename");
        var pcxEncodeMetadataSourceOption = new Option<FileInfo?>("--metadata", "Copy metadata from supplied file, properly sets MGS specific data (useful for texture swapping)");
        var pcxEncodePaletteOption = new Option<FileInfo?>("--palette", "Uses a predefined palette instead of generating one (useful for when multiple textures share the same CLUT)");
        pcxEncodeCommand.AddArgument(pcxEncodeSourceArgument);
        pcxEncodeCommand.AddArgument(pcxEncodeTargetArgument);
        pcxEncodeCommand.AddOption(pcxEncodeMetadataSourceOption);
        pcxEncodeCommand.AddOption(pcxEncodePaletteOption);
        pcxEncodeCommand.SetHandler(EncodeHandler, pcxEncodeSourceArgument, pcxEncodeTargetArgument, pcxEncodeMetadataSourceOption, pcxEncodePaletteOption);

        var pcxDecodeCommand = new Command("decode", "Decodes a PCX or PCC image to a bitmap");
        var pcxDecodeCommandSourceArgument = new Argument<FileInfo>("source", "Source PCX image");
        var pcxDecodeCommandTargetArgument = new Argument<FileInfo?>("target", "Output filename");
        pcxDecodeCommand.AddArgument(pcxDecodeCommandSourceArgument);
        pcxDecodeCommand.AddArgument(pcxDecodeCommandTargetArgument);
        pcxDecodeCommand.SetHandler(DecodeHandler, pcxDecodeCommandSourceArgument, pcxDecodeCommandTargetArgument);

        var pcxBatchDecodeCommand = new Command("batchdecode", "Decodes PCX or PCC images to bitmaps");
        var pcxBatchDecodeCommandSourceArgument = new Argument<DirectoryInfo>("source", "Source directory");
        var pcxBatchDecodeCommandTargetArgument = new Argument<DirectoryInfo>("target", "Output directory");
        pcxBatchDecodeCommand.AddArgument(pcxBatchDecodeCommandSourceArgument);
        pcxBatchDecodeCommand.AddArgument(pcxBatchDecodeCommandTargetArgument);
        pcxBatchDecodeCommand.SetHandler(BatchDecodeHandler, pcxBatchDecodeCommandSourceArgument, pcxBatchDecodeCommandTargetArgument);

        var pcxBatchEncodeCommand = new Command("batchencode", "Encodes a directory of bitmaps back to PCX/PCC images");
        var pcxBatchEncodeSourceArgument = new Argument<DirectoryInfo>("source", "Source directory (edited PNG/BMP images)");
        var pcxBatchEncodeTargetArgument = new Argument<DirectoryInfo>("target", "Output directory");
        var pcxBatchEncodeMetadataOption = new Option<DirectoryInfo?>(new[] { "--originals", "--metadata" }, "Path to the directory of ORIGINAL pcx/pcc files the images were decoded from. Each image is matched by name (e.g. abcd.png -> abcd.pcx/abcd.pcc) to copy its MGS metadata, and -- unless --format is given -- to pick the output extension. Defaults to the source directory.");
        var pcxBatchEncodeFormatOption = new Option<string?>("--format", "Force output format: 'pcx' (PC) or 'pcc' (PSX). Default: follow each matched original, otherwise pcx.");
        pcxBatchEncodeCommand.AddArgument(pcxBatchEncodeSourceArgument);
        pcxBatchEncodeCommand.AddArgument(pcxBatchEncodeTargetArgument);
        pcxBatchEncodeCommand.AddOption(pcxBatchEncodeMetadataOption);
        pcxBatchEncodeCommand.AddOption(pcxBatchEncodeFormatOption);
        pcxBatchEncodeCommand.SetHandler(BatchEncodeHandler, pcxBatchEncodeSourceArgument, pcxBatchEncodeTargetArgument, pcxBatchEncodeMetadataOption, pcxBatchEncodeFormatOption);

        var pcxAnalyzeCommand = new Command("analyze", "Analyzes PCX image(s)");
        var pcxAnalyzeCommandSourceArgument = new Argument<string>("source", "Source file / directory");
        var pcxAnalyzeVerboseOption = new Option<bool>("--verbose", () => false, "Display verbose image info");
        pcxAnalyzeCommand.AddArgument(pcxAnalyzeCommandSourceArgument);
        pcxAnalyzeCommand.AddOption(pcxAnalyzeVerboseOption);
        pcxAnalyzeCommand.SetHandler(AnalyzeHandler, pcxAnalyzeCommandSourceArgument, pcxAnalyzeVerboseOption);

        var pcx2pccCommand = new Command("pcx2pcc", "Convert a PC .pcx texture to a PSX .pcc, using the PSX texture it replaces for VRAM position + CLUT (colour order preserved)");
        var p2pSource = new Argument<FileInfo>("source", "PC .pcx (or .png/.bmp) to convert");
        var p2pTarget = new Argument<FileInfo?>("target", "Output PSX .pcc path (default: <source>.pcc)");
        var p2pMeta = new Option<FileInfo?>(new[] { "--metadata", "--psx", "-m" }, "REQUIRED: the PSX .pcc texture being replaced. Supplies VRAM position, CLUT position, NColors AND the shared CLUT colour order.") { IsRequired = true };
        pcx2pccCommand.AddArgument(p2pSource);
        pcx2pccCommand.AddArgument(p2pTarget);
        pcx2pccCommand.AddOption(p2pMeta);
        pcx2pccCommand.SetHandler(Pcx2PccHandler, p2pSource, p2pTarget, p2pMeta);

        var batchPcx2PccCommand = new Command("batchpcx2pcc", "Convert a directory of PC .pcx textures to PSX .pcc, matched by name to the PSX originals they replace");
        var bp2pSource = new Argument<DirectoryInfo>("source", "Directory of PC .pcx (or .png/.bmp) images");
        var bp2pTarget = new Argument<DirectoryInfo>("target", "Output directory");
        var bp2pOriginals = new Option<DirectoryInfo?>(new[] { "--originals", "--psx" }, "REQUIRED: directory of the PSX .pcc textures being replaced. Each source is matched by name (foo.pcx -> foo.pcc) for VRAM/CLUT.") { IsRequired = true };
        batchPcx2PccCommand.AddArgument(bp2pSource);
        batchPcx2PccCommand.AddArgument(bp2pTarget);
        batchPcx2PccCommand.AddOption(bp2pOriginals);
        var bp2pNewClut = new Option<bool>(new[] { "--new-clut" }, "Replace each shared CLUT with a NEW palette built from the source art, instead of reusing the PSX original's. Use when the PC character's colours differ from the PSX original (e.g. Liquid onto Snake). All textures sharing a CLUT are re-indexed together; each shared group must fit 16 colours (EGA) / 256 (VGA).");
        var bp2pSplit = new Option<bool>(new[] { "--split-cluts" }, "EXACT colour mode: when a shared CLUT is over budget, split its textures into multiple sub-CLUTs (each with their exact <=16/256 colours) placed in FREE VRAM slots, instead of quantizing. Zero colour loss. Falls back to median-cut for a group if VRAM has no room.");
        batchPcx2PccCommand.AddOption(bp2pNewClut);
        batchPcx2PccCommand.AddOption(bp2pSplit);
        batchPcx2PccCommand.SetHandler(BatchPcx2PccHandler, bp2pSource, bp2pTarget, bp2pOriginals, bp2pNewClut, bp2pSplit);

        var pcc2pcxCommand = new Command("pcc2pcx", "Convert a PSX .pcc texture to a PC .pcx, keeping the source artwork's colours and taking header fields + format from the PC texture it replaces");
        var c2pSource = new Argument<FileInfo>("source", "PSX .pcc (or .png/.bmp) to convert");
        var c2pTarget = new Argument<FileInfo?>("target", "Output PC .pcx path (default: <source>.pcx)");
        var c2pMeta = new Option<FileInfo?>(new[] { "--metadata", "--pc", "-m" }, "REQUIRED: the PC .pcx texture being replaced. Supplies header fields and target format (EGA/VGA). The source's own palette is kept so your edits survive.") { IsRequired = true };
        pcc2pcxCommand.AddArgument(c2pSource);
        pcc2pcxCommand.AddArgument(c2pTarget);
        pcc2pcxCommand.AddOption(c2pMeta);
        pcc2pcxCommand.SetHandler(Pcc2PcxHandler, c2pSource, c2pTarget, c2pMeta);

        var batchPcc2PcxCommand = new Command("batchpcc2pcx", "Convert a directory of PSX .pcc textures to PC .pcx, matched by MGS hash to the PC originals they replace (output keeps the PC names)");
        var bc2pSource = new Argument<DirectoryInfo>("source", "Directory of PSX .pcc (or .png/.bmp) images");
        var bc2pTarget = new Argument<DirectoryInfo>("target", "Output directory");
        var bc2pPc = new Option<DirectoryInfo?>(new[] { "--pc", "--originals" }, "REQUIRED: directory of the PC .pcx textures being replaced. Each numbered source is matched by hash (56477.pcc -> sna_face.pcx).") { IsRequired = true };
        batchPcc2PcxCommand.AddArgument(bc2pSource);
        batchPcc2PcxCommand.AddArgument(bc2pTarget);
        batchPcc2PcxCommand.AddOption(bc2pPc);
        batchPcc2PcxCommand.SetHandler(BatchPcc2PcxHandler, bc2pSource, bc2pTarget, bc2pPc);

        pcxCommand.AddCommand(pcxDecodeCommand);
        pcxCommand.AddCommand(pcxEncodeCommand);
        pcxCommand.AddCommand(pcxBatchDecodeCommand);
        pcxCommand.AddCommand(pcxBatchEncodeCommand);
        pcxCommand.AddCommand(pcxAnalyzeCommand);
        pcxCommand.AddCommand(pcx2pccCommand);
        pcxCommand.AddCommand(batchPcx2PccCommand);
        pcxCommand.AddCommand(pcc2pcxCommand);
        pcxCommand.AddCommand(batchPcc2PcxCommand);

        command.Add(pcxCommand);
        return pcxCommand;
    }

    private static void EncodeHandler(FileInfo source, FileInfo? target, FileInfo? metadata, FileInfo? palette)
    {
        if (!source.Exists) throw new FileNotFoundException("Specified bitmap was not found", source.FullName);
        target ??= new FileInfo($"{Path.GetFileNameWithoutExtension(source.FullName)}.pcx");
        EncodeImage(source, target, metadata, palette, verbose: true);
    }

    private static void Pcx2PccHandler(FileInfo source, FileInfo? target, FileInfo? metadata)
    {
        if (!source.Exists) throw new FileNotFoundException("Specified source image was not found", source.FullName);
        if (metadata == null || !metadata.Exists) throw new FileNotFoundException("A PSX original (--metadata/--psx) is required and must exist; without it the texture has no valid VRAM/CLUT and will be distorted in-game", metadata?.FullName ?? "(none)");
        target ??= new FileInfo($"{Path.GetFileNameWithoutExtension(source.FullName)}.pcc");
        EncodeImage(source, target, metadata, palette: null, verbose: true);
        Console.WriteLine($"Converted '{source.Name}' -> '{target.Name}' using PSX original '{metadata.Name}' (CLUT order preserved)");
    }

    private static void BatchPcx2PccHandler(DirectoryInfo source, DirectoryInfo target, DirectoryInfo? originals, bool newClut, bool splitCluts)
    {
        if (!source.Exists) throw new DirectoryNotFoundException("Specified source directory was not found");
        if (originals == null || !originals.Exists) throw new DirectoryNotFoundException("A directory of PSX originals (--originals/--psx) is required and must exist");
        if (!target.Exists) target.Create();

        var images = source.EnumerateFiles("*.*")
            .Where(f => f.Extension.Equals(".pcx", StringComparison.OrdinalIgnoreCase)
                     || f.Extension.Equals(".png", StringComparison.OrdinalIgnoreCase)
                     || f.Extension.Equals(".bmp", StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (images.Count == 0) Console.WriteLine($"No .pcx/.png/.bmp source images found in '{source.FullName}'");

        // Index the PSX originals by their 16-bit MGS hash. PC assets carry real
        // names (e.g. sna_face.pcx); PSX textures are named with the DECIMAL of that
        // same GV_StrCode hash (e.g. 56477.pcc). Matching by hash is the only way to
        // line a NAMED PC set up against a NUMBERED PSX set — literal-name matching
        // never works across the two toolchains.
        var psxByHash = new Dictionary<ushort, FileInfo>();
        var collisions = new List<string>();
        foreach (var orig in originals.EnumerateFiles("*.pcc"))
        {
            var h = StringExtensions.HashFromFileName(Path.GetFileNameWithoutExtension(orig.FullName));
            if (psxByHash.TryGetValue(h, out var existing)) collisions.Add($"{orig.Name} & {existing.Name} (hash {h})");
            else psxByHash[h] = orig;
        }
        if (collisions.Count > 0)
            Console.WriteLine($"Warning: {collisions.Count} hash collision(s) among PSX originals, only the first of each is used: {string.Join("; ", collisions)}");

        // Resolve matches up front.
        var matched = new List<(FileInfo image, FileInfo psx, ushort hash)>();
        int skipped = 0;
        foreach (var image in images)
        {
            var hash = StringExtensions.HashFromFileName(Path.GetFileNameWithoutExtension(image.FullName));
            if (!psxByHash.TryGetValue(hash, out var psx))
            {
                skipped++;
                Console.WriteLine($"  ~ {image.Name}: no PSX original with hash {hash} in originals dir — skipped");
                continue;
            }
            matched.Add((image, psx, hash));
        }

        int converted = 0, failed = 0;

        if (splitCluts)
        {
            // EXACT-colour mode. When a shared CLUT is over budget, instead of
            // quantizing, peel its textures into multiple sub-CLUTs that each fit the
            // cap with their EXACT colours, placing the extra CLUTs in FREE VRAM slots.
            // Zero colour loss, at the cost of a few CLUT slots. Falls back to a single
            // median-cut CLUT for a group if there aren't enough free slots for it.
            // Which textures am I actually converting? And what ELSE lives on each CLUT?
            // A CLUT shared with a texture I can't convert must NOT be rewritten: the
            // un-converted texture still points at it with the original palette, so if I
            // change the colours there both it AND my texture corrupt (last upload wins).
            // For such CLUTs every converted texture is moved to its OWN free slot and the
            // original CLUT is left untouched.
            var convertedHashes = new HashSet<ushort>(matched.Select(m => m.hash));
            var clutAllHashes = new Dictionary<(ushort, ushort), HashSet<ushort>>();
            foreach (var f in originals.EnumerateFiles("*.pcc"))
            {
                var nm = Path.GetFileNameWithoutExtension(f.Name);
                if (!ushort.TryParse(nm, out var hh)) continue;     // PSX files are named by decimal hash
                PcxImage po;
                using (var fs = f.OpenRead())
                using (var r = new BinaryReader(fs)) po = r.ReadPcxImage();
                var k = (po.Header.Cx, po.Header.Cy);
                if (!clutAllHashes.TryGetValue(k, out var set)) { set = new(); clutAllHashes[k] = set; }
                set.Add(hh);
            }

            var groups = new Dictionary<(ushort, ushort), List<(FileInfo image, FileInfo psx, ushort hash)>>();
            var groupVga = new Dictionary<(ushort, ushort), bool>();
            foreach (var m in matched)
            {
                PcxImage po;
                using (var fs = m.psx.OpenRead())
                using (var r = new BinaryReader(fs)) po = r.ReadPcxImage();
                var key = (po.Header.Cx, po.Header.Cy);
                if (!groups.TryGetValue(key, out var list)) { list = new(); groups[key] = list; groupVga[key] = false; }
                groupVga[key] = groupVga[key] || po.Header.IsVga;
                list.Add(m);
            }

            // free CLUT slots, restricted to the character's own CLUT band (the rows its
            // textures' CLUTs already occupy) so we never grab unrelated VRAM.
            var characterRows = new HashSet<ushort>(groups.Keys.Select(k => k.Item2));
            var freeSlots = new Queue<(ushort cx, ushort cy)>(FindFreeClutSlots(originals.EnumerateFiles("*.pcc").ToList(), 16, characterRows));
            Console.WriteLine($"[split-cluts] {freeSlots.Count} free 16-colour CLUT slots available in the character's CLUT band");

            foreach (var (key, members) in groups)
            {
                var cap = groupVga[key] ? 256 : 16;
                var colsets = members.ToDictionary(m => m.image.FullName, m => DistinctColours(LoadBitmap(m.image)));

                bool sharedWithUnconverted = clutAllHashes.TryGetValue(key, out var all)
                                             && all.Any(h => !convertedHashes.Contains(h));

                // first-fit-decreasing pack into sub-groups whose colour UNION fits the cap
                var subs = new List<(List<(FileInfo image, FileInfo psx, ushort hash)> mem, HashSet<SKColor> cols)>();
                foreach (var m in members.OrderByDescending(m => colsets[m.image.FullName].Count))
                {
                    var cs = colsets[m.image.FullName];
                    int idx = -1;
                    for (int i = 0; i < subs.Count; i++)
                    {
                        var u = new HashSet<SKColor>(subs[i].cols); u.UnionWith(cs);
                        if (u.Count <= cap) { idx = i; break; }
                    }
                    if (idx < 0) subs.Add((new() { m }, new HashSet<SKColor>(cs)));
                    else { subs[idx].mem.Add(m); subs[idx].cols.UnionWith(cs); }
                }

                // A shared CLUT can keep NONE of its sub-groups at the original location.
                int keepAtOriginal = sharedWithUnconverted ? 0 : 1;
                int needSlots = subs.Count - keepAtOriginal;

                if (needSlots > freeSlots.Count)
                {
                    // Not enough VRAM to split exactly.
                    if (sharedWithUnconverted)
                    {
                        // Can't safely touch this CLUT — preserve the original palette so the
                        // un-converted textures keep working (converted ones lose some colour).
                        Console.WriteLine($"  ! CLUT (0x{key.Item1:X},0x{key.Item2:X}) shared with un-converted textures but only {freeSlots.Count} free slot(s) for {needSlots} needed; reusing original palette (converted textures may shift colour).");
                        foreach (var m in members)
                        {
                            var outp = new FileInfo(Path.Combine(target.FullName, m.hash + ".pcc"));
                            try { EncodeImage(m.image, outp, m.psx, null, false, true, null); converted++; Console.WriteLine($"  + {m.image.Name} -> {outp.Name}  [reuse original palette]"); }
                            catch (Exception ex) { failed++; Console.WriteLine($"  x {m.image.Name}: {ex.Message}"); }
                        }
                    }
                    else
                    {
                        Console.WriteLine($"  ! CLUT (0x{key.Item1:X},0x{key.Item2:X}): needs {needSlots} extra slots but only {freeSlots.Count} free; falling back to median-cut for this group.");
                        var bitmaps = members.Select(m => LoadBitmap(m.image)).ToList();
                        var (pal, _, _) = BuildGroupPalette(bitmaps, cap);
                        var pd = new PaletteData { Palette = pal, Cx = key.Item1, Cy = key.Item2 };
                        foreach (var m in members)
                        {
                            var outp = new FileInfo(Path.Combine(target.FullName, m.hash + ".pcc"));
                            try { EncodeImage(m.image, outp, m.psx, null, false, false, pd); converted++; Console.WriteLine($"  + {m.image.Name} -> {outp.Name}  [median-cut fallback]"); }
                            catch (Exception ex) { failed++; Console.WriteLine($"  x {m.image.Name}: {ex.Message}"); }
                        }
                    }
                    continue;
                }

                if (sharedWithUnconverted)
                    Console.WriteLine($"  CLUT (0x{key.Item1:X},0x{key.Item2:X}) shared with un-converted textures -> moving all {subs.Count} sub-CLUT(s) to free slots, original left intact.");

                for (int i = 0; i < subs.Count; i++)
                {
                    bool atOriginal = !sharedWithUnconverted && i == 0;
                    var (cx, cy) = atOriginal ? key : freeSlots.Dequeue();
                    var pal = OrderedColours(subs[i].mem, colsets);   // exact union, <= cap
                    var pd = new PaletteData { Palette = pal, Cx = cx, Cy = cy };
                    foreach (var m in subs[i].mem)
                    {
                        var outp = new FileInfo(Path.Combine(target.FullName, m.hash + ".pcc"));
                        try
                        {
                            EncodeImage(m.image, outp, m.psx, null, false, false, pd);
                            converted++;
                            Console.WriteLine($"  + {m.image.Name} (hash {m.hash}) -> {outp.Name}  [CLUT 0x{cx:X},0x{cy:X} exact {pal.Count}c{(atOriginal ? "" : " NEW")}]");
                        }
                        catch (Exception ex) { failed++; Console.WriteLine($"  x {m.image.Name}: {ex.Message}"); }
                    }
                }
            }
        }
        else if (newClut)
        {
            // Group matched textures by the VRAM CLUT location (Cx,Cy) of the PSX slot
            // they fill. Each shared CLUT gets ONE new palette built from all the
            // source art using it, and every texture in the group is re-indexed to
            // that single palette — so the shared CLUT ends up holding the new (e.g.
            // Liquid) colours AND every texture sharing it agrees on the ordering.
            // The whole group's art must fit the CLUT (<=16 EGA / <=256 VGA).
            var groups = new Dictionary<(ushort, ushort), List<(FileInfo image, FileInfo psx, ushort hash)>>();
            var groupVga = new Dictionary<(ushort, ushort), bool>();
            foreach (var m in matched)
            {
                PcxImage po;
                using (var fs = m.psx.OpenRead())
                using (var r = new BinaryReader(fs)) po = r.ReadPcxImage();
                var key = (po.Header.Cx, po.Header.Cy);
                if (!groups.TryGetValue(key, out var list)) { list = new(); groups[key] = list; groupVga[key] = false; }
                groupVga[key] = groupVga[key] || po.Header.IsVga;
                list.Add(m);
            }

            foreach (var (key, members) in groups)
            {
                var cap = groupVga[key] ? 256 : 16;
                var bitmaps = members.Select(m => LoadBitmap(m.image)).ToList();
                var (pal, distinct, truncated) = BuildGroupPalette(bitmaps, cap);
                if (truncated)
                    Console.WriteLine($"  ! CLUT (0x{key.Item1:X},0x{key.Item2:X}): group needs {distinct} colours but the CLUT holds only {cap}; reduced to {cap} via median-cut. For an exact result, reduce these textures to <= {cap} colours TOTAL across the group in your source art.");

                var paletteData = new PaletteData { Palette = pal, Cx = key.Item1, Cy = key.Item2 };
                foreach (var m in members)
                {
                    var outputPath = new FileInfo(Path.Combine(target.FullName, m.hash + ".pcc"));
                    try
                    {
                        EncodeImage(m.image, outputPath, m.psx, palette: null, verbose: false, reuseMetadataPalette: false, explicitPalette: paletteData);
                        converted++;
                        Console.WriteLine($"  + {m.image.Name} (hash {m.hash}) -> {outputPath.Name}  [new CLUT 0x{key.Item1:X},0x{key.Item2:X} x{members.Count}]");
                    }
                    catch (Exception ex) { failed++; Console.WriteLine($"  x {m.image.Name}: {ex.Message}"); }
                }
            }
        }
        else
        {
            foreach (var m in matched)
            {
                // Name the output by the PSX hash (decimal) so it drops straight into a
                // hash-indexed PSX archive / dir repacker.
                var outputPath = new FileInfo(Path.Combine(target.FullName, m.hash + ".pcc"));
                try
                {
                    EncodeImage(m.image, outputPath, m.psx, palette: null, verbose: false);
                    converted++;
                    Console.WriteLine($"  + {m.image.Name} (hash {m.hash}) -> {outputPath.Name}  (PSX original: {m.psx.Name})");
                }
                catch (Exception ex) { failed++; Console.WriteLine($"  x {m.image.Name}: {ex.Message}"); }
            }
        }

        Console.WriteLine($"\nPC->PSX convert complete: {converted} converted, {skipped} skipped (no PSX match), {failed} failed{(splitCluts ? " [split-cluts mode]" : newClut ? " [new-clut mode]" : "")} -> {target.FullName}");
    }

    // Builds ONE palette shared by a group of textures that occupy the same VRAM
    // CLUT. Collects the colours actually used across all of them (first-appearance
    // order); if that exceeds the CLUT capacity it keeps the most-frequent `cap`
    // colours (the rest get nearest-matched at encode time) and flags the shortfall.
    private static (List<SKColor> palette, int distinct, bool truncated) BuildGroupPalette(IEnumerable<SKBitmap> bitmaps, int cap)
    {
        var freq = new Dictionary<SKColor, long>();
        var order = new List<SKColor>();
        foreach (var bmp in bitmaps)
        {
            for (int x = 0; x < bmp.Width; x++)
                for (int y = 0; y < bmp.Height; y++)
                {
                    var c = bmp.GetPixel(x, y);
                    if (c.Alpha != 255) c = new SKColor(0, 0, 0);
                    if (!freq.ContainsKey(c)) { freq[c] = 0; order.Add(c); }
                    freq[c]++;
                }
        }

        var distinct = order.Count;
        if (distinct <= cap) return (order, distinct, false);

        // Over budget: quantize to `cap` colours with median-cut, which spreads the
        // palette across the whole colour range. Far better than keeping the N most
        // frequent (which drops minority regions — e.g. dark leg tones — to black).
        var counted = order.Select(c => (c, freq[c])).ToList();
        var pal = MedianCut(counted, cap);
        return (pal, distinct, true);
    }

    // Median-cut colour quantization: produces `cap` representative colours spread
    // across the group's actual colour range. Used only when a shared CLUT is over
    // budget (more distinct colours than the CLUT can hold).
    private static List<SKColor> MedianCut(List<(SKColor c, long n)> colors, int cap)
    {
        var boxes = new List<List<(SKColor c, long n)>> { colors };
        while (boxes.Count < cap)
        {
            int bestBox = -1, bestRange = -1;
            for (int i = 0; i < boxes.Count; i++)
            {
                if (boxes[i].Count < 2) continue;
                var (rr, gr, br) = ChannelRanges(boxes[i]);
                var m = Math.Max(rr, Math.Max(gr, br));
                if (m > bestRange) { bestRange = m; bestBox = i; }
            }
            if (bestBox < 0) break; // nothing splittable left

            var box = boxes[bestBox];
            var (rR, gR, bR) = ChannelRanges(box);
            Comparison<(SKColor c, long n)> cmp =
                (rR >= gR && rR >= bR) ? ((a, b) => a.c.Red.CompareTo(b.c.Red)) :
                (gR >= bR) ? ((a, b) => a.c.Green.CompareTo(b.c.Green)) :
                             ((a, b) => a.c.Blue.CompareTo(b.c.Blue));
            box.Sort(cmp);

            long total = box.Sum(e => e.n), half = total / 2, run = 0;
            int split = 1;
            for (int i = 0; i < box.Count; i++)
            {
                run += box[i].n;
                if (run >= half) { split = Math.Clamp(i + 1, 1, box.Count - 1); break; }
            }
            boxes[bestBox] = box.GetRange(0, split);
            boxes.Insert(bestBox + 1, box.GetRange(split, box.Count - split));
        }

        var palette = new List<SKColor>();
        foreach (var box in boxes)
        {
            long n = box.Sum(e => e.n);
            if (n == 0) { palette.Add(new SKColor(0, 0, 0)); continue; }
            long r = box.Sum(e => (long)e.c.Red * e.n) / n;
            long g = box.Sum(e => (long)e.c.Green * e.n) / n;
            long b = box.Sum(e => (long)e.c.Blue * e.n) / n;
            palette.Add(new SKColor((byte)r, (byte)g, (byte)b));
        }
        return palette;
    }

    private static (int r, int g, int b) ChannelRanges(List<(SKColor c, long n)> box)
    {
        int rmin = 255, rmax = 0, gmin = 255, gmax = 0, bmin = 255, bmax = 0;
        foreach (var (c, _) in box)
        {
            if (c.Red < rmin) rmin = c.Red; if (c.Red > rmax) rmax = c.Red;
            if (c.Green < gmin) gmin = c.Green; if (c.Green > gmax) gmax = c.Green;
            if (c.Blue < bmin) bmin = c.Blue; if (c.Blue > bmax) bmax = c.Blue;
        }
        return (rmax - rmin, gmax - gmin, bmax - bmin);
    }

    private static HashSet<SKColor> DistinctColours(SKBitmap bmp)
    {
        var set = new HashSet<SKColor>();
        for (int x = 0; x < bmp.Width; x++)
            for (int y = 0; y < bmp.Height; y++)
            {
                var c = bmp.GetPixel(x, y);
                if (c.Alpha != 255) c = new SKColor(0, 0, 0);
                set.Add(c);
            }
        return set;
    }

    // Exact union palette for a sub-group, in first-appearance order (deterministic).
    private static List<SKColor> OrderedColours(List<(FileInfo image, FileInfo psx, ushort hash)> members, Dictionary<string, HashSet<SKColor>> colsets)
    {
        var order = new List<SKColor>();
        var seen = new HashSet<SKColor>();
        foreach (var m in members)
            foreach (var c in colsets[m.image.FullName])
                if (seen.Add(c)) order.Add(c);
        return order;
    }

    // Finds free CLUT slots (clutWidth px wide) within the character's own CLUT band:
    // the VRAM rows its CLUTs already occupy, plus up to 3 rows below (which the game
    // reserves for that character's CLUTs), avoiding both existing CLUTs and image data.
    // Restricting to this band keeps new CLUTs in known-safe VRAM instead of unrelated
    // rows that merely look free in this one texture set.
    private static List<(ushort cx, ushort cy)> FindFreeClutSlots(IReadOnlyList<FileInfo> psxOriginals, int clutWidth, IReadOnlyCollection<ushort> characterRows)
    {
        var imgs = new List<(int x0, int y0, int x1, int y1)>();
        var cluts = new List<(int x0, int y0, int x1)>();
        var clutRows = new HashSet<int>();
        foreach (var f in psxOriginals)
        {
            PcxImage p;
            using (var fs = f.OpenRead())
            using (var r = new BinaryReader(fs)) p = r.ReadPcxImage();
            var h = p.Header;
            int w = h.WindowMax.X + 1, ht = h.WindowMax.Y + 1;
            int wwords = h.IsVga ? (w + 1) / 2 : (w + 3) / 4;   // VRAM is 16-bit words
            imgs.Add((h.Px, h.Py, h.Px + wwords, h.Py + ht));
            int cw = h.IsVga ? 256 : 16;
            cluts.Add((h.Cx, h.Cy, h.Cx + cw));
            clutRows.Add(h.Cy);
        }

        // candidate rows: existing CLUT rows within [charRow, charRow+3] of any character row
        var candidateRows = clutRows
            .Where(cy => characterRows.Any(cr => cy >= cr && cy <= cr + 3))
            .OrderBy(cy => cy).ToList();

        var free = new List<(ushort, ushort)>();
        foreach (var cy in candidateRows)
            for (int cx = 0x300; cx + clutWidth <= 0x400; cx += clutWidth)
            {
                bool clutHit = cluts.Any(c => c.y0 == cy && cx < c.x1 && cx + clutWidth > c.x0);
                bool imgHit = imgs.Any(im => cy >= im.y0 && cy < im.y1 && cx < im.x1 && cx + clutWidth > im.x0);
                if (!clutHit && !imgHit) free.Add(((ushort)cx, (ushort)cy));
            }
        return free;
    }

    private static void Pcc2PcxHandler(FileInfo source, FileInfo? target, FileInfo? metadata)
    {
        if (!source.Exists) throw new FileNotFoundException("Specified source image was not found", source.FullName);
        if (metadata == null || !metadata.Exists) throw new FileNotFoundException("A PC original (--pc/--metadata) is required and must exist; it supplies the PC header fields and target bit depth (EGA/VGA)", metadata?.FullName ?? "(none)");
        target ??= new FileInfo($"{Path.GetFileNameWithoutExtension(source.FullName)}.pcx");
        // reuseMetadataPalette: false -> keep the custom artwork's own colours.
        EncodeImage(source, target, metadata, palette: null, verbose: true, reuseMetadataPalette: false);
        Console.WriteLine($"Converted '{source.Name}' -> '{target.Name}' using PC original '{metadata.Name}' (source palette preserved)");
    }

    private static void BatchPcc2PcxHandler(DirectoryInfo source, DirectoryInfo target, DirectoryInfo? pc)
    {
        if (!source.Exists) throw new DirectoryNotFoundException("Specified source directory was not found");
        if (pc == null || !pc.Exists) throw new DirectoryNotFoundException("A directory of PC originals (--pc) is required and must exist");
        if (!target.Exists) target.Create();

        var images = source.EnumerateFiles("*.*")
            .Where(f => f.Extension.Equals(".pcc", StringComparison.OrdinalIgnoreCase)
                     || f.Extension.Equals(".pcx", StringComparison.OrdinalIgnoreCase)
                     || f.Extension.Equals(".png", StringComparison.OrdinalIgnoreCase)
                     || f.Extension.Equals(".bmp", StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (images.Count == 0) Console.WriteLine($"No .pcc/.pcx/.png/.bmp source images found in '{source.FullName}'");

        // Index PC originals by 16-bit MGS hash so a NUMBERED PSX source (56477.pcc)
        // finds its NAMED PC slot (sna_face.pcx). The output keeps the PC name.
        var pcByHash = new Dictionary<ushort, FileInfo>();
        var collisions = new List<string>();
        foreach (var orig in pc.EnumerateFiles("*.pcx"))
        {
            var h = StringExtensions.HashFromFileName(Path.GetFileNameWithoutExtension(orig.FullName));
            if (pcByHash.TryGetValue(h, out var existing)) collisions.Add($"{orig.Name} & {existing.Name} (hash {h})");
            else pcByHash[h] = orig;
        }
        if (collisions.Count > 0)
            Console.WriteLine($"Warning: {collisions.Count} hash collision(s) among PC originals, only the first of each is used: {string.Join("; ", collisions)}");

        int converted = 0, skipped = 0, failed = 0;
        foreach (var image in images)
        {
            var name = Path.GetFileNameWithoutExtension(image.FullName);
            var hash = StringExtensions.HashFromFileName(name);

            if (!pcByHash.TryGetValue(hash, out var pcOrig))
            {
                skipped++;
                Console.WriteLine($"  ~ {image.Name}: no PC original with hash {hash} in --pc dir — skipped");
                continue;
            }

            // PC archives are name-indexed: keep the PC original's name.
            var outputPath = new FileInfo(Path.Combine(target.FullName, Path.GetFileNameWithoutExtension(pcOrig.FullName) + ".pcx"));
            try
            {
                EncodeImage(image, outputPath, pcOrig, palette: null, verbose: false, reuseMetadataPalette: false);
                converted++;
                Console.WriteLine($"  + {image.Name} (hash {hash}) -> {outputPath.Name}  (PC original: {pcOrig.Name})");
            }
            catch (Exception ex)
            {
                failed++;
                Console.WriteLine($"  x {image.Name}: {ex.Message}");
            }
        }

        Console.WriteLine($"\nPSX->PC convert complete: {converted} converted, {skipped} skipped (no PC match), {failed} failed -> {target.FullName}");
    }

    // Loads any supported source into a bitmap. SkiaSharp cannot decode the MGS PCX
    // variant, so PCX/PCC files (PC or PSX — both carry magic 12345) are read with
    // our own parser; PNG/BMP go through Skia. This is what lets `encode` and
    // `pcx2pcc` accept a PC .pcx as input.
    private static SKBitmap LoadBitmap(FileInfo source)
    {
        if (IsMgsPcx(source))
        {
            using var fs = source.OpenRead();
            using var reader = new BinaryReader(fs);
            return reader.ReadPcxImage().AsBitmap();
        }

        var bmp = SKBitmap.Decode(source.FullName);
        if (bmp == null) throw new NotSupportedException($"Could not decode '{source.Name}' as an image");
        return bmp;
    }

    private static bool IsMgsPcx(FileInfo f)
    {
        var ext = f.Extension.ToLowerInvariant();
        if (ext is ".png" or ".bmp" or ".jpg" or ".jpeg" or ".gif" or ".tga") return false;
        try
        {
            using var fs = f.OpenRead();
            if (fs.Length < 0x4C) return false;
            if (fs.ReadByte() != 0x0A) return false;   // PCX manufacturer
            fs.Position = 0x4A;                          // MGS magic offset
            int lo = fs.ReadByte(), hi = fs.ReadByte();
            return (lo | (hi << 8)) == 12345;
        }
        catch { return false; }
    }

    /// <summary>
    /// Core encode path shared by `encode` and `batchencode`: decode the source
    /// bitmap, build an MGS PCX, optionally copy MGS-specific header fields from a
    /// metadata original (VRAM/CLUT position, flags), optionally apply a fixed
    /// palette, then write the result. This is exactly the single-file encode
    /// logic so batch output is byte-for-byte equivalent to encoding one at a time.
    /// </summary>
    private static void EncodeImage(FileInfo source, FileInfo target, FileInfo? metadata, FileInfo? palette, bool verbose, bool reuseMetadataPalette = true, PaletteData? explicitPalette = null)
    {
        if (!source.Exists) throw new FileNotFoundException("Specified bitmap was not found", source.FullName);

        PaletteData? colors = null;
        if (palette != null)
        {
            if (!palette.Exists) throw new FileNotFoundException("Specified palette file was not found", palette.FullName);
            using var paletteFile = palette.OpenRead();

            colors = JsonSerializer.Deserialize(paletteFile, MintJsonSerializerContext.Default.PaletteData) ?? throw new NotSupportedException("Failed to deserialize palette contents");
            if (colors.Palette.Count > 16) throw new NotSupportedException($"Palette contains {colors.Palette.Count} colors, only up to 16 are supported");
        }

        // An in-memory palette (e.g. a shared new CLUT built for a group) takes
        // precedence and is used verbatim.
        if (explicitPalette != null) colors = explicitPalette;

        var bitmap = LoadBitmap(source);

        // Read the metadata original FIRST so we can encode in the SAME bit depth
        // (EGA 16-color vs VGA 256-color). A VGA texture slot must stay VGA even if
        // the edit uses few colors, and an EGA slot must stay EGA.
        PcxImage? metaPcx = null;
        if (metadata != null)
        {
            if (!metadata.Exists) throw new FileNotFoundException("Specified metadata source PCX file was not found", metadata.FullName);
            using var metadataFile = metadata.OpenRead();
            using var reader = new BinaryReader(metadataFile);
            metaPcx = reader.ReadPcxImage();
        }
        bool? forceVga = metaPcx != null ? metaPcx.Header.IsVga : (bool?)null;

        // For texture swapping, reuse the ORIGINAL texture's CLUT verbatim (unless
        // the caller passed an explicit --palette). This preserves palette ORDER,
        // which MGS relies on because textures share CLUTs in VRAM — rebuilding the
        // palette in a different order corrupts every texture sharing that CLUT.
        // PSX->PC (reuseMetadataPalette = false) deliberately SKIPS this: the source
        // already carries the custom artwork's colours and we must keep them, so a
        // fresh palette is built from the source instead (the PC original is used
        // only for header fields + target bit depth, below).
        if (reuseMetadataPalette && colors == null && metaPcx != null)
        {
            var srcPal = (metaPcx.Header.IsVga && metaPcx.ExtendedPalette != null)
                ? metaPcx.ExtendedPalette.ToList()
                : metaPcx.Header.Palette.ToList();
            colors = new PaletteData { Palette = srcPal, Cx = metaPcx.Header.Cx, Cy = metaPcx.Header.Cy };
        }

        var pcx = PcxImage.FromBitmap(bitmap, colors, forceVga);

        if (metaPcx != null)
        {
            pcx.Header.Flags = metaPcx.Header.Flags;
            pcx.Header.Px = metaPcx.Header.Px;
            pcx.Header.Py = metaPcx.Header.Py;
            pcx.Header.Cx = metaPcx.Header.Cx;
            pcx.Header.Cy = metaPcx.Header.Cy;

            // Just to be sure
            pcx.Header.Padding = metaPcx.Header.Padding;
            pcx.Header.Reserved = metaPcx.Header.Reserved;
            // In fresh-palette mode the colour count comes from the source artwork,
            // so don't overwrite it with the original's.
            if (reuseMetadataPalette) pcx.Header.NColors = metaPcx.Header.NColors;

            if (pcx.Header.WindowMax != metaPcx.Header.WindowMax) Console.WriteLine($"Texture sizes differ ('{pcx.Header.WindowMax}' vs '{metaPcx.Header.WindowMax}'), this will most likely not work!");
        }
        else if (verbose)
        {
            Console.WriteLine("Creating basic PCX file with no MGS specific metadata");
            Console.WriteLine("Specify a --metadata original pcx file for texture swapping");
        }

        if (colors != null)
        {
            pcx.Header.Cx = colors.Cx;
            pcx.Header.Cy = colors.Cy;
        }

        // A shared new CLUT defines its own colour count.
        if (explicitPalette != null) pcx.Header.NColors = (ushort)explicitPalette.Palette.Count;

        using var file = target.Open(FileMode.Create);
        using var writer = new BinaryWriter(file);
        writer.Write(pcx);
    }

    private static void DecodeHandler(FileInfo source, FileInfo? target)
    {
        if (!source.Exists) throw new FileNotFoundException("Specified PCX image was not found", source.FullName);
        target ??= new FileInfo($"{Path.GetFileNameWithoutExtension(source.FullName)}.png");

        using var file = File.Open(source.FullName, FileMode.Open);
        using var reader = new BinaryReader(file);

        var pcx = reader.ReadPcxImage();
        var bitmap = pcx.AsBitmap();
        using var targetFile = target.Create();
        bitmap.Encode(targetFile, SKEncodedImageFormat.Png, 100);
    }

    private static void BatchDecodeHandler(DirectoryInfo source, DirectoryInfo target)
    {
        if (!source.Exists) throw new DirectoryNotFoundException("Specified source directory was not found");
        if (!target.Exists) throw new DirectoryNotFoundException("Specified target directory was not found");

        // PC textures are .pcx, PSX textures are .pcc -- the on-disk byte layout
        // is the same MGS PCX (manufacturer 0x0A, MgsMagic 12345), so the same
        // reader handles both; we just have to stop filtering .pcc out here.
        var files = source.EnumerateFiles("*.*")
            .Where(f => f.Extension.Equals(".pcx", StringComparison.OrdinalIgnoreCase)
                     || f.Extension.Equals(".pcc", StringComparison.OrdinalIgnoreCase));
        foreach (var file in files)
        {
            var fileName = Path.GetFileNameWithoutExtension(file.FullName);
            var outputPath = $"{Path.Combine(target.FullName, fileName)}{".png"}";

            using var stream = file.OpenRead();
            using var reader = new BinaryReader(stream);

            var pcx = reader.ReadPcxImage();
            var bitmap = pcx.AsBitmap();
            using var targetFile = File.Open(outputPath, FileMode.Create);
            bitmap.Encode(targetFile, SKEncodedImageFormat.Png, 100);
        }
    }

    /// <summary>
    /// Batch counterpart to `encode`. For every PNG/BMP in the source directory,
    /// finds the original .pcx/.pcc it was decoded from (by matching base name in
    /// the --metadata directory, defaulting to the source directory), copies that
    /// original's MGS metadata, and re-encodes to PCX/PCC in the target directory.
    /// This is the round-trip partner of `batchdecode`: decode a folder, edit the
    /// PNGs, then batchencode them straight back with correct per-texture metadata.
    /// </summary>
    private static void BatchEncodeHandler(DirectoryInfo source, DirectoryInfo target, DirectoryInfo? metadata, string? format)
    {
        if (!source.Exists) throw new DirectoryNotFoundException("Specified source directory was not found");
        if (!target.Exists) target.Create();

        // Where to look for the original pcx/pcc (for metadata + extension). If the
        // user doesn't pass --metadata, assume the originals sit in the source dir.
        var metaDir = metadata ?? source;
        if (!metaDir.Exists) throw new DirectoryNotFoundException("Specified metadata directory was not found");

        // Normalize a forced --format, if any.
        string? forcedExt = null;
        if (!string.IsNullOrWhiteSpace(format))
        {
            var fmt = format.Trim().TrimStart('.').ToLowerInvariant();
            if (fmt != "pcx" && fmt != "pcc") throw new NotSupportedException($"--format must be 'pcx' or 'pcc', got '{format}'");
            forcedExt = "." + fmt;
        }

        var images = source.EnumerateFiles("*.*")
            .Where(f => f.Extension.Equals(".png", StringComparison.OrdinalIgnoreCase)
                     || f.Extension.Equals(".bmp", StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (images.Count == 0) Console.WriteLine($"No .png or .bmp images found in '{source.FullName}'");

        int encoded = 0, withoutMeta = 0, failed = 0;
        foreach (var image in images)
        {
            var name = Path.GetFileNameWithoutExtension(image.FullName);

            // Find the matching original by base name: prefer .pcx, then .pcc.
            FileInfo? meta = null;
            var pcxCandidate = new FileInfo(Path.Combine(metaDir.FullName, name + ".pcx"));
            var pccCandidate = new FileInfo(Path.Combine(metaDir.FullName, name + ".pcc"));
            if (pcxCandidate.Exists) meta = pcxCandidate;
            else if (pccCandidate.Exists) meta = pccCandidate;

            // Output extension: forced --format wins; else follow the matched
            // original (.pcx -> .pcx, .pcc -> .pcc); else default to .pcx.
            string outExt = forcedExt ?? (meta != null ? meta.Extension.ToLowerInvariant() : ".pcx");
            var outputPath = new FileInfo(Path.Combine(target.FullName, name + outExt));

            try
            {
                EncodeImage(image, outputPath, meta, palette: null, verbose: false);
                encoded++;
                if (meta == null)
                {
                    withoutMeta++;
                    Console.WriteLine($"  ~ {image.Name} -> {outputPath.Name}  (NO matching original found — encoded without MGS metadata; texture may not work in-game)");
                }
                else
                {
                    Console.WriteLine($"  + {image.Name} -> {outputPath.Name}  (metadata: {meta.Name})");
                }
            }
            catch (Exception ex)
            {
                failed++;
                Console.WriteLine($"  x {image.Name}: {ex.Message}");
            }
        }

        Console.WriteLine($"Batch encode complete: {encoded} encoded" +
                          (withoutMeta > 0 ? $" ({withoutMeta} without metadata)" : "") +
                          (failed > 0 ? $", {failed} failed" : "") +
                          $" -> {target.FullName}");
    }

    private static void AnalyzeHandler(string source, bool verbose)
    {
        if (!Path.Exists(source)) throw new DirectoryNotFoundException("Specified source was not found");

        var texturesFiles = Directory.Exists(source) ? new DirectoryInfo(source).GetFiles() : [ new FileInfo(source) ];
        IEnumerable<(string, PcxHeader?)> textures = texturesFiles.Select(file =>
        {
            var name = Path.GetFileNameWithoutExtension(file.FullName);
            PcxHeader? header = null;
            try
            {
                using var stream = file.OpenRead();
                using var reader = new BinaryReader(stream);
                header = reader.ReadPcxImage().Header;
            }
            catch { }

            return (name, header);
        });
        if (verbose == false) Console.WriteLine("Name (hash) width x height @ pX x pY cX x cY (n colors)");

        var names = new string[] {
            "sna_arm1",
            "sna_arm2",
            "sna_boot",
            "sna_chest1",
            "sna_chest2",
            "sna_chest3",
            "sna_chest4",
            "sna_collar1",
            "sna_collar2",
            "sna_ear1",
            "sna_ear2",
            "sna_face",
            "sna_face2",
            "sna_face3",
            "sna_fin",
            "sna_fin2",
            "sna_fin3",
            "sna_hand",
            "sna_hand2",
            "sna_hed",
            "sna_hip1",
            "sna_hip2",
            "sna_leg1",
            "sna_leg2",
            "sna_leg3",
            "sna_leg4",
            "sna_neck",
            "sna_neck2"
        };

        foreach (var (name, header) in textures)
        {
            // Probably not a PCX image 
            if (header == null) continue;

            if (verbose)
            {
                Console.WriteLine($"{name}:");
                Console.WriteLine($"  Hash: {StringExtensions.GV_StrCode_80016CCC(name)}");
                Console.WriteLine($"  Size: {header.WindowMax.X + 1}x{header.WindowMax.Y + 1}");
                Console.WriteLine($"  VRAM Position: {header.Px}x{header.Py}");
                Console.WriteLine($"  CLUT Position: {header.Cx}x{header.Cy}");
                Console.WriteLine($"  Flags: {header.Flags}");
                Console.WriteLine($"  Color count: {header.NColors}");

                Console.WriteLine("  Palette:");
                for (int i = 0; i< header.NColors; i++)
                {
                    var color = header.Palette[i];
                    Console.Write($"    #{color.Red:X2}{color.Green:X2}{color.Blue:X2} ({color.Red}, {color.Green}, {color.Blue})");
                    
                    if (color.Red == 0 && color.Green == 0 && color.Blue == 0) Console.WriteLine(" | Transparent");
                    else Console.WriteLine();
                }
            }
            else
            {
                if (ushort.TryParse(name, out var hash))
                {
                    var readableName = names.FirstOrDefault(n => StringExtensions.GV_StrCode_80016CCC(n) == hash);
                    Console.WriteLine($"{readableName} ({name}): {header.WindowMax.X + 1}x{header.WindowMax.Y + 1} @ {header.Px}x{header.Py} {header.Cx}x{header.Cy} ({header.NColors} colors)");
                }
                else Console.WriteLine($"{name} ({StringExtensions.GV_StrCode_80016CCC(name)}): {header.WindowMax.X + 1}x{header.WindowMax.Y + 1} @ {header.Px}x{header.Py} {header.Cx}x{header.Cy} ({header.NColors} colors)");
            }
        }
    }
}
