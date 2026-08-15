/*
 * DiceAudio - Copyright (C) 2025 Yann Charbon
 * SPDX-License-Identifier: GPL-3.0-or-later
 *
 * This file is part of DiceAudio, released under the GNU GPL v3.
 * See the LICENSE file in the repository root for details.
 */

#if WINDOWS
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

using Point = Windows.Foundation.Point;

namespace DiceAudio
{
    /// <summary>
    /// Windows-only. Overlays the DiceAudio gem in the extended title-bar
    /// strip, above the BlazorWebView.
    ///
    /// The gem is a raw Win32 layered window (WS_EX_LAYERED, owned by the main
    /// window) painted with UpdateLayeredWindow from a premultiplied-alpha
    /// bitmap. This deliberately bypasses XAML entirely: any XAML-composited
    /// overlay (in-tree element or popup) is subject to the WinUI-over-WebView2
    /// compositing bug (microsoft-ui-xaml#2651) where the overlay only paints
    /// after a real OS occlusion. A layered window is composited by the OS
    /// itself and always paints.
    ///
    /// Perks of the layered window: per-pixel alpha hit-testing (clicks on the
    /// transparent corners fall through to the app), and it moves with a plain
    /// SetWindowPos — no recreate, no flicker.
    ///
    /// The gem is scaled so its straight vertical edge equals the web app bar's
    /// height, then positioned so those edges straddle the bar. It stays hidden
    /// until the Blazor app has rendered (past the loading page). Clicking it
    /// navigates Home.
    /// </summary>
    internal static class TitleBarGem
    {
        // ── Geometry (SVG user units, from the DiceAudio logo) ──
        // viewBox: 72.583008 × 83.301239. The hexagon's straight left/right
        // edges are 40.001137 tall; its flat-top corners sit at viewBox y=21.65.
        private const double ViewBoxW = 72.583008;
        private const double ViewBoxH = 83.301239;
        private const double EdgeUnits = 40.001137;      // vertical edge length
        private const double FlatTopUnits = 21.650048;   // y of the upper flat corners

        // The web app bar (.da-bar) is 50 CSS px tall; 1 CSS px == 1 DIP here.
        private const double BarHeightDip = 50.0;

        // Pre-rendered @4x bitmap of the SVG (363×417), decoded and rescaled
        // per-DPI at runtime.
        private const string GemBitmapRelPath = @"Assets\diceaudio_gem.png";

        // Posted from the page once the nav bar exists (app past the loading page).
        private const string ReadyMessage = "da-app-ready";

        // Polls for the nav bar, then signals native that the app UI is up.
        private const string ReadyProbeScript =
            "(function(){function c(){try{if(document.querySelector('.da-bar')){" +
            "window.chrome.webview.postMessage('" + ReadyMessage + "');return;}}catch(e){}" +
            "setTimeout(c,120);}c();})();";

        // SPA-navigate the Blazor router to Home without a full reload.
        private const string NavHomeScript =
            "(function(){try{var a=document.createElement('a');a.href='/';" +
            "a.style.display='none';document.body.appendChild(a);a.click();" +
            "setTimeout(function(){a.remove();},0);}catch(e){window.location.assign('/');}})();";

        // Parking spot for the not-yet-placed gem (standard Windows off-screen
        // coordinate, same one the OS uses for minimized windows).
        private const int OffscreenPos = -32000;

        // Layout size in DIPs (hexagon edge == bar height).
        private static readonly double Scale = BarHeightDip / EdgeUnits;
        private static readonly double ImgWDips = ViewBoxW * Scale;
        private static readonly double ImgHDips = ViewBoxH * Scale;

        private static void Log(string msg)
        {
            System.Diagnostics.Debug.WriteLine($"TitleBarGem: {msg}");
        }

        private static Microsoft.UI.Xaml.Window? _window;
        private static FrameworkElement? _root;
        private static IntPtr _ownerHwnd;
        private static IntPtr _gemHwnd;
        private static bool _revealed;
        private static uint _dpi;
        private static int _pxW;
        private static int _pxH;
        private static byte[]? _pixels; // decoded gem, premultiplied BGRA at _dpi
        private static Task? _pixelsTask; // the single in-flight decode
        private static int _curX;      // last position given to ULW/SetWindowPos
        private static int _curY;
        private static int _fadeGen;   // cancels a running fade when incremented
        private static bool _placed;   // a sane, stable position has been applied;
                                       // visible pixels must never be painted before this
        private static OverlappedPresenterState? _lastPresenterState;
        private static int _moveGen;   // cancels a running maximize/restore transition

        public static void Attach(Microsoft.UI.Xaml.Window window)
        {
            Log("Attach");
            _window = window;
            try
            {
                _ownerHwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
            }
            catch (Exception ex)
            {
                Log($"Attach GetWindowHandle EXCEPTION {ex.Message}");
                return;
            }

            // Create the layered window NOW, synchronously, before the main
            // window is ever activated, with a fully transparent surface. On
            // this platform a layered window shown while its owner is already
            // the active foreground window is not composited until the owner
            // deactivates once (observed on Win11 26200) — showing it
            // pre-activation makes the launch itself provide that transition.
            // Revealing later is then only a pixel update on an already-
            // composited window. The blank surface needs no decoded pixels, so
            // creation must not wait on the (cold-start-slow) PNG decode.
            CreateGemWindowEarly();

            // Decode the gem pixels in the background; the reveal awaits this
            // single task (a second concurrent decode/create used to race the
            // reveal and flash the gem at the screen origin).
            _pixelsTask = DecodePixelsAsync();

            // Poll for content instead of hooking Activated: the first Activated
            // event can fire before MAUI sets Window.Content (or not at all when
            // the app launches unfocused), which would strand the gem forever.
            var poll = window.DispatcherQueue.CreateTimer();
            poll.Interval = TimeSpan.FromMilliseconds(250);
            poll.IsRepeating = true;
            poll.Tick += (s, e) =>
            {
                if (window.Content is FrameworkElement)
                {
                    poll.Stop();
                    Inject(window);
                }
            };
            poll.Start();
        }

        /// <summary>
        /// Creates and shows the gem window with a blank (all-transparent,
        /// click-through) surface. Synchronous and idempotent; must run on the
        /// UI thread before the main window's first activation.
        /// </summary>
        private static void CreateGemWindowEarly()
        {
            if (_gemHwnd != IntPtr.Zero)
                return;

            try
            {
                _dpi = GetDpiForWindow(_ownerHwnd);
                if (_dpi == 0)
                    _dpi = 96;
                double px = _dpi / 96.0;
                _pxW = (int)Math.Round(ImgWDips * px);
                _pxH = (int)Math.Round(ImgHDips * px);

                RegisterGemClass();

                _gemHwnd = CreateWindowExW(
                    WS_EX_LAYERED | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE,
                    GemClassName, string.Empty, WS_POPUP,
                    OffscreenPos, OffscreenPos, _pxW, _pxH,
                    _ownerHwnd, IntPtr.Zero, GetModuleHandleW(null), IntPtr.Zero);
                Log($"early: CreateWindowExW -> 0x{_gemHwnd:X} gle={Marshal.GetLastWin32Error()} dpi={_dpi} px={_pxW}x{_pxH}");
                if (_gemHwnd == IntPtr.Zero)
                    return;

                // Blank premultiplied surface: invisible and click-through
                // (zero-alpha pixels don't hit-test) until revealed. Parked
                // OFF-SCREEN: if any code path ever paints pixels before a
                // real position is applied, the result is an invisible gem —
                // never a flash at the screen origin.
                PaintLayered(_gemHwnd, OffscreenPos, OffscreenPos, _pxW, _pxH, new byte[_pxW * _pxH * 4]);
                ShowWindow(_gemHwnd, SW_SHOWNOACTIVATE);
                Log("early: shown (transparent)");
            }
            catch (Exception ex)
            {
                Log($"early EXCEPTION {ex}");
            }
        }

        /// <summary>Decodes and caches the gem pixels for the current DPI.</summary>
        private static async Task DecodePixelsAsync()
        {
            try
            {
                _pixels = await DecodeGemAsync(_pxW, _pxH);
                Log(_pixels == null ? "decode returned null" : $"decoded {_pixels.Length} bytes");
            }
            catch (Exception ex)
            {
                Log($"decode EXCEPTION {ex}");
            }
        }

        private static void Inject(Microsoft.UI.Xaml.Window window)
        {
            if (_root != null)
                return; // already injected
            if (window.Content is not FrameworkElement root)
                return;

            Log("Inject (content present)");
            _root = root;

            // Keep the gem glued to the bar through layout, move, resize and
            // DPI changes.
            root.SizeChanged += (s, e) => Reposition();
            if (window.AppWindow != null)
            {
                window.AppWindow.Changed += OnAppWindowChanged;
                // Seed the presenter state now: otherwise the first Changed
                // event (which may well be the user's first maximize/restore
                // click) is mistaken for initialization and skips the fade.
                _lastPresenterState = (window.AppWindow.Presenter as OverlappedPresenter)?.State;
            }
            window.VisibilityChanged += OnWindowVisibilityChanged;

            // The native WebView2 may not be in the visual tree yet on a cold
            // start (the one-shot wiring missing it is what made the gem fall
            // back to the timeout reveal). Retry until it exists.
            var wire = window.DispatcherQueue.CreateTimer();
            wire.Interval = TimeSpan.FromMilliseconds(250);
            wire.IsRepeating = true;
            wire.Tick += (s, e) =>
            {
                if (TryWireReadySignal(window, root))
                    wire.Stop();
            };
            wire.Start();

            // Safety net only — the normal path is the web ready message. Long
            // enough that a slow cold start doesn't reveal over the loading page.
            var fallback = window.DispatcherQueue.CreateTimer();
            fallback.Interval = TimeSpan.FromSeconds(15);
            fallback.IsRepeating = false;
            fallback.Tick += (s, e) => { fallback.Stop(); Reveal(); };
            fallback.Start();
        }

        /// <summary>
        /// Runs a probe in the web view that posts <see cref="ReadyMessage"/> once
        /// the app UI (nav bar) exists, and reveals the gem when it arrives.
        /// Returns false while the WebView2 isn't in the visual tree yet.
        /// </summary>
        private static bool TryWireReadySignal(Microsoft.UI.Xaml.Window window, FrameworkElement root)
        {
            var webView = FindWebView2(root);
            if (webView == null)
                return false;

            void Hook()
            {
                var core = webView.CoreWebView2;
                if (core == null)
                    return;

                try
                {
                    core.WebMessageReceived += (s, e) =>
                    {
                        try
                        {
                            if (e.TryGetWebMessageAsString() == ReadyMessage)
                                window.DispatcherQueue.TryEnqueue(Reveal);
                        }
                        catch { }
                    };

                    // Run on future documents and the current one.
                    _ = core.AddScriptToExecuteOnDocumentCreatedAsync(ReadyProbeScript);
                    _ = core.ExecuteScriptAsync(ReadyProbeScript);
                }
                catch { }
            }

            if (webView.CoreWebView2 != null)
                Hook();
            else
                webView.CoreWebView2Initialized += (s, e) => Hook();

            return true;
        }

        private static void Reveal()
        {
            if (_revealed || _window == null)
                return;
            _revealed = true;
            _ = RevealAsync();
        }

        private static async Task RevealAsync()
        {
            try
            {
                // Safety net: if the early creation failed, retry it now
                // (synchronous and idempotent — cannot double-create).
                if (_gemHwnd == IntPtr.Zero)
                {
                    CreateGemWindowEarly();
                    if (_gemHwnd == IntPtr.Zero)
                        return;
                    _pixelsTask = DecodePixelsAsync();
                }

                // Await the one decode task; never start a competing one.
                if (_pixelsTask != null)
                    await _pixelsTask;

                // Monitor DPI may have changed since the early decode.
                uint dpi = GetDpiForWindow(_ownerHwnd);
                if (dpi != 0 && dpi != _dpi)
                {
                    _dpi = dpi;
                    double px = _dpi / 96.0;
                    _pxW = (int)Math.Round(ImgWDips * px);
                    _pxH = (int)Math.Round(ImgHDips * px);
                    _pixels = await DecodeGemAsync(_pxW, _pxH);
                }
                if (_pixels == null)
                {
                    Log("reveal: no pixels");
                    return;
                }

                // The reveal can race the startup maximize/layout: painting
                // with a not-yet-settled origin flashes the gem at the wrong
                // spot before the next layout event snaps it back. Wait until
                // two consecutive samples agree and the geometry looks real —
                // as long as it takes (an invisible gem beats a misplaced one).
                // After ~10s accept a merely-sane sample as a last resort.
                int x = 0, y = 0;
                bool stable = false;
                for (int i = 0; !stable; i++)
                {
                    ComputeTarget(out int x1, out int y1, out bool sane1);
                    await Task.Delay(100);
                    ComputeTarget(out x, out y, out bool sane2);
                    stable = (sane1 && sane2 && x == x1 && y == y1) || (sane2 && i >= 100);
                }

                // The window is already shown and composited; revealing is a
                // pure surface update (position + pixels in one ULW call).
                // Set the position at zero alpha, then fade in.
                PaintLayered(_gemHwnd, x, y, _pxW, _pxH, _pixels, 0);
                _placed = true;
                Log($"revealed target=({x},{y})");
                _ = FadeInAsync();
            }
            catch (Exception ex)
            {
                // The gem is decorative; never take the app down over it.
                Log($"reveal EXCEPTION {ex}");
            }
        }

        /// <summary>Screen coordinates of the gem's top-left corner.</summary>
        private static void ComputeTarget(out int x, out int y)
            => ComputeTarget(out x, out y, out _);

        /// <summary>
        /// Screen coordinates of the gem's top-left corner. <paramref name="sane"/>
        /// is false while the window/layout hasn't settled (degenerate client
        /// rect or WebView2 not yet measurable) and the result can't be trusted.
        /// </summary>
        private static void ComputeTarget(out int x, out int y, out bool sane)
        {
            x = 0;
            y = 0;
            sane = false;
            if (_ownerHwnd == IntPtr.Zero)
                return;

            var origin = default(POINT);
            ClientToScreen(_ownerHwnd, ref origin);
            GetClientRect(_ownerHwnd, out RECT client);

            // Bar top in DIPs (the WebView2's top edge inside the XAML root).
            double barTopDips = 0;
            bool barTopKnown = false;
            var webView = FindWebView2(_root);
            if (webView != null && _root != null)
            {
                try
                {
                    var transform = webView.TransformToVisual(_root);
                    barTopDips = transform.TransformPoint(new Point(0, 0)).Y;
                    barTopKnown = barTopDips > 0;
                }
                catch { }
            }

            // Position so the bar's top edge hits the hexagon's upper flat corners.
            double yDips = Math.Max(0, barTopDips - FlatTopUnits * Scale);
            double px = _dpi / 96.0;

            x = origin.X + (client.Right - client.Left - _pxW) / 2;
            y = origin.Y + (int)Math.Round(yDips * px);
            sane = barTopKnown && (client.Right - client.Left) >= _pxW * 2;
        }

        /// <summary>Re-pins the gem after the owner moved, resized or changed DPI.</summary>
        private static void Reposition()
        {
            if (_gemHwnd == IntPtr.Zero || _ownerHwnd == IntPtr.Zero)
                return;

            // A minimized owner reports garbage geometry (-32000); moving the
            // gem there makes it flash at a wrong spot when the owner is
            // restored (owned windows re-show before layout events run).
            if (IsIconic(_ownerHwnd))
                return;

            uint dpi = GetDpiForWindow(_ownerHwnd);
            if (dpi != 0 && dpi != _dpi)
            {
                // Monitor DPI changed: re-render the bitmap at the new scale.
                _ = RerenderForDpiAsync(dpi);
                return;
            }

            ComputeTarget(out int x, out int y, out bool sane);
            if (!sane)
                return;

            SetWindowPos(_gemHwnd, IntPtr.Zero, x, y, 0, 0,
                SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE);
            _curX = x;
            _curY = y;
        }

        private static async Task RerenderForDpiAsync(uint dpi)
        {
            try
            {
                _dpi = dpi;
                double px = _dpi / 96.0;
                _pxW = (int)Math.Round(ImgWDips * px);
                _pxH = (int)Math.Round(ImgHDips * px);

                _pixels = await DecodeGemAsync(_pxW, _pxH);
                if (_pixels == null || _gemHwnd == IntPtr.Zero)
                    return;

                ComputeTarget(out int x, out int y, out bool sane);
                if (!sane)
                {
                    // Keep the last trusted position rather than a bogus one.
                    x = _curX;
                    y = _curY;
                }
                // Before the reveal has placed the gem the surface must stay
                // transparent.
                byte[] surface = (_revealed && _placed) ? _pixels : new byte[_pxW * _pxH * 4];
                PaintLayered(_gemHwnd, x, y, _pxW, _pxH, surface);
            }
            catch { }
        }

        private static void OnAppWindowChanged(AppWindow sender, AppWindowChangedEventArgs args)
        {
            if (!args.DidPositionChange && !args.DidSizeChange)
                return;

            _window?.DispatcherQueue.TryEnqueue(() =>
            {
                var state = (_window?.AppWindow?.Presenter as OverlappedPresenter)?.State;
                bool known = _lastPresenterState != null;
                bool fromMinimized = _lastPresenterState == OverlappedPresenterState.Minimized;
                bool stateChanged = state != null && known && state != _lastPresenterState;
                if (state != null)
                    _lastPresenterState = state;

                // Maximize <-> windowed: the gem would jump to its new spot
                // while the OS window animation is still playing. Hide it and
                // fade it in at the settled position instead. Minimize/restore
                // is owned by the visibility handler; a plain drag-resize
                // (same state) keeps live tracking.
                if (stateChanged
                    && state != OverlappedPresenterState.Minimized
                    && !fromMinimized
                    && _revealed && _placed)
                {
                    _ = TransitionRepositionAsync();
                }
                else
                {
                    Reposition();
                }
            });
        }

        /// <summary>
        /// Maximize/restore transition: hide the gem, wait for the window
        /// animation and layout to settle, then fade in at the new position.
        /// </summary>
        private static async Task TransitionRepositionAsync()
        {
            if (_gemHwnd == IntPtr.Zero || _pixels == null)
                return;

            int gen = ++_moveGen;
            _fadeGen++; // cancel any running fade
            PaintLayered(_gemHwnd, _curX, _curY, _pxW, _pxH, _pixels, 0);

            // Same rule as the reveal: never paint at a position that wasn't
            // sane — wait as long as it takes, relax to a single sane sample
            // after ~4s, but a garbage position is never used.
            int x = _curX, y = _curY;
            for (int i = 0; ; i++)
            {
                ComputeTarget(out int x1, out int y1, out bool sane1);
                await Task.Delay(100);
                ComputeTarget(out x, out y, out bool sane2);
                if (gen != _moveGen)
                    return; // superseded by a newer transition
                if ((sane1 && sane2 && x == x1 && y == y1) || (sane2 && i >= 40))
                    break;
            }

            PaintLayered(_gemHwnd, x, y, _pxW, _pxH, _pixels, 0);
            _ = FadeInAsync();
        }

        /// <summary>
        /// The gem hides/shows automatically with its owner (owned window).
        /// Around a minimize/restore, pre-zero the alpha so the automatic
        /// re-show doesn't pop at full opacity, then fade back in.
        /// </summary>
        private static void OnWindowVisibilityChanged(object sender, WindowVisibilityChangedEventArgs args)
        {
            if (!_revealed || !_placed || _gemHwnd == IntPtr.Zero || _pixels == null)
                return;

            if (!args.Visible)
            {
                _fadeGen++; // cancel any running fade
                PaintLayered(_gemHwnd, _curX, _curY, _pxW, _pxH, _pixels, 0);
            }
            else
            {
                Reposition();
                _ = FadeInAsync();
            }
        }

        /// <summary>
        /// Decodes the packaged gem PNG scaled to the requested pixel size, as
        /// premultiplied BGRA (the format UpdateLayeredWindow expects).
        /// </summary>
        private static async Task<byte[]?> DecodeGemAsync(int pxW, int pxH)
        {
            string path = Path.Combine(AppContext.BaseDirectory, GemBitmapRelPath);
            if (!File.Exists(path) || pxW <= 0 || pxH <= 0)
                return null;

            byte[] file = File.ReadAllBytes(path);

            using var stream = new InMemoryRandomAccessStream();
            using (var writer = new DataWriter(stream.GetOutputStreamAt(0)))
            {
                writer.WriteBytes(file);
                await writer.StoreAsync();
            }
            stream.Seek(0);

            var decoder = await BitmapDecoder.CreateAsync(stream);
            var transform = new BitmapTransform
            {
                ScaledWidth = (uint)pxW,
                ScaledHeight = (uint)pxH,
                InterpolationMode = BitmapInterpolationMode.Fant,
            };
            var data = await decoder.GetPixelDataAsync(
                BitmapPixelFormat.Bgra8,
                BitmapAlphaMode.Premultiplied,
                transform,
                ExifOrientationMode.IgnoreExifOrientation,
                ColorManagementMode.DoNotColorManage);
            return data.DetachPixelData();
        }

        /// <summary>
        /// Fades the gem in over ~200ms by ramping the layered window's
        /// constant alpha. Restartable; a newer fade cancels an older one.
        /// </summary>
        private static async Task FadeInAsync()
        {
            // Never fade in before a trusted position was applied — a fade at
            // the placeholder origin is exactly the top-left flash.
            if (!_placed || _gemHwnd == IntPtr.Zero || _pixels == null)
                return;

            int gen = ++_fadeGen;
            const int DurationMs = 120;
            const int StepMs = 16;

            for (int t = 0; t <= DurationMs; t += StepMs)
            {
                if (gen != _fadeGen || _gemHwnd == IntPtr.Zero || _pixels == null)
                    return;
                byte alpha = (byte)Math.Min(255, 255 * t / DurationMs);
                PaintLayered(_gemHwnd, _curX, _curY, _pxW, _pxH, _pixels, alpha);
                await Task.Delay(StepMs);
            }

            if (gen == _fadeGen && _gemHwnd != IntPtr.Zero && _pixels != null)
                PaintLayered(_gemHwnd, _curX, _curY, _pxW, _pxH, _pixels, 255);
        }

        /// <summary>Uploads the bitmap and position to the layered window in one call.</summary>
        private static void PaintLayered(IntPtr hwnd, int x, int y, int w, int h, byte[] bgraPremul, byte constantAlpha = 255)
        {
            _curX = x;
            _curY = y;
            IntPtr screenDC = GetDC(IntPtr.Zero);
            IntPtr memDC = CreateCompatibleDC(screenDC);
            IntPtr dib = IntPtr.Zero;
            IntPtr oldBmp = IntPtr.Zero;

            try
            {
                var bmi = new BITMAPINFO
                {
                    biSize = Marshal.SizeOf<BITMAPINFO>(),
                    biWidth = w,
                    biHeight = -h, // top-down
                    biPlanes = 1,
                    biBitCount = 32,
                    biCompression = 0, // BI_RGB
                };
                dib = CreateDIBSection(screenDC, ref bmi, 0, out IntPtr bits, IntPtr.Zero, 0);
                if (dib == IntPtr.Zero || bits == IntPtr.Zero)
                {
                    Log($"CreateDIBSection failed gle={Marshal.GetLastWin32Error()}");
                    return;
                }

                Marshal.Copy(bgraPremul, 0, bits, Math.Min(bgraPremul.Length, w * h * 4));
                oldBmp = SelectObject(memDC, dib);

                var dst = new POINT { X = x, Y = y };
                var size = new SIZE { CX = w, CY = h };
                var src = default(POINT);
                var blend = new BLENDFUNCTION
                {
                    BlendOp = AC_SRC_OVER,
                    SourceConstantAlpha = constantAlpha,
                    AlphaFormat = AC_SRC_ALPHA,
                };
                bool ok = UpdateLayeredWindow(hwnd, screenDC, ref dst, ref size, memDC, ref src, 0, ref blend, ULW_ALPHA);
                Log($"UpdateLayeredWindow -> {ok} gle={Marshal.GetLastWin32Error()}");
            }
            finally
            {
                if (oldBmp != IntPtr.Zero)
                    SelectObject(memDC, oldBmp);
                if (dib != IntPtr.Zero)
                    DeleteObject(dib);
                DeleteDC(memDC);
                ReleaseDC(IntPtr.Zero, screenDC);
            }
        }

        private static async Task NavigateHomeAsync()
        {
            try
            {
                var webView = FindWebView2(_root);
                if (webView?.CoreWebView2 != null)
                    await webView.CoreWebView2.ExecuteScriptAsync(NavHomeScript);
            }
            catch
            {
                // A failed navigation must never crash the shell.
            }
        }

        private static WebView2? FindWebView2(DependencyObject? root)
        {
            if (root == null)
                return null;
            if (root is WebView2 webView)
                return webView;

            int count = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < count; i++)
            {
                var found = FindWebView2(VisualTreeHelper.GetChild(root, i));
                if (found != null)
                    return found;
            }

            return null;
        }

        // ── Win32 window class / message handling ──

        private const string GemClassName = "DiceAudioTitleBarGem";
        private static bool _classRegistered;
        // Kept alive in a static so the marshalled function pointer stays valid.
        private static readonly WndProcDelegate GemWndProcKeepAlive = GemWndProc;

        private static void RegisterGemClass()
        {
            if (_classRegistered)
                return;

            var wc = new WNDCLASSEXW
            {
                cbSize = (uint)Marshal.SizeOf<WNDCLASSEXW>(),
                lpfnWndProc = Marshal.GetFunctionPointerForDelegate(GemWndProcKeepAlive),
                hInstance = GetModuleHandleW(null),
                hCursor = LoadCursorW(IntPtr.Zero, IDC_HAND),
                lpszClassName = GemClassName,
            };
            RegisterClassExW(ref wc);
            _classRegistered = true;
        }

        private static IntPtr GemWndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
        {
            switch (msg)
            {
                case WM_MOUSEACTIVATE:
                    return (IntPtr)MA_NOACTIVATE; // click without stealing focus
                case WM_LBUTTONDOWN:
                    _ = NavigateHomeAsync(); // wndproc runs on the UI thread
                    return IntPtr.Zero;
            }

            return DefWindowProcW(hWnd, msg, wParam, lParam);
        }

        // ── P/Invoke ──

        private delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        private const uint WS_POPUP = 0x80000000;
        private const uint WS_EX_LAYERED = 0x00080000;
        private const uint WS_EX_TOOLWINDOW = 0x00000080;
        private const uint WS_EX_NOACTIVATE = 0x08000000;
        private const int SW_SHOWNOACTIVATE = 4;
        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOZORDER = 0x0004;
        private const uint SWP_NOACTIVATE = 0x0010;
        private const uint ULW_ALPHA = 2;
        private const byte AC_SRC_OVER = 0;
        private const byte AC_SRC_ALPHA = 1;
        private const uint WM_LBUTTONDOWN = 0x0201;
        private const uint WM_MOUSEACTIVATE = 0x0021;
        private const int MA_NOACTIVATE = 3;
        private static readonly IntPtr IDC_HAND = (IntPtr)32649;

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT { public int X; public int Y; }

        [StructLayout(LayoutKind.Sequential)]
        private struct SIZE { public int CX; public int CY; }

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT { public int Left; public int Top; public int Right; public int Bottom; }

        [StructLayout(LayoutKind.Sequential)]
        private struct BLENDFUNCTION
        {
            public byte BlendOp;
            public byte BlendFlags;
            public byte SourceConstantAlpha;
            public byte AlphaFormat;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct BITMAPINFO
        {
            public int biSize;
            public int biWidth;
            public int biHeight;
            public short biPlanes;
            public short biBitCount;
            public int biCompression;
            public int biSizeImage;
            public int biXPelsPerMeter;
            public int biYPelsPerMeter;
            public int biClrUsed;
            public int biClrImportant;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct WNDCLASSEXW
        {
            public uint cbSize;
            public uint style;
            public IntPtr lpfnWndProc;
            public int cbClsExtra;
            public int cbWndExtra;
            public IntPtr hInstance;
            public IntPtr hIcon;
            public IntPtr hCursor;
            public IntPtr hbrBackground;
            [MarshalAs(UnmanagedType.LPWStr)] public string? lpszMenuName;
            [MarshalAs(UnmanagedType.LPWStr)] public string lpszClassName;
            public IntPtr hIconSm;
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern ushort RegisterClassExW(ref WNDCLASSEXW lpwcx);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr CreateWindowExW(
            uint dwExStyle, string lpClassName, string lpWindowName, uint dwStyle,
            int x, int y, int nWidth, int nHeight,
            IntPtr hWndParent, IntPtr hMenu, IntPtr hInstance, IntPtr lpParam);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr DefWindowProcW(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
            int x, int y, int cx, int cy, uint uFlags);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr hdcDst,
            ref POINT pptDst, ref SIZE psize, IntPtr hdcSrc, ref POINT pptSrc,
            uint crKey, ref BLENDFUNCTION pblend, uint dwFlags);

        [DllImport("user32.dll")]
        private static extern bool ClientToScreen(IntPtr hWnd, ref POINT lpPoint);

        [DllImport("user32.dll")]
        private static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll")]
        private static extern uint GetDpiForWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool IsIconic(IntPtr hWnd);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr LoadCursorW(IntPtr hInstance, IntPtr lpCursorName);

        [DllImport("user32.dll")]
        private static extern IntPtr GetDC(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr GetModuleHandleW(string? lpModuleName);

        [DllImport("gdi32.dll")]
        private static extern IntPtr CreateCompatibleDC(IntPtr hdc);

        [DllImport("gdi32.dll")]
        private static extern bool DeleteDC(IntPtr hdc);

        [DllImport("gdi32.dll")]
        private static extern IntPtr SelectObject(IntPtr hdc, IntPtr hgdiobj);

        [DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr hObject);

        [DllImport("gdi32.dll", SetLastError = true)]
        private static extern IntPtr CreateDIBSection(IntPtr hdc, ref BITMAPINFO pbmi,
            uint usage, out IntPtr ppvBits, IntPtr hSection, uint dwOffset);
    }
}
#endif
