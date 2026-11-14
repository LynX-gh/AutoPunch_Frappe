using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Maui.Controls;
using TSHR_AutoClock.Services;

namespace TSHR_AutoClock.Pages;

[QueryProperty(nameof(MissingFields), "missing")]
public partial class CredentialsPage : ContentPage
{
    static readonly Color MissingColor = Color.FromArgb("#55DC2626");

    /// <summary>Comma-separated field names ("domain", "username", "password") to highlight.</summary>
    public string? MissingFields { get; set; }

    private readonly HrmsService _hrmsService;
    private readonly CredentialStore _store;

    private List<SavedCredential> _credentials = new();
    private SavedCredential? _selected;   // the saved entry currently being viewed/edited; null = new entry
    private bool _suppressPicker;

    public CredentialsPage(HrmsService hrmsService, CredentialStore store)
    {
        InitializeComponent();
        _hrmsService = hrmsService;
        _store = store;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await LoadCredentialsAsync();
        UpdateUiState();
        HighlightMissingFields();
    }

    private void HighlightMissingFields()
    {
        var missing = (MissingFields ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries);
        MissingFields = null;
        if (missing.Length == 0) return;

        var targets = new[] { ("domain", DomainEntry), ("username", UsernameEntry), ("password", PasswordEntry) };
        Entry? first = null;
        foreach (var (name, entry) in targets)
        {
            if (!missing.Contains(name)) continue;
            entry.BackgroundColor = MissingColor;
            first ??= entry;
            entry.TextChanged += ClearHighlight;
        }

        StatusLabel.Text = $"Please enter: {string.Join(", ", missing)}.";
        first?.Focus();
    }

    private void ClearHighlight(object? sender, TextChangedEventArgs e)
    {
        if (sender is not Entry entry) return;
        entry.BackgroundColor = null;
        entry.TextChanged -= ClearHighlight;
    }

    private void UpdateUiState()
    {
        var loggedIn = _hrmsService.IsLoggedIn;

        ResultLabel.Text = loggedIn ? $"Logged in as: {_hrmsService.LoggedInUser}" : "Not logged in.";
        LoginButton.IsVisible = !loggedIn;
        LogoutButton.IsVisible = loggedIn;

        // Credentials can only be changed while logged out
        DomainEntry.IsEnabled = !loggedIn;
        UsernameEntry.IsEnabled = !loggedIn;
        PasswordEntry.IsEnabled = !loggedIn;
        SavedPicker.IsEnabled = !loggedIn;
        NewButton.IsEnabled = !loggedIn;
        SaveButton.IsEnabled = !loggedIn;
        DeleteButton.IsEnabled = !loggedIn && _selected is not null;
    }

    async Task LoadCredentialsAsync()
    {
        try
        {
            var active = await _store.GetActiveAsync();
            await RefreshPickerAsync(active);

            if (_selected is null && active is not null)
            {
                // Active credential isn't in the saved list: still show it
                FillEntries(active);
            }

            StatusLabel.Text = _credentials.Count == 0
                ? "No saved credentials yet. They are saved automatically after a successful login."
                : "Loaded saved credentials.";
        }
        catch (Exception)
        {
            StatusLabel.Text = "Could not load credentials.";
        }
    }

    /// <summary>Reloads the saved list into the picker and selects the entry matching <paramref name="select"/>, if any.</summary>
    private async Task RefreshPickerAsync(SavedCredential? select)
    {
        _credentials = await _store.GetAllAsync();

        _suppressPicker = true;
        SavedPicker.ItemsSource = _credentials.Select(c => c.DisplayName).ToList();
        var index = select is null ? -1 : _credentials.FindIndex(c => c.SameAccount(select));
        SavedPicker.SelectedIndex = index;
        _suppressPicker = false;

        _selected = index >= 0 ? _credentials[index] : null;
        if (_selected is not null)
        {
            FillEntries(_selected);
        }
        DeleteButton.IsEnabled = !_hrmsService.IsLoggedIn && _selected is not null;
    }

    private void FillEntries(SavedCredential credential)
    {
        DomainEntry.Text = credential.Domain;
        UsernameEntry.Text = credential.Username;
        PasswordEntry.Text = credential.Password;
    }

    void OnSavedSelected(object? sender, EventArgs e)
    {
        if (_suppressPicker || SavedPicker.SelectedIndex < 0 || SavedPicker.SelectedIndex >= _credentials.Count)
        {
            return;
        }

        _selected = _credentials[SavedPicker.SelectedIndex];
        FillEntries(_selected);
        DeleteButton.IsEnabled = !_hrmsService.IsLoggedIn;

        // It becomes the credential remembered for auto sync only after a successful login
        StatusLabel.Text = $"Selected {_selected.DisplayName}. Log in to use it.";
    }

    void OnNewClicked(object? sender, EventArgs e)
    {
        _selected = null;
        _suppressPicker = true;
        SavedPicker.SelectedIndex = -1;
        _suppressPicker = false;

        DomainEntry.Text = string.Empty;
        UsernameEntry.Text = string.Empty;
        PasswordEntry.Text = string.Empty;
        DeleteButton.IsEnabled = false;
        StatusLabel.Text = "Enter the details for a new credential.";
        DomainEntry.Focus();
    }

    async void OnSaveClicked(object? sender, EventArgs e)
    {
        var credential = SavedCredential.Normalize(DomainEntry.Text ?? string.Empty, UsernameEntry.Text ?? string.Empty, PasswordEntry.Text ?? string.Empty);

        try
        {
            var error = await _store.SaveAsync(credential, _selected);
            if (error is not null)
            {
                StatusLabel.Text = "Failed to save credentials.";
                await DisplayAlertAsync("Error", error, "OK");
                return;
            }

            await RefreshPickerAsync(credential);
            StatusLabel.Text = $"Saved {credential.DisplayName}.";
            await DisplayAlertAsync("Saved", "Credentials have been saved.", "OK");
        }
        catch (Exception ex)
        {
            StatusLabel.Text = "Failed to save credentials.";
            await DisplayAlertAsync("Error", $"Failed to save credentials: {ex.Message}", "OK");
        }
    }

    async void OnDeleteClicked(object? sender, EventArgs e)
    {
        if (_selected is null) return;

        var confirm = await DisplayAlertAsync("Delete credentials", $"Delete {_selected.DisplayName}?", "Delete", "Cancel");
        if (!confirm) return;

        try
        {
            await _store.DeleteAsync(_selected);
            await RefreshPickerAsync(null);
            OnNewClicked(null, EventArgs.Empty);
            StatusLabel.Text = "Credentials deleted.";
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("Error", $"Failed to delete credentials: {ex.Message}", "OK");
        }
    }

    async void OnLoginClicked(object? sender, EventArgs e)
    {
        var domain = DomainEntry.Text;
        var user = UsernameEntry.Text;
        var pass = PasswordEntry.Text;

        if (string.IsNullOrWhiteSpace(domain) || string.IsNullOrWhiteSpace(user) || string.IsNullOrWhiteSpace(pass))
        {
            await DisplayAlertAsync("Validation", "Please enter a valid Domain, Username, and Password.", "OK");
            return;
        }

        SetLoading(true);
        var (success, message) = await _hrmsService.LoginAndSyncAsync(domain, user, pass);

        // A valid login means the credentials are good, even if the follow-up sync failed: save/update them
        if (_hrmsService.IsLoggedIn)
        {
            var credential = SavedCredential.Normalize(domain, user, pass);
            var error = await _store.SaveAsync(credential, _selected, makeActive: true);
            if (error is not null)
            {
                message += $" (Not saved: {error})";
            }
            else
            {
                await RefreshPickerAsync(credential);
            }
        }
        SetLoading(false);

        await DisplayAlertAsync(success ? "Success" : "Failure", message, "OK");
        UpdateUiState();
    }

    async void OnLogoutClicked(object? sender, EventArgs e)
    {
        SetLoading(true);
        await _hrmsService.LogoutAsync();
        SetLoading(false);
        UpdateUiState();
        await DisplayAlertAsync("Logged Out", "You have been logged out.", "OK");
    }

    private void SetLoading(bool isLoading)
    {
        LoadingIndicator.IsRunning = isLoading;
        SaveButton.IsEnabled = !isLoading;
        LoginButton.IsEnabled = !isLoading;
        LogoutButton.IsEnabled = !isLoading;
    }
}
