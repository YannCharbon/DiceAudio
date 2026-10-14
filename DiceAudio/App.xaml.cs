/*
 * DiceAudio - Copyright (C) 2025 Yann Charbon
 * SPDX-License-Identifier: GPL-3.0-or-later
 *
 * This file is part of DiceAudio, released under the GNU GPL v3.
 * See the LICENSE file in the repository root for details.
 */

using Microsoft.Maui.Controls.PlatformConfiguration.AndroidSpecific;
using Application = Microsoft.Maui.Controls.Application;

namespace DiceAudio
{
    public partial class App : Application
    {
        public App()
        {
            InitializeComponent();

            Current.On<Microsoft.Maui.Controls.PlatformConfiguration.Android>().UseWindowSoftInputModeAdjust(WindowSoftInputModeAdjust.Resize);
        }

        // Replaces the deprecated MainPage assignment: MAUI builds the root page
        // through the window now. The caption is hidden because the web page draws
        // the title bar, but Windows still reads the window Title for the taskbar
        // tooltip, Alt+Tab and the window list - without it the app showed its
        // icon there with no name beside it.
        protected override Microsoft.Maui.Controls.Window CreateWindow(IActivationState? activationState)
        {
            return new Microsoft.Maui.Controls.Window(new MainPage()) { Title = "DiceAudio" };
        }

        /// <summary>
        /// Raised when the pages must be built again. The main layout answers by
        /// reloading the web page in place.
        /// </summary>
        public static event Action? PagesReloadRequested;

        /// <summary>
        /// Builds every page again, so that no page keeps an object from before
        /// a restore or an import.
        ///
        /// On Windows the web page is reloaded inside the same BlazorWebView:
        /// replacing the MAUI page creates a new WebView2 under a new navigation
        /// view, and WebTitleBar only lifts the first one into the title-bar
        /// strip, so the new one sat 32 px lower, under a dark band, and the
        /// window could no longer be dragged. Replacing the page is kept for the
        /// other platforms, and for the case where no layout is there to answer.
        /// </summary>
        public void ReloadPages()
        {
            var reloadInPlace = PagesReloadRequested;
#if WINDOWS
            if (reloadInPlace != null)
            {
                reloadInPlace();
                return;
            }
#endif
            if (Windows.Count > 0)
            {
                Windows[0].Page = new MainPage();
            }
        }
    }
}
