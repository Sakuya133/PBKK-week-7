using System.Data;
using Microsoft.Data.SqlClient;

namespace PosBackend;

internal static class ProductRepository
{
    public static async Task<(string Name, decimal Price)?> GetActiveForCheckoutAsync(
        SqlConnection connection, SqlTransaction transaction, int productId, CancellationToken ct)
    {
        await using var command = new SqlCommand(
            "SELECT Name, Price FROM dbo.Products WITH (HOLDLOCK) WHERE Id=@id AND IsActive=1",
            connection, transaction);
        command.Parameters.Add("@id", SqlDbType.Int).Value = productId;
        await using var reader = await command.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? (reader.GetString(0), reader.GetDecimal(1)) : null;
    }
}
