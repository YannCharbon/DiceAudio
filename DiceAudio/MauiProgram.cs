/*
 * DiceAudio - Copyright (C) 2025 Yann Charbon
 * SPDX-License-Identifier: GPL-3.0-or-later
 *
 * This file is part of DiceAudio, released under the GNU GPL v3.
 * See the LICENSE file in the repository root for details.
 */

using Microsoft.Extensions.Logging;
using Microsoft.Maui.LifecycleEvents;
using MudBlazor.Services;
using Plugin.Maui.Audio;

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
                            // Extend content into the title bar at the AppWindow
                            // (OS) level, NOT via the XAML Window property: the
                            // XAML property activates MAUI's own title-bar strip
                            // machinery, which re-inserts a native strip above
                            // the WebView2 on every interactive state change
                            // (restore, drag-resize) and fights any suppression.
                            // AppWindow-level extension bypasses MAUI entirely:
                            // the webview owns the full client area, the system
                            // draws the caption buttons above it and provides
                            // the default drag region across the top strip.
                            window.ExtendsContentIntoTitleBar = false;
                            IntPtr hWnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
                            WindowId myWndId = Win32Interop.GetWindowIdFromWindow(hWnd);
                            var _appWindow = AppWindow.GetFromWindowId(myWndId);
                            _appWindow.SetPresenter(AppWindowPresenterKind.Overlapped);

                            var titleBar = _appWindow.TitleBar;
                            titleBar.ExtendsContentIntoTitleBar = true;
                            titleBar.ButtonBackgroundColor = Microsoft.UI.Colors.Transparent;
                            titleBar.ButtonInactiveBackgroundColor = Microsoft.UI.Colors.Transparent;
                            titleBar.ButtonForegroundColor = Microsoft.UI.Colors.White;
                            titleBar.ButtonInactiveForegroundColor = Windows.UI.Color.FromArgb(160, 255, 255, 255);
                            titleBar.ButtonHoverBackgroundColor = Windows.UI.Color.FromArgb(32, 255, 255, 255);
                            titleBar.ButtonHoverForegroundColor = Microsoft.UI.Colors.White;

                            // Maximize the window on creation
                            if (_appWindow.Presenter is OverlappedPresenter p)
                            {
                                p.Maximize();
                            }

                            // The web page IS the title bar (Teams/Outlook style):
                            // the WebView2 extends to the window top, the web app
                            // bar carries the gem, and CSS app-region regions
                            // provide dragging. (TitleBarGem, the previous native
                            // overlay approach, is kept on disk but disabled.)
                            WebTitleBar.Attach(window);

                            // Attach to the Closing event and use the service provider
                            _appWindow.Closing += (s, e) =>
                            {
                                var diceService = ServiceProvider.GetRequiredService<DiceAudioService>();
                                if (diceService != null)
                                {
                                    // Block until the writes flush. Previously these were
                                    // fire-and-forget, so the process could exit before the
                                    // async save completed, losing recent edits (e.g. a just
                                    // downloaded/moved item). Task.Run keeps the continuations
                                    // off the UI context so GetResult can't deadlock.
                                    try
                                    {
                                        Task.Run(async () =>
                                        {
                                            await diceService.SaveAudioItemListAsync();
                                            await diceService.SaveAudioVirtualFoldersAsync();
                                            await diceService.SaveScenarioGroupsAsync();
                                            await diceService.SaveScenesAsync();
                                            await diceService.SavePresetsAsync();
                                        }).GetAwaiter().GetResult();
                                    }
                                    catch (Exception ex)
                                    {
                                        System.Diagnostics.Debug.WriteLine($"Save-on-close failed: {ex.Message}");
                                    }
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

            builder.AddAudio();

            builder.Services.AddMudServices();
            builder.Services.AddSingleton<DiceAudioService>();
            builder.Services.AddSingleton<DAPlaybackCoordinator>();
            builder.Services.AddSingleton<DAControlServer>();

#if ANDROID
            builder.Services.AddSingleton<IFileHandler, FileHandler>();
#elif WINDOWS
            builder.Services.AddSingleton<IFileHandler, FileHandler>();
#endif

            builder.Services.AddScoped<PopupNotificationService>();

            builder.Services.AddSingleton<YtDlpService>();

            var app = builder.Build();
            ServiceProvider = app.Services; // Assign the root service provider

            // Start the remote-control server if the user enabled it in Settings.
            app.Services.GetRequiredService<DAControlServer>().ApplySavedSettings();

            return app;
        }
    }
}
