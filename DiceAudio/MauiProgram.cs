using Microsoft.Extensions.Logging;
using Microsoft.Maui.LifecycleEvents;
using MudBlazor.Services;

#if WINDOWS
using Microsoft.UI;
using Microsoft.UI.Windowing;
using WinRT.Interop;
using System.Runtime.InteropServices;
#endif

namespace DiceAudio
{
    public static class MauiProgram
    {
        public static IServiceProvider ServiceProvider { get; private set; } = default!;

        public static MauiApp CreateMauiApp()
        {
            var builder = MauiApp.CreateBuilder();
            builder
                .UseMauiApp<App>()
                .ConfigureFonts(fonts =>
                {
                    fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                })
                .ConfigureLifecycleEvents(events =>
                {
#if WINDOWS
                    events.AddWindows(w =>
                    {
                        w.OnWindowCreated(window =>
                        {
                            // Extend the title bar
                            window.ExtendsContentIntoTitleBar = true;
                            IntPtr hWnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
                            WindowId myWndId = Win32Interop.GetWindowIdFromWindow(hWnd);
                            var _appWindow = AppWindow.GetFromWindowId(myWndId);
                            _appWindow.SetPresenter(AppWindowPresenterKind.Overlapped);

                            // Maximize the window on creation
                            if (_appWindow.Presenter is OverlappedPresenter p)
                            {
                                p.Maximize();
                            }

                            // Attach to the Closing event and use the service provider
                            _appWindow.Closing += (s, e) =>
                            {
                                var diceService = ServiceProvider.GetRequiredService<DiceAudioService>();
                                if (diceService != null)
                                {
                                    // @todo : e.g. save files, etc.
                                }
                            };
                        });
                    });
#endif

#if ANDROID
                    events.AddAndroid(android =>
                    {
                        android.OnPause(activity =>
                        {
                            // Use the root service provider
                            var diceService = ServiceProvider.GetRequiredService<DiceAudioService>();
                            if (diceService != null)
                            {
                                // @todo : e.g. save files, etc.
                            }
                        });
                    });
#endif
                });

            builder.Services.AddMauiBlazorWebView();

#if DEBUG
            builder.Services.AddBlazorWebViewDeveloperTools();
            builder.Logging.AddDebug();
#endif

            builder.Services.AddMudServices();
            builder.Services.AddSingleton<DiceAudioService>();

#if ANDROID
            builder.Services.AddSingleton<IFileHandler, FileHandler>();
#elif WINDOWS
            builder.Services.AddSingleton<IFileHandler, FileHandler>();
#endif

            builder.Services.AddScoped<PopupNotificationService>();

            var app = builder.Build();
            ServiceProvider = app.Services; // Assign the root service provider
            return app;
        }
    }
}
