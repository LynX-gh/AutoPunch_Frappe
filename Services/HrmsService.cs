using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;
using TSHR_AutoClock.Models;
using System;
using System.IO;

namespace TSHR_AutoClock.Services;

public class HrmsService
{
    private readonly HttpClient _client;
    private readonly CookieContainer _cookieContainer;
    // Older versions kept the session cookies in this plain file; it is migrated into SecureStorage and deleted
    private readonly string _legacyCookieFilePath;

    private const string EmployeeIdKey = "hrms_employee_id";
    private const string SessionKey = "hrms_session_cookies";
    private const string SessionExpiredMessage = "Session expired. Please log in again.";
    // Same keys CredentialsPage uses to save settings in SecureStorage
    public const string DomainKey = "hrms_domain";
    public const string UsernameKey = "hrms_username";
    public const string PasswordKey = "hrms_password";

    public string? LoggedInUser { get; private set; }
    public bool IsLoggedIn => !string.IsNullOrEmpty(LoggedInUser);
    public string? EmployeeId { get; private set; } = Preferences.Get(EmployeeIdKey, null);

    /// <summary>Latest synced check-in; null until a sync finds one (or after logout).</summary>
    public LastCheckin? CurrentStatus { get; private set; }

    /// <summary>True only while CurrentStatus comes from a successful sync; false after a failed login or sync, or logout.</summary>
    public bool IsStatusSynced { get; private set; }
    public event Action? StatusChanged;

    // Domain/username of the current login. Settings may not be saved yet, so prefer these over SecureStorage.
    private string? _sessionDomain;
    private string? _sessionUsername;

    public HrmsService()
    {
        _cookieContainer = new CookieContainer();
        var handler = new HttpClientHandler { CookieContainer = _cookieContainer };
        _client = new HttpClient(handler);
        _legacyCookieFilePath = Path.Combine(FileSystem.AppDataDirectory, "session.cookies");

        // Synchronous so IsLoggedIn is already correct when the first page reads it
        LoadCookies();
    }

    public async Task<(bool Success, string Message)> LoginAsync(string domain, string username, string password)
    {
        var result = await LoginCoreAsync(domain, username, password);
        if (!result.Success)
        {
            MarkSyncFailed();
        }
        return result;
    }

    private async Task<(bool Success, string Message)> LoginCoreAsync(string domain, string username, string password)
    {
        try
        {
            domain = domain.Trim().TrimEnd('/');
            var content = new MultipartFormDataContent
            {
                { new StringContent("login"), "cmd" },
                { new StringContent(username), "usr" },
                { new StringContent(password), "pwd" }
            };

            var response = await _client.PostAsync($"{domain}/login", content);

            if (response.IsSuccessStatusCode)
            {
                var cookies = _cookieContainer.GetCookies(new Uri(domain));
                var fullNameCookie = cookies["full_name"];
                if (fullNameCookie != null)
                {
                    LoggedInUser = Uri.UnescapeDataString(fullNameCookie.Value);
                    await SaveCookiesAsync(domain);

                    _sessionDomain = domain;
                    _sessionUsername = username;

                    var (empOk, empMessage) = await FetchEmployeeIdAsync(domain, username);
                    return empOk
                        ? (true, $"Welcome, {LoggedInUser}")
                        : (false, $"Employee lookup failed: {empMessage}");
                }
                return (false, "Login successful, but full name not found in cookies.");
            }
            else
            {
                return (false, $"Login failed. Status: {response.StatusCode}");
            }
        }
        catch (Exception ex)
        {
            return (false, $"An error occurred: {ex.Message}");
        }
    }

    /// <summary>
    /// Looks up the Employee record linked to the user and stores its ID (e.g. "35").
    /// Uses the logged-in session cookies.
    /// </summary>
    public async Task<(bool Success, string Message)> FetchEmployeeIdAsync(string domain, string username)
    {
        try
        {
            domain = domain.Trim().TrimEnd('/');
            var filters = JsonSerializer.Serialize(new { user_id = username });
            var url = $"{domain}/api/method/frappe.client.get_value" +
                      $"?doctype=Employee&fieldname=name&filters={Uri.EscapeDataString(filters)}";

            var response = await _client.GetAsync(url);
            if (!response.IsSuccessStatusCode)
            {
                return (false, $"Status: {response.StatusCode}");
            }

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("message", out var message) ||
                message.ValueKind != JsonValueKind.Object ||
                !message.TryGetProperty("name", out var name) ||
                string.IsNullOrWhiteSpace(name.GetString()))
            {
                return (false, $"No employee found for user '{username}'.");
            }

            EmployeeId = name.GetString();
            Preferences.Set(EmployeeIdKey, EmployeeId);
            return (true, EmployeeId!);
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    public async Task LogoutAsync()
    {
        ClearSession();
        EmployeeId = null;
        Preferences.Remove(EmployeeIdKey);
        SetCurrentStatus(null, synced: false);
        await Task.CompletedTask;
    }

    /// <summary>
    /// Forgets the server session: every in-memory cookie (after a restart the session domain isn't known, the cookies
    /// came from storage) and the stored copy. IsLoggedIn becomes false, so the next action logs in fresh.
    /// </summary>
    private void ClearSession()
    {
        foreach (Cookie cookie in _cookieContainer.GetAllCookies())
        {
            cookie.Expired = true;
        }
        LoggedInUser = null;
        _sessionDomain = null;
        _sessionUsername = null;

        SecureStorage.Remove(SessionKey);
        try
        {
            File.Delete(_legacyCookieFilePath);
        }
        catch (Exception)
        {
            // Nothing to delete, or not deletable; it is never read once SecureStorage has a session
        }
    }

    private async Task SaveCookiesAsync(string domain)
    {
        var cookies = _cookieContainer.GetCookies(new Uri(domain)).Cast<Cookie>();
        var cookieDtos = cookies.Select(c => new CookieDto
        {
            Name = c.Name,
            Value = c.Value,
            Domain = c.Domain,
            Path = c.Path,
            Expires = c.Expires,
            Secure = c.Secure
        }).ToList();

        var json = JsonSerializer.Serialize(cookieDtos);
        await SecureStorage.SetAsync(SessionKey, json);
    }

    private void LoadCookies()
    {
        try
        {
            // SecureStorage is async-only; Task.Run keeps the blocking wait off the UI thread's context
            var json = Task.Run(() => SecureStorage.GetAsync(SessionKey)).GetAwaiter().GetResult();
            if (json is null && File.Exists(_legacyCookieFilePath))
            {
                json = File.ReadAllText(_legacyCookieFilePath);
                Task.Run(() => SecureStorage.SetAsync(SessionKey, json)).GetAwaiter().GetResult();
                File.Delete(_legacyCookieFilePath);
            }
            if (string.IsNullOrEmpty(json)) return;

            var cookieDtos = JsonSerializer.Deserialize<List<CookieDto>>(json);

            if (cookieDtos == null) return;

            foreach (var dto in cookieDtos)
            {
                if (dto.Expires > DateTime.UtcNow)
                {
                    _cookieContainer.Add(new Uri($"https://{dto.Domain}"), new Cookie(dto.Name, dto.Value, dto.Path, dto.Domain));
                    if (dto.Name == "full_name")
                    {
                        LoggedInUser = Uri.UnescapeDataString(dto.Value);
                    }
                }
            }
        }
        catch (Exception)
        {
            // Corrupt or unreadable session: start logged out, the next sync logs in with saved credentials
            ClearSession();
        }
    }

    /// <summary>Log in, resolve the employee ID, then sync the current status. Stops at the first failure.</summary>
    public async Task<(bool Success, string Message)> LoginAndSyncAsync(string domain, string username, string password)
    {
        var (loggedIn, loginMessage) = await LoginAsync(domain, username, password);
        if (!loggedIn)
        {
            return (false, loginMessage);
        }

        var (synced, syncMessage, _) = await GetLastCheckinAsync();
        return synced ? (true, loginMessage) : (false, syncMessage);
    }

    private void SetCurrentStatus(LastCheckin? checkin, bool synced = true)
    {
        CurrentStatus = checkin;
        IsStatusSynced = synced;
        StatusChanged?.Invoke();
    }

    // Keeps the last known status for display, but it is no longer trusted for suggesting the next action
    private void MarkSyncFailed()
    {
        if (!IsStatusSynced)
        {
            return;
        }

        IsStatusSynced = false;
        StatusChanged?.Invoke();
    }

    /// <summary>
    /// Checks the session is usable: logged in, domain from settings (or current login) and employee ID resolved.
    /// Returns the domain, or an error message.
    /// </summary>
    private async Task<(string? Domain, string? Error)> PrepareSessionAsync()
    {
        if (!IsLoggedIn)
        {
            return (null, "Not logged in. Please log in from Settings.");
        }

        var domain = (_sessionDomain ?? await SecureStorage.GetAsync(DomainKey) ?? string.Empty).Trim().TrimEnd('/');
        if (string.IsNullOrEmpty(domain))
        {
            return (null, "No domain saved. Please set it in Settings.");
        }

        if (string.IsNullOrEmpty(EmployeeId))
        {
            var username = _sessionUsername ?? await SecureStorage.GetAsync(UsernameKey) ?? string.Empty;
            var (ok, msg) = await FetchEmployeeIdAsync(domain, username);
            if (!ok)
            {
                return (null, $"Employee lookup failed: {msg}");
            }
        }

        return (domain, null);
    }

    /// <summary>
    /// Creates an Employee Checkin ("IN" or "OUT") at the given static coordinates, stamped with the current time.
    /// </summary>
    public async Task<(bool Success, string Message)> ClockAsync(string logType, double latitude, double longitude)
    {
        // A failed post may or may not have been recorded, so the synced status can't be trusted until the next sync
        var result = await ClockCoreAsync(logType, latitude, longitude);
        if (!result.Success)
        {
            MarkSyncFailed();
        }
        return result;
    }

    private async Task<(bool Success, string Message)> ClockCoreAsync(string logType, double latitude, double longitude)
    {
        try
        {
            var (domain, error) = await PrepareSessionAsync();
            if (domain is null)
            {
                return (false, error!);
            }

            var doc = JsonSerializer.Serialize(new
            {
                doctype = "Employee Checkin",
                employee = EmployeeId,
                log_type = logType,
                time = DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture),
                latitude,
                longitude
            });

            var body = new FormUrlEncodedContent(new[] { new KeyValuePair<string, string>("doc", doc) });
            var response = await _client.PostAsync($"{domain}/api/method/frappe.client.insert", body);
            var responseText = await response.Content.ReadAsStringAsync();

            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                ClearSession();
                return (false, SessionExpiredMessage);
            }
            if (!response.IsSuccessStatusCode)
            {
                return (false, $"Check {(logType == "IN" ? "in" : "out")} failed. Status: {response.StatusCode}{ExtractServerMessage(responseText)}");
            }

            using var json = JsonDocument.Parse(responseText);
            if (!json.RootElement.TryGetProperty("message", out var message) ||
                message.ValueKind != JsonValueKind.Object ||
                !message.TryGetProperty("name", out _))
            {
                return (false, "Unexpected response from server. Check-in may not have been recorded.");
            }

            return (true, logType == "IN" ? "Checked in." : "Checked out.");
        }
        catch (Exception ex)
        {
            return (false, $"Clock error: {ex.Message}");
        }
    }

    // Frappe reports validation errors in "_server_messages" (a JSON string of JSON strings); best-effort extract
    private static string ExtractServerMessage(string responseText)
    {
        try
        {
            using var doc = JsonDocument.Parse(responseText);
            if (doc.RootElement.TryGetProperty("_server_messages", out var sm) && sm.GetString() is { } raw)
            {
                var first = JsonSerializer.Deserialize<List<string>>(raw)?.FirstOrDefault();
                if (first is not null)
                {
                    using var inner = JsonDocument.Parse(first);
                    if (inner.RootElement.TryGetProperty("message", out var m) && m.GetString() is { } text)
                    {
                        return $" - {text}";
                    }
                }
            }
        }
        catch
        {
            // Not a Frappe error payload; ignore
        }
        return string.Empty;
    }

    /// <summary>
    /// Fetches the employee's most recent Employee Checkin record from TSHR.
    /// Uses the domain saved in app settings and the logged-in session.
    /// Returns a null LastCheckin when the employee has no check-ins yet.
    /// </summary>
    public async Task<(bool Success, string Message, LastCheckin? Checkin)> GetLastCheckinAsync()
    {
        var result = await GetLastCheckinCoreAsync();
        if (!result.Success)
        {
            MarkSyncFailed();
        }
        return result;
    }

    private async Task<(bool Success, string Message, LastCheckin? Checkin)> GetLastCheckinCoreAsync()
    {
        try
        {
            var (domain, error) = await PrepareSessionAsync();
            if (domain is null)
            {
                return (false, error!, null);
            }

            var filters = JsonSerializer.Serialize(new { employee = EmployeeId });
            var fields = JsonSerializer.Serialize(new[] { "log_type", "time" });
            var url = $"{domain}/api/method/frappe.desk.reportview.get_list" +
                      $"?filters={Uri.EscapeDataString(filters)}" +
                      $"&fields={Uri.EscapeDataString(fields)}" +
                      $"&order_by={Uri.EscapeDataString("time desc")}" +
                      "&limit=1" +
                      $"&doctype={Uri.EscapeDataString("Employee Checkin")}";

            var response = await _client.GetAsync(url);
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                ClearSession();
                return (false, SessionExpiredMessage, null);
            }
            if (!response.IsSuccessStatusCode)
            {
                return (false, $"Sync failed. Status: {response.StatusCode}", null);
            }

            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            if (!doc.RootElement.TryGetProperty("message", out var message) ||
                message.ValueKind != JsonValueKind.Array)
            {
                return (false, "Unexpected response from server.", null);
            }

            if (message.GetArrayLength() == 0)
            {
                SetCurrentStatus(null);
                return (true, "No check-ins found.", null);
            }

            var item = message[0];
            var logType = item.GetProperty("log_type").GetString();
            var timeText = item.GetProperty("time").GetString();
            if (!string.Equals(logType, "IN", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(logType, "OUT", StringComparison.OrdinalIgnoreCase))
            {
                return (false, $"Unexpected log type '{logType}' from server.", null);
            }
            if (!DateTime.TryParse(timeText, CultureInfo.InvariantCulture, DateTimeStyles.None, out var time))
            {
                return (false, $"Could not parse check-in time '{timeText}'.", null);
            }

            var checkin = new LastCheckin(logType!, time);
            SetCurrentStatus(checkin);
            return (true, "Synced.", checkin);
        }
        catch (Exception ex)
        {
            return (false, $"Sync error: {ex.Message}", null);
        }
    }
}

/// <summary>Most recent Employee Checkin: LogType is "IN" or "OUT"; Time is as returned by the server.</summary>
public record LastCheckin(string LogType, DateTime Time)
{
    public bool IsCheckedIn => string.Equals(LogType, "IN", StringComparison.OrdinalIgnoreCase);
}
