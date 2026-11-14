using System.Globalization;
using System.Text.Json.Serialization;

namespace TSHR_AutoClock.Models;

/// <summary>The two preset slots. The slot is the stable key (shortcuts, saved selection); the preset's Name is just its label.</summary>
public enum PresetSlot
{
    Office,
    Home
}

/// <summary>A named static location. Latitude/Longitude are null until set.</summary>
public sealed record PresetLocation(string Name, double? Latitude, double? Longitude)
{
    [JsonIgnore]
    public bool IsSet => Latitude is { } lat && Longitude is { } lng && IsValidCoordinate(lat, lng);

    /// <summary>"lat, lng" for editing, or empty when unset.</summary>
    [JsonIgnore]
    public string CoordinatesText => IsSet
        ? $"{Latitude!.Value.ToString(CultureInfo.InvariantCulture)}, {Longitude!.Value.ToString(CultureInfo.InvariantCulture)}"
        : string.Empty;

    /// <summary>Parses "lat, lng" (as copied from Google Maps). Rejects out-of-range values and the unset 0, 0.</summary>
    public static bool TryParseCoordinates(string? input, out double lat, out double lng)
    {
        var parts = (input ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length != 2)
        {
            lat = lng = 0;
            return false;
        }
        return TryParseCoordinates(parts[0], parts[1], out lat, out lng);
    }

    public static bool TryParseCoordinates(string? latText, string? lngText, out double lat, out double lng)
    {
        lng = 0;
        return double.TryParse(latText, NumberStyles.Float, CultureInfo.InvariantCulture, out lat) &&
               double.TryParse(lngText, NumberStyles.Float, CultureInfo.InvariantCulture, out lng) &&
               IsValidCoordinate(lat, lng);
    }

    // 0, 0 is the "not set" placeholder, never a real preset
    public static bool IsValidCoordinate(double lat, double lng) =>
        lat is >= -90 and <= 90 && lng is >= -180 and <= 180 && !(lat == 0 && lng == 0);
}
