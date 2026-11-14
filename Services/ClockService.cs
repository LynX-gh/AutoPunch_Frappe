using TSHR_AutoClock.Models;

namespace TSHR_AutoClock.Services;

public enum ClockOutcome
{
    /// <summary>Worked; the status card already shows it.</summary>
    Done,
    /// <summary>The check-in/out was recorded, but confirming it afterwards failed.</summary>
    Warning,
    /// <summary>Nothing was posted on purpose (already in/out, or another action is running).</summary>
    Skipped,
    Failed,
    /// <summary>Credentials are missing; Missing lists the fields ("domain", "username", "password").</summary>
    NeedsSettings
}

public sealed record ClockResult(ClockOutcome Outcome, string Message, string Title = "Error", IReadOnlyList<string>? Missing = null);

/// <summary>
/// Sync and clock flows without any UI, shared by the main page and the Android/Windows triggers.
/// Logs in with the remembered credentials when needed, and once more if the stored session turns out to be expired.
/// </summary>
public class ClockService
{
    private readonly HrmsService _hrms;
    private readonly PresetStore _presets;

    // One action at a time across the app: a trigger can arrive while the UI is busy
    private readonly SemaphoreSlim _gate = new(1, 1);

    private static readonly ClockResult Busy =
        new(ClockOutcome.Skipped, "Another action is already running. Try again when it finishes.", "Busy");

    public ClockService(HrmsService hrms, PresetStore presets)
    {
        _hrms = hrms;
        _presets = presets;
    }

    /// <summary>Fetches the latest status, logging in first if needed.</summary>
    public async Task<ClockResult> SyncAsync()
    {
        if (!await _gate.WaitAsync(0))
        {
            return Busy;
        }

        try
        {
            return await WithReloginAsync(SyncCoreAsync);
        }
        catch (Exception ex)
        {
            return new ClockResult(ClockOutcome.Failed, $"Sync error: {ex.Message}");
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Posts "IN" or "OUT" at the slot's preset (null = the selected preset), but only when it is the opposite of the
    /// latest status on the server: Frappe would happily accept duplicate IN/INs.
    /// </summary>
    public async Task<ClockResult> ClockAsync(string logType, PresetSlot? slot = null)
    {
        if (!await _gate.WaitAsync(0))
        {
            return Busy;
        }

        try
        {
            var preset = _presets.Get(slot ?? _presets.SelectedSlot);
            return await WithReloginAsync(() => ClockCoreAsync(logType, preset));
        }
        catch (Exception ex)
        {
            return new ClockResult(ClockOutcome.Failed, $"Clock error: {ex.Message}");
        }
        finally
        {
            _gate.Release();
        }
    }

    // HrmsService logs out when the server rejects the session (401/403). If that happened to a session we started
    // with, log in fresh once and run the whole attempt again; a fresh login that still fails is reported as is.
    private async Task<ClockResult> WithReloginAsync(Func<Task<ClockResult>> attempt)
    {
        var hadSession = _hrms.IsLoggedIn;
        var result = await attempt();
        if (result.Outcome == ClockOutcome.Failed && hadSession && !_hrms.IsLoggedIn)
        {
            result = await attempt();
        }
        return result;
    }

    private async Task<ClockResult> SyncCoreAsync()
    {
        if (await EnsureLoggedInAsync() is { } loginProblem)
        {
            return loginProblem;
        }

        var (synced, message, _) = await _hrms.GetLastCheckinAsync();
        return new ClockResult(synced ? ClockOutcome.Done : ClockOutcome.Failed, message);
    }

    private async Task<ClockResult> ClockCoreAsync(string logType, PresetLocation preset)
    {
        // Coordinates are validated when saved, so a set preset is ready to send
        if (preset is not { IsSet: true, Latitude: { } lat, Longitude: { } lng })
        {
            return new ClockResult(ClockOutcome.Failed, $"Set a valid {preset.Name} location before clocking.");
        }

        if (await EnsureLoggedInAsync() is { } loginProblem)
        {
            return loginProblem;
        }

        // Always check the latest server status first
        var (fetched, fetchMessage, latest) = await _hrms.GetLastCheckinAsync();
        if (!fetched)
        {
            return new ClockResult(ClockOutcome.Failed, fetchMessage);
        }

        var wantsIn = logType == "IN";
        if (wantsIn && latest is { IsCheckedIn: true })
        {
            return new ClockResult(ClockOutcome.Skipped, $"Already checked in (since {latest.Time:g}).", "Check in skipped");
        }
        if (!wantsIn && latest is not { IsCheckedIn: true })
        {
            return new ClockResult(ClockOutcome.Skipped, "You're not checked in, so there's nothing to check out of.", "Check out skipped");
        }

        var (ok, message) = await _hrms.ClockAsync(logType, lat, lng);
        if (!ok)
        {
            return new ClockResult(ClockOutcome.Failed, message);
        }

        // Refresh status from the server to confirm what was recorded
        var (synced, syncMessage, _) = await _hrms.GetLastCheckinAsync();
        return synced
            ? new ClockResult(ClockOutcome.Done, $"{(wantsIn ? "Checked in" : "Checked out")} at {preset.Name}.", "Done")
            : new ClockResult(ClockOutcome.Warning, $"{message} But sync failed: {syncMessage}", "Recorded, not confirmed");
    }

    /// <summary>Null when logged in (logging in with the remembered credentials if needed); otherwise the problem.</summary>
    private async Task<ClockResult?> EnsureLoggedInAsync()
    {
        if (_hrms.IsLoggedIn)
        {
            return null;
        }

        var domain = await SecureStorage.GetAsync(HrmsService.DomainKey);
        var username = await SecureStorage.GetAsync(HrmsService.UsernameKey);
        var password = await SecureStorage.GetAsync(HrmsService.PasswordKey);

        var missing = new List<string>();
        if (string.IsNullOrWhiteSpace(domain)) missing.Add("domain");
        if (string.IsNullOrWhiteSpace(username)) missing.Add("username");
        if (string.IsNullOrWhiteSpace(password)) missing.Add("password");
        if (missing.Count > 0)
        {
            return new ClockResult(ClockOutcome.NeedsSettings, $"Log in from Settings first (missing: {string.Join(", ", missing)}).", "Setup needed", missing);
        }

        var (ok, message) = await _hrms.LoginAsync(domain!, username!, password!);
        return ok ? null : new ClockResult(ClockOutcome.Failed, message);
    }
}
