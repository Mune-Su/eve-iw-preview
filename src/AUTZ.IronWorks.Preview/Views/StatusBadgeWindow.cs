// EVE-IW-Preview
// Copyright (C) 2026 Mune-Su
// SPDX-License-Identifier: GPL-3.0-only
// This program is free software, licensed under the GNU GPL version 3. It comes with NO WARRANTY. See the LICENSE file.
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using AUTZ.IronWorks.Preview.Core;

namespace AUTZ.IronWorks.Preview.Views;

/// <summary>A small badge at the top of the screen ("HOTKEYS PAUSED"). Clicks go straight through it and it never takes focus.</summary>
public sealed class StatusBadgeWindow : Window
{
    private readonly TextBlock _text = new()
    {
        FontSize = 12,
        FontWeight = FontWeights.Bold,
        Foreground = Brushes.White
    };

    public StatusBadgeWindow(string text)
    {
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        Focusable = false;
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.Manual;
        _text.Text = text;
        Content = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(225, 12, 12, 14)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(249, 115, 22)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(3),
            Padding = new Thickness(12, 5, 12, 5),
            Child = _text
        };
        SourceInitialized += (_, _) =>
        {
            nint hwnd = new WindowInteropHelper(this).Handle;
            int style = NativeMethods.GetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE);
            NativeMethods.SetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE,
                style | NativeMethods.WS_EX_TRANSPARENT | NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_NOACTIVATE);
        };
        Loaded += (_, _) => PlaceTopCentre();
    }

    private void PlaceTopCentre()
    {
        var area = SystemParameters.WorkArea;
        Left = area.Left + (area.Width - ActualWidth) / 2;
        Top = area.Top + 6;
    }
}
