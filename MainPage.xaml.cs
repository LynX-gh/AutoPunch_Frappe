using TSHR_AutoClock.Services;

namespace TSHR_AutoClock
{
    public partial class MainPage : ContentPage
    {
        bool _startupDone;

        public MainPage()
        {
            InitializeComponent();
        }

        protected override async void OnAppearing()
        {
            base.OnAppearing();

            // Runs once on app open (not every time we return from Settings)
            if (_startupDone || BindingContext is not MainPageViewModel vm)
            {
                return;
            }

            _startupDone = true;

            // A launch with --clock (Windows) runs that action instead of the startup sync; later launches are handed
            // over by the running instance and arrive here too
            LaunchActionQueue.Arrived += () => MainThread.BeginInvokeOnMainThread(async () =>
            {
                if (LaunchActionQueue.Take() is { } next)
                {
                    await vm.RunLaunchActionAsync(next);
                }
            });

            if (LaunchActionQueue.Take() is { } action)
            {
                await vm.RunLaunchActionAsync(action);
            }
            else
            {
                await vm.AutoSyncAsync();
            }

#if ANDROID
            await RequestNotificationPermissionOnceAsync();
#endif
        }

#if ANDROID
        // Shortcut/Routine results are posted as notifications; Android 13+ needs permission for that. Ask once, here,
        // because the shortcut activity has no UI to ask from. If denied, results still show as a toast.
        static async Task RequestNotificationPermissionOnceAsync()
        {
            const string AskedKey = "notification_permission_asked";
            if (!OperatingSystem.IsAndroidVersionAtLeast(33) || Preferences.Get(AskedKey, false))
            {
                return;
            }

            Preferences.Set(AskedKey, true);
            try
            {
                await Permissions.RequestAsync<Permissions.PostNotifications>();
            }
            catch (Exception)
            {
                // Not fatal: results fall back to toasts
            }
        }
#endif
    }
}
