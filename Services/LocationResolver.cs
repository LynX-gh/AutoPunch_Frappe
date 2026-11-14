using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using TSHR_AutoClock.Models;

namespace TSHR_AutoClock.Services;

/// <summary>Where a pasted link or code points. Message says how it was found (or why it wasn't).</summary>
public sealed record ResolvedLocation(bool Success, double Latitude, double Longitude, string Message);

/// <summary>
/// Turns a Google Maps share link (maps.app.goo.gl/..., google.com/maps/...) or a Plus Code into coordinates.
/// Links: follow the redirects and read the coordinates from the address, or from the final page if the address has none.
/// Full Plus Codes decode offline; short ones ("2HCV+X8 Ahmedabad") look the town up on OpenStreetMap (Nominatim).
/// </summary>
public partial class LocationResolver
{
    // Redirects are followed by hand so every hop's address can be checked for coordinates
    private static readonly HttpClient Http = new(new HttpClientHandler { AllowAutoRedirect = false })
    {
        Timeout = TimeSpan.FromSeconds(15)
    };

    // Google serves the full page (with the place's coordinates) to browsers
    private const string BrowserUserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0.0.0 Safari/537.36";
    // Nominatim's usage policy asks apps to identify themselves
    private const string AppUserAgent = "AutoPunch/1.0 (com.autopunch.app)";
    private const int MaxRedirects = 10;

    // Links that only name a place (e.g. ?q=Name&ftid=...) carry no coordinates, and Google's page only loads them with
    // JavaScript (its preview coordinates are the viewer's area, not the place), so ask for a link that has them
    private const string CantReadLink =
        "This link only names the place, so it has no coordinates. In Google Maps, long-press the exact spot to drop a pin " +
        "and share that, or copy the place's Plus Code.";

    // Query parameters Google uses for a searched/destination point
    private static readonly string[] PointParameters = ["q", "query", "ll", "sll", "destination", "daddr", "center"];
    // Parameters that wrap another URL (consent page, dynamic links)
    private static readonly string[] WrappedUrlParameters = ["continue", "link", "url"];

    [GeneratedRegex(@"!3d(-?\d{1,2}(?:\.\d+)?)!4d(-?\d{1,3}(?:\.\d+)?)")]
    private static partial Regex PlacePinPattern();

    [GeneratedRegex(@"@(-?\d{1,2}\.\d+),(-?\d{1,3}\.\d+)")]
    private static partial Regex ViewportPattern();

    [GeneratedRegex(@"^\s*(-?\d{1,2}(?:\.\d+)?)\s*,\s*\+?\s*(-?\d{1,3}(?:\.\d+)?)\s*$")]
    private static partial Regex CoordinatePairPattern();

    [GeneratedRegex(@"/maps/(?:search|place|dir)/(?:[^/]*/)*?(-?\d{1,2}\.\d+),\+?\s*(-?\d{1,3}\.\d+)(?:/|$)")]
    private static partial Regex PathCoordinatesPattern();

    [GeneratedRegex(@"https?://\S+", RegexOptions.IgnoreCase)]
    private static partial Regex UrlPattern();

    [GeneratedRegex(@"(?:maps\.app\.goo\.gl|goo\.gl/maps|(?:www\.)?google\.[a-z.]+/maps|maps\.google\.[a-z.]+)\S*", RegexOptions.IgnoreCase)]
    private static partial Regex BareMapsUrlPattern();

    /// <summary>True when the text holds a link or Plus Code worth resolving (not plain coordinates).</summary>
    public static bool LooksResolvable(string? input) => FindUrl(input) is not null || PlusCode.Find(input) is not null;

    public async Task<ResolvedLocation> ResolveAsync(string input, CancellationToken cancellationToken = default)
    {
        try
        {
            // Google's share text is often "Place name\nAddress\nhttps://maps.app.goo.gl/..."
            if (FindUrl(input) is { } url)
            {
                return await FromLinkAsync(url, cancellationToken);
            }

            if (PlusCode.Find(input) is { } plusCode)
            {
                return await FromPlusCodeAsync(plusCode.Code, plusCode.Locality, cancellationToken);
            }

            return Fail("Paste coordinates (lat, lng), a Google Maps link or a Plus Code.");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Fail("Timed out. Check your internet connection and try again.");
        }
        catch (HttpRequestException ex)
        {
            return Fail($"Couldn't reach the server: {ex.Message}");
        }
    }

    private static Uri? FindUrl(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return null;
        }

        var text = UrlPattern().Match(input) is { Success: true } full
            ? full.Value
            : BareMapsUrlPattern().Match(input) is { Success: true } bare ? "https://" + bare.Value : null;
        text = text?.TrimEnd('.', ',', ')', ']', '"', '\'');
        return Uri.TryCreate(text, UriKind.Absolute, out var uri) ? uri : null;
    }

    private async Task<ResolvedLocation> FromLinkAsync(Uri url, CancellationToken cancellationToken)
    {
        var current = url;
        for (var hop = 0; hop <= MaxRedirects; hop++)
        {
            switch (ReadUrl(current))
            {
                case { Coordinates: { } found }:
                    return found;
                case { PlusCodeText: { } plusCodeText } when PlusCode.Find(plusCodeText) is { } plusCode:
                    return await FromPlusCodeAsync(plusCode.Code, plusCode.Locality, cancellationToken);
            }

            using var request = new HttpRequestMessage(HttpMethod.Get, current);
            request.Headers.UserAgent.ParseAdd(BrowserUserAgent);
            request.Headers.AcceptLanguage.ParseAdd("en");
            using var response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

            if ((int)response.StatusCode is >= 300 and < 400 && response.Headers.Location is { } location)
            {
                current = location.IsAbsoluteUri ? location : new Uri(current, location);
                continue;
            }
            if (!response.IsSuccessStatusCode)
            {
                return Fail($"Google Maps answered {(int)response.StatusCode} for this link. Check that the link is complete.");
            }

            // Final address has no coordinates: the page may still carry the place's pin data
            var html = await response.Content.ReadAsStringAsync(cancellationToken);
            return PlacePinPattern().Match(html) is { Success: true } pin && Found(pin, "Found the place in Google Maps.") is { } inPage
                ? inPage
                : Fail(CantReadLink);
        }

        return Fail("The link redirected too many times.");
    }

    /// <summary>Coordinates in a Maps address, or text that may hold a Plus Code (q=2HCV+X8 Ahmedabad).</summary>
    private static (ResolvedLocation? Coordinates, string? PlusCodeText) ReadUrl(Uri uri)
    {
        var decoded = Uri.UnescapeDataString(uri.AbsoluteUri);

        // The place's own pin, as in /maps/place/Name/@viewport/data=...!3d23.02!4d72.55
        if (PlacePinPattern().Match(decoded) is { Success: true } pin && Found(pin, "Found the place in the Google Maps link.") is { } pinned)
        {
            return (pinned, null);
        }

        var query = ParseQuery(uri.Query);
        string? plusCodeText = null;
        foreach (var name in PointParameters)
        {
            if (!query.TryGetValue(name, out var value))
            {
                continue;
            }
            if (CoordinatePairPattern().Match(value) is { Success: true } pair && Found(pair, "Found the point in the Google Maps link.") is { } point)
            {
                return (point, null);
            }
            if (PlusCode.Find(value) is not null)
            {
                plusCodeText ??= value;
            }
        }

        // /maps/search/23.0228,+72.5556 (a dropped pin)
        var path = Uri.UnescapeDataString(uri.AbsolutePath);
        if (PathCoordinatesPattern().Match(path) is { Success: true } inPath && Found(inPath, "Found the point in the Google Maps link.") is { } pathPoint)
        {
            return (pathPoint, null);
        }
        // /maps/place/2HCV+X8+Ahmedabad,+Gujarat/@...: only that segment, so the town isn't followed by the rest of the path
        plusCodeText ??= path.Split('/').FirstOrDefault(segment => PlusCode.Find(segment) is not null);
        if (plusCodeText is not null)
        {
            return (null, plusCodeText);
        }

        // A consent page or dynamic link wrapping the real Maps address
        foreach (var name in WrappedUrlParameters)
        {
            if (query.TryGetValue(name, out var wrapped) && Uri.TryCreate(wrapped, UriKind.Absolute, out var inner) && ReadUrl(inner) is { } innerResult &&
                (innerResult.Coordinates is not null || innerResult.PlusCodeText is not null))
            {
                return innerResult;
            }
        }

        // Last resort: the centre of the shared map view, which may be off from the actual spot
        if (ViewportPattern().Match(decoded) is { Success: true } viewport &&
            Found(viewport, "Approximate: this is the centre of the shared map view. Check the pin.") is { } centre)
        {
            return (centre, null);
        }

        return (null, null);
    }

    private async Task<ResolvedLocation> FromPlusCodeAsync(string code, string? locality, CancellationToken cancellationToken)
    {
        if (PlusCode.IsFull(code))
        {
            var (lat, lng) = PlusCode.Decode(code);
            return Ok(lat, lng, $"Decoded Plus Code {code}.");
        }

        if (locality is null)
        {
            return Fail($"{code} is a short Plus Code. Add the town after it (e.g. {code} Ahmedabad), or copy the full code from Google Maps.");
        }

        if (await GeocodeAsync(locality, cancellationToken) is not { } reference)
        {
            return Fail($"Couldn't find '{locality}'. Check the town name, or copy the full Plus Code from Google Maps.");
        }

        var (latitude, longitude) = PlusCode.RecoverNearest(code, reference.Latitude, reference.Longitude);
        return Ok(latitude, longitude, $"Plus Code {code} near {locality}.");
    }

    // Town centre from OpenStreetMap's free search (no key; one request per lookup is within its usage policy)
    private static async Task<(double Latitude, double Longitude)?> GeocodeAsync(string place, CancellationToken cancellationToken)
    {
        var url = $"https://nominatim.openstreetmap.org/search?format=jsonv2&limit=1&q={Uri.EscapeDataString(place)}";
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.UserAgent.ParseAdd(AppUserAgent);
        using var response = await Http.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        if (doc.RootElement.ValueKind != JsonValueKind.Array || doc.RootElement.GetArrayLength() == 0)
        {
            return null;
        }

        var first = doc.RootElement[0];
        return double.TryParse(first.GetProperty("lat").GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var lat) &&
               double.TryParse(first.GetProperty("lon").GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var lng)
            ? (lat, lng)
            : null;
    }

    private static Dictionary<string, string> ParseQuery(string query)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var part in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = part.IndexOf('=');
            var name = eq < 0 ? part : part[..eq];
            // In a query, '+' is a space and a real '+' (as in a Plus Code) arrives as %2B
            var value = eq < 0 ? string.Empty : Uri.UnescapeDataString(part[(eq + 1)..].Replace('+', ' '));
            result.TryAdd(Uri.UnescapeDataString(name), value);
        }
        return result;
    }

    private static ResolvedLocation? Found(Match match, string message) =>
        double.TryParse(match.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var lat) &&
        double.TryParse(match.Groups[2].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var lng) &&
        PresetLocation.IsValidCoordinate(lat, lng)
            ? Ok(lat, lng, message)
            : null;

    private static ResolvedLocation Ok(double latitude, double longitude, string message) =>
        new(true, Math.Round(latitude, 7), Math.Round(longitude, 7), message);

    private static ResolvedLocation Fail(string message) => new(false, 0, 0, message);
}
