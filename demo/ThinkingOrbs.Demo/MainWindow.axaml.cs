using Avalonia.Controls;
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
    }

    private void OnThemeToggled(object? sender, RoutedEventArgs e) =>
        RequestedThemeVariant = ((ToggleSwitch)sender!).IsChecked == true
            ? ThemeVariant.Dark
            : ThemeVariant.Light;
}
