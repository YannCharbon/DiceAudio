/*
 * DiceAudio - Copyright (C) 2025 Yann Charbon
 * SPDX-License-Identifier: GPL-3.0-or-later
 *
 * This file is part of DiceAudio, released under the GNU GPL v3.
 * See the LICENSE file in the repository root for details.
 */

// Keyboard behaviour of every dialog drawn as .da-confirm (DaConfirm, the
// import wizard, the export dialog, the notice window, the data folder
// questions): the focus moves into the dialog when it opens, Tab cycles
// inside it and never reaches the page behind, and Escape presses the
// button marked data-da-cancel, when the dialog has one and it is enabled.
// The topmost dialog is the last one in the page.
(function () {
    var FOCUSABLE = 'button:not([disabled]), [href], input:not([disabled]), select:not([disabled]), ' +
                    'textarea:not([disabled]), [tabindex]:not([tabindex="-1"])';

    function topDialog() {
        var all = document.querySelectorAll('.da-confirm');
        return all.length ? all[all.length - 1] : null;
    }

    function focusables(dialog) {
        return Array.prototype.filter.call(dialog.querySelectorAll(FOCUSABLE),
            function (el) { return el.offsetParent !== null || el === document.activeElement; });
    }

    function focusInto(dialog) {
        var list = focusables(dialog);
        var first = list.find(function (el) { return el.tagName !== 'BUTTON'; }) || list[0];
        if (first) first.focus();
        else {
            dialog.setAttribute('tabindex', '-1');
            dialog.focus();
        }
    }

    var lastDialog = null;

    // A dialog that opens, or whose content was redrawn while the focus was
    // on a control that went away, takes the focus.
    new MutationObserver(function () {
        var dialog = topDialog();
        if (dialog && (dialog !== lastDialog || !dialog.contains(document.activeElement))) focusInto(dialog);
        lastDialog = dialog;
    }).observe(document.documentElement, { childList: true, subtree: true });

    document.addEventListener('focusin', function (e) {
        var dialog = topDialog();
        if (dialog && !dialog.contains(e.target)) focusInto(dialog);
    });

    document.addEventListener('keydown', function (e) {
        var dialog = topDialog();
        if (!dialog) return;

        if (e.key === 'Escape') {
            var cancel = dialog.querySelector('[data-da-cancel]:not([disabled])');
            if (cancel) {
                e.preventDefault();
                cancel.click();
            }
            return;
        }

        if (e.key !== 'Tab') return;
        var list = focusables(dialog);
        if (list.length === 0) { e.preventDefault(); return; }
        var first = list[0], last = list[list.length - 1];
        if (!dialog.contains(document.activeElement)) {
            e.preventDefault();
            first.focus();
        } else if (e.shiftKey && document.activeElement === first) {
            e.preventDefault();
            last.focus();
        } else if (!e.shiftKey && document.activeElement === last) {
            e.preventDefault();
            first.focus();
        }
    });
})();
