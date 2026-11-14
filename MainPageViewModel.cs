using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using TSHR_AutoClock.Models;
using TSHR_AutoClock.Services;

namespace TSHR_AutoClock;

public class MainPageViewModel : INotifyPropertyChanged
{
    string statusText = "Not checked in";
    string lastActionText = "Last action: --";
    bool isLoading;
    bool isEditingLocation;

    public event PropertyChangedEventHandler? PropertyChanged;

    // VM is created from XAML (no DI), so resolve the services from the app's service provider
    readonly HrmsService? hrms = IPlatformApplication.Current?.Services.GetService<HrmsService>();
    readonly ClockService? clock = IPlatformApplication.Current?.Services.GetService<ClockService>();
    readonly PresetStore? presets = IPlatformApplication.Current?.Services.GetService<PresetStore>();

    public MainPageViewModel()
    {
        if (hrms is not null)
        {
            hrms.StatusChanged += () => MainThread.BeginInvokeOnMainThread(OnStatusChanged);
        }
        if (presets is not null)
        {
            presets.Changed += () => MainThread.BeginInvokeOnMainThread(OnPresetsChanged);
        }

        CheckInCommand = new Command(async () => await ClockAsync("IN"));
        CheckOutCommand = new Command(async () => await ClockAsync("OUT"));
        SyncCommand = new Command(async () => await OnSyncAsync());
        OpenCredentialsCommand = new Command(async () => await Shell.Current.GoToAsync(nameof(Pages.CredentialsPage)));

        // One row per slot, so adding a slot needs no UI change
        var slots = Enum.GetValues<PresetSlot>();
        Presets = new ObservableCollection<PresetItem>(slots.Select((slot, i) =>
            new PresetItem(slot, presets, new Command(async () => await EditLocationAsync(slot)), showDivider: i < slots.Length - 1)));
    }

    public string StatusText
    {
        get => statusText;
        set => SetProperty(ref statusText, value);
    }

    public string LastActionText
    {
        get => lastActionText;
        set => SetProperty(ref lastActionText, value);
    }

    /// <summary>The preset list on the main page.</summary>
    public ObservableCollection<PresetItem> Presets { get; }

    void OnPresetsChanged()
    {
        foreach (var item in Presets)
        {
            item.Refresh();
        }
    }

    public bool IsLoading
    {
        get => isLoading;
        set
        {
            if (isLoading == value) return;
            isLoading = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsLoading)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsNotLoading)));
            RaiseSuggestedActionChanged();
        }
    }

    public bool IsNotLoading => !isLoading;

    // Highlight the next valid action, but only when the status comes from a successful sync
    public bool IsCheckInSuggested => !isLoading && hrms is { IsStatusSynced: true, CurrentStatus: not { IsCheckedIn: true } };

    public bool IsCheckOutSuggested => !isLoading && hrms is { IsStatusSynced: true, CurrentStatus.IsCheckedIn: true };

    void RaiseSuggestedActionChanged()
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsCheckInSuggested)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsCheckOutSuggested)));
    }

    public ICommand CheckInCommand { get; }

    public ICommand CheckOutCommand { get; }

    public ICommand SyncCommand { get; }

    public ICommand OpenCredentialsCommand { get; }

    // Clocks with the given slot's preset, or the selected one
    async Task ClockAsync(string logType, PresetSlot? slot = null)
    {
        if (IsLoading)
        {
            return;
        }

        if (clock is null)
        {
            await ShowErrorAsync("Clock unavailable: clock service not found.");
            return;
        }

        try
        {
            IsLoading = true;
            await ReportAsync(await clock.ClockAsync(logType, slot), interactive: true);
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>Runs a clock action requested from outside the UI (the Windows command line).</summary>
    public async Task RunLaunchActionAsync(LaunchAction action)
    {
        if (action.Error is not null)
        {
            await ShowErrorAsync(action.Error, "Invalid launch arguments");
            return;
        }
        if (IsLoading)
        {
            await ShowErrorAsync("Another action is already running. Try again when it finishes.", "Busy");
            return;
        }

        await ClockAsync(action.LogType, action.Slot);
    }

    /// <summary>Runs once when the main page first appears: syncs only if a session or saved credentials exist.</summary>
    public Task AutoSyncAsync() => OnSyncAsync(interactive: false);

    async Task OnSyncAsync(bool interactive = true)
    {
        if (IsLoading)
        {
            return;
        }

        if (clock is null)
        {
            if (interactive)
            {
                await ShowErrorAsync("Sync unavailable: clock service not found.");
            }
            return;
        }

        try
        {
            IsLoading = true;
            await ReportAsync(await clock.SyncAsync(), interactive);
        }
        finally
        {
            IsLoading = false;
        }
    }

    // Done is quiet (the status card shows it). Missing credentials open Settings, except on the quiet startup sync.
    async Task ReportAsync(ClockResult result, bool interactive)
    {
        switch (result.Outcome)
        {
            case ClockOutcome.Done:
                return;
            case ClockOutcome.NeedsSettings:
                if (interactive)
                {
                    await OpenSettingsForMissingAsync(result.Missing ?? []);
                }
                return;
            default:
                await ShowErrorAsync(result.Message, result.Title);
                return;
        }
    }

    Task OpenSettingsForMissingAsync(IReadOnlyList<string> missing) =>
        Shell.Current.GoToAsync(nameof(Pages.CredentialsPage),
            new Dictionary<string, object> { ["missing"] = string.Join(',', missing) });

    void OnStatusChanged()
    {
        RaiseSuggestedActionChanged();

        var checkin = hrms?.CurrentStatus;
        if (checkin is null)
        {
            StatusText = "Not checked in";
            LastActionText = "Last action: --";
            return;
        }

        StatusText = checkin.IsCheckedIn ? "Checked in" : "Checked out";
        LastActionText = $"Last action: Check {(checkin.IsCheckedIn ? "in" : "out")} at {checkin.Time:g}";
    }

    async Task ShowErrorAsync(string message, string title = "Error")
    {
        LastActionText = $"Last action: {message}";
        await MainThread.InvokeOnMainThreadAsync(() =>
            Shell.Current.DisplayAlertAsync(title, message, "OK"));
    }

    // Opens the edit form for the slot; nothing changes unless it is saved with valid values
    async Task EditLocationAsync(PresetSlot slot)
    {
        // A quick double tap would otherwise stack two forms
        if (isEditingLocation)
        {
            return;
        }

        if (presets is null)
        {
            await ShowErrorAsync("Locations unavailable: preset store not found.");
            return;
        }

        isEditingLocation = true;
        try
        {
            var form = new Pages.LocationEditPage(presets.Get(slot), IPlatformApplication.Current?.Services.GetService<LocationResolver>());
            await Shell.Current.Navigation.PushModalAsync(form);
            if (await form.Result is not { } edited)
            {
                return;
            }

            await presets.SaveAsync(slot, edited);
            LastActionText = $"Last action: Updated {edited.Name} at {DateTime.Now:t}";
        }
        catch (Exception ex)
        {
            await ShowErrorAsync($"Could not save locations: {ex.Message}");
        }
        finally
        {
            isEditingLocation = false;
        }
    }

    void SetProperty<T>(ref T backingStore, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(backingStore, value))
        {
            return;
        }

        backingStore = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

/// <summary>One row of the main page's preset list: radio to select, tap to edit.</summary>
public class PresetItem : INotifyPropertyChanged
{
    readonly PresetStore? presets;

    public PresetItem(PresetSlot slot, PresetStore? presets, ICommand editCommand, bool showDivider)
    {
        Slot = slot;
        this.presets = presets;
        EditCommand = editCommand;
        ShowDivider = showDivider;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public PresetSlot Slot { get; }

    PresetLocation Preset => presets?.Get(Slot) ?? new PresetLocation(Slot.ToString(), null, null);

    public string Name => Preset.Name;

    public string CoordinatesText => Preset.IsSet ? Preset.CoordinatesText : "Not set. Tap to add a location.";

    public bool IsSelected
    {
        get => (presets?.SelectedSlot ?? PresetSlot.Office) == Slot;
        set { if (value && presets is not null) presets.SelectedSlot = Slot; }
    }

    public ICommand EditCommand { get; }

    /// <summary>A line under every row except the last.</summary>
    public bool ShowDivider { get; }

    public void Refresh()
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Name)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CoordinatesText)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
    }
}
