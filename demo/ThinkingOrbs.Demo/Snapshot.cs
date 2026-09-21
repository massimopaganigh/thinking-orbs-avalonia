// `--snapshot <dir>` renders every (state x size) at a pinned instant into
// one contact sheet per theme, without opening a window. The visual layer
// that the golden-vector check cannot reach — brush values, alpha, stroke
// widths, antialiasing — gets looked at here.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace ThinkingOrbs.Demo;

internal static class Snapshot
{
    public static void Write(string dir, double t)
    {
        Directory.CreateDirectory(dir);
        ThinkingOrb.FrozenTime = t;
        foreach (var theme in (ReadOnlySpan<OrbTheme>)[OrbTheme.Dark, OrbTheme.Light])
        {
            var sheet = BuildSheet(theme);
            sheet.Measure(Avalonia.Size.Infinity);
            sheet.Arrange(new Rect(sheet.DesiredSize));
            var px = new PixelSize((int)sheet.DesiredSize.Width, (int)sheet.DesiredSize.Height);
            using var bmp = new RenderTargetBitmap(px, new Vector(96, 96));
            bmp.Render(sheet);
            var path = Path.Combine(dir, FormattableString.Invariant($"orbs-{theme.ToString().ToLowerInvariant()}-t{t}.png"));
            bmp.Save(path);
            Console.WriteLine(path);
        }
        ThinkingOrb.FrozenTime = null;
    }

    private static Control BuildSheet(OrbTheme theme)
    {
        var grid = new WrapPanel { Width = 3 * 180, Orientation = Orientation.Horizontal };
        foreach (var state in Enum.GetValues<OrbState>())
        {
            var cell = new StackPanel { Width = 180, Margin = new Thickness(0, 0, 0, 24) };
            cell.Children.Add(new ThinkingOrb
            {
                State = state, Size = OrbSize.Px64, ThemeMode = theme, HorizontalAlignment = HorizontalAlignment.Center
            });
            cell.Children.Add(new ThinkingOrb
            {
                State = state, Size = OrbSize.Px20, ThemeMode = theme, Margin = new Thickness(0, 12, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Center
            });
            cell.Children.Add(new TextBlock
            {
                Text = state.ToString(),
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 8, 0, 0),
                Foreground = new SolidColorBrush(theme == OrbTheme.Dark ? Colors.Gray : Colors.DimGray)
            });
            grid.Children.Add(cell);
        }
        return new Border
        {
            Background = new SolidColorBrush(theme == OrbTheme.Dark
                ? Color.FromRgb(0x14, 0x14, 0x16)
                : Color.FromRgb(0xF7, 0xF7, 0xF5)),
            Padding = new Thickness(24),
            Child = grid
        };
    }
}
