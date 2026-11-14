using System.Text.Json;
using System.Text.Json.Serialization;
using TSHR_AutoClock.Models;

namespace TSHR_AutoClock.Services;

/// <summary>
/// The Office/Home presets (locations.json) and which one is selected (Preferences).
/// Shared by the main page and the clock triggers, which run without the UI.
/// </summary>
public class PresetStore
{
    private const string SelectedKey = "selected_location";

    private readonly string _path = Path.Combine(FileSystem.AppDataDirectory, "locations.json");
    private PresetLocation _office = new(nameof(PresetSlot.Office), null, null);
    private PresetLocation _home = new(nameof(PresetSlot.Home), null, null);
    private PresetSlot _selected = Preferences.Get(SelectedKey, nameof(PresetSlot.Office)) == nameof(PresetSlot.Home)
        ? PresetSlot.Home
        : PresetSlot.Office;

    /// <summary>Raised after a preset or the selection changes (on the caller's thread).</summary>
    public event Action? Changed;

    public PresetStore()
    {
        // Synchronous (the file is tiny) so the presets are in place before anything reads them
        Load();
    }

    public PresetLocation Get(PresetSlot slot) => slot == PresetSlot.Home ? _home : _office;

    public PresetSlot SelectedSlot
    {
        get => _selected;
        set
        {
            if (_selected == value)
            {
                return;
            }

            _selected = value;
            Preferences.Set(SelectedKey, value.ToString());
            Changed?.Invoke();
        }
    }

    /// <summary>Saves the preset into its slot. The in-memory preset only changes once the file is written.</summary>
    public async Task SaveAsync(PresetSlot slot, PresetLocation preset)
    {
        var store = slot == PresetSlot.Home
            ? new LocationFile { Office = _office, Home = preset }
            : new LocationFile { Office = preset, Home = _home };

        var json = JsonSerializer.Serialize(store, new JsonSerializerOptions { WriteIndented = true });
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        await File.WriteAllTextAsync(_path, json);

        if (slot == PresetSlot.Home)
        {
            _home = preset;
        }
        else
        {
            _office = preset;
        }
        Changed?.Invoke();
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(_path))
            {
                return;
            }

            var stored = JsonSerializer.Deserialize<LocationFile>(File.ReadAllText(_path));
            if (stored is null)
            {
                return;
            }

            _office = stored.Office is { } o
                ? Sanitize(o, nameof(PresetSlot.Office))
                : FromLegacy(nameof(PresetSlot.Office), stored.OfficeLatitude, stored.OfficeLongitude);
            _home = stored.Home is { } h
                ? Sanitize(h, nameof(PresetSlot.Home))
                : FromLegacy(nameof(PresetSlot.Home), stored.HomeLatitude, stored.HomeLongitude);
        }
        catch (Exception)
        {
            // Corrupt or unreadable file: keep the defaults; the next save overwrites it
        }

        static PresetLocation Sanitize(PresetLocation stored, string defaultName) =>
            stored with { Name = string.IsNullOrWhiteSpace(stored.Name) ? defaultName : stored.Name.Trim() };

        // Files written before names were stored kept the coordinates as strings
        static PresetLocation FromLegacy(string name, string? latText, string? lngText) =>
            PresetLocation.TryParseCoordinates(latText, lngText, out var lat, out var lng)
                ? new PresetLocation(name, lat, lng)
                : new PresetLocation(name, null, null);
    }

    private sealed class LocationFile
    {
        public PresetLocation? Office { get; set; }
        public PresetLocation? Home { get; set; }

        // Old format (coordinates as strings, no names): read once for migration, never written
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? OfficeLatitude { get; set; }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? OfficeLongitude { get; set; }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? HomeLatitude { get; set; }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? HomeLongitude { get; set; }
    }
}
