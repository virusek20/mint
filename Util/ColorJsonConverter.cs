using System.Drawing;
using System.Text.Json.Serialization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace MetalMintSolid.Util;

public partial class ColorJsonConverter : JsonConverter<Color>
{
    private readonly Regex _rgbRegex = RgbRegex();

    public override Color Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var colorCode = reader.GetString() ?? throw new JsonException();

        var rgbMatch = _rgbRegex.Matches(colorCode);

        if (rgbMatch.Count != 0)
        {
            var components = rgbMatch[0].Groups.Values
                .Skip(1)
                .Select(v => int.Parse(v.Value))
                .ToList();

            return Color.FromArgb(255, components[0], components[1], components[2]);
        }
        else return ColorTranslator.FromHtml(colorCode);
    }

    public override void Write(Utf8JsonWriter writer, Color value, JsonSerializerOptions options) => writer.WriteStringValue($"#{value.R:X2}{value.G:X2}{value.B:X2}".ToLower());

    [GeneratedRegex("rgb\\((\\d{1,3}),\\s?(\\d{1,3}),\\s?(\\d{1,3})\\)")]
    private static partial Regex RgbRegex();
}
