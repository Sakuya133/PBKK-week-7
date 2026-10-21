using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using PosClient;
using PosDesktop.ViewModels;

namespace PosDesktop;

public sealed class App : Application
{
    private PosRemoteServices? _services;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var config = ClientConfiguration.Load();
            _services = config.ApiBaseAddress.Scheme == Uri.UriSchemeHttp
                ? PosRemoteServices.CreateForDevelopment(config.ApiBaseAddress)
                : new PosRemoteServices(config.ApiBaseAddress);
            var viewModel = new MainViewModel(_services, config);
            var window = new MainWindow { DataContext = viewModel };
            window.Closed += (_, _) => _services.Dispose();
            desktop.MainWindow = window;
            desktop.Exit += (_, _) => _services.Dispose();
            _ = viewModel.InitializeAsync();
        }
        base.OnFrameworkInitializationCompleted();
    }
}
