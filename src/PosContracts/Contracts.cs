namespace PosBackend;

public enum UserRole { Admin, Kasir, Pelanggan }
public enum PaymentMethod { Tunai, Qris }
public enum ErrorCode { None, Validation, Unauthorized, Forbidden, NotFound, Conflict, DatabaseUnavailable, DatabaseError, InternalError, ServiceUnavailable, RateLimited }

public sealed record ServiceResult<T>(bool Success, T? Value, ErrorCode Error, string Message)
{
    public static ServiceResult<T> Ok(T value) => new(true, value, ErrorCode.None, "OK");
    public static ServiceResult<T> Fail(ErrorCode code, string message) => new(false, default, code, message);
}

public sealed record UserDto(int Id, string Username, string FullName, UserRole Role);
public sealed record CategoryDto(int Id, string Name);
public sealed record ProductDto(int Id, string Name, int CategoryId, string CategoryName, decimal Price, bool IsActive);
public sealed record ProductInput(string Name, int CategoryId, decimal Price, bool IsActive = true);
public sealed record CustomerOptionDto(int Id, string FullName);
public sealed record CheckoutItem(int ProductId, int Quantity);
public sealed record CheckoutRequest(int? CustomerId, PaymentMethod PaymentMethod, IReadOnlyList<CheckoutItem> Items, Guid RequestId);
public sealed record GuestOrderRequest(string GuestName, PaymentMethod PaymentMethod, IReadOnlyList<CheckoutItem> Items, Guid RequestId);
public sealed record TransactionItemDto(int ProductId, string ProductName, decimal UnitPrice, int Quantity, decimal Subtotal);
public sealed record GuestOrderDto(long Id, string Number, string GuestName, DateTimeOffset CreatedAt,
    PaymentMethod PaymentMethod, decimal Total, IReadOnlyList<TransactionItemDto> Items);
public sealed record TransactionDto(long Id, string Number, int CashierId, string CashierName, int? CustomerId,
    string? CustomerName, DateTimeOffset CreatedAt, PaymentMethod PaymentMethod, decimal Total,
    IReadOnlyList<TransactionItemDto> Items);
public sealed record TransactionSummaryDto(long Id, string Number, DateTimeOffset CreatedAt, string CashierName,
    string? CustomerName, PaymentMethod PaymentMethod, decimal Total);
public sealed record TransactionPageDto(IReadOnlyList<TransactionSummaryDto> Items, int Page, int PageSize, bool HasMore);
public sealed record TopProductDto(int ProductId, string ProductName, int UnitsSold);
public sealed record DailySalesDto(DateOnly DateJakarta, decimal Revenue, int TransactionCount, IReadOnlyList<TopProductDto> TopProducts);

public interface IAuthService
{
    UserDto? CurrentUser { get; }
    Task<ServiceResult<UserDto>> LoginAsync(string username, string password, CancellationToken cancellationToken = default);
    Task<ServiceResult<bool>> LogoutAsync(CancellationToken cancellationToken = default);
}
public interface IProductService
{
    Task<ServiceResult<IReadOnlyList<ProductDto>>> GetCatalogAsync(CancellationToken cancellationToken = default);
    Task<ServiceResult<IReadOnlyList<CategoryDto>>> GetCategoriesAsync(CancellationToken cancellationToken = default);
    Task<ServiceResult<IReadOnlyList<ProductDto>>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<ServiceResult<ProductDto>> CreateAsync(ProductInput input, CancellationToken cancellationToken = default);
    Task<ServiceResult<ProductDto>> UpdateAsync(int id, ProductInput input, CancellationToken cancellationToken = default);
    Task<ServiceResult<bool>> DeactivateAsync(int id, CancellationToken cancellationToken = default);
}
public interface ITransactionService
{
    Task<ServiceResult<IReadOnlyList<CustomerOptionDto>>> GetCustomerOptionsAsync(CancellationToken cancellationToken = default);
    Task<ServiceResult<TransactionDto>> CheckoutAsync(CheckoutRequest request, CancellationToken cancellationToken = default);
    Task<ServiceResult<TransactionDto>> GetByRequestIdAsync(Guid requestId, CancellationToken cancellationToken = default);
    Task<ServiceResult<IReadOnlyList<TransactionSummaryDto>>> GetTransactionsAsync(CancellationToken cancellationToken = default);
    Task<ServiceResult<TransactionPageDto>> GetTransactionsPageAsync(int page, int pageSize = 50,
        CancellationToken cancellationToken = default);
    Task<ServiceResult<TransactionDto>> GetDetailAsync(long id, CancellationToken cancellationToken = default);
}
public interface IDashboardService
{
    Task<ServiceResult<DailySalesDto>> GetTodayAsync(CancellationToken cancellationToken = default);
}
public interface IKioskService
{
    Task<ServiceResult<IReadOnlyList<ProductDto>>> GetCatalogAsync(CancellationToken cancellationToken = default);
    Task<ServiceResult<GuestOrderDto>> PlaceOrderAsync(GuestOrderRequest request, CancellationToken cancellationToken = default);
    Task<ServiceResult<IReadOnlyList<GuestOrderDto>>> GetPendingOrdersAsync(CancellationToken cancellationToken = default);
    Task<ServiceResult<TransactionDto>> CompleteOrderAsync(long id, CancellationToken cancellationToken = default);
    Task<ServiceResult<bool>> RejectOrderAsync(long id, CancellationToken cancellationToken = default);
}

public interface IPosAppServices : IDisposable
{
    IAuthService Auth { get; }
    IProductService Products { get; }
    ITransactionService Transactions { get; }
    IDashboardService Dashboard { get; }
    IKioskService Kiosk { get; }
    Task<ServiceResult<bool>> CheckHealthAsync(CancellationToken cancellationToken = default);
}
