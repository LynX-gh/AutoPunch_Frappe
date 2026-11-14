using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;
using TSHR_AutoClock.Services;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace TSHR_AutoClock.WinUI
{
    /// <summary>
    /// Provides application-specific behavior to supplement the default Application class.
    /// </summary>
    public partial class App : MauiWinUIApplication
    {
        const string InstanceKey = "AutoPunch.Main";

        DispatcherQueue? _dispatcher;

        /// <summary>
        /// Initializes the singleton application object.  This is the first line of authored code
        /// executed, and as such is the logical equivalent of main() or WinMain().
        /// </summary>
        public App()
        {
            this.InitializeComponent();
        }

        protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();

        // Single instance: "AutoPunch.exe --clock IN --preset Office" (shortcut, Task Scheduler) while the app is
        // already open hands its arguments to the open window instead of starting a second one
        protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
        {
            var mainInstance = AppInstance.FindOrRegisterForKey(InstanceKey);
            if (!mainInstance.IsCurrent)
            {
                var activation = AppInstance.GetCurrent().GetActivatedEventArgs();
                // Off the UI thread: blocking it on this cross-process call can deadlock
                Task.Run(() => mainInstance.RedirectActivationToAsync(activation).AsTask()).Wait();
                System.Diagnostics.Process.GetCurrentProcess().Kill();
                return;
            }

            _dispatcher = DispatcherQueue.GetForCurrentThread();
            mainInstance.Activated += OnRedirectedActivation;

            // Queued before the UI exists; the main page runs it instead of its startup sync
            if (LaunchAction.FromCommandLine(Environment.GetCommandLineArgs()) is { } action)
            {
                LaunchActionQueue.Post(action);
            }

            base.OnLaunched(args);
        }

        // A second launch was redirected here (raised on a background thread)
        void OnRedirectedActivation(object? sender, AppActivationArguments e)
        {
            if (e.Kind == ExtendedActivationKind.Launch &&
                e.Data is global::Windows.ApplicationModel.Activation.ILaunchActivatedEventArgs launch &&
                LaunchAction.FromCommandLine(LaunchAction.SplitCommandLine(launch.Arguments)) is { } action)
            {
                LaunchActionQueue.Post(action);
            }

            _dispatcher?.TryEnqueue(BringToFront);
        }

        static void BringToFront()
        {
            if (Microsoft.Maui.Controls.Application.Current?.Windows.FirstOrDefault()?.Handler?.PlatformView is not Microsoft.UI.Xaml.Window window)
            {
                return;
            }

            if (window.AppWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Minimized } presenter)
            {
                presenter.Restore();
            }
            window.Activate();
        }
    }

}
