/*
 * DiceAudio - Copyright (C) 2025 Yann Charbon
 * SPDX-License-Identifier: GPL-3.0-or-later
 *
 * This file is part of DiceAudio, released under the GNU GPL v3.
 * See the LICENSE file in the repository root for details.
 */

using DiceAudio.Integrations;
using DiceAudio.Storage;
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
                                // A yt-dlp left running would outlive the app and
                                // keep writing into the audio folder.
                                try { ServiceProvider.GetRequiredService<DADownloadQueue>().CancelAll(); }
                                catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Stopping the downloads failed: {ex.Message}"); }

                                var diceService = ServiceProvider.GetRequiredService<DiceAudioService>();
                                if (diceService != null)
                                {
                                    // Block until every write is on disk, including saves
                                    // started elsewhere just before (FlushAsync), but never
                                    // more than 10 s: a drive that does not answer must not
                                    // stop the window from closing. Task.Run keeps the
                                    // continuations off the UI context so Wait can't deadlock.
                                    try
                                    {
                                        if (!Task.Run(() => diceService.SaveEverythingAsync()).Wait(TimeSpan.FromSeconds(10)))
                                            System.Diagnostics.Debug.WriteLine("Save-on-close did not finish within 10 s.");
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

            // Where the data is, and the store every data file goes through.
            // Built first, before anything reads a file or a preference: in a
            // debug run on a sandbox (DICE_DATA_DIR) both are the sandbox's,
            // never the real ones of this computer.
#if DEBUG
            string? debugOverride = Environment.GetEnvironmentVariable(DataLocation.DebugOverrideVariable);
#else
            string? debugOverride = null;
#endif
            if (string.IsNullOrWhiteSpace(debugOverride))
                debugOverride = null;

            var localPreferences = LocalPreferences.Create(debugOverride, () => new MauiLocalPreferences());
            var dataLocation = new DataLocation("DiceAudio", debugOverride ?? FileSystem.AppDataDirectory,
                                                localPreferences, GetMachineName(), debugOverride);
            System.Diagnostics.Debug.WriteLine(
                $"Data folder: {dataLocation.Root} ({dataLocation.State}{(dataLocation.IsDebugOverride ? ", " + DataLocation.DebugOverrideVariable : "")})");
#if DEBUG
            // Until the startup screen exists, a folder that cannot be used
            // stops a debug run here rather than showing an empty library.
            if (dataLocation.State != DataLocationState.Ok)
            {
                throw new InvalidOperationException(
                    $"The data folder {dataLocation.Root} cannot be used ({dataLocation.State}). Check {DataLocation.DebugOverrideVariable}.");
            }
#endif
            builder.Services.AddSingleton<ILocalPreferences>(localPreferences);
            builder.Services.AddSingleton(dataLocation);
            builder.Services.AddSingleton(new SafeFileStore(dataLocation.GetPath));

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
            builder.Services.AddSingleton<YtDlpUpdateService>();
            builder.Services.AddSingleton<IDADownloadRunner>(sp => sp.GetRequiredService<YtDlpService>());
            builder.Services.AddSingleton<IDADownloadLibrary, DADownloadLibrary>();
            builder.Services.AddSingleton<DADownloadQueue>();

            var app = builder.Build();
            ServiceProvider = app.Services; // Assign the root service provider

            // Reads the library in the background; every save waits for it.
            // A folder that cannot be used (only possible in a release build,
            // a debug build stops above) is never read and never written: its
            // files are blocked, and the user is told by the main layout until
            // the startup screen exists.
            var diceAudioService = app.Services.GetRequiredService<DiceAudioService>();
            if (dataLocation.State == DataLocationState.Ok)
                _ = diceAudioService.InitializeAsync();
            else
                diceAudioService.MarkDataFolderUnusable($"The data folder cannot be used ({dataLocation.State}).");

            // Start the remote-control server if the user enabled it in Settings.
            app.Services.GetRequiredService<DAControlServer>().ApplySavedSettings();

            return app;
        }

        private static string GetMachineName()
        {
#if ANDROID
            return DeviceInfo.Current.Name;
#else
            return Environment.MachineName;
#endif
        }
    }
}
