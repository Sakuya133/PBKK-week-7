using System.Net;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Data.SqlClient;
using PosBackend;
using PosServer;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = 65_536);
var connectionString = Environment.GetEnvironmentVariable("POS_CONNECTION_STRING");
if (string.IsNullOrWhiteSpace(connectionString)) throw new InvalidOperationException("POS_CONNECTION_STRING wajib diatur pada server.");
var allowHttp = builder.Environment.IsDevelopment() &&
    string.Equals(Environment.GetEnvironmentVariable("POS_API_ALLOW_HTTP_DEV"), "true", StringComparison.OrdinalIgnoreCase);
_ = allowHttp ? PosServices.CreateForDevelopment(connectionString) : new PosServices(connectionString);
builder.Services.AddSingleton(new SessionStore(connectionString));
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = async (context, _) =>
        await ApiReply.Fail(context.HttpContext, ErrorCode.RateLimited, "Terlalu banyak percobaan. Coba lagi sebentar.");
    options.AddPolicy("login", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new FixedWindowRateLimiterOptions
        { PermitLimit = 10, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
});
var app = builder.Build();
app.UseExceptionHandler(handler => handler.Run(async context =>
    await ApiReply.Fail(context, ErrorCode.InternalError, "Terjadi kesalahan internal. Hubungi pengelola aplikasi.")));
app.UseStatusCodePages(async statusContext =>
{
    var context = statusContext.HttpContext;
    var code = context.Response.StatusCode switch
    {
        404 => ErrorCode.NotFound,
        429 => ErrorCode.RateLimited,
        _ => ErrorCode.Validation
    };
    await ApiReply.Fail(context, code, "Permintaan API tidak valid atau endpoint tidak ditemukan.");
});
app.UseRouting();
app.UseRateLimiter();
app.Use(async (context, next) =>
{
    context.Response.Headers.CacheControl = "no-store";
    if (!context.Request.IsHttps && !(allowHttp && IPAddress.IsLoopback(context.Connection.RemoteIpAddress ?? IPAddress.None)))
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        await context.Response.WriteAsJsonAsync(ServiceResult<bool>.Fail(ErrorCode.Validation, "HTTPS wajib digunakan."));
        return;
    }
    if (context.Request.ContentLength is > 65_536)
    {
        await ApiReply.Fail(context, ErrorCode.Validation, "Permintaan terlalu besar.");
        return;
    }
    if (context.Request.Path.StartsWithSegments("/api/auth/login") || context.Request.Path == "/health")
    {
        await next();
        return;
    }
    var bearer = context.Request.Headers.Authorization.ToString();
    if (!bearer.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
    {
        await ApiReply.Fail(context, ErrorCode.Unauthorized, "Silakan login terlebih dahulu.");
        return;
    }
    try
    {
        var user = await context.RequestServices.GetRequiredService<SessionStore>()
            .ResolveAsync(bearer[7..].Trim(), context.RequestAborted);
        if (user is null)
        {
            await ApiReply.Fail(context, ErrorCode.Unauthorized, "Sesi berakhir. Silakan login kembali.");
            return;
        }
        context.Items["user"] = user;
        await next();
    }
    catch (SqlException)
    {
        if (!context.Response.HasStarted)
            await ApiReply.Fail(context, ErrorCode.DatabaseUnavailable, "Database server tidak tersedia.");
    }
});

app.MapGet("/health", () => Results.Ok(new { Status = "ok" }));
app.MapPost("/api/auth/login", async (LoginInput input, SessionStore sessions, CancellationToken ct) =>
{
    var services = NewServices();
    var login = await services.Auth.LoginAsync(input.Username ?? "", input.Password ?? "", ct);
    if (!login.Success) return ApiReply.From(login);
    try
    {
        var session = await sessions.IssueAsync(login.Value!, ct);
        return ApiReply.From(ServiceResult<LoginSessionDto>.Ok(session));
    }
    catch (SqlException) { return ApiReply.From(ServiceResult<LoginSessionDto>.Fail(ErrorCode.DatabaseUnavailable, "Database server tidak tersedia.")); }
}).RequireRateLimiting("login");
app.MapGet("/api/auth/me", (HttpContext ctx) => ApiReply.From(ServiceResult<UserDto>.Ok(GetUser(ctx))));
app.MapPost("/api/auth/logout", async (HttpContext ctx, SessionStore sessions, CancellationToken ct) =>
{
    await sessions.RevokeAsync(ctx.Request.Headers.Authorization.ToString()[7..].Trim(), ct);
    return ApiReply.From(ServiceResult<bool>.Ok(true));
});

app.MapGet("/api/products/catalog", (HttpContext ctx, CancellationToken ct) => Call(ctx, s => s.Products.GetCatalogAsync(ct)));
app.MapGet("/api/products/categories", (HttpContext ctx, CancellationToken ct) => Call(ctx, s => s.Products.GetCategoriesAsync(ct)));
app.MapGet("/api/products", (HttpContext ctx, CancellationToken ct) => Call(ctx, s => s.Products.GetAllAsync(ct)));
app.MapPost("/api/products", (HttpContext ctx, ProductInput input, CancellationToken ct) => Call(ctx, s => s.Products.CreateAsync(input, ct)));
app.MapPut("/api/products/{id:int}", (HttpContext ctx, int id, ProductInput input, CancellationToken ct) => Call(ctx, s => s.Products.UpdateAsync(id, input, ct)));
app.MapDelete("/api/products/{id:int}", (HttpContext ctx, int id, CancellationToken ct) => Call(ctx, s => s.Products.DeactivateAsync(id, ct)));

app.MapGet("/api/transactions/customers", (HttpContext ctx, CancellationToken ct) => Call(ctx, s => s.Transactions.GetCustomerOptionsAsync(ct)));
app.MapPost("/api/transactions/checkout", (HttpContext ctx, CheckoutRequest input, CancellationToken ct) => Call(ctx, s => s.Transactions.CheckoutAsync(input, ct)));
app.MapGet("/api/transactions/request/{requestId:guid}", (HttpContext ctx, Guid requestId, CancellationToken ct) => Call(ctx, s => s.Transactions.GetByRequestIdAsync(requestId, ct)));
app.MapGet("/api/transactions", (HttpContext ctx, int? page, int? pageSize, CancellationToken ct) => Call(ctx, s => s.Transactions.GetTransactionsPageAsync(page ?? 1, pageSize ?? 50, ct)));
app.MapGet("/api/transactions/{id:long}", (HttpContext ctx, long id, CancellationToken ct) => Call(ctx, s => s.Transactions.GetDetailAsync(id, ct)));
app.MapGet("/api/dashboard/today", (HttpContext ctx, CancellationToken ct) => Call(ctx, s => s.Dashboard.GetTodayAsync(ct)));

app.Run();

PosServices NewServices() => allowHttp ? PosServices.CreateForDevelopment(connectionString) : new PosServices(connectionString);
UserDto GetUser(HttpContext context) => (UserDto)context.Items["user"]!;
async Task<IResult> Call<T>(HttpContext context, Func<PosServices, Task<ServiceResult<T>>> action)
{
    var services = PosServices.ForAuthenticatedUser(connectionString, GetUser(context), allowHttp);
    return ApiReply.From(await action(services));
}
