using Microsoft.Data.Sqlite;
using PosBackend;

namespace PosDemo;

public sealed partial class PosDemoServices
{
    Task<ServiceResult<GuestOrderDto>> IKioskService.PlaceOrderAsync(GuestOrderRequest request, CancellationToken ct)
        => Run(db =>
        {
            Demand(request is not null && !string.IsNullOrWhiteSpace(request.GuestName)
                && request.GuestName.Trim().Length <= 80 && request.RequestId != Guid.Empty
                && Enum.IsDefined(request.PaymentMethod) && ValidItems(request.Items),
                ErrorCode.Validation, "Nama, metode pembayaran, atau item pesanan tidak valid.");
            using (var prior = Command(db, "SELECT Id FROM GuestOrders WHERE RequestId=$r", null,
                ("$r", request!.RequestId.ToString())))
            {
                var existing = prior.ExecuteScalar();
                if (existing is not null) return ReadGuestOrder(db, (long)existing)!;
            }
            using var tx = db.BeginTransaction();
            var (items, total) = SnapshotItems(db, tx, request.Items);
            var number = NewNumber("ORD");
            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            using (var header = Command(db, "INSERT INTO GuestOrders(Number,RequestId,GuestName,CreatedAt,PaymentMethod,TotalCents,Status) " +
                "VALUES($n,$r,$guest,$at,$payment,$total,'Menunggu')", tx,
                ("$n", number), ("$r", request.RequestId.ToString()), ("$guest", request.GuestName.Trim()),
                ("$at", now), ("$payment", request.PaymentMethod.ToString()), ("$total", total))) header.ExecuteNonQuery();
            var id = LastId(db, tx);
            foreach (var item in items)
                using (var line = Command(db, "INSERT INTO GuestOrderItems(GuestOrderId,ProductId,ProductName,UnitPriceCents,Quantity,SubtotalCents) " +
                    "VALUES($o,$p,$n,$price,$q,$subtotal)", tx, ("$o", id), ("$p", item.ProductId),
                    ("$n", item.ProductName), ("$price", Cents(item.UnitPrice)), ("$q", item.Quantity),
                    ("$subtotal", Cents(item.Subtotal)))) line.ExecuteNonQuery();
            tx.Commit();
            return new GuestOrderDto(id, number, request.GuestName.Trim(), When(now), request.PaymentMethod,
                Money(total), items);
        }, ct);

    Task<ServiceResult<IReadOnlyList<GuestOrderDto>>> IKioskService.GetPendingOrdersAsync(CancellationToken ct)
        => Run<IReadOnlyList<GuestOrderDto>>(db =>
        {
            Require(db, UserRole.Kasir);
            using var cmd = Command(db, "SELECT Id FROM GuestOrders WHERE Status='Menunggu' ORDER BY CreatedAt,Id");
            using var reader = cmd.ExecuteReader();
            var ids = new List<long>();
            while (reader.Read()) ids.Add(reader.GetInt64(0));
            reader.Close();
            return ids.Select(id => ReadGuestOrder(db, id)!).ToList();
        }, ct);

    Task<ServiceResult<TransactionDto>> IKioskService.CompleteOrderAsync(long id, CancellationToken ct)
        => Run(db =>
        {
            var cashier = Require(db, UserRole.Kasir);
            Demand(id > 0, ErrorCode.Validation, "Id pesanan tidak valid.");
            using var tx = db.BeginTransaction();
            string number, guestName, payment, requestId, status; long total;
            using (var cmd = Command(db, "SELECT Number,GuestName,PaymentMethod,RequestId,Status,TotalCents " +
                "FROM GuestOrders WHERE Id=$id", tx, ("$id", id)))
            using (var reader = cmd.ExecuteReader())
            {
                Demand(reader.Read(), ErrorCode.NotFound, "Pesanan tidak ditemukan.");
                number = reader.GetString(0); guestName = reader.GetString(1); payment = reader.GetString(2);
                requestId = reader.GetString(3); status = reader.GetString(4); total = reader.GetInt64(5);
            }
            Demand(status == "Menunggu", ErrorCode.Conflict, "Pesanan sudah diproses dan tidak dapat dikonfirmasi.");
            var items = ReadGuestItems(db, id, tx);
            Demand(items.Count > 0, ErrorCode.Validation, "Pesanan tidak memiliki item.");
            var sale = InsertSale(db, tx, cashier, null, null, guestName, Method(payment), Guid.Parse(requestId),
                number.Replace("ORD-", "TRX-", StringComparison.Ordinal), items, total);
            using (var update = Command(db, "UPDATE GuestOrders SET Status='Selesai',TransactionId=$transaction " +
                "WHERE Id=$id AND Status='Menunggu'", tx, ("$transaction", sale.Id), ("$id", id)))
                Demand(update.ExecuteNonQuery() == 1, ErrorCode.Conflict, "Pesanan sudah diproses oleh kasir lain.");
            tx.Commit();
            return sale;
        }, ct);

    Task<ServiceResult<bool>> IKioskService.RejectOrderAsync(long id, CancellationToken ct)
        => Run(db =>
        {
            var cashier = Require(db, UserRole.Kasir);
            Demand(id > 0, ErrorCode.Validation, "Id pesanan tidak valid.");
            using var tx = db.BeginTransaction();
            using (var state = Command(db, "SELECT Status FROM GuestOrders WHERE Id=$id", tx, ("$id", id)))
            {
                var status = state.ExecuteScalar() as string;
                Demand(status is not null, ErrorCode.NotFound, "Pesanan tidak ditemukan.");
                Demand(status == "Menunggu", ErrorCode.Conflict, "Pesanan sudah diproses dan tidak dapat ditolak.");
            }
            using (var cmd = Command(db, "UPDATE GuestOrders SET Status='Ditolak',RejectedAt=$at,RejectedBy=$cashier " +
                "WHERE Id=$id AND Status='Menunggu'", tx, ("$at", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()),
                ("$cashier", cashier.Id), ("$id", id)))
                Demand(cmd.ExecuteNonQuery() == 1, ErrorCode.Conflict, "Pesanan sudah diproses oleh kasir lain.");
            tx.Commit();
            return true;
        }, ct);

    private static GuestOrderDto? ReadGuestOrder(SqliteConnection db, long id)
    {
        long orderId, created, total; string number, name, payment;
        using (var cmd = Command(db, "SELECT Id,Number,GuestName,CreatedAt,PaymentMethod,TotalCents FROM GuestOrders WHERE Id=$id", null,
            ("$id", id)))
        using (var reader = cmd.ExecuteReader())
        {
            if (!reader.Read()) return null;
            orderId = reader.GetInt64(0); number = reader.GetString(1); name = reader.GetString(2);
            created = reader.GetInt64(3); payment = reader.GetString(4); total = reader.GetInt64(5);
        }
        return new GuestOrderDto(orderId, number, name, When(created), Method(payment), Money(total), ReadGuestItems(db, id));
    }

    private static List<TransactionItemDto> ReadGuestItems(SqliteConnection db, long id, SqliteTransaction? tx = null)
    {
        using var cmd = Command(db, "SELECT ProductId,ProductName,UnitPriceCents,Quantity,SubtotalCents " +
            "FROM GuestOrderItems WHERE GuestOrderId=$id ORDER BY Id", tx, ("$id", id));
        using var reader = cmd.ExecuteReader();
        var items = new List<TransactionItemDto>();
        while (reader.Read()) items.Add(new(reader.GetInt32(0), reader.GetString(1), Money(reader.GetInt64(2)),
            reader.GetInt32(3), Money(reader.GetInt64(4))));
        return items;
    }
}
