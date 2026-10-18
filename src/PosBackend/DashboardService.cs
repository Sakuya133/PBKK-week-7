using System.Data;
using Microsoft.Data.SqlClient;

namespace PosBackend;

internal sealed class DashboardService(Database db, Session session) : IDashboardService
{
    public Task<ServiceResult<DailySalesDto>> GetTodayAsync(CancellationToken cancellationToken = default)
        => Execute.Run(async () =>
        {
            await session.RequireAsync(db, cancellationToken, UserRole.Admin);
            TimeZoneInfo jakarta;
            try { jakarta = TimeZoneInfo.FindSystemTimeZoneById("Asia/Jakarta"); }
            catch (TimeZoneNotFoundException) { jakarta = TimeZoneInfo.FindSystemTimeZoneById("SE Asia Standard Time"); }
            await using var connection = await db.OpenAsync(cancellationToken);
            await using var clock = new SqlCommand("SELECT TODATETIMEOFFSET(SYSUTCDATETIME(), '+00:00')", connection);
            var serverNow = (DateTimeOffset)(await clock.ExecuteScalarAsync(cancellationToken))!;
            var localNow = TimeZoneInfo.ConvertTime(serverNow, jakarta);
            var date = DateOnly.FromDateTime(localNow.DateTime);
            var startLocal = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
            var startUtc = new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(startLocal, jakarta));
            var endUtc = new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(startLocal.AddDays(1), jakarta));

            await using var totals = new SqlCommand(
                "SELECT COALESCE(SUM(Total),0), COUNT(*) FROM dbo.Transactions " +
                "WHERE CreatedAt>=@start AND CreatedAt<@end", connection);
            AddBounds(totals, startUtc, endUtc);
            decimal revenue; int count;
            await using (var reader = await totals.ExecuteReaderAsync(cancellationToken))
            {
                await reader.ReadAsync(cancellationToken);
                revenue = reader.GetDecimal(0); count = reader.GetInt32(1);
            }
            await using var top = new SqlCommand(
                "SELECT TOP (5) i.ProductId, p.Name, SUM(i.Quantity) AS UnitsSold " +
                "FROM dbo.TransactionItems i JOIN dbo.Transactions t ON t.Id=i.TransactionId " +
                "JOIN dbo.Products p ON p.Id=i.ProductId " +
                "WHERE t.CreatedAt>=@start AND t.CreatedAt<@end " +
                "GROUP BY i.ProductId,p.Name ORDER BY UnitsSold DESC,i.ProductId", connection);
            AddBounds(top, startUtc, endUtc);
            var products = new List<TopProductDto>();
            await using var topReader = await top.ExecuteReaderAsync(cancellationToken);
            while (await topReader.ReadAsync(cancellationToken)) products.Add(new(topReader.GetInt32(0),
                topReader.GetString(1), topReader.GetInt32(2)));
            return new DailySalesDto(date, revenue, count, products);
        });

    private static void AddBounds(SqlCommand command, DateTimeOffset start, DateTimeOffset end)
    {
        command.Parameters.Add("@start", SqlDbType.DateTimeOffset).Value = start;
        command.Parameters.Add("@end", SqlDbType.DateTimeOffset).Value = end;
    }
}
