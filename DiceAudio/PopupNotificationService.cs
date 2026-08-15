/*
 * DiceAudio - Copyright (C) 2025 Yann Charbon
 * SPDX-License-Identifier: GPL-3.0-or-later
 *
 * This file is part of DiceAudio, released under the GNU GPL v3.
 * See the LICENSE file in the repository root for details.
 */

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DiceAudio
{
    public class PopupNotificationService
    {
        public enum Type { SUCCESS, FAILURE }

        public event Action<string, string, Type, int> OnShow;
        public event Action OnClose;

        public void Show(string title, string message, Type type, int durationMs)
        {
            OnShow?.Invoke(title, message, type, durationMs);
        }

        public void Close()
        {
            OnClose?.Invoke();
        }
    }

}
