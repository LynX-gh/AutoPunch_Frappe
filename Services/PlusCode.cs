using System.Text;
using System.Text.RegularExpressions;

namespace TSHR_AutoClock.Services;

/// <summary>
/// Google Maps Plus Codes (Open Location Code): finding one in text, decoding full codes offline,
/// and recovering short codes ("2HCV+X8") from a nearby reference point.
/// Follows the reference algorithm at https://github.com/google/open-location-code.
/// </summary>
public static partial class PlusCode
{
    private const string Alphabet = "23456789CFGHJMPQRVWX";
    private const int SeparatorPosition = 8;
    private const int PairCodeLength = 10;
    private const int MaxDigits = 15;
    // Degrees covered by each pair digit: 20, 1, 1/20, 1/400, 1/8000
    private static readonly double[] PairResolutions = [20.0, 1.0, 0.05, 0.0025, 0.000125];

    // 2-8 digits, '+', then 2+ digits; not glued to other letters/digits
    [GeneratedRegex(@"(?<![0-9A-Za-z+])([23456789CFGHJMPQRVWXcfghjmpqrvwx]{2,8}\+[23456789CFGHJMPQRVWXcfghjmpqrvwx]{2,7})(?![0-9A-Za-z])")]
    private static partial Regex CodePattern();

    /// <summary>
    /// Finds a Plus Code in text such as "2HCV+X8 Ahmedabad, Gujarat". Locality is the text after the code
    /// (null when there is none). Returns null when there is no valid code.
    /// </summary>
    public static (string Code, string? Locality)? Find(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var match = CodePattern().Match(text);
        if (!match.Success)
        {
            return null;
        }

        var code = match.Value.ToUpperInvariant();
        var separator = code.IndexOf('+');
        // Digits before '+' come in pairs; a full code has 8 of them and a valid first pair
        if (separator % 2 != 0 || (separator == SeparatorPosition && !IsValidFirstPair(code)))
        {
            return null;
        }

        // Google writes the town after the code ("2HCV+X8 Ahmedabad, Gujarat"); from URLs, '+' often stands for a space
        var locality = text[(match.Index + match.Length)..].Replace('+', ' ').Trim(' ', ',', '\n', '\r', '\t');
        return (code, locality.Length == 0 ? null : locality);
    }

    public static bool IsFull(string code) => code.IndexOf('+') == SeparatorPosition;

    /// <summary>Centre of a full code's area.</summary>
    public static (double Latitude, double Longitude) Decode(string fullCode)
    {
        var digits = fullCode.ToUpperInvariant().Replace("+", string.Empty);
        double lat = -90, lng = -180, latSize = 0, lngSize = 0;

        var i = 0;
        for (; i < Math.Min(digits.Length, PairCodeLength); i += 2)
        {
            var resolution = PairResolutions[i / 2];
            lat += Alphabet.IndexOf(digits[i]) * resolution;
            lng += Alphabet.IndexOf(digits[i + 1]) * resolution;
            latSize = lngSize = resolution;
        }

        // Grid refinement after 10 digits: each digit splits the cell into 5 rows x 4 columns
        for (; i < Math.Min(digits.Length, MaxDigits); i++)
        {
            latSize /= 5;
            lngSize /= 4;
            var d = Alphabet.IndexOf(digits[i]);
            lat += d / 4 * latSize;
            lng += d % 4 * lngSize;
        }

        return (Math.Clamp(lat + latSize / 2, -90, 90), NormalizeLongitude(lng + lngSize / 2));
    }

    /// <summary>Centre of the area a short code (e.g. "2HCV+X8") names nearest to the reference point.</summary>
    public static (double Latitude, double Longitude) RecoverNearest(string shortCode, double referenceLatitude, double referenceLongitude)
    {
        var code = shortCode.ToUpperInvariant();
        var paddingLength = SeparatorPosition - code.IndexOf('+');
        var resolution = Math.Pow(20, 2 - paddingLength / 2);
        var halfResolution = resolution / 2;

        referenceLatitude = Math.Clamp(referenceLatitude, -90, 90);
        referenceLongitude = NormalizeLongitude(referenceLongitude);

        // Borrow the missing leading digits from the reference, then shift by one cell if that lands too far away
        var (lat, lng) = Decode(EncodePairs(referenceLatitude, referenceLongitude, paddingLength) + code);
        if (referenceLatitude + halfResolution < lat && lat - resolution >= -90)
        {
            lat -= resolution;
        }
        else if (referenceLatitude - halfResolution > lat && lat + resolution <= 90)
        {
            lat += resolution;
        }
        if (referenceLongitude + halfResolution < lng)
        {
            lng -= resolution;
        }
        else if (referenceLongitude - halfResolution > lng)
        {
            lng += resolution;
        }

        return (lat, NormalizeLongitude(lng));
    }

    // The first `length` (even, <= 10) pair digits of the code for a point
    private static string EncodePairs(double latitude, double longitude, int length)
    {
        // Latitude 90 would encode past the last row
        var lat = Math.Min(latitude + 90, 180 - 1e-10);
        var lng = NormalizeLongitude(longitude) + 180;
        var sb = new StringBuilder(length);
        for (var i = 0; i < length / 2; i++)
        {
            var resolution = PairResolutions[i];
            var latDigit = Math.Clamp((int)Math.Floor(lat / resolution), 0, Alphabet.Length - 1);
            var lngDigit = Math.Clamp((int)Math.Floor(lng / resolution), 0, Alphabet.Length - 1);
            lat -= latDigit * resolution;
            lng -= lngDigit * resolution;
            sb.Append(Alphabet[latDigit]).Append(Alphabet[lngDigit]);
        }
        return sb.ToString();
    }

    // First latitude digit covers 0..8 (9 x 20 = 180 degrees), first longitude digit 0..17 (360 degrees)
    private static bool IsValidFirstPair(string code) =>
        Alphabet.IndexOf(code[0]) <= 8 && Alphabet.IndexOf(code[1]) <= 17;

    private static double NormalizeLongitude(double longitude)
    {
        while (longitude < -180) longitude += 360;
        while (longitude >= 180) longitude -= 360;
        return longitude;
    }
}
