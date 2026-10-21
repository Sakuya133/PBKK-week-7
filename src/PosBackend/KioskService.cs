using System.Data;
using Microsoft.Data.SqlClient;

namespace PosBackend;

internal sealed class KioskService(Database db, Session session) : IKioskService
{
    public Task<ServiceResult<IReadOnlyList<ProductDto>>> GetCatalogAsync(CancellationToken cancellationToken = default)
        => Execute.Run<IReadOnlyList<ProductDto>>(async () =>
        {
            await using var connection = await db.OpenAsync(cancellationToken);
            await using var command = new SqlCommand(
                "SELECT p.Id,p.Name,p.CategoryId,c.Name,p.Price,p.IsActive FROM dbo.Products p " +
                "JOIN dbo.Categories c ON c.Id=p.CategoryId WHERE p.IsActive=1 ORDER BY c.Name,p.Name", connection);
            var products = new List<ProductDto>();
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
                products.Add(new(reader.GetInt32(0), reader.GetString(1), reader.GetInt32(2),
                    reader.GetString(3), reader.GetDecimal(4), reader.GetBoolean(5)));
            return products;
        });

    public Task<ServiceResult<GuestOrderDto>> PlaceOrderAsync(GuestOrderRequest request, CancellationToken cancellationToken = default)
        => Execute.Run(async () =>
        {
            Guard.That(request is not null && !string.IsNullOrWhiteSpace(request.GuestName)
                && request.GuestName.Trim().Length <= 80 && request.RequestId != Guid.Empty
                && Enum.IsDefined(request.PaymentMethod)
                && request.Items is { Count: > 0 and <= 100 }
                && request.Items.All(x => x.ProductId > 0 && x.Quantity is > 0 and <= 100)
                && request.Items.Select(x => x.ProductId).Distinct().Count() == request.Items.Count,
                ErrorCode.Validation, "Nama, metode pembayaran, atau item pesanan tidak valid.");

            await using var connection = await db.OpenAsync(cancellationToken);
            await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
            try
            {
                var items = new List<TransactionItemDto>();
                decimal total = 0;
                foreach (var requested in request!.Items)
                {
                    var product = await ProductRepository.GetActiveForCheckoutAsync(connection, transaction,
                        requested.ProductId, cancellationToken);
                    Guard.That(product is not null, ErrorCode.Validation,
                        $"Produk {requested.ProductId} tidak ditemukan atau tidak tersedia.");
                    var (name, price) = product!.Value;
                    var subtotal = checked(price * requested.Quantity);
                    total = checked(total + subtotal);
                    items.Add(new(requested.ProductId, name, price, requested.Quantity, subtotal));
                }
                Guard.That(total is > 0 and <= 9_999_999_999_999_999.99m,
                    ErrorCode.Validation, "Total pesanan terlalu besar.");

                var number = "ORD-" + DateTime.UtcNow.ToString("yyyyMMddHHmmss") + "-" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
                await using var header = new SqlCommand(
                    "INSERT dbo.GuestOrders (Number,RequestId,GuestName,CreatedAt,PaymentMethod,Total,Status) " +
                    "OUTPUT INSERTED.Id,INSERTED.CreatedAt VALUES (@number,@request,@name," +
                    "TODATETIMEOFFSET(SYSUTCDATETIME(), '+00:00'),@payment,@total,'Menunggu')", connection, transaction);
                header.Parameters.Add("@number", SqlDbType.VarChar, 48).Value = number;
                header.Parameters.Add("@request", SqlDbType.UniqueIdentifier).Value = request.RequestId;
                header.Parameters.Add("@name", SqlDbType.NVarChar, 80).Value = request.GuestName.Trim();
                header.Parameters.Add("@payment", SqlDbType.VarChar, 10).Value = request.PaymentMethod.ToString();
                AddMoney(header, "@total", total);
                long id; DateTimeOffset createdAt;
                await using (var reader = await header.ExecuteReaderAsync(cancellationToken))
                {
                    await reader.ReadAsync(cancellationToken);
                    id = reader.GetInt64(0); createdAt = reader.GetDateTimeOffset(1);
                }
                foreach (var item in items)
                    await InsertItemAsync(connection, transaction, id, item, cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return new GuestOrderDto(id, number, request.GuestName.Trim(), createdAt,
                    request.PaymentMethod, total, items);
            }
            catch (SqlException e) when (e.Number is 2601 or 2627)
            {
                try { await transaction.RollbackAsync(CancellationToken.None); } catch { /* original error wins */ }
                var previous = await FindByRequestIdAsync(request!.RequestId, cancellationToken);
                Guard.That(previous is not null, ErrorCode.Conflict, "Pesanan ini sudah pernah dikirim.");
                return previous!;
            }
            catch
            {
                try { await transaction.RollbackAsync(CancellationToken.None); } catch { /* original error wins */ }
                throw;
            }
        });

    public Task<ServiceResult<IReadOnlyList<GuestOrderDto>>> GetPendingOrdersAsync(CancellationToken cancellationToken = default)
        => Execute.Run<IReadOnlyList<GuestOrderDto>>(async () =>
        {
            await session.RequireAsync(db, cancellationToken, UserRole.Kasir);
            await using var connection = await db.OpenAsync(cancellationToken);
            await using var command = new SqlCommand(
                "SELECT o.Id,o.Number,o.GuestName,o.CreatedAt,o.PaymentMethod,o.Total,i.ProductId,i.ProductName,i.UnitPrice,i.Quantity,i.Subtotal " +
                "FROM dbo.GuestOrders o JOIN dbo.GuestOrderItems i ON i.GuestOrderId=o.Id " +
                "WHERE o.Status='Menunggu' ORDER BY o.CreatedAt,o.Id,i.Id", connection);
            var orders = new List<GuestOrderDto>();
            var items = new List<TransactionItemDto>();
            long currentId = 0; string number = "", name = ""; DateTimeOffset createdAt = default;
            PaymentMethod payment = default; decimal total = 0;
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var id = reader.GetInt64(0);
                if (currentId != id)
                {
                    if (currentId != 0) orders.Add(new(currentId, number, name, createdAt, payment, total, items.ToArray()));
                    currentId = id; number = reader.GetString(1); name = reader.GetString(2);
                    createdAt = reader.GetDateTimeOffset(3); payment = Enum.Parse<PaymentMethod>(reader.GetString(4));
                    total = reader.GetDecimal(5); items = [];
                }
                items.Add(new(reader.GetInt32(6), reader.GetString(7), reader.GetDecimal(8),
                    reader.GetInt32(9), reader.GetDecimal(10)));
            }
            if (currentId != 0) orders.Add(new(currentId, number, name, createdAt, payment, total, items.ToArray()));
            return orders;
        });

    public Task<ServiceResult<TransactionDto>> CompleteOrderAsync(long id, CancellationToken cancellationToken = default)
        => Execute.Run(async () =>
        {
            var cashier = await session.RequireAsync(db, cancellationToken, UserRole.Kasir);
            Guard.That(id > 0, ErrorCode.Validation, "Id pesanan tidak valid.");
            await using var connection = await db.OpenAsync(cancellationToken);
            await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
            try
            {
                Guard.That(await UserRepository.IsActiveCashierAsync(connection, transaction, cashier.Id, cancellationToken),
                    ErrorCode.Unauthorized, "Sesi kasir tidak aktif. Silakan login kembali.");
                string number, guestName; Guid requestId; DateTimeOffset createdAt;
                PaymentMethod payment; decimal total;
                await using (var command = new SqlCommand(
                    "SELECT Number,RequestId,GuestName,CreatedAt,PaymentMethod,Total,Status FROM dbo.GuestOrders WITH (UPDLOCK,HOLDLOCK) WHERE Id=@id",
                    connection, transaction))
                {
                    command.Parameters.Add("@id", SqlDbType.BigInt).Value = id;
                    await using var reader = await command.ExecuteReaderAsync(cancellationToken);
                    Guard.That(await reader.ReadAsync(cancellationToken), ErrorCode.NotFound, "Pesanan tidak ditemukan.");
                    number = reader.GetString(0); requestId = reader.GetGuid(1); guestName = reader.GetString(2);
                    createdAt = reader.GetDateTimeOffset(3); payment = Enum.Parse<PaymentMethod>(reader.GetString(4));
                    total = reader.GetDecimal(5);
                    Guard.That(reader.GetString(6) == "Menunggu", ErrorCode.Conflict, "Pesanan ini sudah dikonfirmasi.");
                }
                var items = new List<TransactionItemDto>();
                await using (var command = new SqlCommand(
                    "SELECT ProductId,ProductName,UnitPrice,Quantity,Subtotal FROM dbo.GuestOrderItems WHERE GuestOrderId=@id ORDER BY Id",
                    connection, transaction))
                {
                    command.Parameters.Add("@id", SqlDbType.BigInt).Value = id;
                    await using var reader = await command.ExecuteReaderAsync(cancellationToken);
                    while (await reader.ReadAsync(cancellationToken)) items.Add(new(reader.GetInt32(0), reader.GetString(1),
                        reader.GetDecimal(2), reader.GetInt32(3), reader.GetDecimal(4)));
                }
                Guard.That(items.Count > 0, ErrorCode.Validation, "Pesanan tidak memiliki item.");
                var (transactionId, transactionCreatedAt) = await TransactionRepository.InsertHeaderAsync(connection,
                    transaction, number.Replace("ORD-", "TRX-", StringComparison.Ordinal), requestId,
                    cashier.Id, null, payment, total, cancellationToken, guestName);
                foreach (var item in items)
                    await TransactionRepository.InsertItemAsync(connection, transaction, transactionId, item, cancellationToken);
                await using (var update = new SqlCommand(
                    "UPDATE dbo.GuestOrders SET Status='Selesai',TransactionId=@transaction WHERE Id=@id AND Status='Menunggu'",
                    connection, transaction))
                {
                    update.Parameters.Add("@transaction", SqlDbType.BigInt).Value = transactionId;
                    update.Parameters.Add("@id", SqlDbType.BigInt).Value = id;
                    Guard.That(await update.ExecuteNonQueryAsync(cancellationToken) == 1,
                        ErrorCode.Conflict, "Pesanan sudah diproses oleh kasir lain.");
                }
                await transaction.CommitAsync(cancellationToken);
                return new TransactionDto(transactionId, number.Replace("ORD-", "TRX-", StringComparison.Ordinal),
                    cashier.Id, cashier.FullName, null, guestName, transactionCreatedAt, payment, total, items);
            }
            catch
            {
                try { await transaction.RollbackAsync(CancellationToken.None); } catch { /* original error wins */ }
                throw;
            }
        });

    private async Task<GuestOrderDto?> FindByRequestIdAsync(Guid requestId, CancellationToken ct)
    {
        await using var connection = await db.OpenAsync(ct);
        await using var command = new SqlCommand(
            "SELECT Id,Number,GuestName,CreatedAt,PaymentMethod,Total FROM dbo.GuestOrders WHERE RequestId=@request", connection);
        command.Parameters.Add("@request", SqlDbType.UniqueIdentifier).Value = requestId;
        long id; string number, name; DateTimeOffset created; PaymentMethod method; decimal total;
        await using (var reader = await command.ExecuteReaderAsync(ct))
        {
            if (!await reader.ReadAsync(ct)) return null;
            id = reader.GetInt64(0); number = reader.GetString(1); name = reader.GetString(2);
            created = reader.GetDateTimeOffset(3); method = Enum.Parse<PaymentMethod>(reader.GetString(4)); total = reader.GetDecimal(5);
        }
        var items = new List<TransactionItemDto>();
        await using (var lines = new SqlCommand(
            "SELECT ProductId,ProductName,UnitPrice,Quantity,Subtotal FROM dbo.GuestOrderItems WHERE GuestOrderId=@id ORDER BY Id", connection))
        {
            lines.Parameters.Add("@id", SqlDbType.BigInt).Value = id;
            await using var reader = await lines.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct)) items.Add(new(reader.GetInt32(0), reader.GetString(1), reader.GetDecimal(2),
                reader.GetInt32(3), reader.GetDecimal(4)));
        }
        return new(id, number, name, created, method, total, items);
    }

    private static async Task InsertItemAsync(SqlConnection connection, SqlTransaction transaction, long orderId,
        TransactionItemDto item, CancellationToken ct)
    {
        await using var command = new SqlCommand(
            "INSERT dbo.GuestOrderItems (GuestOrderId,ProductId,ProductName,UnitPrice,Quantity,Subtotal) " +
            "VALUES (@order,@product,@name,@price,@quantity,@subtotal)", connection, transaction);
        command.Parameters.Add("@order", SqlDbType.BigInt).Value = orderId;
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
