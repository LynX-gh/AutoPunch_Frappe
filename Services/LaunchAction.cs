using System.Text;
using TSHR_AutoClock.Models;

namespace TSHR_AutoClock.Services;

/// <summary>
/// A clock action requested from outside the UI: an Android shortcut / autopunch:// link, or the Windows command line.
/// Slot null means "the preset selected in the app". Error is set when the request was malformed.
/// </summary>
public sealed record LaunchAction(string LogType, PresetSlot? Slot, string? Error = null)
{
    /// <summary>type: IN or OUT; preset: Office, Home or empty (all case-insensitive).</summary>
    public static LaunchAction Parse(string? type, string? preset)
    {
        var logType = type?.Trim().ToUpperInvariant();
        if (logType is not ("IN" or "OUT"))
        {
            return Invalid($"Unknown action '{type}'. Use IN or OUT.");
        }

        if (string.IsNullOrWhiteSpace(preset))
        {
            return new LaunchAction(logType, null);
        }

        // Match names only: Enum.TryParse would also accept numbers like "1"
        var name = Enum.GetNames<PresetSlot>().FirstOrDefault(n => n.Equals(preset.Trim(), StringComparison.OrdinalIgnoreCase));
        return name is null
            ? Invalid($"Unknown preset '{preset}'. Use {string.Join(" or ", Enum.GetNames<PresetSlot>())}.")
            : new LaunchAction(logType, Enum.Parse<PresetSlot>(name));
    }

    /// <summary>
    /// Reads "--clock IN|OUT [--preset Office|Home]" from command-line tokens (other tokens, like the exe path, are ignored).
    /// Null when there is no --clock, i.e. a normal launch.
    /// </summary>
    public static LaunchAction? FromCommandLine(IReadOnlyList<string> args)
    {
        string? type = null;
        string? preset = null;
        var hasClock = false;
        for (var i = 0; i < args.Count; i++)
        {
            var next = i + 1 < args.Count ? args[i + 1] : null;
            if (args[i].Equals("--clock", StringComparison.OrdinalIgnoreCase))
            {
                hasClock = true;
                type = next;
            }
            else if (args[i].Equals("--preset", StringComparison.OrdinalIgnoreCase))
            {
                preset = next;
            }
        }

        return hasClock ? Parse(type, preset) : null;
    }

    /// <summary>Splits a raw command line (as handed to an already-running instance) into tokens, honouring double quotes.</summary>
    public static List<string> SplitCommandLine(string? commandLine)
    {
        var tokens = new List<string>();
        var current = new StringBuilder();
        var inQuotes = false;
        foreach (var c in commandLine ?? string.Empty)
        {
            if (c == '"')
            {
                inQuotes = !inQuotes;
            }
            else if (char.IsWhiteSpace(c) && !inQuotes)
            {
                if (current.Length > 0)
                {
                    tokens.Add(current.ToString());
                    current.Clear();
                }
            }
            else
            {
                current.Append(c);
            }
        }
        if (current.Length > 0)
        {
            tokens.Add(current.ToString());
        }
        return tokens;
    }

    private static LaunchAction Invalid(string error) => new("IN", null, error);
}

/// <summary>
/// Holds a launch action until the main page can run it. Static because the Windows app posts the first one
/// before MAUI's services exist; later ones (from a second launch) raise Arrived.
/// </summary>
public static class LaunchActionQueue
{
    private static readonly object Gate = new();
    private static LaunchAction? _pending;

    /// <summary>Raised on the posting thread after an action is queued.</summary>
    public static event Action? Arrived;

    public static void Post(LaunchAction action)
    {
        lock (Gate)
        {
            _pending = action;
        }
        Arrived?.Invoke();
    }

    public static LaunchAction? Take()
    {
        lock (Gate)
        {
            var action = _pending;
            _pending = null;
            return action;
        }
    }
}
