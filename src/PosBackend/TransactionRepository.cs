using System.Data;
using Microsoft.Data.SqlClient;

namespace PosBackend;

internal static class TransactionRepository
{
    public static async Task<(long Id, DateTimeOffset CreatedAt)> InsertHeaderAsync(SqlConnection connection, SqlTransaction transaction,
        string number, Guid requestId, int cashierId, int? customerId, PaymentMethod payment,
        decimal total, CancellationToken ct, string? guestName = null)
    {
        await using var command = new SqlCommand(
            "INSERT dbo.Transactions (Number,RequestId,CashierId,CustomerId,GuestName,CreatedAt,PaymentMethod,Total) " +
            "OUTPUT INSERTED.Id,INSERTED.CreatedAt VALUES " +
            "(@number,@request,@cashier,@customer,@guestName,TODATETIMEOFFSET(SYSUTCDATETIME(), '+00:00'),@payment,@total)",
            connection, transaction);
        command.Parameters.Add("@number", SqlDbType.VarChar, 48).Value = number;
        command.Parameters.Add("@request", SqlDbType.UniqueIdentifier).Value = requestId;
        command.Parameters.Add("@cashier", SqlDbType.Int).Value = cashierId;
        command.Parameters.Add("@customer", SqlDbType.Int).Value = (object?)customerId ?? DBNull.Value;
        command.Parameters.Add("@guestName", SqlDbType.NVarChar, 80).Value = (object?)guestName ?? DBNull.Value;
        command.Parameters.Add("@payment", SqlDbType.VarChar, 10).Value = payment.ToString();
        AddMoney(command, "@total", total);
        await using var reader = await command.ExecuteReaderAsync(ct);
        await reader.ReadAsync(ct);
        return (reader.GetInt64(0), reader.GetDateTimeOffset(1));
    }

    public static async Task InsertItemAsync(SqlConnection connection, SqlTransaction transaction, long id,
        TransactionItemDto item, CancellationToken ct)
    {
        await using var command = new SqlCommand(
            "INSERT dbo.TransactionItems (TransactionId,ProductId,ProductName,UnitPrice,Quantity,Subtotal) " +
            "VALUES (@transaction,@product,@name,@price,@quantity,@subtotal)", connection, transaction);
        command.Parameters.Add("@transaction", SqlDbType.BigInt).Value = id;
        command.Parameters.Add("@product", SqlDbType.Int).Value = item.ProductId;
        command.Parameters.Add("@name", SqlDbType.NVarChar, 160).Value = item.ProductName;
        AddMoney(command, "@price", item.UnitPrice);
        command.Parameters.Add("@quantity", SqlDbType.Int).Value = item.Quantity;
        AddMoney(command, "@subtotal", item.Subtotal);
        await command.ExecuteNonQueryAsync(ct);
    }

    private static void AddMoney(SqlCommand command, string name, decimal value)
    {
        var parameter = command.Parameters.Add(name, SqlDbType.Decimal);
        parameter.Precision = 18; parameter.Scale = 2; parameter.Value = value;
    }
}
