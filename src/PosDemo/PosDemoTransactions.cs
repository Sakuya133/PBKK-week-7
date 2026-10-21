using Microsoft.Data.Sqlite;
using PosBackend;

namespace PosDemo;

public sealed partial class PosDemoServices
{
    Task<ServiceResult<IReadOnlyList<CustomerOptionDto>>> ITransactionService.GetCustomerOptionsAsync(CancellationToken ct)
        => Run<IReadOnlyList<CustomerOptionDto>>(db =>
        {
            Require(db, UserRole.Kasir);
            using var cmd = Command(db, "SELECT Id,FullName FROM Users WHERE Role='Pelanggan' AND IsActive=1 ORDER BY FullName");
            using var reader = cmd.ExecuteReader();
            var result = new List<CustomerOptionDto>();
            while (reader.Read()) result.Add(new(reader.GetInt32(0), reader.GetString(1)));
            return result;
        }, ct);

    Task<ServiceResult<TransactionDto>> ITransactionService.CheckoutAsync(CheckoutRequest request, CancellationToken ct)
        => Run(db =>
        {
            var cashier = Require(db, UserRole.Kasir);
            Demand(request is not null && request.RequestId != Guid.Empty && Enum.IsDefined(request.PaymentMethod)
                && ValidItems(request.Items) && (request.CustomerId is null || request.CustomerId > 0),
                ErrorCode.Validation, "Data checkout tidak valid.");
            using (var duplicate = Command(db, "SELECT COUNT(*) FROM Transactions WHERE RequestId=$r", null,
                ("$r", request!.RequestId.ToString())))
                Demand((long)duplicate.ExecuteScalar()! == 0, ErrorCode.Conflict, "Permintaan checkout sudah diproses.");
            string? customerName = null;
            if (request.CustomerId is int customerId)
            {
                using var customer = Command(db, "SELECT FullName FROM Users WHERE Id=$id AND Role='Pelanggan' AND IsActive=1", null,
                    ("$id", customerId));
                customerName = customer.ExecuteScalar() as string;
                Demand(customerName is not null, ErrorCode.Validation, "Pelanggan tidak ditemukan atau tidak aktif.");
            }
            using var tx = db.BeginTransaction();
            var (items, total) = SnapshotItems(db, tx, request.Items);
            var sale = InsertSale(db, tx, cashier, request.CustomerId, customerName, null,
                request.PaymentMethod, request.RequestId, NewNumber("TRX"), items, total);
            tx.Commit();
            return sale;
        }, ct);

    Task<ServiceResult<TransactionDto>> ITransactionService.GetByRequestIdAsync(Guid requestId, CancellationToken ct)
        => Run(db =>
        {
            var user = Require(db, UserRole.Kasir);
            Demand(requestId != Guid.Empty, ErrorCode.Validation, "RequestId tidak valid.");
            using var cmd = Command(db, "SELECT Id FROM Transactions WHERE RequestId=$r AND CashierId=$c", null,
                ("$r", requestId.ToString()), ("$c", user.Id));
            var value = cmd.ExecuteScalar();
            Demand(value is not null, ErrorCode.NotFound, "Transaksi untuk permintaan ini tidak ditemukan.");
            return ReadSale(db, (long)value!, user)!;
        }, ct);

    async Task<ServiceResult<IReadOnlyList<TransactionSummaryDto>>> ITransactionService.GetTransactionsAsync(CancellationToken ct)
    {
        var page = await ((ITransactionService)this).GetTransactionsPageAsync(1, 100, ct);
        return page.Success
            ? ServiceResult<IReadOnlyList<TransactionSummaryDto>>.Ok(page.Value!.Items)
            : ServiceResult<IReadOnlyList<TransactionSummaryDto>>.Fail(page.Error, page.Message);
    }

    Task<ServiceResult<TransactionPageDto>> ITransactionService.GetTransactionsPageAsync(int page, int pageSize, CancellationToken ct)
        => Run(db =>
        {
            var user = Require(db, UserRole.Admin, UserRole.Kasir, UserRole.Pelanggan);
            Demand(page > 0 && pageSize is >= 1 and <= 100 && (long)(page - 1) * pageSize <= int.MaxValue - pageSize - 1,
                ErrorCode.Validation, "Halaman transaksi tidak valid.");
            using var cmd = Command(db, "SELECT t.Id,t.Number,t.CreatedAt,c.FullName,COALESCE(p.FullName,t.GuestName)," +
                "t.PaymentMethod,t.TotalCents FROM Transactions t JOIN Users c ON c.Id=t.CashierId " +
                "LEFT JOIN Users p ON p.Id=t.CustomerId WHERE " +
                "($role='Admin' OR ($role='Kasir' AND t.CashierId=$user) OR ($role='Pelanggan' AND t.CustomerId=$user)) " +
                "ORDER BY t.CreatedAt DESC,t.Id DESC LIMIT $take OFFSET $skip", null,
                ("$role", user.Role.ToString()), ("$user", user.Id), ("$take", pageSize + 1), ("$skip", (page - 1) * pageSize));
            using var reader = cmd.ExecuteReader();
            var rows = new List<TransactionSummaryDto>();
            while (reader.Read()) rows.Add(new(reader.GetInt64(0), reader.GetString(1), When(reader.GetInt64(2)),
                reader.GetString(3), reader.IsDBNull(4) ? null : reader.GetString(4),
                Method(reader.GetString(5)), Money(reader.GetInt64(6))));
            var hasMore = rows.Count > pageSize;
            if (hasMore) rows.RemoveAt(rows.Count - 1);
            return new TransactionPageDto(rows, page, pageSize, hasMore);
        }, ct);

    Task<ServiceResult<TransactionDto>> ITransactionService.GetDetailAsync(long id, CancellationToken ct)
        => Run(db =>
        {
            var user = Require(db, UserRole.Admin, UserRole.Kasir, UserRole.Pelanggan);
            Demand(id > 0, ErrorCode.Validation, "Id transaksi tidak valid.");
            var sale = ReadSale(db, id, user);
            Demand(sale is not null, ErrorCode.NotFound, "Transaksi tidak ditemukan atau tidak dapat diakses.");
            return sale!;
        }, ct);

    Task<ServiceResult<DailySalesDto>> IDashboardService.GetTodayAsync(CancellationToken ct)
        => Run(db =>
        {
            Require(db, UserRole.Admin);
            var jakarta = DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(7));
            var date = DateOnly.FromDateTime(jakarta.DateTime);
            var start = new DateTimeOffset(date.ToDateTime(TimeOnly.MinValue), TimeSpan.FromHours(7)).ToUnixTimeMilliseconds();
            var end = start + 86_400_000;
            long revenue, count;
            using (var totals = Command(db, "SELECT COALESCE(SUM(TotalCents),0),COUNT(*) FROM Transactions " +
                "WHERE CreatedAt >= $start AND CreatedAt < $end", null, ("$start", start), ("$end", end)))
            using (var reader = totals.ExecuteReader())
            {
                reader.Read();
                revenue = reader.GetInt64(0); count = reader.GetInt64(1);
            }
            using var top = Command(db, "SELECT i.ProductId,p.Name,SUM(i.Quantity) FROM TransactionItems i " +
                "JOIN Transactions t ON t.Id=i.TransactionId JOIN Products p ON p.Id=i.ProductId " +
                "WHERE t.CreatedAt >= $start AND t.CreatedAt < $end GROUP BY i.ProductId,p.Name " +
                "ORDER BY SUM(i.Quantity) DESC,i.ProductId LIMIT 5", null, ("$start", start), ("$end", end));
            using var topReader = top.ExecuteReader();
            var products = new List<TopProductDto>();
            while (topReader.Read()) products.Add(new(topReader.GetInt32(0), topReader.GetString(1), topReader.GetInt32(2)));
            return new DailySalesDto(date, Money(revenue), checked((int)count), products);
        }, ct);

    private static bool ValidItems(IReadOnlyList<CheckoutItem>? items) => items is { Count: > 0 and <= 100 }
        && items.All(x => x.ProductId > 0 && x.Quantity is > 0 and <= 100)
        && items.Select(x => x.ProductId).Distinct().Count() == items.Count;

    private static (List<TransactionItemDto> Items, long Total) SnapshotItems(SqliteConnection db, SqliteTransaction tx,
        IReadOnlyList<CheckoutItem> requested)
    {
        var items = new List<TransactionItemDto>();
        long total = 0;
        foreach (var item in requested)
        {
            using var cmd = Command(db, "SELECT Name,PriceCents FROM Products WHERE Id=$id AND IsActive=1", tx,
                ("$id", item.ProductId));
            using var reader = cmd.ExecuteReader();
            Demand(reader.Read(), ErrorCode.Validation, $"Produk {item.ProductId} tidak ditemukan atau tidak aktif.");
            var name = reader.GetString(0);
            var price = reader.GetInt64(1);
            var subtotal = checked(price * item.Quantity);
            total = checked(total + subtotal);
            items.Add(new(item.ProductId, name, Money(price), item.Quantity, Money(subtotal)));
        }
        Demand(total > 0 && total <= 999_999_999_999_999_999L, ErrorCode.Validation, "Total terlalu besar.");
        return (items, total);
    }

    private static TransactionDto InsertSale(SqliteConnection db, SqliteTransaction tx, UserDto cashier,
        int? customerId, string? customerName, string? guestName, PaymentMethod method, Guid requestId,
        string number, IReadOnlyList<TransactionItemDto> items, long total)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        using (var header = Command(db, "INSERT INTO Transactions(Number,RequestId,CashierId,CustomerId,GuestName,CreatedAt,PaymentMethod,TotalCents) " +
            "VALUES($n,$r,$c,$customer,$guest,$at,$payment,$total)", tx,
            ("$n", number), ("$r", requestId.ToString()), ("$c", cashier.Id), ("$customer", customerId),
            ("$guest", guestName), ("$at", now), ("$payment", method.ToString()), ("$total", total))) header.ExecuteNonQuery();
        var id = LastId(db, tx);
        foreach (var item in items)
            using (var line = Command(db, "INSERT INTO TransactionItems(TransactionId,ProductId,ProductName,UnitPriceCents,Quantity,SubtotalCents) " +
                "VALUES($t,$p,$n,$price,$q,$subtotal)", tx, ("$t", id), ("$p", item.ProductId),
                ("$n", item.ProductName), ("$price", Cents(item.UnitPrice)), ("$q", item.Quantity),
                ("$subtotal", Cents(item.Subtotal)))) line.ExecuteNonQuery();
        return new TransactionDto(id, number, cashier.Id, cashier.FullName, customerId,
            customerName ?? guestName, When(now), method, Money(total), items);
    }

    private static TransactionDto? ReadSale(SqliteConnection db, long id, UserDto user)
    {
        long saleId, created, total; int cashierId; int? customerId; string number, cashierName, payment;
        string? customerName;
        using (var cmd = Command(db, "SELECT t.Id,t.Number,t.CashierId,c.FullName,t.CustomerId," +
            "COALESCE(p.FullName,t.GuestName),t.CreatedAt,t.PaymentMethod,t.TotalCents FROM Transactions t " +
            "JOIN Users c ON c.Id=t.CashierId LEFT JOIN Users p ON p.Id=t.CustomerId WHERE t.Id=$id AND " +
            "($role='Admin' OR ($role='Kasir' AND t.CashierId=$user) OR ($role='Pelanggan' AND t.CustomerId=$user))", null,
            ("$id", id), ("$role", user.Role.ToString()), ("$user", user.Id)))
        using (var reader = cmd.ExecuteReader())
        {
            if (!reader.Read()) return null;
            saleId = reader.GetInt64(0); number = reader.GetString(1); cashierId = reader.GetInt32(2);
            cashierName = reader.GetString(3); customerId = reader.IsDBNull(4) ? null : reader.GetInt32(4);
            customerName = reader.IsDBNull(5) ? null : reader.GetString(5); created = reader.GetInt64(6);
            payment = reader.GetString(7); total = reader.GetInt64(8);
        }
        using var lines = Command(db, "SELECT ProductId,ProductName,UnitPriceCents,Quantity,SubtotalCents " +
            "FROM TransactionItems WHERE TransactionId=$id ORDER BY Id", null, ("$id", id));
        using var itemReader = lines.ExecuteReader();
        var items = new List<TransactionItemDto>();
        while (itemReader.Read()) items.Add(new(itemReader.GetInt32(0), itemReader.GetString(1),
            Money(itemReader.GetInt64(2)), itemReader.GetInt32(3), Money(itemReader.GetInt64(4))));
        return new TransactionDto(saleId, number, cashierId, cashierName, customerId, customerName,
            When(created), Method(payment), Money(total), items);
    }
}
