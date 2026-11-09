using System;
using System.Diagnostics;
using System.IO;
using Microsoft.UI;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using TitleBarStudio.Services;
using Windows.Graphics;
using Windows.UI;

namespace TitleBarStudio;

public sealed partial class MainWindow : Window
{
    private const double TallHeight = 48d;
    private const double StandardHeight = 32d;

    private readonly TitleBarRegionService _regions;

    public MainWindow()
    {
        InitializeComponent();

        Title = "TitleBar Studio";
        AppWindow.Resize(new SizeInt32(1120, 780));

        ConfigureTitleBar();
        ApplyMaterial(WindowMaterial.MicaBase);

        _regions = new TitleBarRegionService(
            this,
            AppTitleBar,
            TitleBarSearch,
            ThemeButton,
            HeightButton,
            RegionToggleButton,
            SettingsButton);

        _regions.RegionsChanged += (_, _) => RedrawRegions();

        AppTitleBar.Loaded += (_, _) => _regions.Attach();
        AppTitleBar.SizeChanged += (_, _) => UpdateCaptionButtonPadding();
        RootGrid.ActualThemeChanged += (_, _) => RedrawRegions();
    }

    private void ConfigureTitleBar()
    {
        // 1. Give the whole window to XAML.
        ExtendsContentIntoTitleBar = true;

        // 2. Tell Windows which element is the drag surface.
        SetTitleBar(AppTitleBar);

        if (AppWindowTitleBar.IsCustomizationSupported())
        {
            AppWindowTitleBar bar = AppWindow.TitleBar;
            bar.PreferredHeightOption = TitleBarHeightOption.Tall;

            // Let Mica show through behind the caption buttons.
            bar.ButtonBackgroundColor = Colors.Transparent;
            bar.ButtonInactiveBackgroundColor = Colors.Transparent;
        }

        ApplyIcon();
        UpdateCaptionButtonPadding();
    }

    /// <summary>
    /// Leaves room for the system caption buttons by shrinking our right-hand
    /// button stack, instead of padding the whole title bar (which would also
    /// shift the debug overlay).
    /// </summary>
    private void UpdateCaptionButtonPadding()
    {
        if (!ExtendsContentIntoTitleBar || !AppWindowTitleBar.IsCustomizationSupported())
        {
            return;
        }

        AppWindowTitleBar bar = AppWindow.TitleBar;
        RightActions.Margin = new Thickness(0, 0, Math.Max(bar.RightInset, 8), 0);
    }

    private void ApplyIcon()
    {
        string iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "app.ico");
        if (!File.Exists(iconPath))
        {
            return;
        }

        try
        {
            AppWindow.SetIcon(iconPath);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"SetIcon failed: {ex.Message}");
        }
    }

    private void ApplyMaterial(WindowMaterial material)
    {
        RootGrid.Background = null;

        switch (material)
        {
            case WindowMaterial.MicaAlt when MicaController.IsSupported():
                SystemBackdrop = new MicaBackdrop { Kind = MicaKind.BaseAlt };
                break;

            case WindowMaterial.Acrylic when DesktopAcrylicController.IsSupported():
                SystemBackdrop = new DesktopAcrylicBackdrop();
                break;

            case WindowMaterial.Solid:
                SystemBackdrop = null;
                RootGrid.Background = new SolidColorBrush(Color.FromArgb(255, 22, 24, 28));
                break;

            default:
                SystemBackdrop = MicaController.IsSupported()
                    ? new MicaBackdrop { Kind = MicaKind.Base }
                    : new DesktopAcrylicBackdrop();
                break;
        }
    }

    private void RedrawRegions()
    {
        RegionOverlayCanvas.Children.Clear();

        XamlRoot? root = AppTitleBar.XamlRoot;
        double scale = root?.RasterizationScale ?? 1d;

        TitleBarRegionService? regions = _regions;
        if (regions is not null)
        {
            RegionReadout.Text = string.Create(
                System.Globalization.CultureInfo.InvariantCulture,
                $"caption {(regions.UsesExplicitCaptionRegions ? "manual" : "SetTitleBar")} · bar {regions.BarRect.Width}×{regions.BarRect.Height}px · passthrough {regions.PassthroughRects.Count} · scale {scale:0.##}");
        }

        if (root is null || regions is null || RegionSwitch is null || !RegionSwitch.IsOn)
        {
            return;
        }

        DrawRect(regions.BarRect, scale, Color.FromArgb(48, 0, 120, 212), Color.FromArgb(255, 96, 205, 255));

        foreach (RectInt32 rect in regions.PassthroughRects)
        {
            DrawRect(rect, scale, Color.FromArgb(56, 16, 124, 16), Color.FromArgb(255, 108, 203, 95));
        }
    }

    private void DrawRect(RectInt32 rect, double scale, Color fill, Color stroke)
    {
        var box = new Border
        {
            Width = Math.Max(0, rect.Width / scale),
            Height = Math.Max(0, rect.Height / scale),
            Background = new SolidColorBrush(fill),
            BorderBrush = new SolidColorBrush(stroke),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            IsHitTestVisible = false,
        };

        Canvas.SetLeft(box, rect.X / scale);
        Canvas.SetTop(box, rect.Y / scale);
        RegionOverlayCanvas.Children.Add(box);
    }

    private void ThemeButton_Click(object sender, RoutedEventArgs e)
    {
        RootGrid.RequestedTheme = RootGrid.RequestedTheme == ElementTheme.Dark
            ? ElementTheme.Light
            : ElementTheme.Dark;

        StatusText.Text = $"Theme: {RootGrid.RequestedTheme}. Mica follows the theme automatically.";
    }

    private void HeightButton_Click(object sender, RoutedEventArgs e)
    {
        bool tall = TallSwitch.IsOn;
        TallSwitch.IsOn = !tall; // raises Toggled, which applies the height
    }

    private void TallSwitch_Toggled(object sender, RoutedEventArgs e)
    {
        // IsOn="True" in XAML also raises Toggled during load, before the
        // fields below exist. The initial height comes from ConfigureTitleBar().
        if (sender is not ToggleSwitch toggle || AppTitleBar is null || StatusText is null)
        {
            return;
        }

        AppTitleBar.Height = toggle.IsOn ? TallHeight : StandardHeight;

        if (AppWindowTitleBar.IsCustomizationSupported())
        {
            AppWindow.TitleBar.PreferredHeightOption = toggle.IsOn
                ? TitleBarHeightOption.Tall
                : TitleBarHeightOption.Standard;
        }

        _regions?.Update();
        RedrawRegions();
        StatusText.Text = toggle.IsOn
            ? "Tall title bar: 48 px. PreferredHeightOption = Tall."
            : "Standard title bar: 32 px. PreferredHeightOption = Standard.";
    }

    private void RegionToggleButton_Click(object sender, RoutedEventArgs e)
    {
        RegionSwitch.IsOn = !RegionSwitch.IsOn;
    }

    private void RegionSwitch_Toggled(object sender, RoutedEventArgs e)
    {
        RedrawRegions();
        StatusText.Text = RegionSwitch.IsOn
            ? "Blue is the caption region, green are the passthrough holes."
            : "Overlay hidden.";
    }

    private void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        StatusText.Text = "Tip: drag the window from empty title bar space, and double-click it to maximize.";
    }

    private void MaterialCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // SelectedIndex="0" in XAML raises this while InitializeComponent is still
        // parsing, so the x:Name fields (including MaterialCombo itself) can be null.
        // Use the sender instead of the field, and guard anything declared later.
        if (sender is not ComboBox combo || combo.SelectedIndex < 0)
        {
            return;
        }

        WindowMaterial material = combo.SelectedIndex switch
        {
            1 => WindowMaterial.MicaAlt,
            2 => WindowMaterial.Acrylic,
            3 => WindowMaterial.Solid,
            _ => WindowMaterial.MicaBase,
        };

        ApplyMaterial(material);

        if (StatusText is not null)
        {
            StatusText.Text = $"Material: {material}. Mica needs Windows 11; the app falls back to Acrylic.";
        }
    }

    private void IconSwitch_Toggled(object sender, RoutedEventArgs e)
    {
        if (sender is not ToggleSwitch toggle || AppIconImage is null || StatusText is null)
        {
            return;
        }

        AppIconImage.Visibility = toggle.IsOn ? Visibility.Visible : Visibility.Collapsed;
        _regions?.Update();
        StatusText.Text = toggle.IsOn
            ? "Title bar icon visible. The taskbar icon comes from AppWindow.SetIcon."
            : "Title bar icon hidden; the taskbar icon is unchanged.";
    }

    private void ExplicitCaptionSwitch_Toggled(object sender, RoutedEventArgs e)
    {
        if (_regions is null)
        {
            return;
        }

        _regions.UseExplicitCaptionRegions(ExplicitCaptionSwitch.IsOn);
        RedrawRegions();
        StatusText.Text = ExplicitCaptionSwitch.IsOn
            ? "Manual mode: NonClientRegionKind.Caption is set from the XAML bounds."
            : "High-level mode: Window.SetTitleBar owns the drag region.";
    }

    private enum WindowMaterial
    {
        MicaBase,
        MicaAlt,
        Acrylic,
        Solid,
    }
}
