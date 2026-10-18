using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using PosBackend;

var demo = args.Length == 1 && args[0] == "--demo";
var createAdmin = args.Length == 2 && args[0] == "--create-admin";
var createUser = args.Length == 4 && args[0] == "--create-user";
if (args.Length > 0 && !demo && !createAdmin && !createUser)
{
    Console.Error.WriteLine("Penggunaan: PosSetup [--demo | --create-admin USERNAME | --create-user ROLE USERNAME FULL_NAME]");
    return 1;
}
var configured = Environment.GetEnvironmentVariable("POS_CONNECTION_STRING");
if (string.IsNullOrWhiteSpace(configured))
{
    Console.Error.WriteLine("POS_CONNECTION_STRING belum diatur.");
    return 1;
}
try
{
    var builder = new SqlConnectionStringBuilder(configured);
    builder.InitialCatalog = "master";
    await using var connection = new SqlConnection(builder.ConnectionString);
    await connection.OpenAsync();
    foreach (var filename in demo ? new[] { "schema.sql", "server.sql", "seed.sql", "permissions.sql", "demo.sql" }
                                  : new[] { "schema.sql", "server.sql", "seed.sql", "permissions.sql" })
    {
        var sql = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "database", filename));
        foreach (var batch in Regex.Split(sql, @"^\s*GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(batch)) continue;
            await using var command = new SqlCommand(batch, connection) { CommandTimeout = 60 };
            await command.ExecuteNonQueryAsync();
        }
        Console.WriteLine($"{filename}: selesai");
    }
    builder.InitialCatalog = "PosDesktop";
    if (demo)
    {
        var inserted = await DemoSeeder.SeedUsersAsync(builder.ConnectionString);
        Console.WriteLine($"Seed akun demo selesai. {inserted} akun baru ditambahkan.");
    }
    if (createAdmin)
    {
        var password = ReadSecret("POS_INITIAL_ADMIN_PASSWORD");
        if (password is null)
        {
            Console.Error.WriteLine("POS_INITIAL_ADMIN_PASSWORD belum diatur dan input interaktif tidak tersedia.");
            return 1;
        }
        var result = await AdminBootstrapper.CreateInitialAdminAsync(builder.ConnectionString, args[1], password);
        if (!result.Success)
        {
            Console.Error.WriteLine($"Bootstrap admin gagal: {result.Message}");
            return 1;
        }
        Console.WriteLine("Admin awal berhasil dibuat.");
    }
    if (createUser)
    {
        var password = ReadSecret("POS_NEW_USER_PASSWORD");
        if (password is null)
        {
            Console.Error.WriteLine("POS_NEW_USER_PASSWORD belum diatur dan input interaktif tidak tersedia.");
            return 1;
        }
        await UserProvisioner.CreateAsync(builder.ConnectionString, args[1], args[2], args[3], password);
        Console.WriteLine("Akun baru berhasil dibuat.");
    }
    return 0;
}
catch (SqlException)
{
    Console.Error.WriteLine("Setup gagal. Periksa akses SQL Server dan hak membuat database; detail koneksi tidak ditampilkan.");
    return 1;
}
catch (ArgumentException)
{
    Console.Error.WriteLine("Konfigurasi atau input setup tidak valid.");
    return 1;
}

static string? ReadSecret(string environmentVariable)
{
    var password = Environment.GetEnvironmentVariable(environmentVariable);
    if (password is not null || Console.IsInputRedirected) return password;
    Console.Write("Password (tidak ditampilkan): ");
    var characters = new List<char>();
    while (true)
    {
        var key = Console.ReadKey(intercept: true);
        if (key.Key == ConsoleKey.Enter) break;
        if (key.Key == ConsoleKey.Backspace && characters.Count > 0) characters.RemoveAt(characters.Count - 1);
        else if (!char.IsControl(key.KeyChar)) characters.Add(key.KeyChar);
    }
    Console.WriteLine();
    return new string(characters.ToArray());
}
