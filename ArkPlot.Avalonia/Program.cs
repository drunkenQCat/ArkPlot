using Avalonia;
using System;
using System.Threading;
using System.Threading.Tasks;
using ArkPlot.Avalonia.Services;

namespace ArkPlot.Avalonia;

sealed class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        try
        {
            LogStressOptions.Current = LogStressOptions.Parse(args);
        }
        catch (ArgumentException ex)
        {
            Console.Error.WriteLine(ex.Message);
            Environment.ExitCode = 2;
            return;
        }

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            var ex = e.ExceptionObject as Exception;
            Console.Error.WriteLine($"[FATAL] {ex?.GetType().Name}: {ex?.Message}");
            Console.Error.WriteLine(ex?.StackTrace);
        };
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Console.Error.WriteLine($"[FATAL UnobservedTask] {e.Exception.GetType().Name}: {e.Exception.Message}");
            Console.Error.WriteLine(e.Exception.StackTrace);
            e.SetObserved();
        };

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
