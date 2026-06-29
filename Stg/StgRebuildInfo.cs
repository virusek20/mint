namespace MetalMintSolid.Stg;

public record StgRebuildInfo
{
    public required byte Field0 { get; set; }
    public required byte Field1 { get; set; }

    /// <summary>
    /// The stage's original header size in sectors, captured on extract.
    /// Most stages declare exactly the size of their on-disk content, but a few
    /// system stages (e.g. "order") declare far MORE sectors than they physically
    /// occupy — the surplus is a load-time reservation hint, not real data.
    /// Recomputing the header purely from content would silently shrink those
    /// stages and drop the reservation, so on pack we keep whichever is larger:
    /// the freshly-computed content size, or this original declaration.
    /// Nullable so older rebuild.json files (without it) still load.
    /// </summary>
    public int? OriginalSize { get; set; }

    public required List<StgConfigRebuildInfo> Configs { get; set; }
}

public record StgConfigRebuildInfo
{
    public required string Filename { get; set; }
    public required ushort Hash { get; set; }
    public required byte Mode { get; set; }
    public required byte Extension { get; set; }
}
