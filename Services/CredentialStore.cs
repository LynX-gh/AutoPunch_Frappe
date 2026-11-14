using System.Text.Json;

namespace TSHR_AutoClock.Services;

/// <summary>A saved HRMS login. Unique by domain + username (case-insensitive).</summary>
public record SavedCredential(string Domain, string Username, string Password)
{
    public string DisplayName => $"{Username} @ {Domain}";

    public bool SameAccount(SavedCredential other) =>
        string.Equals(Domain, other.Domain, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(Username, other.Username, StringComparison.OrdinalIgnoreCase);

    public static SavedCredential Normalize(string domain, string username, string password) =>
        new(domain.Trim().TrimEnd('/'), username.Trim(), password);
}

/// <summary>
/// Keeps the list of saved credentials in SecureStorage. The "active" one is mirrored into the
/// single domain/username/password keys that the main page and HrmsService already read.
/// </summary>
public class CredentialStore
{
    private const string ListKey = "hrms_credentials";

    public async Task<List<SavedCredential>> GetAllAsync()
    {
        var json = await SecureStorage.GetAsync(ListKey);
        if (!string.IsNullOrEmpty(json))
        {
            try
            {
                return JsonSerializer.Deserialize<List<SavedCredential>>(json) ?? new();
            }
            catch (JsonException)
            {
                return new();
            }
        }

        // First run after upgrade: import the single credential saved by earlier versions
        var active = await GetActiveAsync();
        if (active is not null)
        {
            var list = new List<SavedCredential> { active };
            await SaveAllAsync(list);
            return list;
        }

        return new();
    }

    /// <summary>The credential currently mirrored into the single-credential keys, if complete.</summary>
    public async Task<SavedCredential?> GetActiveAsync()
    {
        var domain = await SecureStorage.GetAsync(HrmsService.DomainKey);
        var username = await SecureStorage.GetAsync(HrmsService.UsernameKey);
        var password = await SecureStorage.GetAsync(HrmsService.PasswordKey);
        if (string.IsNullOrWhiteSpace(domain) || string.IsNullOrWhiteSpace(username) || string.IsNullOrEmpty(password))
        {
            return null;
        }
        return SavedCredential.Normalize(domain, username, password);
    }

    /// <summary>
    /// Adds the credential, or updates the password if the domain + username already exist.
    /// When <paramref name="replacing"/> is given, that entry is edited in place (domain/username may change).
    /// Returns an error message, or null on success.
    /// The active credential (the last one that logged in successfully, used for auto sync on reopen) only changes when
    /// <paramref name="makeActive"/> is true, or when the entry being saved already is the active one.
    /// </summary>
    public async Task<string?> SaveAsync(SavedCredential credential, SavedCredential? replacing = null, bool makeActive = false)
    {
        var error = await SaveCoreAsync(credential, replacing);
        if (error is not null)
        {
            return error;
        }

        var active = await GetActiveAsync();
        if (makeActive || (active is not null && active.SameAccount(replacing ?? credential)))
        {
            await SetActiveAsync(credential);
        }
        return null;
    }

    private async Task<string?> SaveCoreAsync(SavedCredential credential, SavedCredential? replacing)
    {
        if (string.IsNullOrWhiteSpace(credential.Domain) || string.IsNullOrWhiteSpace(credential.Username) || string.IsNullOrEmpty(credential.Password))
        {
            return "Domain, username and password are all required.";
        }

        var list = await GetAllAsync();

        if (replacing is not null)
        {
            var index = list.FindIndex(c => c.SameAccount(replacing));
            if (index >= 0)
            {
                // Editing must not collide with a different saved entry
                if (list.Where((_, i) => i != index).Any(c => c.SameAccount(credential)))
                {
                    return $"{credential.DisplayName} is already saved.";
                }
                list[index] = credential;
                await SaveAllAsync(list);
                return null;
            }
        }

        var existing = list.FindIndex(c => c.SameAccount(credential));
        if (existing >= 0)
        {
            list[existing] = credential;
        }
        else
        {
            list.Add(credential);
        }

        await SaveAllAsync(list);
        return null;
    }

    public async Task DeleteAsync(SavedCredential credential)
    {
        var list = await GetAllAsync();
        list.RemoveAll(c => c.SameAccount(credential));
        await SaveAllAsync(list);

        // Deleting the active credential clears it, so nothing logs in with it by accident
        var active = await GetActiveAsync();
        if (active is not null && active.SameAccount(credential))
        {
            SecureStorage.Remove(HrmsService.DomainKey);
            SecureStorage.Remove(HrmsService.UsernameKey);
            SecureStorage.Remove(HrmsService.PasswordKey);
        }
    }

    public async Task SetActiveAsync(SavedCredential credential)
    {
        await SecureStorage.SetAsync(HrmsService.DomainKey, credential.Domain);
        await SecureStorage.SetAsync(HrmsService.UsernameKey, credential.Username);
        await SecureStorage.SetAsync(HrmsService.PasswordKey, credential.Password);
    }

    private Task SaveAllAsync(List<SavedCredential> list) =>
        SecureStorage.SetAsync(ListKey, JsonSerializer.Serialize(list));
}
