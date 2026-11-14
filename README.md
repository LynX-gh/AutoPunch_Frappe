# AutoPunch

One-tap check in and check out for a Frappe / ERPNext HRMS portal, on Android and Windows.

AutoPunch logs in to your company's HRMS portal, shows whether you are currently checked in or out, and records an Employee Checkin when you tap a button. On Android it can also be run by a home-screen shortcut or a Samsung Modes & Routines automation, without opening the app.

## What it does

- **Check in and out with one tap.** The app checks your latest status first and only posts the opposite action, so you can't check in twice by accident.
- **Preset locations.** Save two named locations (for example Office and Home) and pick which one to send with each check-in.
- **Automation on Android.** Launcher shortcuts for each preset, usable from Samsung Modes & Routines, Tasker or MacroDroid.
- **Automation on Windows.** A command-line option for desktop shortcuts and Task Scheduler.
- **Multiple accounts.** Save several logins and switch between them.
- **Stays signed in.** If your session expires, the app logs in again by itself.
- **Light and dark themes.**

> **How location works:** AutoPunch does not read your device's GPS. Each check-in is sent with the coordinates of the preset you selected, whatever your actual position is. Make sure using it this way is in line with your employer's attendance policy.

## Requirements

- An account on a Frappe / ERPNext HRMS portal, with an Employee record linked to your user
- Android 5.0 or later, or Windows 10 (version 1809) or later

## Install

Check the [Releases page](https://github.com/LynX-gh/AutoPunch_Frappe/releases) for a ready-made build:

- **Android:** download the `.apk`, open it and allow installation from that source when Android asks.
- **Windows:** download and unzip the Windows build, then run `TSHR_AutoClock.exe`.

If there is no build for your platform, see [Build from source](#build-from-source).

## Getting started

1. **Open Settings** (the gear button at the top right).
2. **Enter your portal details:** the portal address (for example `https://hr.example.com`), your username and your password.
3. **Tap Login.** The app signs in, finds your employee record and shows your current status. Your login is saved for next time.
4. **Set your locations.** On the main page, tap a preset's name (Office or Home) to edit it. Give it a name and a location, tap **Find** to check the pin on the map, then **Save**.
5. **Select a location** with its radio button, then tap **Check In** or **Check Out**.

The button for your next valid action is highlighted: **Check In** when you are out, **Check Out** when you are in. Tap **Sync** at any time to refresh your status from the portal.

### Setting a location

The location field accepts any of these:

| What you paste | Example |
|---|---|
| Coordinates | `23.0228, 72.5556` |
| A Google Maps share link | `https://maps.app.goo.gl/...` |
| A full Plus Code | `7JMJ2HCV+X8` |
| A short Plus Code with a town | `2HCV+X8 Ahmedabad, Gujarat` |

Tap **Find** to convert a link or code into coordinates and see the spot on a map before you save.

If a Google Maps link can't be resolved, open Google Maps, long-press the exact spot to drop a pin, and share that pin instead (or copy the place's Plus Code).

### Managing saved logins

In Settings, the dropdown lists your saved logins as `username @ portal`. Pick one to fill the form, **New** to add another, **Save** to update the selected one, or **Delete** to remove it. Log out first if you want to edit a login you are currently using.

## Automating on Android

### Home-screen shortcuts

Long-press the AutoPunch icon to see four shortcuts, named after your presets: **In: Office**, **Out: Office**, **In: Home**, **Out: Home**. Each one checks you in or out for that preset without opening the app, then shows the result as a toast and a notification. You can drag a shortcut onto your home screen.

The shortcuts appear after you have opened AutoPunch once, and their names follow your preset names.

### Samsung Modes & Routines

1. Open **Modes and Routines** and create a routine.
2. Under **If**, choose a trigger, for example connecting to your office Wi-Fi.
3. Under **Then**, choose **Apps** > **Open an app or do an app action** > **AutoPunch**, and pick a shortcut.

### Tasker, MacroDroid and other automation apps

Have the app open this link:

```
autopunch://clock?type=IN&preset=Office
```

- `type` is `IN` or `OUT`.
- `preset` is `Office` or `Home` (the first or second preset slot, even if you renamed it). Leave it out to use the preset currently selected in the app.

### Notifications

On Android 13 and later, the app asks for notification permission the first time it opens. If you decline, results from shortcuts still appear as a toast.

## Automating on Windows

Run the app with `--clock IN` or `--clock OUT`, and optionally `--preset Office` or `--preset Home`:

```
TSHR_AutoClock.exe --clock IN --preset Office
```

The window opens, performs the action and shows the result. If AutoPunch is already open, the existing window handles it. Use this as the target of a desktop shortcut, or as the action of a Task Scheduler task (for example, at log on).

## Troubleshooting

| Problem | What to do |
|---|---|
| "Already checked in" or a similar message | Your latest status on the portal already matches that action, so nothing was posted. Tap **Sync** to see the current status. |
| Settings opens with fields marked in red | Those details are missing. Fill them in and log in. |
| Check In / Check Out says the location is not set | The selected preset has no coordinates yet. Tap its name and set a location. |
| "Another action is already running" | A check-in, check-out or sync is still in progress. Wait for it to finish and try again. |
| A shortcut says to open the app and log in | No saved login was found. Open AutoPunch and log in once. |
| No shortcuts on long-press | Open AutoPunch once, then try again. |
| A shortcut didn't run while the phone was locked | Unlock the phone. Some triggers only run once the device is unlocked. |

## Privacy and security

- Your passwords and session are stored in the device's secure storage (Android Keystore / Windows Data Protection) and are not included in Android backups.
- The app talks only to the portal address you enter. The exceptions are when you tap **Find** or **Save** with a link or Plus Code: Google is contacted to follow a Maps link, OpenStreetMap to look up a town name for a short Plus Code, and the map preview loads from openstreetmap.org.
- There are no analytics, ads or tracking.
- On Android, other apps on your phone can open the `autopunch://clock` link. It can only check you in or out with your own saved presets, and the double check-in guard still applies.

## Build from source

You need the [.NET 10 SDK](https://dotnet.microsoft.com/download) with the MAUI workload:

```bash
dotnet workload install maui
```

Then, from the project folder:

```bash
dotnet build -t:Run -f net10.0-android
```

```bash
dotnet build -t:Run -f net10.0-windows10.0.19041.0
```

Android builds need the Android SDK and a connected device or emulator.

## For developers

Architecture, the Frappe API calls used, project structure and known issues are in [PLAN.md](PLAN.md).

## Disclaimer

AutoPunch is an independent project. It is not affiliated with or endorsed by Frappe, ERPNext or any employer. Use it at your own risk and in line with your organisation's policies.
