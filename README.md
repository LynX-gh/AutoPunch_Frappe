# AutoPunch

**User-facing name:** AutoPunch. **Internal/project name:** TSHR AutoClock (project, namespace and folder stay `TSHR_AutoClock`). **Application ID:** `com.autopunch.app`.

A .NET MAUI app for clocking in and out of a Frappe/ERPNext HRMS portal (TSHR) with **one tap, using fixed preset locations**. It never looks up your live position when you clock. The coordinates you saved are the coordinates it sends.

**Primary targets:** Android (to be triggered from Samsung *Modes & Routines*) and Windows.

> **Status:** login, status sync and clock in/out are implemented against the Frappe HRMS API and **verified end to end against the live server**. The Android shortcut / Samsung Routines trigger and the Windows command-line trigger are built but not yet tested on a device.

## Goals

- Clock in/out quickly, with no GPS lookup and no geofencing. Locations are static by design.
- Use named preset locations (Office, Home, ...) with coordinates taken from Google Maps and stored in the app. *(Today: two fixed slots, entered manually.)*
- On Android, let a Samsung Routine start a clock-in or clock-out automatically, for example when you connect to office Wi-Fi. *(Built: app shortcuts + `autopunch://` link.)*
- On Windows, support launching a clock action from a shortcut or scheduler. *(Built: `--clock` arguments.)*

## Current status

| Area | State |
|---|---|
| Login to Frappe (`/login`) and session cookie persistence | Done (session in `SecureStorage`) |
| Automatic re-login when the stored session has expired | Done |
| Employee ID lookup after login | Done (stored, never shown in the UI) |
| Sync latest check-in status | Done |
| Check In / Check Out (Employee Checkin insert) | Done, verified against live server |
| Only post when the last status is the opposite action | Done |
| Highlight the next valid action (none if login/sync failed) | Done |
| One action at a time (spinner + disabled buttons) | Done |
| Auto-sync when the app opens | Done |
| Selectable Office / Home preset location | Done (choice is remembered) |
| Multiple saved credentials (pick, edit, delete) | Done |
| Last successfully used credentials remembered | Done |
| Themed UI: navy and teal-to-purple gradient, light and dark | Done |
| Centered header with gradient "AutoPunch" title and settings button | Done |
| App name (AutoPunch), application ID and gradient clock icon/splash | Done |
| Named presets with Google Maps coordinates | Done for the two slots (rename + validated coordinates) |
| Android launcher shortcuts / Samsung Modes & Routines trigger | Built, not yet tested on device |
| Windows `--clock` command-line trigger (single instance) | Built, not yet tested |
| Tests | Manual, on device (no test project by choice) |

## How it works

### Login (Settings page)
Login runs a chain: **log in, look up the employee ID, sync the current status**. A failure at any step shows an error popup and stops the chain. After a successful login the credentials are saved or updated automatically, and they become the credentials remembered for the next launch.

### Sync (main page)
- **Logged in:** it syncs the latest status.
- **Not logged in, saved credentials available:** it logs in, looks up the employee ID and syncs.
- **Not logged in, something missing:** it opens Settings and highlights the missing field(s) in red.

### On app open
The main page runs one sync when it first appears, using the existing session or the last successfully used credentials. If nothing is saved it stays quiet.

### Expired session
If the server rejects the stored session (401/403), the app drops it, logs in again once with the remembered credentials and retries the whole action. If that fresh login or the retry fails too, you get the error. This applies to the buttons, the startup sync and the triggers.

### Check In / Check Out
1. Log in first if needed (saved credentials, otherwise Settings opens).
2. Fetch the latest status from the server.
3. Post only if it is the opposite action. **Check In** is blocked if you are already IN. **Check Out** is blocked unless you are IN. A blocked action shows a message and posts nothing.
4. Post the Employee Checkin with the selected location's coordinates.
5. Fetch the status again to confirm what was recorded.

Both buttons are outlined by default. After a successful sync, the next valid action gets the gradient fill: **Check In** when you are OUT or have no check-ins yet, **Check Out** when you are IN. If login, sync or a clock post fails (or you log out), neither is highlighted until the next successful sync. While an action runs, neither is highlighted.

A spinner shows while anything is running, and Check In, Check Out and Sync are disabled, so only one action can run at a time. `ClockService` enforces the same rule app-wide, so a trigger that arrives while the app is busy is turned away ("Another action is already running") instead of posting twice. Frappe itself would accept duplicate IN/IN entries, so this guard matters.

### Android trigger (shortcuts, Samsung Modes & Routines)
Long-press the app icon for four shortcuts, named after your presets: **In: <name>** and **Out: <name>** for each (e.g. *In: Office*, *Out: Home*). They are published by the app at runtime, so they appear after AutoPunch has been opened once, and their labels update as soon as you rename a preset. Each runs the full Check In / Check Out flow above for that preset **without opening the app**, then shows a toast and a notification with the result (tap it to open the app). Skips ("Already checked in") and errors are reported the same way. Missing credentials say to open the app and log in.

- **Samsung Modes & Routines:** add a routine (e.g. *If* connected to office Wi-Fi), then *Then* > *Apps* > *Open an app or do an app action* > AutoPunch, and pick a shortcut. Since the shortcuts became runtime (dynamic) shortcuts, a routine set up with the older fixed shortcuts needs its action re-picked once.
- **Other automation apps (Tasker, MacroDroid) or adb:** open the link `autopunch://clock?type=IN&preset=Office` (`type` = `IN`/`OUT`; `preset` = `Office`/`Home`, or leave it out to use the preset selected in the app).
  ```bash
  adb shell am start -a android.intent.action.VIEW -d "autopunch://clock?type=IN&preset=Office"
  ```
- A shortcut clocks its slot, not its label: renaming a preset changes the label, and the shortcut keeps clocking that same preset. The `autopunch://` link uses the slot key (`preset=Office` or `Home`), whatever the preset is called.
- Notifications need permission on Android 13+. The app asks once, the first time the main page opens. If you deny it, results still show as a toast.

### Windows trigger (shortcut, Task Scheduler)
Run the app with `--clock IN|OUT` and optionally `--preset Office|Home` (without it, the preset selected in the app is used):
```bash
TSHR_AutoClock.exe --clock IN --preset Office
```
The window opens and runs that action instead of the startup sync, and the result shows like a button tap (status card, plus a popup for skips and errors). If AutoPunch is already open, the new launch hands its arguments to the open window, brings it to the front and exits, so there is never a second window. Invalid arguments show an "Invalid launch arguments" popup.

### Locations
Two preset slots, Office and Home, listed one under another (the list is generated from the slots, ready for more). Select one with its radio button (the choice is remembered). Tap a preset's name to open its edit form, with the name and the location. Save checks both (name not blank; latitude -90 to 90, longitude -180 to 180, not `0, 0`). Invalid input shows an error in the form and keeps what you typed. Cancel or back changes nothing. Renaming only changes the label, so the selected slot stays selected. Clocking is blocked while the selected slot is still unset (`00.000000`).

The location field takes any of these. Tap **Find** to turn a link or code into `lat, lng` (Save does the same first and asks you to check the pin):

| You paste | How it is resolved |
|---|---|
| Coordinates `23.0228, 72.5556` | Used as is |
| Google Maps share link (`maps.app.goo.gl/...`, or the share text containing it) or a full `google.com/maps/...` URL | The redirects are followed and the coordinates read from the address: the place's pin (`!3d..!4d..`), a dropped pin (`/search/lat,lng`, `q=lat,lng`), or a Plus Code in it. As a last resort, the centre of the shared map view (`@lat,lng`), marked *Approximate* |
| Full Plus Code `7JMJ2HCV+X8` | Decoded offline |
| Short Plus Code with a town `2HCV+X8 Ahmedabad, Gujarat` | The town is looked up on OpenStreetMap (Nominatim), then the code is resolved exactly from it |

Some share links only name a place (`?q=Name&ftid=...`) and contain no coordinates; Google only loads those in its web app. For those the form says to drop a pin (long-press the spot) and share that, or copy the place's Plus Code. It never guesses.

Valid coordinates are shown on a free **OpenStreetMap** preview with a marker (the official embeddable map, no API key), so you can check the spot before saving. Typing coordinates by hand moves the map too.

### Saved credentials
- Entries are unique by **domain + username** (case-insensitive, trailing `/` on the domain ignored).
- The dropdown lists them as `username @ domain`. Selecting one fills the form. It only becomes the remembered credential after a successful login.
- **Save** updates the selected entry in place (including a changed domain or username). **New** clears the form to add another. **Delete** asks for confirmation. Deleting the remembered credential clears it.
- Editing is locked while you are logged in. Log out first.
- A credential saved by earlier versions is imported automatically on first run.

## Frappe API calls

All calls go to the domain from the app's settings and use the session cookies from login.

| Purpose | Request |
|---|---|
| Employee ID | `GET /api/method/frappe.client.get_value?doctype=Employee&fieldname=name&filters={"user_id":"<username>"}` -> `message.name` |
| Latest check-in | `GET /api/method/frappe.desk.reportview.get_list?doctype=Employee Checkin&fields=["log_type","time"]&filters={"employee":"<id>"}&order_by=time desc&limit=1` -> `message[0]` |
| Clock in/out | `POST /api/method/frappe.client.insert`, form field `doc` = `{"doctype":"Employee Checkin","employee":"<id>","log_type":"IN"\|"OUT","time":"<UTC ISO timestamp>","latitude":..,"longitude":..}` |

## Project structure

```
TSHR_AutoClock/
├── MauiProgram.cs            # App bootstrap and DI registrations
├── App.xaml(.cs)             # Application and window creation
├── AppShell.xaml(.cs)        # Shell, plus the CredentialsPage route
├── MainPage.xaml(.cs)        # Custom header, status, location selection, clock buttons, spinner; triggers startup sync
├── MainPageViewModel.cs      # Thin UI layer over ClockService / PresetStore (commands, alerts, highlight) + PresetItem rows for the preset list
├── Pages/
│   ├── CredentialsPage.xaml(.cs)   # Settings: saved-credentials picker, login/logout, missing-field highlight
│   └── LocationEditPage.xaml(.cs)  # Modal form: name + location (coordinates, link or Plus Code), Find, OpenStreetMap preview
├── Services/
│   ├── HrmsService.cs        # Frappe API: login, session (SecureStorage), employee ID, latest check-in, clock in/out
│   ├── ClockService.cs       # UI-free sync / clock flows: auto login, opposite-status guard, re-login retry, one action at a time
│   ├── PresetStore.cs        # Office/Home presets (locations.json) + selected slot
│   ├── LaunchAction.cs       # Parses trigger requests (autopunch:// link, --clock args) + queue for the main page
│   ├── LocationResolver.cs   # Google Maps link / Plus Code -> coordinates (redirects, Nominatim for short codes)
│   ├── PlusCode.cs           # Open Location Code: find, decode, recover short codes
│   └── CredentialStore.cs    # Saved credentials list + remembered (last used) credential
├── Models/
│   ├── CookieDto.cs          # Serializable cookie for session persistence
│   └── PresetLocation.cs     # PresetSlot enum, named preset + coordinate parsing/validation
├── Platforms/
│   ├── Android/
│   │   ├── ClockActionActivity.cs   # Invisible autopunch://clock entry point used by the shortcuts
│   │   ├── ClockNotifier.cs         # Toast + notification for trigger results
│   │   ├── ShortcutPublisher.cs     # Launcher shortcuts named after the presets (dynamic, republished on change)
│   │   └── Resources/               # drawable/ic_stat_clock.xml (notification icon)
│   └── Windows/App.xaml.cs          # Single instance + --clock argument hand-off
├── Resources/                # AppIcon + Splash (gradient clock glyph), fonts, images, Colors/Styles (themes, gradient, button styles)
└── Properties/launchSettings.json
```

## Tech stack

- .NET 10 MAUI, single project
- Target frameworks: `net10.0-android` and `net10.0-windows10.0.19041.0`. The csproj also lists iOS and MacCatalyst targets, but they are not project goals.
- Packages: `Microsoft.Maui.Controls`, `Microsoft.Extensions.Logging.Debug`
- Minimum OS: Android 21, Windows 10.0.17763
- Windows is built unpackaged (`WindowsPackageType=None`)

## Theme and branding

The UI uses a navy and teal-to-purple gradient theme: Deep Navy `#2A2D54`, teal `#00C4C0` to purple `#8C47F5` signature gradient, warm white light theme and navy dark theme. Palette and gradient live in `Resources/Styles/Colors.xaml` (`BrandGradient`), button styles in `Resources/Styles/Styles.xaml` (gradient primary, outlined `SecondaryButton`, `DangerButton`). The main page hides the Shell nav bar and draws its own header: a gradient "AutoPunch" title (letters coloured per step, since MAUI labels can't take a gradient fill) centered across the full width, with the settings button overlaid on the right, so the title stays centered on both Android and Windows. The Settings page keeps the standard nav bar with a back arrow. Primary buttons use the gradient; `SecondaryButton` and `DangerButton` are outlined. The app icon and splash are a simple original clock glyph in the gradient on navy (`Resources/AppIcon`). The app uses Open Sans under the `AppFont` / `AppFontSemibold` aliases (`MauiProgram.cs`), so swapping the font is a one-line change per weight.

## Build and run

Prerequisites: the .NET 10 SDK with the MAUI workload (`dotnet workload install maui`). Android needs the Android SDK and an emulator or device. Windows needs Windows 10 or 11.

```bash
# Android
dotnet build -t:Run -f net10.0-android

# Windows
dotnet build -t:Run -f net10.0-windows10.0.19041.0
```

If a Windows build fails copying `TSHR_AutoClock.exe`, the app is still running. Close it and rebuild.

## Architecture notes

- Hand-rolled MVVM. `MainPageViewModel` implements `INotifyPropertyChanged` and is created in XAML, so it is not built by DI. It gets its services from `IPlatformApplication.Current.Services`.
- The sync and clock logic lives in `ClockService`, not the view-model, so the Android shortcut activity can run it with no UI. It returns a `ClockResult` (`Done`, `Warning`, `Skipped`, `Failed`, `NeedsSettings`) and each caller decides how to show it.
- DI registrations in `MauiProgram.cs`: `HrmsService`, `CredentialStore`, `PresetStore`, `ClockService`, `LocationResolver` (singletons), `CredentialsPage` (transient).
- `HrmsService` raises `StatusChanged` whenever the synced status changes, and `PresetStore` raises `Changed`; the main page listens to both, so a trigger's result also shows in an open window.
- `LaunchActionQueue` is static because the Windows app queues the first `--clock` action before MAUI's services exist.
- Storage:
  - `SecureStorage`: saved credentials list, the remembered credential's domain, username and password, and the session cookies (`hrms_session_cookies`).
  - `Preferences`: employee ID, the selected location, and whether the notification permission was already asked (Android).
  - App data folder: `locations.json` (`{"Office": {Name, Latitude, Longitude}, "Home": {...}}`, coordinates as numbers; the older string-only format is still read and upgraded on the next save).
  - The session and the presets load synchronously at startup. A corrupt session is removed (you start logged out and the next sync logs in again); a corrupt `locations.json` is ignored and replaced on the next save.
  - A `session.cookies` file from older versions is moved into `SecureStorage` on first start and deleted.

## Roadmap

Done: Frappe login, employee ID lookup, status sync, clock in/out (with opposite-status check and single-action guard), startup sync, live-server verification, selectable and renameable locations with validated coordinates, saved-credential management, themed UI, next-action highlight, session in `SecureStorage`, automatic re-login, `ClockService`, Android shortcuts / Routines trigger, Windows `--clock` trigger.

Next:
1. Test the triggers on devices: the shortcuts on the phone (and whether Samsung Routines lists them), the adb link, and Windows `--clock` with the app closed and open.
2. A visual check on a real Android device.

## Known issues and risks

- The clock-in/out `time` is sent as UTC ISO, and no `X-Frappe-CSRF-Token` header is sent. Both work against the live server today; recheck them if the server's Frappe version or timezone settings change.
- **Race window:** another device or the portal could clock you in between the status check and the post.
- The Windows package identity (`Package.appxmanifest`) is still a template placeholder. Not needed: the app is built unpackaged and the Windows trigger uses command-line arguments.
- **Samsung Routines:** unconfirmed that *Open an app or do an app action* lists the app's shortcuts. If not, use Tasker/MacroDroid with the `autopunch://` link.
- An Android trigger that fires while the phone is locked may only run once it is unlocked; check this on the device.
- The Android application ID changed from the old template placeholder to `com.autopunch.app`, so an install of an older build is treated as a separate app (log in again; uninstall the old one).
- The header, gradient title and icon have been built for both targets but not yet checked visually on a real Android device.

## Security notes

- All saved credentials, including passwords, are stored in `SecureStorage`.
- The session cookies are stored in `SecureStorage` too (encrypted by the platform).
- Android backup is off (`allowBackup="false"`), so neither the session nor anything else is copied into device backups. `SecureStorage` values couldn't be decrypted after a restore anyway.
- The `autopunch://clock` link is exported, so any app on the phone can trigger a check-in or check-out. It can only post with your saved preset coordinates and still obeys the opposite-status guard.
- Finding a location contacts Google (to follow a share link) and OpenStreetMap's Nominatim (only for a short Plus Code's town name). The map preview loads from openstreetmap.org. Nothing else is sent, and only when you tap Find or Save with a link or code.
- Never commit real domains, credentials or cookies to source control.
