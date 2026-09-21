using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;

namespace ThinkingOrbs.Demo;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        AvaloniaXamlLoader.Load(this);
        this.FindControl<ItemsControl>("Gallery")!.ItemsSource = Enum.GetValues<OrbState>();
        // null is the tuned monochrome; the rest just show that a tint keeps
        // the depth ramp, spending it on opacity instead of on luminance
        this.FindControl<ComboBox>("InkPicker")!.ItemsSource = new Color?[]
        {
            null,
            Color.Parse("#E8590C"),
            Color.Parse("#1971C2"),
            Color.Parse("#2F9E44"),
            Color.Parse("#AE3EC9")
        };
    }

    private void OnThemeToggled(object? sender, RoutedEventArgs e) =>
        RequestedThemeVariant = ((ToggleSwitch)sender!).IsChecked == true
            ? ThemeVariant.Dark
            : ThemeVariant.Light;
}
