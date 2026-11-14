using System.Globalization;
using TSHR_AutoClock.Models;
using TSHR_AutoClock.Services;

namespace TSHR_AutoClock.Pages;

/// <summary>
/// Modal form for a preset's name and coordinates. Push it, then await <see cref="Result"/>:
/// the edited preset on Save, or null on Cancel/back.
/// The location field also takes a Google Maps link or Plus Code (Find turns it into coordinates),
/// and valid coordinates are shown on an OpenStreetMap preview.
/// </summary>
public partial class LocationEditPage : ContentPage
{
    readonly TaskCompletionSource<PresetLocation?> _result = new();
    readonly LocationResolver? _resolver;
    readonly CancellationTokenSource _closing = new();

    CancellationTokenSource? _mapDelay;
    (double Lat, double Lng)? _shownOnMap;
    bool _busy;
    bool _settingCoordinates;

    public Task<PresetLocation?> Result => _result.Task;

    public LocationEditPage(PresetLocation current, LocationResolver? resolver)
    {
        InitializeComponent();
        _resolver = resolver;
        NameEntry.Text = current.Name;
        CoordinatesEntry.Text = current.CoordinatesText;
        if (current is { IsSet: true, Latitude: { } lat, Longitude: { } lng })
        {
            ShowMap(lat, lng);
        }
    }

    async void OnSaveClicked(object? sender, EventArgs e)
    {
        if (_busy)
        {
            return;
        }

        var name = NameEntry.Text?.Trim() ?? string.Empty;
        if (name.Length == 0)
        {
            ShowError("Enter a name for this location.");
            NameEntry.Focus();
            return;
        }

        if (!PresetLocation.TryParseCoordinates(CoordinatesEntry.Text, out var lat, out var lng))
        {
            // A link or Plus Code: turn it into coordinates first, so the pin can be checked before saving
            if (LocationResolver.LooksResolvable(CoordinatesEntry.Text))
            {
                if (await FindAsync())
                {
                    SourceLabel.Text += " Check the pin, then tap Save.";
                }
                return;
            }

            // Stays open on invalid input so nothing typed is lost
            ShowError("Enter 'latitude, longitude' (e.g. 23.0228, 72.5556), a Google Maps link or a Plus Code. Latitude must be between -90 and 90, longitude between -180 and 180.");
            CoordinatesEntry.Focus();
            return;
        }

        await CloseAsync(new PresetLocation(name, lat, lng));
    }

    async void OnFindClicked(object? sender, EventArgs e) => await FindAsync();

    /// <summary>Resolves the location field to coordinates (replacing its text) and shows them on the map.</summary>
    async Task<bool> FindAsync()
    {
        if (_busy)
        {
            return false;
        }

        var input = CoordinatesEntry.Text?.Trim() ?? string.Empty;
        if (input.Length == 0)
        {
            ShowError("Paste coordinates, a Google Maps link or a Plus Code first.");
            return false;
        }

        if (PresetLocation.TryParseCoordinates(input, out var typedLat, out var typedLng))
        {
            ShowMap(typedLat, typedLng);
            return true;
        }

        if (_resolver is null)
        {
            ShowError("Link lookup is unavailable. Enter the coordinates instead.");
            return false;
        }

        SetBusy(true);
        try
        {
            var found = await _resolver.ResolveAsync(input, _closing.Token);
            if (!found.Success)
            {
                ShowError(found.Message);
                return false;
            }

            _settingCoordinates = true;
            CoordinatesEntry.Text = $"{Format(found.Latitude)}, {Format(found.Longitude)}";
            _settingCoordinates = false;

            ErrorLabel.IsVisible = false;
            SourceLabel.Text = found.Message;
            SourceLabel.IsVisible = true;
            ShowMap(found.Latitude, found.Longitude);
            return true;
        }
        catch (OperationCanceledException)
        {
            // The form was closed while looking up
            return false;
        }
        finally
        {
            SetBusy(false);
        }
    }

    // Typing or pasting coordinates moves the map (after a short pause, so it doesn't reload on every keystroke)
    void OnLocationTextChanged(object? sender, TextChangedEventArgs e)
    {
        _mapDelay?.Cancel();
        ErrorLabel.IsVisible = false;
        if (!_settingCoordinates)
        {
            SourceLabel.IsVisible = false;
        }

        if (!PresetLocation.TryParseCoordinates(e.NewTextValue, out var lat, out var lng))
        {
            // A half-typed value or a link: don't leave an old pin showing as if it were this location
            MapFrame.IsVisible = false;
            return;
        }

        var delay = _mapDelay = new CancellationTokenSource();
        Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(600), () =>
        {
            if (!delay.IsCancellationRequested)
            {
                ShowMap(lat, lng);
            }
        });
    }

    void ShowMap(double lat, double lng)
    {
        MapFrame.IsVisible = true;
        if (_shownOnMap == (lat, lng))
        {
            return;
        }

        _shownOnMap = (lat, lng);
        MapView.Source = new UrlWebViewSource { Url = OpenStreetMapEmbedUrl(lat, lng) };
    }

    // OpenStreetMap's official embeddable map (free, no key, attribution built in) with a marker at the point
    static string OpenStreetMapEmbedUrl(double lat, double lng)
    {
        const double Span = 0.004; // roughly 400 m either side of the pin
        return "https://www.openstreetmap.org/export/embed.html" +
               $"?bbox={Format(lng - Span)},{Format(lat - Span)},{Format(lng + Span)},{Format(lat + Span)}" +
               $"&layer=mapnik&marker={Format(lat)},{Format(lng)}";
    }

    static string Format(double value) => value.ToString("0.#######", CultureInfo.InvariantCulture);

    void SetBusy(bool busy)
    {
        _busy = busy;
        FindingIndicator.IsRunning = busy;
        FindingIndicator.IsVisible = busy;
        FindButton.IsEnabled = !busy;
        SaveButton.IsEnabled = !busy;
        CoordinatesEntry.IsEnabled = !busy;
    }

    async void OnCancelClicked(object? sender, EventArgs e) => await CloseAsync(null);

    protected override bool OnBackButtonPressed()
    {
        _ = CloseAsync(null);
        return true;
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _closing.Cancel();
        // Closed some other way: the caller still gets an answer
        _result.TrySetResult(null);
    }

    void ShowError(string message)
    {
        SourceLabel.IsVisible = false;
        ErrorLabel.Text = message;
        ErrorLabel.IsVisible = true;
    }

    async Task CloseAsync(PresetLocation? result)
    {
        // Set before popping, otherwise OnDisappearing would report a cancel; also ignores double taps
        if (!_result.TrySetResult(result))
        {
            return;
        }
        await Navigation.PopModalAsync();
    }
}
