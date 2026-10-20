using PosBackend;

namespace WpfApp1;

// TEMPORARY: sample data for testing the UI because database is not up.
// Replace this with the real API service before final integration.
public sealed class DemoTransactionService : ITransactionService
{
    private readonly List<TransactionSummaryDto> _transactions = new()
    {
        new(1003, "TRX-20261020-003",
            DateTimeOffset.Now.AddMinutes(-15),
            "Andi", null, PaymentMethod.Qris, 85000m),

        new(1002, "TRX-20261020-002",
            DateTimeOffset.Now.AddHours(-1),
            "Budi", "Pelanggan Demo", PaymentMethod.Tunai, 42000m),

        new(1001, "TRX-20261020-001",
            DateTimeOffset.Now.AddHours(-2),
            "Andi", null, PaymentMethod.Tunai, 30000m)
    };

    public Task<ServiceResult<IReadOnlyList<CustomerOptionDto>>>
        GetCustomerOptionsAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<CustomerOptionDto> customers =
            new List<CustomerOptionDto>
            {
                new(1, "Pelanggan Demo")
            };

        return Task.FromResult(
            ServiceResult<IReadOnlyList<CustomerOptionDto>>.Ok(customers));
    }

    public Task<ServiceResult<TransactionDto>> CheckoutAsync(
        CheckoutRequest request,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(ServiceResult<TransactionDto>.Fail(
            ErrorCode.Validation,
            "Checkout is not available in demo mode."));
    }

    public Task<ServiceResult<TransactionDto>> GetByRequestIdAsync(
        Guid requestId,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(ServiceResult<TransactionDto>.Fail(
            ErrorCode.NotFound, "Demo request not found."));
    }

    public Task<ServiceResult<IReadOnlyList<TransactionSummaryDto>>>
        GetTransactionsAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<TransactionSummaryDto> items = _transactions;

        return Task.FromResult(
            ServiceResult<IReadOnlyList<TransactionSummaryDto>>.Ok(items));
    }

    public Task<ServiceResult<TransactionPageDto>> GetTransactionsPageAsync(
        int page,
        int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        if (page < 1 || pageSize < 1 || pageSize > 100)
        {
            return Task.FromResult(
                ServiceResult<TransactionPageDto>.Fail(
                    ErrorCode.Validation, "Halaman tidak valid."));
        }

        var items = _transactions
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToList();

        var result = new TransactionPageDto(
            items, page, pageSize,
            page * pageSize < _transactions.Count);

        return Task.FromResult(
            ServiceResult<TransactionPageDto>.Ok(result));
    }

    public Task<ServiceResult<TransactionDto>> GetDetailAsync(
        long id,
        CancellationToken cancellationToken = default)
    {
        var summary = _transactions.FirstOrDefault(t => t.Id == id);

        if (summary is null)
        {
            return Task.FromResult(
                ServiceResult<TransactionDto>.Fail(
                    ErrorCode.NotFound, "Transaksi tidak ditemukan."));
        }

        var items = id switch
        {
            1003 => new[]
            {
                new TransactionItemDto(1, "Mie Goreng", 25000m, 2, 50000m),
                new TransactionItemDto(2, "Es Teh", 10000m, 1, 10000m),
                new TransactionItemDto(3, "Roti", 25000m, 1, 25000m)
            },
            1002 => new[]
            {
                new TransactionItemDto(4, "Ramen", 30000m, 1, 30000m),
                new TransactionItemDto(5, "Es Teh", 12000m, 1, 12000m)
            },
            _ => new[]
            {
                new TransactionItemDto(6, "Nasi Goreng", 30000m, 1, 30000m)
            }
        };

        var transaction = new TransactionDto(
            summary.Id,
            summary.Number,
            1,
            summary.CashierName,
            summary.CustomerName is null ? null : 1,
            summary.CustomerName,
            summary.CreatedAt,
            summary.PaymentMethod,
            summary.Total,
            items);

        return Task.FromResult(
            ServiceResult<TransactionDto>.Ok(transaction));
    }
}