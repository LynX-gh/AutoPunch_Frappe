using Microsoft.Extensions.Logging;
using TSHR_AutoClock.Services;

namespace TSHR_AutoClock
{
    public static class MauiProgram
    {
        public static MauiApp CreateMauiApp()
        {
            var builder = MauiApp.CreateBuilder();
            builder
                .UseMauiApp<App>()
                .ConfigureFonts(fonts =>
                {
                    fonts.AddFont("OpenSans-Regular.ttf", "AppFont");
                    fonts.AddFont("OpenSans-Semibold.ttf", "AppFontSemibold");
                });

            builder.Services.AddSingleton<HrmsService>();
            builder.Services.AddSingleton<CredentialStore>();
            builder.Services.AddSingleton<PresetStore>();
            builder.Services.AddSingleton<ClockService>();
            builder.Services.AddSingleton<LocationResolver>();
            builder.Services.AddTransient<Pages.CredentialsPage>();

#if DEBUG
            builder.Logging.AddDebug();
#endif

            return builder.Build();
        }
    }
}
