namespace MetalMintSolid.Stg;

public record StgRebuildInfo
{
    public required byte Field0 { get; set; }
    public required byte Field1 { get; set; }
    public required List<StgConfigRebuildInfo> Configs { get; set; }
}

public record StgConfigRebuildInfo
{
    public required string Filename { get; set; }
    public required ushort Hash { get; set; }
    public required byte Mode { get; set; }
    public required byte Extension { get; set; }
}
