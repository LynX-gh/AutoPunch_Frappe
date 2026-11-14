using Android.App;
using Android.Content;
using Android.Widget;
using AndroidX.Core.App;

namespace TSHR_AutoClock
{
    /// <summary>Reports a shortcut/Routine clock result: a toast now, plus a notification that stays (tap opens the app).</summary>
    static class ClockNotifier
    {
        const string ChannelId = "clock_results";
        // One id: the latest result replaces the previous one
        const int NotificationId = 1001;

        public static void Show(Context context, string title, string message)
        {
            var appContext = context.ApplicationContext ?? context;
            Toast.MakeText(appContext, message, ToastLength.Long)?.Show();

            try
            {
                if (OperatingSystem.IsAndroidVersionAtLeast(26) &&
                    appContext.GetSystemService(Context.NotificationService) is NotificationManager manager)
                {
                    manager.CreateNotificationChannel(new NotificationChannel(ChannelId, "Clock results", NotificationImportance.Default)
                    {
                        Description = "Results of check-ins and check-outs started from shortcuts or routines"
                    });
                }

                var openApp = new Intent(appContext, typeof(MainActivity));
                openApp.AddFlags(ActivityFlags.NewTask | ActivityFlags.ClearTop);
                var pendingIntent = PendingIntent.GetActivity(appContext, 0, openApp, PendingIntentFlags.Immutable | PendingIntentFlags.UpdateCurrent);

                var builder = new NotificationCompat.Builder(appContext, ChannelId);
                builder.SetSmallIcon(Resource.Drawable.ic_stat_clock);
                builder.SetContentTitle(title);
                builder.SetContentText(message);
                builder.SetStyle(new NotificationCompat.BigTextStyle().BigText(message));
                builder.SetContentIntent(pendingIntent);
                builder.SetAutoCancel(true);
                var notification = builder.Build();

                // Silently dropped by Android when notification permission was denied; the toast still showed
                NotificationManagerCompat.From(appContext)?.Notify(NotificationId, notification);
            }
            catch (Exception)
            {
                // A notification problem must never hide the result: the toast already showed it
            }
        }
    }
}
