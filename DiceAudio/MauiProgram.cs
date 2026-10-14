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

                                // The marker says who wrote the folder last (spec §5.4).
                                var dataFolder = ServiceProvider.GetRequiredService<DataFolderService>();
                                if (dataFolder.IsLoaded)
                                {
                                    _ = dataFolder.WriteMarkerAsync();
                                }

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

                                // A save refused because the file changed on another
                                // computer: the user's version is kept next to it.
                                try { ServiceProvider.GetRequiredService<DAExternalChanges>().KeepUnsavedVersions(); }
                                catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Keeping the unsaved versions failed: {ex.Message}"); }

                                // Last: the lock says the folder is open until everything is written.
                                dataFolder.ReleaseLock();
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
            var store = new SafeFileStore(dataLocation.GetPath);
            var settings = new AppSettings(store, "DiceAudio");

            // The preferences that travel with the data (settings.json) are read
            // now, before any page asks for one; the first time, the values kept
            // on this computer are copied into it. When the data folder cannot be
            // used, the startup screen shows (DataFolderGate) and they are read
            // once a usable folder is chosen.
            if (dataLocation.State == DataLocationState.Ok)
            {
                Task.Run(() => settings.InitializeAsync(localPreferences, DataFolderService.MovedPreferenceKeys)).GetAwaiter().GetResult();
            }

            builder.Services.AddSingleton<ILocalPreferences>(localPreferences);
            builder.Services.AddSingleton(dataLocation);
            builder.Services.AddSingleton(store);
            builder.Services.AddSingleton(settings);

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
            builder.Services.AddSingleton<DABundleExportService>();
            builder.Services.AddSingleton<DAStorageNotices>();
            builder.Services.AddSingleton<IBackupHost, DABackupHost>();
            builder.Services.AddSingleton<BackupService>();
            builder.Services.AddSingleton<DAImportService>();
            builder.Services.AddSingleton<DataFolderService>();
            builder.Services.AddSingleton<DAExternalChanges>();
            builder.Services.AddSingleton(sp => new DataFolderLock(store, () => dataLocation.Root, "DiceAudio", dataLocation.MachineName,
                                                                   sp.GetRequiredService<DiceAudioService>().GetVersion()));

            var app = builder.Build();
            ServiceProvider = app.Services; // Assign the root service provider

            // When the data folder cannot be used, nothing is read: the startup
            // screen (DataFolderGate) asks what to do, and loads once a usable
            // folder is chosen. Otherwise the library is read in the background
            // (every save waits for it), then the remote-control server starts
            // if it is on and the automatic backup of the day is made.
            if (dataLocation.State == DataLocationState.Ok)
            {
                var dataFolder = app.Services.GetRequiredService<DataFolderService>();
                _ = Task.Run(async () =>
                {
                    try { await dataFolder.LoadAsync(); }
                    catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Loading the data failed: {ex}"); }
                });
            }

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
