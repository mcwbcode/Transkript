using Avalonia;
using System;

namespace Transkript;

class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // Log any unhandled exception before the process aborts
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            try
            {
                var ex = e.ExceptionObject as Exception;
                Logger.Write($"[FATAL] UnhandledException (terminating={e.IsTerminating}): " +
                             $"{ex?.GetType().FullName}: {ex?.Message}\n{ex?.StackTrace}");
            }
            catch { }
        };

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
