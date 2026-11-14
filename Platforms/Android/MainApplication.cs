using Android.App;
using Android.Runtime;
using TSHR_AutoClock.Services;

namespace TSHR_AutoClock
{
    [Application]
    public class MainApplication : MauiApplication
    {
        public MainApplication(IntPtr handle, JniHandleOwnership ownership)
            : base(handle, ownership)
        {
        }

        protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();

        public override void OnCreate()
        {
            base.OnCreate();

            // Launcher shortcuts carry the preset names: publish them on start and again whenever a preset changes
            if (IPlatformApplication.Current?.Services.GetService<PresetStore>() is { } presets)
            {
                ShortcutPublisher.Publish(this, presets);
                presets.Changed += () => ShortcutPublisher.Publish(this, presets);
            }
        }
    }
}
