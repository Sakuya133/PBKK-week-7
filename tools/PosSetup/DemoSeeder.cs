using System.Data;
using Microsoft.Data.SqlClient;
using PosBackend;

internal static class DemoSeeder
{
    public static async Task<int> SeedUsersAsync(string connectionString)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        var users = new (string Username, string Password, string FullName, string Role)[]
        {
            ("admin", "Admin123!", "Admin Demo", "Admin"),
            ("kasir", "Kasir123!", "Kasir Demo", "Kasir"),
            ("pelanggan", "Pelanggan123!", "Pelanggan Demo", "Pelanggan"),
            ("pelanggan2", "Pelanggan234!", "Pelanggan Dua", "Pelanggan")
        };
        var inserted = 0;
        foreach (var user in users)
        {
            await using var command = new SqlCommand(
                "IF NOT EXISTS (SELECT 1 FROM dbo.Users WITH (UPDLOCK,HOLDLOCK) WHERE Username=@username) " +
                "BEGIN INSERT dbo.Users (Username,PasswordHash,FullName,Role,IsActive) " +
                "VALUES (@username,@hash,@name,@role,1); SELECT 1; END ELSE SELECT 0;", connection);
            command.Parameters.Add("@username", SqlDbType.NVarChar, 80).Value = user.Username;
            command.Parameters.Add("@hash", SqlDbType.NVarChar, 250).Value = PasswordHash.Create(user.Password);
            command.Parameters.Add("@name", SqlDbType.NVarChar, 160).Value = user.FullName;
            command.Parameters.Add("@role", SqlDbType.VarChar, 16).Value = user.Role;
            inserted += (int)(await command.ExecuteScalarAsync())!;
        }
        return inserted;
    }
}
