using System.Data;
using System.Security.Cryptography;
using Microsoft.Data.SqlClient;
using PosBackend;

namespace PosServer;

public sealed record LoginInput(string? Username, string? Password);
public sealed record LoginSessionDto(UserDto User, string Token, DateTimeOffset ExpiresAt);

internal sealed class SessionStore(string connectionString)
{
    public async Task<LoginSessionDto> IssueAsync(UserDto user, CancellationToken ct)
    {
        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var hash = SHA256.HashData(System.Text.Encoding.ASCII.GetBytes(token));
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(ct);
        await using var command = new SqlCommand("DECLARE @now datetimeoffset(7)=TODATETIMEOFFSET(SYSUTCDATETIME(), '+00:00'); " +
            "DELETE FROM dbo.AuthSessions WHERE ExpiresAt<DATEADD(day,-1,@now); " +
            "INSERT dbo.AuthSessions(UserId,TokenHash,CreatedAt,ExpiresAt) " +
            "OUTPUT INSERTED.ExpiresAt VALUES (@user,@hash,@now,DATEADD(hour,8,@now))", connection);
        command.Parameters.Add("@user", SqlDbType.Int).Value = user.Id;
        command.Parameters.Add("@hash", SqlDbType.Binary, 32).Value = hash;
        var expiry = (DateTimeOffset)(await command.ExecuteScalarAsync(ct))!;
        return new LoginSessionDto(user, token, expiry);
    }

    public async Task<UserDto?> ResolveAsync(string token, CancellationToken ct)
    {
        if (token.Length != 43 || token.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '-' or '_'))) return null;
        var hash = SHA256.HashData(System.Text.Encoding.ASCII.GetBytes(token));
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(ct);
        await using var command = new SqlCommand("SELECT u.Id,u.Username,u.FullName,u.Role FROM dbo.AuthSessions s " +
            "JOIN dbo.Users u ON u.Id=s.UserId WHERE s.TokenHash=@hash AND s.RevokedAt IS NULL " +
            "AND s.ExpiresAt>TODATETIMEOFFSET(SYSUTCDATETIME(), '+00:00') AND u.IsActive=1", connection);
        command.Parameters.Add("@hash", SqlDbType.Binary, 32).Value = hash;
        await using var reader = await command.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? new UserDto(reader.GetInt32(0), reader.GetString(1), reader.GetString(2),
            Enum.Parse<UserRole>(reader.GetString(3))) : null;
    }

    public async Task RevokeAsync(string token, CancellationToken ct)
    {
        var hash = SHA256.HashData(System.Text.Encoding.ASCII.GetBytes(token));
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(ct);
        await using var command = new SqlCommand("UPDATE dbo.AuthSessions SET RevokedAt=TODATETIMEOFFSET(SYSUTCDATETIME(), '+00:00') " +
            "WHERE TokenHash=@hash AND RevokedAt IS NULL", connection);
        command.Parameters.Add("@hash", SqlDbType.Binary, 32).Value = hash;
        await command.ExecuteNonQueryAsync(ct);
    }
}
