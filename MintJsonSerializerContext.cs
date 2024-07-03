using MetalMintSolid.Pcx;
using MetalMintSolid.Stg;
using MetalMintSolid.Util;
using System.Text.Json.Serialization;

namespace MetalMintSolid;

[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true, WriteIndented = true, Converters = [typeof(ColorJsonConverter)])]
[JsonSerializable(typeof(PaletteData))]
[JsonSerializable(typeof(StgRebuildInfo))]
internal partial class MintJsonSerializerContext : JsonSerializerContext { }
