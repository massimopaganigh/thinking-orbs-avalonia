using System.Globalization;
using Avalonia;
using Avalonia.Media;

namespace ThinkingOrbs.Demo;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // `--snapshot <dir> [t] [#rrggbb]` renders contact sheets and exits.
        if (args.Length >= 2 && args[0] == "--snapshot")
        {
            BuildAvaloniaApp().SetupWithoutStarting();
            var t = args.Length >= 3 ? double.Parse(args[2], CultureInfo.InvariantCulture) : 0.6;
            Color? ink = args.Length >= 4 ? Color.Parse(args[3]) : null;
            Snapshot.Write(args[1], t, ink);
            return;
        }

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>().UsePlatformDetect().WithInterFont().LogToTrace();
}
