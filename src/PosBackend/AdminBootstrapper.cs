using System.Data;
using Microsoft.Data.SqlClient;

namespace PosBackend;

public static class AdminBootstrapper
{
    // One-time setup, run from a trusted operator environment.
    public static Task<ServiceResult<int>> CreateInitialAdminAsync(string connectionString, string username,
        string password, CancellationToken cancellationToken = default)
        => Execute.Run(async () =>
        {
            Guard.That(!string.IsNullOrWhiteSpace(username) && username.Trim().Length <= 80
                && !string.IsNullOrWhiteSpace(password) && password.Length is >= 12 and <= 128,
                ErrorCode.Validation, "Username atau password awal tidak valid (password minimal 12 karakter).");
            var db = new Database(connectionString);
            await using var connection = await db.OpenAsync(cancellationToken);
            await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
            await using var exists = new SqlCommand("SELECT TOP (1) Id FROM dbo.Users WITH (UPDLOCK,HOLDLOCK) WHERE Role='Admin'",
                connection, transaction);
            Guard.That(await exists.ExecuteScalarAsync(cancellationToken) is null,
                ErrorCode.Conflict, "Admin awal sudah ada; bootstrap ditolak.");
            await using var insert = new SqlCommand(
                "INSERT dbo.Users (Username,PasswordHash,FullName,Role,IsActive) OUTPUT INSERTED.Id " +
                "VALUES (@username,@hash,@name,'Admin',1)", connection, transaction);
            insert.Parameters.Add("@username", SqlDbType.NVarChar, 80).Value = username.Trim();
            insert.Parameters.Add("@hash", SqlDbType.NVarChar, 250).Value = PasswordHash.Create(password);
            insert.Parameters.Add("@name", SqlDbType.NVarChar, 160).Value = "Administrator";
            try
            {
                var id = (int)(await insert.ExecuteScalarAsync(cancellationToken))!;
                await transaction.CommitAsync(cancellationToken);
                return id;
            }
            catch (SqlException e) when (e.Number is 2601 or 2627)
            { throw new ServiceFault(ErrorCode.Conflict, "Username sudah dipakai."); }
        });
}
