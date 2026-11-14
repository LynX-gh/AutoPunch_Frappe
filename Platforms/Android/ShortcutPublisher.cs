using Android.Content;
using AndroidX.Core.Content.PM;
using AndroidX.Core.Graphics.Drawable;
using TSHR_AutoClock.Models;
using TSHR_AutoClock.Services;

namespace TSHR_AutoClock
{
    /// <summary>
    /// Publishes the launcher shortcuts (also offered to Samsung Modes &amp; Routines): Check In / Check Out per preset,
    /// labelled with the preset's current name. Dynamic rather than declared in the manifest, because Android can't
    /// rename manifest shortcuts. Each opens ClockActionActivity with an autopunch://clock link.
    /// </summary>
    static class ShortcutPublisher
    {
        public static void Publish(Context context, PresetStore presets)
        {
            try
            {
                var shortcuts = new List<ShortcutInfoCompat>();
                foreach (var slot in Enum.GetValues<PresetSlot>())
                {
                    var name = presets.Get(slot).Name;
                    shortcuts.Add(Build(context, slot, "IN", $"In: {name}", $"Check in at {name}", shortcuts.Count));
                    shortcuts.Add(Build(context, slot, "OUT", $"Out: {name}", $"Check out at {name}", shortcuts.Count));
                }

                ShortcutManagerCompat.SetDynamicShortcuts(context, shortcuts);
            }
            catch (Exception)
            {
                // A launcher refusing shortcuts must never stop the app; the buttons and links still work
            }
        }

        static ShortcutInfoCompat Build(Context context, PresetSlot slot, string logType, string shortLabel, string longLabel, int rank)
        {
            // The link names the slot (the stable key), so a renamed preset still clocks the right one
            var intent = new Intent(Intent.ActionView, Android.Net.Uri.Parse($"autopunch://clock?type={logType}&preset={slot}"));
            intent.SetClass(context, typeof(ClockActionActivity));

            // New ids: the old manifest shortcuts used check_in_office etc., which can't be reused via the API
            var builder = new ShortcutInfoCompat.Builder(context, $"clock_{logType.ToLowerInvariant()}_{slot.ToString().ToLowerInvariant()}");
            builder.SetShortLabel(shortLabel);
            builder.SetLongLabel(longLabel);
            builder.SetIcon(IconCompat.CreateWithResource(context, Resource.Mipmap.appicon));
            builder.SetIntent(intent);
            builder.SetRank(rank);
            return builder.Build() ?? throw new InvalidOperationException($"Shortcut for {slot} {logType} could not be built.");
        }
    }
}
