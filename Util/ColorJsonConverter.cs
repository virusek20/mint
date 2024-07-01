using System.Text.Json.Serialization;
using System.Text.Json;
using System.Text.RegularExpressions;
using SkiaSharp;

namespace MetalMintSolid.Util;

public partial class ColorJsonConverter : JsonConverter<SKColor>
{
    private readonly Regex _rgbRegex = RgbRegex();

    public override SKColor Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var colorCode = reader.GetString() ?? throw new JsonException();

        var rgbMatch = _rgbRegex.Matches(colorCode);

        if (rgbMatch.Count != 0)
        {
            var components = rgbMatch[0].Groups.Values
                .Skip(1)
                .Select(v => int.Parse(v.Value))
                .ToList();

            return new SKColor((byte)components[0], (byte)components[1], (byte)components[2]);
        }
        else
        {
            if (SKColor.TryParse(colorCode, out var color)) return color;
            else throw new NotSupportedException($"Cannot decode color '{colorCode}'");
        }
    }

    public override void Write(Utf8JsonWriter writer, SKColor value, JsonSerializerOptions options) => writer.WriteStringValue($"#{value.Red:X2}{value.Green:X2}{value.Blue:X2}".ToLower());

    [GeneratedRegex("rgb\\((\\d{1,3}),\\s?(\\d{1,3}),\\s?(\\d{1,3})\\)")]
    private static partial Regex RgbRegex();
}
