using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Hotline.Core.Windowing;

/// <summary>A width in scaled pixels or a percentage of the monitor work area.</summary>
[JsonConverter(typeof(PanelWidthConverter))]
public readonly record struct PanelWidth
{
    public double Value { get; }
    public bool IsPercent { get; }

    public PanelWidth(double value, bool isPercent = false)
    {
        if (!double.IsFinite(value) || value < (isPercent ? 20 : 200) || value > (isPercent ? 90 : 4000))
            throw new ArgumentOutOfRangeException(nameof(value));
        // A pixel width is whole: a fraction of a scaled pixel shows nothing, and the saved spot compares widths exactly.
        (Value, IsPercent) = (isPercent ? value : Math.Round(value), isPercent);
    }

    public static bool TryParse(string? text, out PanelWidth width)
    {
        width = default;
        text = text?.Trim();
        if (string.IsNullOrEmpty(text)) return false;
        var percent = text.EndsWith('%');
        var number = percent ? text[..^1] : text;
        if (!double.TryParse(number, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value)
            || !double.IsFinite(value) || value < (percent ? 20 : 200) || value > (percent ? 90 : 4000)) return false;
        width = new(value, percent);
        return true;
    }

    public override string ToString() => Value.ToString("0.################", CultureInfo.InvariantCulture) + (IsPercent ? "%" : "");
    public static implicit operator PanelWidth(int pixels) => new(pixels);

    /// <summary>The minimum drag width in real pixels for the monitor.</summary>
    public static int MinimumPixels(Settings.WindowSettings settings, double scale) =>
        (int)Math.Round(Math.Max(200, settings.MinWidth) * scale);

    /// <summary>Opening or chat width in real pixels. Null chat width means no growth.</summary>
    public static int ResolvePixels(Settings.WindowSettings settings, int workAreaPixels, double scale, bool chat = false)
    {
        if (!double.IsFinite(scale) || scale <= 0) scale = 1;
        var width = chat ? settings.ChatWidth ?? settings.Width : settings.Width;
        var work = Math.Max(0, workAreaPixels) / scale;
        var dips = width.IsPercent ? work * width.Value / 100 : width.Value;
        dips = Math.Clamp(dips, settings.MinWidth, Math.Max(settings.MinWidth, settings.MaxWidth));
        return (int)Math.Round(Math.Min(work, dips) * scale);
    }
}

public sealed class PanelWidthConverter : JsonConverter<PanelWidth>
{
    public override PanelWidth Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Number && reader.TryGetDouble(out var value) && value is >= 200 and <= 4000)
            return new(value);
        if (reader.TokenType == JsonTokenType.String && reader.GetString() is { } text && text.EndsWith('%')
            && PanelWidth.TryParse(text, out var width)) return width;
        throw new JsonException("Expected scaled pixels (200-4000) or a percentage (20%-90%).");
    }

    public override void Write(Utf8JsonWriter writer, PanelWidth value, JsonSerializerOptions options)
    {
        if (value.IsPercent) writer.WriteStringValue(value.ToString());
        else writer.WriteNumberValue(value.Value);
    }
}
