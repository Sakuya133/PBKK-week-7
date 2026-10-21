using System.Text.Json;

namespace PosDesktop;

public sealed record ClientConfiguration(Uri ApiBaseAddress, string FilePath, string? Warning)
{
    private const string DefaultApiUrl = "http://127.0.0.1:5077/";

    public static ClientConfiguration Load()
    {
        var path = Environment.GetEnvironmentVariable("POS_CLIENT_CONFIG");
        path = string.IsNullOrWhiteSpace(path) ? GetConfigurationPath() : Path.GetFullPath(path);
        string? warning = null;
        var address = Environment.GetEnvironmentVariable("POS_API_URL");

        if (string.IsNullOrWhiteSpace(address))
        {
            try
            {
                if (File.Exists(path))
                    address = JsonSerializer.Deserialize<ConfigFile>(File.ReadAllText(path))?.ApiUrl;
                else
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                    File.WriteAllText(path, JsonSerializer.Serialize(new ConfigFile(DefaultApiUrl),
                        new JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine);
                    address = DefaultApiUrl;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                warning = $"Konfigurasi API tidak dapat dibaca ({ex.Message}); memakai alamat lokal.";
            }
        }

        if (!Uri.TryCreate(string.IsNullOrWhiteSpace(address) ? DefaultApiUrl : address, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttps && !(uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback)))
        {
            warning = "Alamat API tidak valid. Pakai HTTPS atau HTTP loopback untuk development.";
            uri = new Uri(DefaultApiUrl);
        }

        var baseAddress = uri.AbsoluteUri.EndsWith('/') ? uri : new Uri(uri.AbsoluteUri + "/");
        return new(baseAddress, path, warning);
    }

    public static string GetConfigurationPath()
    {
        var configRoot = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        if (OperatingSystem.IsWindows())
            configRoot = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        if (string.IsNullOrWhiteSpace(configRoot))
            configRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config");
        return Path.Combine(configRoot, "posdesktop", "pos-client.json");
    }

    public static string GetStateDirectory()
    {
        var stateRoot = Environment.GetEnvironmentVariable("XDG_STATE_HOME");
        if (OperatingSystem.IsWindows())
            stateRoot = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(stateRoot))
            stateRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "state");
        return Path.Combine(stateRoot, OperatingSystem.IsWindows() ? "POS Desktop" : "posdesktop");
    }

    private sealed record ConfigFile(string ApiUrl);
}
