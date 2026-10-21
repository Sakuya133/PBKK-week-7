using Avalonia;

namespace PosDesktop;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        try
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
            return 0;
        }
        catch (Exception exception)
        {
            var message = $"POS Desktop gagal membuka jendela: {exception.Message}";
            Console.Error.WriteLine(message);
            Console.Error.WriteLine(exception);
            try
            {
                var path = Path.Combine(ClientConfiguration.GetStateDirectory(), "logs", "posdesktop.log");
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.AppendAllText(path, $"{DateTimeOffset.Now:O} {message}{Environment.NewLine}{exception}{Environment.NewLine}");
            }
            catch { }
            return 1;
        }
    }

    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>()
        .UsePlatformDetect()
        .LogToTrace();
}
