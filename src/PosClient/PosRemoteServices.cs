using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using PosBackend;

namespace PosClient;

/// <summary>Client HTTP untuk ViewModel desktop. Simpan instance ini selama satu sesi aplikasi.</summary>
public sealed class PosRemoteServices : IPosAppServices, IAuthService, IProductService, ITransactionService, IDashboardService, IKioskService
{
    private readonly HttpClient _http;
    private readonly bool _ownsClient;
    private readonly object _gate = new();
    private string? _token;
    private UserDto? _user;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    { Converters = { new JsonStringEnumConverter() } };

    public IAuthService Auth => this;
    public IProductService Products => this;
    public ITransactionService Transactions => this;
    public IDashboardService Dashboard => this;
    public IKioskService Kiosk => this;
    public UserDto? CurrentUser { get { lock (_gate) return _user; } }

    /// <summary>Checks API reachability without requiring a database-backed login.</summary>
    public async Task<ServiceResult<bool>> CheckHealthAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(4));
            using var response = await _http.GetAsync("health", HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            return response.IsSuccessStatusCode
                ? ServiceResult<bool>.Ok(true)
                : ServiceResult<bool>.Fail(ErrorCode.ServiceUnavailable, $"API memberi status HTTP {(int)response.StatusCode}.");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        { return ServiceResult<bool>.Fail(ErrorCode.ServiceUnavailable, "API belum merespons."); }
        catch (HttpRequestException)
        { return ServiceResult<bool>.Fail(ErrorCode.ServiceUnavailable, "API tidak dapat dihubungi."); }
    }

    public PosRemoteServices(Uri apiBaseAddress) : this(new HttpClient { BaseAddress = apiBaseAddress }, true, false) { }
    public static PosRemoteServices CreateForDevelopment(Uri loopbackBaseAddress) =>
        new(new HttpClient { BaseAddress = loopbackBaseAddress }, true, true);
    public PosRemoteServices(HttpClient httpClient) : this(httpClient, false, false) { }

    private PosRemoteServices(HttpClient httpClient, bool ownsClient, bool development)
    {
        var uri = httpClient.BaseAddress ?? throw new ArgumentException("Alamat API wajib diatur.", nameof(httpClient));
        if (uri.Scheme != Uri.UriSchemeHttps &&
            !(development && uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback))
            throw new ArgumentException("API harus menggunakan HTTPS; HTTP hanya untuk development di loopback.", nameof(httpClient));
        _http = httpClient;
        _ownsClient = ownsClient;
    }

    public async Task<ServiceResult<UserDto>> LoginAsync(string username, string password, CancellationToken cancellationToken = default)
    {
        if (CurrentUser is not null)
        {
            var logout = await LogoutAsync(cancellationToken);
            if (!logout.Success) return ServiceResult<UserDto>.Fail(logout.Error, logout.Message);
        }
        lock (_gate) { _token = null; _user = null; }
        var result = await SendAsync<LoginSessionDto>(HttpMethod.Post, "api/auth/login",
            new LoginInput(username, password), false, cancellationToken);
        if (!result.Success || result.Value is null)
            return ServiceResult<UserDto>.Fail(result.Error, result.Message);
        lock (_gate) { _token = result.Value.Token; _user = result.Value.User; }
        return ServiceResult<UserDto>.Ok(result.Value.User);
    }

    public async Task<ServiceResult<bool>> LogoutAsync(CancellationToken cancellationToken = default)
    {
        try { return await SendAsync<bool>(HttpMethod.Post, "api/auth/logout", null, true, cancellationToken); }
        finally { lock (_gate) { _token = null; _user = null; } }
    }

    Task<ServiceResult<IReadOnlyList<ProductDto>>> IProductService.GetCatalogAsync(CancellationToken ct) =>
        SendAsync<IReadOnlyList<ProductDto>>(HttpMethod.Get, "api/products/catalog", null, true, ct);
    Task<ServiceResult<IReadOnlyList<CategoryDto>>> IProductService.GetCategoriesAsync(CancellationToken ct) =>
        SendAsync<IReadOnlyList<CategoryDto>>(HttpMethod.Get, "api/products/categories", null, true, ct);
    Task<ServiceResult<IReadOnlyList<ProductDto>>> IProductService.GetAllAsync(CancellationToken ct) =>
        SendAsync<IReadOnlyList<ProductDto>>(HttpMethod.Get, "api/products", null, true, ct);
    Task<ServiceResult<ProductDto>> IProductService.CreateAsync(ProductInput input, CancellationToken ct) =>
        SendAsync<ProductDto>(HttpMethod.Post, "api/products", input, true, ct);
    Task<ServiceResult<ProductDto>> IProductService.UpdateAsync(int id, ProductInput input, CancellationToken ct) =>
        SendAsync<ProductDto>(HttpMethod.Put, $"api/products/{id}", input, true, ct);
    Task<ServiceResult<bool>> IProductService.DeactivateAsync(int id, CancellationToken ct) =>
        SendAsync<bool>(HttpMethod.Delete, $"api/products/{id}", null, true, ct);

    Task<ServiceResult<IReadOnlyList<CustomerOptionDto>>> ITransactionService.GetCustomerOptionsAsync(CancellationToken ct) =>
        SendAsync<IReadOnlyList<CustomerOptionDto>>(HttpMethod.Get, "api/transactions/customers", null, true, ct);
    Task<ServiceResult<TransactionDto>> ITransactionService.CheckoutAsync(CheckoutRequest request, CancellationToken ct) =>
        SendAsync<TransactionDto>(HttpMethod.Post, "api/transactions/checkout", request, true, ct);
    Task<ServiceResult<TransactionDto>> ITransactionService.GetByRequestIdAsync(Guid requestId, CancellationToken ct) =>
        SendAsync<TransactionDto>(HttpMethod.Get, $"api/transactions/request/{requestId:D}", null, true, ct);
    async Task<ServiceResult<IReadOnlyList<TransactionSummaryDto>>> ITransactionService.GetTransactionsAsync(CancellationToken ct)
    {
        var page = await ((ITransactionService)this).GetTransactionsPageAsync(1, 100, ct);
        return page.Success ? ServiceResult<IReadOnlyList<TransactionSummaryDto>>.Ok(page.Value!.Items)
            : ServiceResult<IReadOnlyList<TransactionSummaryDto>>.Fail(page.Error, page.Message);
    }
    Task<ServiceResult<TransactionPageDto>> ITransactionService.GetTransactionsPageAsync(int page, int pageSize, CancellationToken ct) =>
        SendAsync<TransactionPageDto>(HttpMethod.Get, $"api/transactions?page={page}&pageSize={pageSize}", null, true, ct);
    Task<ServiceResult<TransactionDto>> ITransactionService.GetDetailAsync(long id, CancellationToken ct) =>
        SendAsync<TransactionDto>(HttpMethod.Get, $"api/transactions/{id}", null, true, ct);
    Task<ServiceResult<DailySalesDto>> IDashboardService.GetTodayAsync(CancellationToken ct) =>
        SendAsync<DailySalesDto>(HttpMethod.Get, "api/dashboard/today", null, true, ct);

    Task<ServiceResult<IReadOnlyList<ProductDto>>> IKioskService.GetCatalogAsync(CancellationToken ct) =>
        SendAsync<IReadOnlyList<ProductDto>>(HttpMethod.Get, "api/kiosk/catalog", null, false, ct);
    Task<ServiceResult<GuestOrderDto>> IKioskService.PlaceOrderAsync(GuestOrderRequest request, CancellationToken ct) =>
        SendAsync<GuestOrderDto>(HttpMethod.Post, "api/kiosk/orders", request, false, ct);
    Task<ServiceResult<IReadOnlyList<GuestOrderDto>>> IKioskService.GetPendingOrdersAsync(CancellationToken ct) =>
        SendAsync<IReadOnlyList<GuestOrderDto>>(HttpMethod.Get, "api/kiosk/orders/pending", null, true, ct);
    Task<ServiceResult<TransactionDto>> IKioskService.CompleteOrderAsync(long id, CancellationToken ct) =>
        SendAsync<TransactionDto>(HttpMethod.Post, $"api/kiosk/orders/{id}/complete", null, true, ct);
    Task<ServiceResult<bool>> IKioskService.RejectOrderAsync(long id, CancellationToken ct) =>
        SendAsync<bool>(HttpMethod.Post, $"api/kiosk/orders/{id}/reject", null, true, ct);

    private async Task<ServiceResult<T>> SendAsync<T>(HttpMethod method, string path, object? body, bool authenticated, CancellationToken ct)
    {
        string? token;
        lock (_gate) token = _token;
        if (authenticated && token is null)
            return ServiceResult<T>.Fail(ErrorCode.Unauthorized, "Silakan login terlebih dahulu.");
        try
        {
            using var request = new HttpRequestMessage(method, path);
            if (authenticated) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            if (body is not null) request.Content = JsonContent.Create(body, options: Json);
            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            var result = await response.Content.ReadFromJsonAsync<ServiceResult<T>>(Json, ct);
            if (result is null) throw new JsonException();
            if (result.Error == ErrorCode.Unauthorized && authenticated)
                lock (_gate) { if (_token == token) { _token = null; _user = null; } }
            return result;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        { return ServiceResult<T>.Fail(ErrorCode.ServiceUnavailable, "Server tidak merespons tepat waktu."); }
        catch (HttpRequestException)
        { return ServiceResult<T>.Fail(ErrorCode.ServiceUnavailable, "Server aplikasi tidak dapat dihubungi."); }
        catch (JsonException)
        { return ServiceResult<T>.Fail(ErrorCode.ServiceUnavailable, "Respons server tidak valid."); }
    }

    public void Dispose() { if (_ownsClient) _http.Dispose(); }

    private sealed record LoginInput(string Username, string Password);
    private sealed record LoginSessionDto(UserDto User, string Token, DateTimeOffset ExpiresAt);
}
