using System.Data;
using Microsoft.Data.SqlClient;

namespace PosBackend;

internal sealed class TransactionService(Database db, Session session) : ITransactionService
{
    private readonly UserRepository users = new(db);
    public Task<ServiceResult<IReadOnlyList<CustomerOptionDto>>> GetCustomerOptionsAsync(CancellationToken cancellationToken = default)
        => Execute.Run<IReadOnlyList<CustomerOptionDto>>(async () =>
        {
            await session.RequireAsync(db, cancellationToken, UserRole.Kasir);
            return await users.GetActiveCustomersAsync(cancellationToken);
        });

    public Task<ServiceResult<TransactionDto>> CheckoutAsync(CheckoutRequest request, CancellationToken cancellationToken = default)
        => Execute.Run(async () =>
        {
            var cashier = await session.RequireAsync(db, cancellationToken, UserRole.Kasir);
            Guard.That(request is not null && request.RequestId != Guid.Empty && Enum.IsDefined(request.PaymentMethod)
                && request.Items is { Count: > 0 and <= 100 }
                && request.Items.All(x => x.ProductId > 0 && x.Quantity > 0)
                && request.Items.Select(x => x.ProductId).Distinct().Count() == request.Items.Count
                && (request.CustomerId is null || request.CustomerId > 0),
                ErrorCode.Validation, "Data checkout tidak valid. Produk harus unik dan kuantitas harus positif.");

            await using var connection = await db.OpenAsync(cancellationToken);
            await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
            try
            {
                Guard.That(await UserRepository.IsActiveCashierAsync(connection, transaction, cashier.Id,
                    cancellationToken), ErrorCode.Unauthorized, "Sesi kasir tidak aktif. Silakan login kembali.");
                string? customerName = null;
                if (request!.CustomerId is int customerId)
                {
                    customerName = await UserRepository.GetActiveCustomerNameAsync(connection, transaction,
                        customerId, cancellationToken);
                    Guard.That(customerName is not null, ErrorCode.Validation, "Pelanggan tidak ditemukan atau tidak aktif.");
                }

                var items = new List<TransactionItemDto>();
                decimal total = 0;
                foreach (var requested in request.Items)
                {
                    var product = await ProductRepository.GetActiveForCheckoutAsync(connection, transaction,
                        requested.ProductId, cancellationToken);
                    Guard.That(product is not null, ErrorCode.Validation,
                        $"Produk {requested.ProductId} tidak ditemukan atau tidak aktif.");
                    var (name, price) = product!.Value;
                    var subtotal = checked(price * requested.Quantity);
                    total = checked(total + subtotal);
                    items.Add(new(requested.ProductId, name, price, requested.Quantity, subtotal));
                }
                Guard.That(total <= 9_999_999_999_999_999.99m, ErrorCode.Validation, "Total transaksi terlalu besar.");
                var number = "TRX-" + DateTime.UtcNow.ToString("yyyyMMddHHmmss") + "-" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant();
                var (id, createdAt) = await TransactionRepository.InsertHeaderAsync(connection, transaction, number,
                    request.RequestId, cashier.Id, request.CustomerId, request.PaymentMethod, total, cancellationToken);
                foreach (var item in items)
                    await TransactionRepository.InsertItemAsync(connection, transaction, id, item, cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return new TransactionDto(id, number, cashier.Id, cashier.FullName, request.CustomerId,
                    customerName, createdAt, request.PaymentMethod, total, items);
            }
            catch (SqlException e) when (e.Number is 2601 or 2627)
            {
                try { await transaction.RollbackAsync(CancellationToken.None); } catch { /* original error wins */ }
                throw new ServiceFault(ErrorCode.Conflict,
                    "Permintaan checkout sudah pernah diproses. Cari transaksi dengan RequestId yang sama.");
            }
            catch
            {
                try { await transaction.RollbackAsync(CancellationToken.None); } catch { /* original error wins */ }
                throw;
            }
        });

    public Task<ServiceResult<TransactionDto>> GetByRequestIdAsync(Guid requestId, CancellationToken cancellationToken = default)
        => Execute.Run(async () =>
        {
            var cashier = await session.RequireAsync(db, cancellationToken, UserRole.Kasir);
            Guard.That(requestId != Guid.Empty, ErrorCode.Validation, "RequestId tidak valid.");
            await using var connection = await db.OpenAsync(cancellationToken);
            await using var command = new SqlCommand(
                "SELECT Id FROM dbo.Transactions WHERE RequestId=@request AND CashierId=@cashier", connection);
            command.Parameters.Add("@request", SqlDbType.UniqueIdentifier).Value = requestId;
            command.Parameters.Add("@cashier", SqlDbType.Int).Value = cashier.Id;
            var value = await command.ExecuteScalarAsync(cancellationToken);
            Guard.That(value is not null, ErrorCode.NotFound, "Transaksi untuk permintaan ini tidak ditemukan.");
            var detail = await GetDetailAsync((long)value!, cancellationToken);
            Guard.That(detail.Success && detail.Value is not null, detail.Error, detail.Message);
            return detail.Value!;
        });

    public async Task<ServiceResult<IReadOnlyList<TransactionSummaryDto>>> GetTransactionsAsync(CancellationToken cancellationToken = default)
    {
        var page = await GetTransactionsPageAsync(1, 100, cancellationToken);
        return page.Success
            ? ServiceResult<IReadOnlyList<TransactionSummaryDto>>.Ok(page.Value!.Items)
            : ServiceResult<IReadOnlyList<TransactionSummaryDto>>.Fail(page.Error, page.Message);
    }

    public Task<ServiceResult<TransactionPageDto>> GetTransactionsPageAsync(int page, int pageSize = 50,
        CancellationToken cancellationToken = default)
        => Execute.Run(async () =>
        {
            var user = await session.RequireAsync(db, cancellationToken, UserRole.Admin, UserRole.Kasir, UserRole.Pelanggan);
            Guard.That(page > 0 && pageSize is >= 1 and <= 100 && (long)(page - 1) * pageSize <= int.MaxValue - pageSize - 1,
                ErrorCode.Validation, "Halaman transaksi tidak valid.");
            await using var connection = await db.OpenAsync(cancellationToken);
            await using var command = new SqlCommand(
                "SELECT t.Id,t.Number,t.CreatedAt,c.FullName,p.FullName,t.PaymentMethod,t.Total " +
                "FROM dbo.Transactions t JOIN dbo.Users c ON c.Id=t.CashierId " +
                "LEFT JOIN dbo.Users p ON p.Id=t.CustomerId " +
                "WHERE (@role='Admin' OR (@role='Kasir' AND t.CashierId=@user) " +
                "OR (@role='Pelanggan' AND t.CustomerId=@user)) ORDER BY t.CreatedAt DESC,t.Id DESC " +
                "OFFSET @skip ROWS FETCH NEXT @take ROWS ONLY", connection);
            command.Parameters.Add("@role", SqlDbType.VarChar, 16).Value = user.Role.ToString();
            command.Parameters.Add("@user", SqlDbType.Int).Value = user.Id;
            command.Parameters.Add("@skip", SqlDbType.Int).Value = (page - 1) * pageSize;
            command.Parameters.Add("@take", SqlDbType.Int).Value = pageSize + 1;
            var rows = new List<TransactionSummaryDto>();
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken)) rows.Add(new(reader.GetInt64(0), reader.GetString(1),
                reader.GetDateTimeOffset(2), reader.GetString(3), reader.IsDBNull(4) ? null : reader.GetString(4),
                Enum.Parse<PaymentMethod>(reader.GetString(5)), reader.GetDecimal(6)));
            var hasMore = rows.Count > pageSize;
            if (hasMore) rows.RemoveAt(rows.Count - 1);
            return new TransactionPageDto(rows, page, pageSize, hasMore);
        });

    public Task<ServiceResult<TransactionDto>> GetDetailAsync(long id, CancellationToken cancellationToken = default)
        => Execute.Run(async () =>
        {
            var user = await session.RequireAsync(db, cancellationToken, UserRole.Admin, UserRole.Kasir, UserRole.Pelanggan);
            Guard.That(id > 0, ErrorCode.Validation, "Id transaksi tidak valid.");
            await using var connection = await db.OpenAsync(cancellationToken);
            await using var command = new SqlCommand(
                "SELECT t.Id,t.Number,t.CashierId,c.FullName,t.CustomerId,p.FullName,t.CreatedAt,t.PaymentMethod,t.Total " +
                "FROM dbo.Transactions t JOIN dbo.Users c ON c.Id=t.CashierId " +
                "LEFT JOIN dbo.Users p ON p.Id=t.CustomerId WHERE t.Id=@id AND " +
                "(@role='Admin' OR (@role='Kasir' AND t.CashierId=@user) " +
                "OR (@role='Pelanggan' AND t.CustomerId=@user))", connection);
            command.Parameters.Add("@id", SqlDbType.BigInt).Value = id;
            command.Parameters.Add("@role", SqlDbType.VarChar, 16).Value = user.Role.ToString();
            command.Parameters.Add("@user", SqlDbType.Int).Value = user.Id;
            long transactionId; string number, cashierName; int cashierId; int? customerId; string? customerName;
            DateTimeOffset createdAt; PaymentMethod payment; decimal total;
            await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
            {
                Guard.That(await reader.ReadAsync(cancellationToken), ErrorCode.NotFound, "Transaksi tidak ditemukan atau tidak dapat diakses.");
                transactionId = reader.GetInt64(0); number = reader.GetString(1); cashierId = reader.GetInt32(2);
                cashierName = reader.GetString(3); customerId = reader.IsDBNull(4) ? null : reader.GetInt32(4);
                customerName = reader.IsDBNull(5) ? null : reader.GetString(5); createdAt = reader.GetDateTimeOffset(6);
                payment = Enum.Parse<PaymentMethod>(reader.GetString(7)); total = reader.GetDecimal(8);
            }
            await using var lines = new SqlCommand(
                "SELECT ProductId,ProductName,UnitPrice,Quantity,Subtotal FROM dbo.TransactionItems WHERE TransactionId=@id ORDER BY Id", connection);
            lines.Parameters.Add("@id", SqlDbType.BigInt).Value = id;
            var items = new List<TransactionItemDto>();
            await using var itemReader = await lines.ExecuteReaderAsync(cancellationToken);
            while (await itemReader.ReadAsync(cancellationToken)) items.Add(new(itemReader.GetInt32(0), itemReader.GetString(1),
                itemReader.GetDecimal(2), itemReader.GetInt32(3), itemReader.GetDecimal(4)));
            return new TransactionDto(transactionId, number, cashierId, cashierName, customerId, customerName,
                createdAt, payment, total, items);
        });

}
