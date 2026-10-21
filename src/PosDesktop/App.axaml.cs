using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using PosClient;
using PosBackend;
using PosDemo;
using PosDesktop.ViewModels;

namespace PosDesktop;

public sealed class App : Application
{
    private IPosAppServices? _services;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var serverMode = Environment.GetEnvironmentVariable("POS_DESKTOP_MODE")
                ?.Equals("server", StringComparison.OrdinalIgnoreCase) == true;
            MainViewModel viewModel;
            if (serverMode)
            {
                var config = ClientConfiguration.Load();
                _services = config.ApiBaseAddress.Scheme == Uri.UriSchemeHttp
                    ? PosRemoteServices.CreateForDevelopment(config.ApiBaseAddress)
                    : new PosRemoteServices(config.ApiBaseAddress);
                viewModel = new MainViewModel(_services, config.ApiBaseAddress.ToString(), false, config.Warning);
            }
            else
            {
                _services = new PosDemoServices(DemoPaths.GetDatabasePath());
                viewModel = new MainViewModel(_services, "SQLite lokal · tanpa API", true);
            }
            var window = new MainWindow { DataContext = viewModel };
            window.Closed += (_, _) => _services.Dispose();
            desktop.MainWindow = window;
            desktop.Exit += (_, _) => _services.Dispose();
            _ = viewModel.InitializeAsync();
        }
        base.OnFrameworkInitializationCompleted();
    }
}
