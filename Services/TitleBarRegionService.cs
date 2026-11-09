using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.UI.Input;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Windows.Foundation;
using Windows.Graphics;

namespace TitleBarStudio.Services;

/// <summary>
/// Keeps a window's non-client hit-test regions in sync with a XAML title bar.
/// The caption region makes an area draggable; passthrough regions hand input
/// back to the interactive controls that live inside the title bar.
/// </summary>
public sealed class TitleBarRegionService : IDisposable
{
    private readonly Window _window;
    private readonly FrameworkElement _titleBar;
    private readonly FrameworkElement[] _interactiveElements;

    private InputNonClientPointerSource? _input;
    private RectInt32 _barRect;
    private RectInt32[] _passthroughRects = Array.Empty<RectInt32>();
    private bool _explicitCaptionRegions;
    private bool _attached;
    private bool _disposed;

    public TitleBarRegionService(Window window, FrameworkElement titleBar, params FrameworkElement[] interactiveElements)
    {
        _window = window;
        _titleBar = titleBar;
        _interactiveElements = interactiveElements;
    }

    /// <summary>The rectangle the title bar occupies, in physical pixels.</summary>
    public RectInt32 BarRect => _barRect;

    /// <summary>Hit-test holes that belong to interactive controls, in physical pixels.</summary>
    public IReadOnlyList<RectInt32> PassthroughRects => _passthroughRects;

    /// <summary>Raised whenever the computed geometry changes.</summary>
    public event EventHandler? RegionsChanged;

    public bool UsesExplicitCaptionRegions => _explicitCaptionRegions;

    public void Attach()
    {
        if (_attached || _disposed)
        {
            return;
        }

        _attached = true;
        _input = InputNonClientPointerSource.GetForWindowId(_window.AppWindow.Id);

        _titleBar.SizeChanged += OnSizeChanged;
        _titleBar.LayoutUpdated += OnLayoutUpdated;

        if (_titleBar.XamlRoot is { } xamlRoot)
        {
            xamlRoot.Changed += OnXamlRootChanged;
        }

        Update();
    }

    /// <summary>
    /// Switches between the high-level Window.SetTitleBar drag region and
    /// fully manual NonClientRegionKind.Caption regions.
    /// Never use both at the same time.
    /// </summary>
    public void UseExplicitCaptionRegions(bool explicitRegions)
    {
        _explicitCaptionRegions = explicitRegions;
        _window.SetTitleBar(explicitRegions ? null : _titleBar);
        Update();
    }

    /// <summary>Recomputes and applies every non-client region.</summary>
    public void Update()
    {
        if (!_attached || _disposed || _input is null || _titleBar.XamlRoot is not { } root)
        {
            return;
        }

        double scale = root.RasterizationScale;

        RectInt32 barRect = ToPhysicalRect(_titleBar, scale);
        RectInt32[] passthrough = _interactiveElements
            .Where(element => element is { Visibility: Visibility.Visible, ActualWidth: > 0, ActualHeight: > 0 })
            .Select(element => ToPhysicalRect(element, scale))
            .ToArray();

        if (barRect.Equals(_barRect) && SameRects(_passthroughRects, passthrough))
        {
            // Geometry is unchanged; avoid re-applying regions on every layout pass.
            return;
        }

        if (_explicitCaptionRegions)
        {
            _input.SetRegionRects(NonClientRegionKind.Caption, new[] { barRect });
        }

        if (passthrough.Length > 0)
        {
            _input.SetRegionRects(NonClientRegionKind.Passthrough, passthrough);
        }
        else
        {
            _input.ClearRegionRects(NonClientRegionKind.Passthrough);
        }

        _barRect = barRect;
        _passthroughRects = passthrough;

        RegionsChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _titleBar.SizeChanged -= OnSizeChanged;
        _titleBar.LayoutUpdated -= OnLayoutUpdated;

        if (_titleBar.XamlRoot is { } xamlRoot)
        {
            xamlRoot.Changed -= OnXamlRootChanged;
        }
    }

    /// <summary>Maps XAML device-independent bounds into window pixels.</summary>
    private static RectInt32 ToPhysicalRect(FrameworkElement element, double scale)
    {
        Rect bounds = element
            .TransformToVisual(null)
            .TransformBounds(new Rect(0, 0, element.ActualWidth, element.ActualHeight));

        return new RectInt32(
            (int)Math.Round(bounds.X * scale),
            (int)Math.Round(bounds.Y * scale),
            (int)Math.Round(bounds.Width * scale),
            (int)Math.Round(bounds.Height * scale));
    }

    private static bool SameRects(RectInt32[] left, RectInt32[] right)
    {
        if (left.Length != right.Length)
        {
            return false;
        }

        for (int i = 0; i < left.Length; i++)
        {
            if (!left[i].Equals(right[i]))
            {
                return false;
            }
        }

        return true;
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs e) => Update();

    private void OnLayoutUpdated(object? sender, object e) => Update();

    private void OnXamlRootChanged(XamlRoot sender, XamlRootChangedEventArgs args) => Update();
}
