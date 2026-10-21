namespace PosDemo;

public static class DemoPaths
{
    public static string GetDatabasePath()
    {
        var configured = Environment.GetEnvironmentVariable("POS_DEMO_DB_PATH");
        if (!string.IsNullOrWhiteSpace(configured)) return Path.GetFullPath(configured);
        var root = OperatingSystem.IsWindows()
            ? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)
            : Environment.GetEnvironmentVariable("XDG_DATA_HOME");
        if (string.IsNullOrWhiteSpace(root))
            root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share");
        return Path.Combine(root, "mikugomi-pos", "demo.sqlite3");
    }
}
