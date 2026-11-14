using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using TSHR_AutoClock.Services;

namespace TSHR_AutoClock
{
    /// <summary>
    /// Invisible entry point for clocking without opening the app: the launcher shortcuts (and so Samsung
    /// Modes &amp; Routines), Tasker, or adb open autopunch://clock?type=IN|OUT[&amp;preset=Office|Home].
    /// It runs the same flow as the buttons, reports the result as a toast and a notification, then closes.
    /// </summary>
    [Activity(
        Name = "com.autopunch.app.ClockActionActivity",
        Exported = true,
        NoHistory = true,
        ExcludeFromRecents = true,
        TaskAffinity = "",
        Theme = "@android:style/Theme.Translucent.NoTitleBar",
        // Never recreated mid-action, which would run it twice
        ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density | ConfigChanges.KeyboardHidden | ConfigChanges.Keyboard)]
    [IntentFilter(
        new[] { Intent.ActionView },
        Categories = new[] { Intent.CategoryDefault, Intent.CategoryBrowsable },
        DataScheme = "autopunch",
        DataHost = "clock")]
    public class ClockActionActivity : Activity
    {
        protected override async void OnCreate(Bundle? savedInstanceState)
        {
            base.OnCreate(savedInstanceState);

            // Restored by the system after being killed: the action already ran (or was abandoned), don't repeat it
            if (savedInstanceState is not null)
            {
                Finish();
                return;
            }

            try
            {
                var uri = Intent?.Data;
                var action = LaunchAction.Parse(uri?.GetQueryParameter("type"), uri?.GetQueryParameter("preset"));
                if (action.Error is not null)
                {
                    ClockNotifier.Show(this, "Invalid AutoPunch link", action.Error);
                    return;
                }

                var clock = IPlatformApplication.Current?.Services.GetService<ClockService>();
                if (clock is null)
                {
                    ClockNotifier.Show(this, "AutoPunch error", "Clock service not available.");
                    return;
                }

                var result = await clock.ClockAsync(action.LogType, action.Slot);
                var message = result.Outcome == ClockOutcome.NeedsSettings
                    ? "Open AutoPunch and log in from Settings first."
                    : result.Message;
                ClockNotifier.Show(this, result.Outcome == ClockOutcome.Done ? "AutoPunch" : result.Title, message);
            }
            catch (Exception ex)
            {
                ClockNotifier.Show(this, "AutoPunch error", ex.Message);
            }
            finally
            {
                Finish();
            }
        }
    }
}
