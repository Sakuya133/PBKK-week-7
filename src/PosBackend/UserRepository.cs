using System.Data;
using Microsoft.Data.SqlClient;

namespace PosBackend;

internal sealed class UserRepository(Database db)
{
    private static readonly Lazy<string> DummyHash = new(() => PasswordHash.Create("unused-password-for-timing"));

    public static async Task<UserDto?> GetActiveByIdAsync(Database db, int id, CancellationToken ct)
    {
        await using var connection = await db.OpenAsync(ct);
        await using var command = new SqlCommand(
            "SELECT Id,Username,FullName,Role FROM dbo.Users WHERE Id=@id AND IsActive=1", connection);
        command.Parameters.Add("@id", SqlDbType.Int).Value = id;
        await using var reader = await command.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct)
            ? new UserDto(reader.GetInt32(0), reader.GetString(1), reader.GetString(2),
                Enum.Parse<UserRole>(reader.GetString(3)))
            : null;
    }

    public async Task<UserDto?> TryLoginAsync(string username, string password, CancellationToken ct)
    {
        await using var connection = await db.OpenAsync(ct);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
        await using var command = new SqlCommand(
            "SELECT Id,Username,FullName,Role,PasswordHash,IsActive,FailedLoginCount,LockoutUntil, " +
            "TODATETIMEOFFSET(SYSUTCDATETIME(), '+00:00') " +
            "FROM dbo.Users WITH (UPDLOCK,HOLDLOCK) WHERE Username=@username", connection, transaction);
        command.Parameters.Add("@username", SqlDbType.NVarChar, 80).Value = username;
        int id, attempts; string storedHash; UserDto? user; bool active; DateTimeOffset? lockoutUntil;
        DateTimeOffset now;
        await using (var reader = await command.ExecuteReaderAsync(ct))
        {
            if (!await reader.ReadAsync(ct))
            {
                PasswordHash.Verify(password, DummyHash.Value);
                return null;
            }
            id = reader.GetInt32(0);
            user = new UserDto(id, reader.GetString(1), reader.GetString(2), Enum.Parse<UserRole>(reader.GetString(3)));
            storedHash = reader.GetString(4);
            active = reader.GetBoolean(5);
            attempts = reader.GetInt32(6);
            lockoutUntil = reader.IsDBNull(7) ? null : reader.GetDateTimeOffset(7);
            now = reader.GetDateTimeOffset(8);
        }

        if (!active || lockoutUntil > now) return null;
        var verification = PasswordHash.VerifyWithUpgrade(password, storedHash);
        if (!verification.Valid)
        {
            var nextAttempts = attempts + 1;
            var lockout = nextAttempts >= 5 ? now.AddMinutes(15) : (DateTimeOffset?)null;
            await using var failure = new SqlCommand(
                "UPDATE dbo.Users SET FailedLoginCount=@attempts,LockoutUntil=@lockout WHERE Id=@id", connection, transaction);
            failure.Parameters.Add("@attempts", SqlDbType.Int).Value = lockout is null ? nextAttempts : 0;
            failure.Parameters.Add("@lockout", SqlDbType.DateTimeOffset).Value = (object?)lockout ?? DBNull.Value;
            failure.Parameters.Add("@id", SqlDbType.Int).Value = id;
            await failure.ExecuteNonQueryAsync(ct);
            await transaction.CommitAsync(ct);
            return null;
        }
        await using var success = new SqlCommand(
            "UPDATE dbo.Users SET FailedLoginCount=0,LockoutUntil=NULL,PasswordHash=@hash WHERE Id=@id", connection, transaction);
        success.Parameters.Add("@hash", SqlDbType.NVarChar, 250).Value = verification.NeedsUpgrade
            ? PasswordHash.Create(password) : storedHash;
        success.Parameters.Add("@id", SqlDbType.Int).Value = id;
        await success.ExecuteNonQueryAsync(ct);
        await transaction.CommitAsync(ct);
        return user;
    }

    public async Task<IReadOnlyList<CustomerOptionDto>> GetActiveCustomersAsync(CancellationToken ct)
    {
        await using var connection = await db.OpenAsync(ct);
        await using var command = new SqlCommand(
            "SELECT Id, FullName FROM dbo.Users WHERE Role='Pelanggan' AND IsActive=1 ORDER BY FullName", connection);
        var customers = new List<CustomerOptionDto>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct)) customers.Add(new(reader.GetInt32(0), reader.GetString(1)));
        return customers;
    }

    public static async Task<string?> GetActiveCustomerNameAsync(SqlConnection connection, SqlTransaction transaction,
        int customerId, CancellationToken ct)
    {
        await using var command = new SqlCommand(
            "SELECT FullName FROM dbo.Users WITH (HOLDLOCK) WHERE Id=@id AND Role='Pelanggan' AND IsActive=1",
            connection, transaction);
        command.Parameters.Add("@id", SqlDbType.Int).Value = customerId;
        return (string?)await command.ExecuteScalarAsync(ct);
    }

    public static async Task<bool> IsActiveCashierAsync(SqlConnection connection, SqlTransaction transaction,
        int cashierId, CancellationToken ct)
    {
        await using var command = new SqlCommand(
            "SELECT 1 FROM dbo.Users WITH (HOLDLOCK) WHERE Id=@id AND Role='Kasir' AND IsActive=1",
            connection, transaction);
        command.Parameters.Add("@id", SqlDbType.Int).Value = cashierId;
        return await command.ExecuteScalarAsync(ct) is not null;
    }
}
