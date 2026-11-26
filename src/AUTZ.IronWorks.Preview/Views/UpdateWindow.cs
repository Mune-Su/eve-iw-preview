// EVE-IW-Preview
// Copyright (C) 2026 Mune-Su
// SPDX-License-Identifier: GPL-3.0-only
// This program is free software, licensed under the GNU GPL version 3. It comes with NO WARRANTY. See the LICENSE file.
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using AUTZ.IronWorks.Preview.Core;

namespace AUTZ.IronWorks.Preview.Views;

/// <summary>
/// Shown only while an update downloads. The crucible (the app's icon) pours lava, and the lava flows along the bar as the
/// download goes. It has no buttons, so it can't be closed half-way.
/// </summary>
internal sealed class UpdateWindow : Window
{
    private const double BarWidth = 436;       // the trough the lava flows along
    private const double BarHeight = 18;
    private const double BarTop = 70;
    private const double PoolWidth = 26;       // the lava is already this long when the download starts (it is pouring out of the crucible)

    private readonly Rectangle _lava = new() { RadiusX = 9, RadiusY = 9, Height = BarHeight, HorizontalAlignment = HorizontalAlignment.Left };
    private readonly Rectangle _shine = new() { RadiusX = 9, RadiusY = 9, Height = BarHeight, HorizontalAlignment = HorizontalAlignment.Left, IsHitTestVisible = false };
    private readonly Ellipse _head = new() { Width = 30, Height = 30, IsHitTestVisible = false };
    private readonly Ellipse _drip = new() { Width = 7, Height = 10, IsHitTestVisible = false };
    private readonly Image _crucible = new() { Width = 60, Height = 60 };
    private readonly DropShadowEffect _glow = new() { Color = Color.FromRgb(0xF9, 0x73, 0x16), ShadowDepth = 0, BlurRadius = 12, Opacity = 0.7 };
    private readonly TextBlock _status = new() { FontSize = 12, Margin = new Thickness(22, 8, 22, 16) };

    private double _target;                    // what the download has reached, 0 to 1
    private double _shown;                     // what the lava shows now: it eases toward the target
    private bool _allowClose;
    private bool _rendering;

    public UpdateWindow(string version)
    {
        Title = "Updating " + AppInfo.ShortName;
        Width = 480;
        SizeToContent = SizeToContent.Height;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ShowInTaskbar = true;
        Topmost = true;
        Background = (Brush)FindResource("BrushBg");
        Foreground = (Brush)FindResource("BrushText");
        FontFamily = new FontFamily("Inter, Segoe UI");
        FontSize = 13;
        try { Icon = new System.Windows.Media.Imaging.BitmapImage(new Uri("pack://application:,,,/Assets/app.ico")); }
        catch { }

        var root = new StackPanel();
        root.Children.Add(new Border { Height = 3, Background = (Brush)FindResource("BrushAccent") });
        root.Children.Add(new TextBlock
        {
            Text = "Updating to v" + version,
            FontSize = 17,
            FontWeight = FontWeights.Bold,
            Foreground = Brushes.White,
            Margin = new Thickness(22, 16, 22, 8)
        });
        root.Children.Add(BuildScene());
        _status.Foreground = (Brush)FindResource("BrushMuted");
        _status.Text = "Downloading... 0%";
        root.Children.Add(_status);

        Content = new Border
        {
            BorderBrush = (Brush)FindResource("BrushAccentLine"),
            BorderThickness = new Thickness(1),
            Child = root
        };

        Loaded += (_, _) => StartAnimations();
        Closing += (_, e) =>
        {
            if (!_allowClose) e.Cancel = true;      // no buttons, and Alt+F4 doesn't close it half-way
        };
        Closed += (_, _) => StopRendering();
    }

    /// <summary>The crucible, its drip, and the trough with the lava in it.</summary>
    private Canvas BuildScene()
    {
        var scene = new Canvas { Width = BarWidth, Height = 96, Margin = new Thickness(22, 0, 22, 0), HorizontalAlignment = HorizontalAlignment.Left, ClipToBounds = false };

        // the trough
        var trough = new Border
        {
            Width = BarWidth,
            Height = BarHeight + 2,
            CornerRadius = new CornerRadius(10),
            Background = (Brush)FindResource("BrushPanel"),
            BorderBrush = (Brush)FindResource("BrushLine"),
            BorderThickness = new Thickness(1)
        };
        Canvas.SetLeft(trough, 0);
        Canvas.SetTop(trough, BarTop - 1);
        scene.Children.Add(trough);

        // the lava: deep red at the back, bright at the front
        _lava.Fill = new LinearGradientBrush(new GradientStopCollection
        {
            new GradientStop(Color.FromRgb(0x6B, 0x14, 0x08), 0.0),
            new GradientStop(Color.FromRgb(0xB9, 0x30, 0x0C), 0.35),
            new GradientStop(Color.FromRgb(0xF9, 0x73, 0x16), 0.8),
            new GradientStop(Color.FromRgb(0xFD, 0xD6, 0x7A), 1.0)
        }, 0);
        _lava.Effect = new DropShadowEffect { Color = Color.FromRgb(0xF9, 0x73, 0x16), ShadowDepth = 0, BlurRadius = 14, Opacity = 0.65 };
        Canvas.SetLeft(_lava, 1);
        Canvas.SetTop(_lava, BarTop);
        scene.Children.Add(_lava);

        // a shine that slides along the lava, so it looks like it is moving
        var slide = new TranslateTransform(-1, 0);
        _shine.Fill = new LinearGradientBrush(new GradientStopCollection
        {
            new GradientStop(Color.FromArgb(0, 0xFF, 0xE0, 0x9A), 0.0),
            new GradientStop(Color.FromArgb(120, 0xFF, 0xE0, 0x9A), 0.5),
            new GradientStop(Color.FromArgb(0, 0xFF, 0xE0, 0x9A), 1.0)
        }, 0) { RelativeTransform = slide };
        Canvas.SetLeft(_shine, 1);
        Canvas.SetTop(_shine, BarTop);
        scene.Children.Add(_shine);
        slide.BeginAnimation(TranslateTransform.XProperty,
            new DoubleAnimation(-1, 1, TimeSpan.FromSeconds(1.7)) { RepeatBehavior = RepeatBehavior.Forever });

        // the molten front
        _head.Fill = new RadialGradientBrush(new GradientStopCollection
        {
            new GradientStop(Color.FromArgb(255, 0xFF, 0xF1, 0xB8), 0.0),
            new GradientStop(Color.FromArgb(235, 0xFB, 0x92, 0x3C), 0.45),
            new GradientStop(Color.FromArgb(0, 0xF9, 0x73, 0x16), 1.0)
        });
        scene.Children.Add(_head);
        Canvas.SetTop(_head, BarTop + BarHeight / 2 - 15);

        // the crucible
        try { _crucible.Source = new System.Windows.Media.Imaging.BitmapImage(new Uri("pack://application:,,,/Assets/logo.png")); }
        catch { }
        System.Windows.Media.RenderOptions.SetBitmapScalingMode(_crucible, BitmapScalingMode.HighQuality);
        _crucible.Effect = _glow;
        Canvas.SetLeft(_crucible, 0);
        Canvas.SetTop(_crucible, 2);
        scene.Children.Add(_crucible);

        // the drip that falls from the crucible into the trough
        _drip.Fill = new LinearGradientBrush(Color.FromRgb(0xFD, 0xD6, 0x7A), Color.FromRgb(0xF9, 0x73, 0x16), 90);
        Canvas.SetLeft(_drip, 27);
        Canvas.SetTop(_drip, 52);
        scene.Children.Add(_drip);

        Place();
        return scene;
    }

    private void StartAnimations()
    {
        // the crucible glows and dims
        _glow.BeginAnimation(DropShadowEffect.BlurRadiusProperty,
            new DoubleAnimation(10, 26, TimeSpan.FromSeconds(1.1)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever });
        _glow.BeginAnimation(DropShadowEffect.OpacityProperty,
            new DoubleAnimation(0.55, 1.0, TimeSpan.FromSeconds(1.1)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever });

        // the drip falls and fades
        _drip.BeginAnimation(Canvas.TopProperty,
            new DoubleAnimation(50, BarTop - 4, TimeSpan.FromSeconds(0.8)) { RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn } });
        _drip.BeginAnimation(OpacityProperty,
            new DoubleAnimation(1, 0.2, TimeSpan.FromSeconds(0.8)) { RepeatBehavior = RepeatBehavior.Forever });

        if (!_rendering)
        {
            CompositionTarget.Rendering += OnRendering;
            _rendering = true;
        }
    }

    private void StopRendering()
    {
        if (!_rendering) return;
        CompositionTarget.Rendering -= OnRendering;
        _rendering = false;
    }

    /// <summary>Each frame the lava moves a little closer to where the download is, so it flows instead of jumping.</summary>
    private void OnRendering(object? sender, EventArgs e)
    {
        if (Math.Abs(_target - _shown) < 0.0005) return;
        _shown += (_target - _shown) * 0.12;
        Place();
    }

    /// <summary>Sets the lava's length and puts the molten front at its end.</summary>
    private void Place()
    {
        double length = Math.Min(BarWidth - 2, PoolWidth + _shown * (BarWidth - 2 - PoolWidth));
        _lava.Width = length;
        _shine.Width = length;
        Canvas.SetLeft(_head, 1 + length - 15 - 4);
    }

    /// <summary>The download has reached this much (0 to 1).</summary>
    public void SetProgress(double fraction)
    {
        _target = Math.Clamp(fraction, 0, 1);
        _status.Text = "Downloading... " + (int)Math.Round(_target * 100) + "%";
    }

    /// <summary>The download is done: the lava fills the bar, and the crucible burns bright.</summary>
    public void SetInstalling()
    {
        _target = 1;
        _status.Text = "Installing...";
        _status.Foreground = (Brush)FindResource("BrushAccentHover");
        _drip.BeginAnimation(OpacityProperty, null);
        _drip.Opacity = 0;
        _glow.BeginAnimation(DropShadowEffect.BlurRadiusProperty, null);
        _glow.BeginAnimation(DropShadowEffect.OpacityProperty, null);
        _glow.BlurRadius = 30;
        _glow.Opacity = 1;
    }

    /// <summary>Closes the window (the only way it closes).</summary>
    public void Finish()
    {
        _allowClose = true;
        Close();
    }
}
