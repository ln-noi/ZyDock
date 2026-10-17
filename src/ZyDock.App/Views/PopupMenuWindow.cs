using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using ZyDock.App.Interop;
using ZyDock.Core.Diagnostics;

namespace ZyDock.App.Views;

/// <summary>One row of a popup menu.</summary>
internal readonly record struct PopupMenuItem(string Label, Action Activate, bool IsChecked = false, bool IsSeparator = false, bool IsDanger = false)
{
    public static PopupMenuItem Separator { get; } = new(string.Empty, () => { }, false, true);
}

/// <summary>
/// A small dark glass context menu that matches the dock. It closes itself as soon as
/// it loses focus, like a native menu.
/// </summary>
internal sealed class PopupMenuWindow : Window
{
    private PopupMenuWindow(IReadOnlyList<PopupMenuItem> items)
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        Topmost = true;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        SnapsToDevicePixels = true;
        Foreground = (Brush)Application.Current.Resources["TextPrimaryBrush"];
        FontFamily = (FontFamily)Application.Current.Resources["UiFont"];

        var panel = new StackPanel { Margin = new Thickness(4) };

        foreach (var item in items)
        {
            panel.Children.Add(item.IsSeparator ? CreateSeparator() : CreateRow(item));
        }

        var shell = new Border
        {
            CornerRadius = new CornerRadius(11),
            Background = new SolidColorBrush(Color.FromArgb(246, 15, 19, 26)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(38, 255, 255, 255)),
            BorderThickness = new Thickness(1),
            Child = panel,
            Effect = new DropShadowEffect
            {
                BlurRadius = 20,
                ShadowDepth = 5,
                Direction = 270,
                Opacity = 0.55,
                Color = Colors.Black,
            },
        };

        Content = shell;
        MinWidth = 168;
    }

    public static void ShowAt(Point screenPixels, IReadOnlyList<PopupMenuItem> items)
    {
        try
        {
            var window = new PopupMenuWindow(items)
            {
                WindowStartupLocation = WindowStartupLocation.Manual,

                // Placed after the window has a real size; staying invisible until then avoids a
                // one-frame flash in the top-left corner.
                Opacity = 0,
            };

            window._shownAt = DateTime.UtcNow;
            window._anchor = screenPixels;

            // SizeToContent can still resize the window after the first placement; re-clamp so it
            // can never end up partially off-screen.
            window.SizeChanged += (_, _) => window.ClampToScreen(window._anchor);

            window.Show();
            window.UpdateLayout();
            window.ClampToScreen(screenPixels);
            window.Activate();
            window.Opacity = 1;
        }
        catch (Exception ex)
        {
            Log.Warn("显示右键菜单失败。", ex);
        }
    }

    protected override void OnDeactivated(EventArgs e)
    {
        base.OnDeactivated(e);

        // Deactivation can fire again while the window is already closing (e.g. a second
        // WM_ACTIVATE arrives during Close), and Close() would then throw
        // "Cannot ... while a Window is closing". Guard it.
        if (_closing)
        {
            return;
        }

        _closing = true;
        try
        {
            Close();
        }
        catch (InvalidOperationException)
        {
            // Already closing: nothing to do.
        }
    }

    private bool _closing;
    private DateTime _shownAt;

    /// <summary>Anchor (physical px) to clamp against; kept so a late size change can re-place.</summary>
    private Point _anchor;
    private int _placedWidth;
    private int _placedHeight;

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.Escape)
        {
            Close();
        }
    }

    private void ClampToScreen(Point screenPixels)
    {
        var handle = new WindowInteropHelper(this).Handle;

        // ActualWidth/ActualHeight can still be zero here (the window was just shown), which is
        // exactly why a dock at the bottom of the screen used to get its menu pushed off-screen:
        // the "is there room below?" test compared against a height of 0. Take the size from the
        // real window rect instead.
        if (!NativeMethods.GetWindowRect(handle, out var windowRect))
        {
            return;
        }

        var width = windowRect.Right - windowRect.Left;
        var height = windowRect.Bottom - windowRect.Top;
        if (width <= 0 || height <= 0)
        {
            return;
        }

        // Re-placing at the same size would loop through SizeChanged; only react to real changes.
        if (width == _placedWidth && height == _placedHeight)
        {
            return;
        }

        _placedWidth = width;
        _placedHeight = height;

        var screens = Displays.All();
        var screen = Displays.FromPoint((int)screenPixels.X, (int)screenPixels.Y, screens);

        var workLeft = screen.WorkLeft;
        var workTop = screen.WorkTop;
        var workRight = screen.WorkLeft + screen.WorkWidth;
        var workBottom = screen.WorkTop + screen.WorkHeight;

        var x = (int)Math.Round(screenPixels.X);
        var y = (int)Math.Round(screenPixels.Y);

        // Open above the cursor when the menu does not fit below it (the usual case for a dock).
        if (y + height > workBottom - 4)
        {
            y -= height + 4;
        }

        if (x + width > workRight - 4)
        {
            x = workRight - width - 4;
        }

        x = Math.Max(workLeft + 4, x);
        y = Math.Max(workTop + 4, y);

        // Still too tall for the work area: pin to the top so the first entries stay reachable.
        if (y + height > workBottom - 4)
        {
            y = Math.Max(workTop + 4, workBottom - height - 4);
        }

        NativeMethods.SetWindowPos(
            handle,
            NativeMethods.HWND_TOPMOST,
            x,
            y,
            0,
            0,
            NativeMethods.SWP_NOSIZE | NativeMethods.SWP_SHOWWINDOW);
    }

    private UIElement CreateRow(PopupMenuItem item)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var label = new TextBlock
        {
            Text = item.Label,
            FontSize = 12.5,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = item.IsDanger
                ? new SolidColorBrush(Color.FromArgb(255, 255, 190, 184))
                : (Brush)Application.Current.Resources["TextPrimaryBrush"],
        };
        Grid.SetColumn(label, 0);
        grid.Children.Add(label);

        if (item.IsChecked)
        {
            var check = new TextBlock
            {
                Text = "✓",
                FontSize = 12,
                Margin = new Thickness(18, 0, 2, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = (Brush)Application.Current.Resources["AccentBrush"],
            };
            Grid.SetColumn(check, 1);
            grid.Children.Add(check);
        }

        var row = new Border
        {
            CornerRadius = new CornerRadius(7),
            Padding = new Thickness(12, 7, 12, 7),
            Background = Brushes.Transparent,
            Cursor = Cursors.Hand,
            Child = grid,
        };

        row.MouseEnter += (_, _) => row.Background = new SolidColorBrush(Color.FromArgb(26, 255, 255, 255));
        row.MouseLeave += (_, _) => row.Background = Brushes.Transparent;
        row.MouseLeftButtonUp += (_, _) =>
        {
            Close();
            try
            {
                item.Activate();
            }
            catch (Exception ex)
            {
                Log.Warn($"菜单操作失败：{item.Label}", ex);
            }
        };

        return row;
    }

    private static UIElement CreateSeparator()
    {
        return new Border
        {
            Height = 1,
            Margin = new Thickness(8, 5, 8, 5),
            Background = new SolidColorBrush(Color.FromArgb(30, 255, 255, 255)),
        };
    }
}
