using System.Data;
using Microsoft.Data.SqlClient;
using PosBackend;

internal static class UserProvisioner
{
    public static async Task CreateAsync(string connectionString, string role, string username, string fullName, string password)
    {
        if (role is not ("Kasir" or "Pelanggan") || string.IsNullOrWhiteSpace(username)
            || username.Trim().Length > 80 || string.IsNullOrWhiteSpace(fullName)
            || fullName.Trim().Length > 160 || password.Length is < 12 or > 128)
            throw new ArgumentException("Role, username, nama, atau password tidak valid. Password minimal 12 karakter.");
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(
            "INSERT dbo.Users (Username,PasswordHash,FullName,Role,IsActive) VALUES (@username,@hash,@name,@role,1)", connection);
        command.Parameters.Add("@username", SqlDbType.NVarChar, 80).Value = username.Trim();
        command.Parameters.Add("@hash", SqlDbType.NVarChar, 250).Value = PasswordHash.Create(password);
        command.Parameters.Add("@name", SqlDbType.NVarChar, 160).Value = fullName.Trim();
        command.Parameters.Add("@role", SqlDbType.VarChar, 16).Value = role;
        await command.ExecuteNonQueryAsync();
    }
}
