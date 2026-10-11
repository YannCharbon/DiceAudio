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
    }
}
