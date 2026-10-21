using System.Net;
using System.Text;
using System.Text.Json;
using PosBackend;
using PosClient;

static void Check(bool condition, string name)
{
    if (!condition) throw new Exception("GAGAL: " + name);
    Console.WriteLine("OK: " + name);
}

try
{
    using var insecure = new HttpClient { BaseAddress = new Uri("http://127.0.0.1:5077/") };
    try { _ = new PosRemoteServices(insecure); throw new Exception("HTTP diterima"); }
    catch (ArgumentException) { Check(true, "client normal menolak HTTP"); }

    var token = Convert.ToBase64String(new byte[32]).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    var handler = new FakeHandler(token);
    using var http = new HttpClient(handler) { BaseAddress = new Uri("https://pos.example.test/") };
    using var services = new PosRemoteServices(http);
    var health = await services.CheckHealthAsync();
    Check(health.Success && handler.HealthSeen, "health API dapat diperiksa tanpa login");
    var login = await services.Auth.LoginAsync("kasir", "Password123!");
    Check(login.Success && login.Value?.Role == UserRole.Kasir && services.Auth.CurrentUser?.Id == 5,
        "login menyimpan pengguna dan token sesi");
    var catalog = await services.Products.GetCatalogAsync();
    Check(catalog.Success && catalog.Value?.Count == 1 && catalog.Value[0].Price == 12500m && handler.TokenSeen,
        "catalog membaca DTO dan mengirim bearer token");
    var kioskCatalog = await services.Kiosk.GetCatalogAsync();
    Check(kioskCatalog.Success && handler.PublicKioskCatalogSeen,
        "katalog kiosk dapat dibaca tanpa token login");
    var guestOrder = await services.Kiosk.PlaceOrderAsync(new("Budi", PaymentMethod.Qris,
        [new CheckoutItem(1, 2)], Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc")));
    Check(guestOrder.Success && guestOrder.Value?.GuestName == "Budi" && handler.GuestOrderSeen,
        "pesanan tamu dikirim tanpa bearer dan nama pemesan diterima");
    var rejection = await services.Kiosk.RejectOrderAsync(8);
    Check(rejection.Success && rejection.Value && handler.RejectSeen,
        "penolakan pesanan dikirim dengan bearer kasir");
    var denied = await services.Dashboard.GetTodayAsync();
    Check(denied.Error == ErrorCode.Forbidden, "error role server diteruskan");
    var logout = await services.Auth.LogoutAsync();
    Check(logout.Success && handler.LogoutSeen && services.Auth.CurrentUser is null,
        "logout mencabut token dan menghapus sesi lokal");
    var after = await services.Products.GetCatalogAsync();
    Check(after.Error == ErrorCode.Unauthorized, "client menolak permintaan tanpa sesi");
    using var brokenHttp = new HttpClient(new BrokenHandler()) { BaseAddress = new Uri("https://pos.example.test/") };
    using var broken = new PosRemoteServices(brokenHttp);
    Check((await broken.Auth.LoginAsync("x", "y")).Error == ErrorCode.ServiceUnavailable,
        "koneksi gagal menghasilkan error yang dapat ditampilkan");
    Check((await broken.CheckHealthAsync()).Error == ErrorCode.ServiceUnavailable,
        "health API menampilkan koneksi gagal tanpa crash");
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine(ex.Message);
    return 1;
}

sealed class FakeHandler(string token) : HttpMessageHandler
{
    public bool TokenSeen { get; private set; }
    public bool LogoutSeen { get; private set; }
    public bool HealthSeen { get; private set; }
    public bool PublicKioskCatalogSeen { get; private set; }
    public bool GuestOrderSeen { get; private set; }
    public bool RejectSeen { get; private set; }
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var path = request.RequestUri!.AbsolutePath;
        var bearer = request.Headers.Authorization?.Parameter;
        if (path == "/health")
        {
            HealthSeen = true;
            return Reply(HttpStatusCode.OK, "{\"status\":\"ok\"}");
        }
        if (path == "/api/auth/login")
            return Reply(HttpStatusCode.OK, JsonSerializer.Serialize(new { success = true,
                value = new { user = new { id = 5, username = "kasir", fullName = "Kasir", role = "Kasir" },
                    token, expiresAt = "2030-01-01T00:00:00+00:00" }, error = "None", message = "OK" }));
        if (path == "/api/products/catalog" && bearer == token)
        {
            TokenSeen = true;
            return Reply(HttpStatusCode.OK, """{"success":true,"value":[{"id":1,"name":"Kopi","categoryId":1,"categoryName":"Minuman","price":12500,"isActive":true}],"error":"None","message":"OK"}""");
        }
        if (path == "/api/kiosk/catalog" && bearer is null)
        {
            PublicKioskCatalogSeen = true;
            return Reply(HttpStatusCode.OK, """{"success":true,"value":[{"id":1,"name":"Kopi","categoryId":1,"categoryName":"Minuman","price":12500,"isActive":true}],"error":"None","message":"OK"}""");
        }
        if (path == "/api/kiosk/orders" && bearer is null)
        {
            using var body = JsonDocument.Parse(request.Content!.ReadAsStringAsync(cancellationToken).GetAwaiter().GetResult());
            GuestOrderSeen = body.RootElement.GetProperty("guestName").GetString() == "Budi";
            return Reply(HttpStatusCode.OK, """{"success":true,"value":{"id":8,"number":"ORD-TEST","guestName":"Budi","createdAt":"2026-01-01T00:00:00+00:00","paymentMethod":"Qris","total":25000,"items":[{"productId":1,"productName":"Kopi","unitPrice":12500,"quantity":2,"subtotal":25000}]},"error":"None","message":"OK"}""");
        }
        if (path == "/api/kiosk/orders/8/reject" && bearer == token && request.Method == HttpMethod.Post)
        {
            RejectSeen = true;
            return Reply(HttpStatusCode.OK, """{"success":true,"value":true,"error":"None","message":"OK"}""");
        }
        if (path == "/api/dashboard/today" && bearer == token)
            return Reply(HttpStatusCode.Forbidden, """{"success":false,"value":null,"error":"Forbidden","message":"Akses ditolak."}""");
        if (path == "/api/auth/logout" && bearer == token)
        {
            LogoutSeen = true;
            return Reply(HttpStatusCode.OK, """{"success":true,"value":true,"error":"None","message":"OK"}""");
        }
        throw new Exception("Permintaan HTTP tidak diduga: " + path);
    }
    private static Task<HttpResponseMessage> Reply(HttpStatusCode status, string json) =>
        Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") });
}

sealed class BrokenHandler : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        throw new HttpRequestException("Jaringan putus");
}
