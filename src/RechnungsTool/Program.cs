using System;
using System.Linq;
using Avalonia;
using RechnungsTool.Cli;

namespace RechnungsTool;

sealed class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static int Main(string[] args)
    {
        // macOS-Launcher-Artefakte (z. B. -psn_…) sind keine CLI-Argumente
        var cliArgs = args.Where(a => !a.StartsWith("-psn", StringComparison.Ordinal)).ToArray();

        // Mit Argumenten: CLI-Modus (Automatisierung/KI), ohne: GUI
        if (cliArgs.Length > 0)
            return CliApp.Run(cliArgs);

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        return 0;
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
