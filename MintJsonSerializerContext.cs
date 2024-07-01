using MetalMintSolid.Pcx;
using MetalMintSolid.Util;
using System.Text.Json.Serialization;

namespace MetalMintSolid;

[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true, Converters = [typeof(ColorJsonConverter)])]
[JsonSerializable(typeof(PaletteData))]
internal partial class MintJsonSerializerContext : JsonSerializerContext { }
