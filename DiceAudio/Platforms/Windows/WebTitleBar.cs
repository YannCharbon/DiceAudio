/*
 * DiceAudio - Copyright (C) 2025 Yann Charbon
 * SPDX-License-Identifier: GPL-3.0-or-later
 *
 * This file is part of DiceAudio, released under the GNU GPL v3.
 * See the LICENSE file in the repository root for details.
 */

#if WINDOWS
using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

// Disambiguate from the MAUI types of the same name (ImplicitUsings pulls in both).
using Visibility = Microsoft.UI.Xaml.Visibility;
using Grid = Microsoft.UI.Xaml.Controls.Grid;
using Control = Microsoft.UI.Xaml.Controls.Control;

namespace DiceAudio
{
    /// <summary>
    /// Windows-only. Makes the BlazorWebView occupy the full window height —
    /// including the strip MAUI normally reserves for the native title bar —
    /// so the web app bar (and its gem) IS the title bar, the way Teams and
    /// the new Outlook do it. Window dragging comes from WebView2 non-client
    /// region support: page elements with CSS `app-region: drag` hit-test as
    /// title bar (drag, double-click maximize, system menu). The system
    /// caption buttons remain drawn by the OS above the web content.
    /// </summary>
    internal static class WebTitleBar
    {
        private static void Log(string msg)
        {
            System.Diagnostics.Debug.WriteLine($"WebTitleBar: {msg}");
        }

        private static Microsoft.UI.Xaml.Window? _window;

        public static void Attach(Microsoft.UI.Xaml.Window window)
        {
            _window = window;
            var poll = window.DispatcherQueue.CreateTimer();
            poll.Interval = TimeSpan.FromMilliseconds(100);
            poll.IsRepeating = true;
            poll.Tick += (s, e) =>
            {
                if (window.Content is not FrameworkElement root)
                    return;
                var webView = FindWebView2(root);
                if (webView == null)
                    return;
                poll.Stop();

                EnableNonClientRegions(webView);
                Enforce(root, webView);

                // MAUI re-applies the native strip (visibility AND the 32px
                // content margin) whenever the window state changes — e.g.
                // maximized -> windowed via the caption button or by dragging
                // the strip. Re-enforce immediately on those events, with a
                // slow periodic backstop for anything that slips through.
                if (window.AppWindow != null)
                {
                    window.AppWindow.Changed += (s2, a) =>
                    {
                        if (a.DidSizeChange || a.DidPositionChange || a.DidPresenterChange)
                            window.DispatcherQueue.TryEnqueue(() => Enforce(root, webView));
                    };
                }
                root.SizeChanged += (s2, e2) => Enforce(root, webView);

                var check = window.DispatcherQueue.CreateTimer();
                check.Interval = TimeSpan.FromMilliseconds(500);
                check.IsRepeating = true;
                check.Tick += (s2, e2) => Enforce(root, webView);
                check.Start();
            };
            poll.Start();
        }

        /// <summary>
        /// Keeps the web content at the window's top edge: collapses MAUI's
        /// native title-bar strip and zeroes the content offset MAUI parks on
        /// the NavigationView's ContentGrid. Idempotent — safe (and cheap) to
        /// run after every window/layout event, because MAUI re-applies both
        /// on state changes (e.g. maximized -> windowed).
        /// </summary>
        private static void Enforce(FrameworkElement root, WebView2 webView)
        {
            try
            {
                // MAUI's window initialization (and possibly later state
                // handling) writes the AppWindow title-bar state after
                // OnWindowCreated, reverting the OS-level extension set there.
                // Re-assert it whenever it flips back.
                var titleBar = _window?.AppWindow?.TitleBar;
                if (titleBar != null && !titleBar.ExtendsContentIntoTitleBar)
                {
                    titleBar.ExtendsContentIntoTitleBar = true;
                    Log("re-extended AppWindow title bar");
                }

                Walk(root, fe =>
                {
                    if (fe.Visibility != Visibility.Collapsed &&
                        !string.IsNullOrEmpty(fe.Name) &&
                        fe.Name.Contains("TitleBar", StringComparison.OrdinalIgnoreCase))
                    {
                        Log($"collapsing '{fe.Name}' ({fe.GetType().Name}, h={fe.ActualHeight})");
                        fe.Visibility = Visibility.Collapsed;
                    }
                });

                // MAUI re-applies a 32px top margin on the NavigationView's
                // 'ContentGrid' on EVERY layout pass — zeroing it just loses a
                // war (visible as rapid oscillation during drag-resize).
                // Instead, compensate: a negative top margin of the same size
                // on the child element along the webview's chain. Both values
                // are then stable, so no relayout ping-pong occurs.
                DependencyObject? node = webView;
                while (node is FrameworkElement fe)
                {
                    if (VisualTreeHelper.GetParent(fe) is FrameworkElement parent)
                    {
                        if (parent.Name == "ContentGrid")
                        {
                            double want = -parent.Margin.Top;
                            if (Math.Abs(fe.Margin.Top - want) > 0.1)
                            {
                                var m = fe.Margin;
                                m.Top = want;
                                fe.Margin = m;
                                Log($"compensating ContentGrid margin: {want} on {fe.GetType().Name} '{fe.Name}'");
                            }
                            break;
                        }
                        node = parent;
                    }
                    else
                    {
                        break;
                    }
                }
            }
            catch { }
        }

        /// <summary>
        /// Turns on WebView2 draggable-region support (CSS app-region). The
        /// setting only affects documents loaded afterwards, so if the page is
        /// already up we reload once (cheap at startup, local content).
        /// </summary>
        private static void EnableNonClientRegions(WebView2 webView)
        {
            void Apply()
            {
                try
                {
                    var core = webView.CoreWebView2;
                    if (core == null)
                        return;
                    core.Settings.IsNonClientRegionSupportEnabled = true;

                    // Only reload if the APP document is already loaded (the
                    // setting applies from the next navigation). Reloading
                    // while the webview still shows about:blank hijacks
                    // Blazor's initial navigation into the external-link
                    // handler ("open 'about' link" popup, blank app).
                    string src = core.Source ?? string.Empty;
                    bool appLoaded = src.StartsWith("http", StringComparison.OrdinalIgnoreCase);
                    Log($"IsNonClientRegionSupportEnabled=true src='{src}' reload={appLoaded}");
                    if (appLoaded)
                        core.Reload();
                }
                catch (Exception ex)
                {
                    Log($"non-client EXCEPTION {ex.Message}");
                }
            }

            if (webView.CoreWebView2 != null)
                Apply();
            else
                webView.CoreWebView2Initialized += (s, e) => Apply();
        }

        private static void Walk(DependencyObject node, Action<FrameworkElement> visit)
        {
            if (node is FrameworkElement fe)
                visit(fe);
            int count = VisualTreeHelper.GetChildrenCount(node);
            for (int i = 0; i < count; i++)
                Walk(VisualTreeHelper.GetChild(node, i), visit);
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
    }
}
#endif
