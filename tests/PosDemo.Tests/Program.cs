using PosBackend;
using PosDemo;

static void Check(bool condition, string name)
{
    if (!condition) throw new Exception("GAGAL: " + name);
    Console.WriteLine("OK: " + name);
}
static T Value<T>(ServiceResult<T> result, string name)
{
    Check(result.Success && result.Value is not null, name + (result.Success ? "" : " — " + result.Message));
    return result.Value!;
}

var folder = Path.Combine(Path.GetTempPath(), "mikugomi-demo-test-" + Guid.NewGuid().ToString("N"));
var path = Path.Combine(folder, "demo.sqlite3");
try
{
    using var demo = new PosDemoServices(path);
    Check(Value(await demo.CheckHealthAsync(), "database dibuat otomatis"), "SQLite siap");
    Check(File.Exists(path), "file SQLite tersimpan di folder lokal");
    Check((await demo.Products.GetCatalogAsync()).Error == ErrorCode.Unauthorized, "katalog akun memerlukan login");
    Check(Value(await demo.Kiosk.GetCatalogAsync(), "katalog kiosk publik").Count >= 5,
        "katalog demo terisi saat pertama dibuka");
    Check((await demo.Auth.LoginAsync("admin", "salah")).Error == ErrorCode.Unauthorized,
        "password salah ditolak");
    Check(Value(await demo.Auth.LoginAsync("admin", "admin"), "login admin").Role == UserRole.Admin,
        "admin/admin dapat masuk");
    var initial = Value(await demo.Dashboard.GetTodayAsync(), "dashboard demo");
    Check(initial.Revenue > 0 && initial.TransactionCount > 0, "dashboard berisi contoh penjualan");
    var category = Value(await demo.Products.GetCategoriesAsync(), "kategori admin")[0];
    var product = Value(await demo.Products.CreateAsync(new("Produk Uji", category.Id, 13500m)), "tambah produk");
    Check(product.Price == 13500m && product.IsActive, "harga dan status produk baru sesuai");
    product = Value(await demo.Products.UpdateAsync(product.Id, new("Produk Uji Edit", category.Id, 14500m)),
        "ubah produk");
    Check(product.Name == "Produk Uji Edit" && product.Price == 14500m, "perubahan produk tersimpan");
    Check(Value(await demo.Products.DeactivateAsync(product.Id), "nonaktifkan produk"), "produk dinonaktifkan");
    Check(Value(await demo.Products.GetCatalogAsync(), "katalog aktif").All(x => x.Id != product.Id),
        "produk nonaktif tidak tampil di katalog");
    Check((await demo.Kiosk.RejectOrderAsync(1)).Error == ErrorCode.Forbidden, "admin tidak dapat menolak pesanan");
    await demo.Auth.LogoutAsync();

    var catalog = Value(await demo.Kiosk.GetCatalogAsync(), "katalog tamu");
    var menu = catalog[0];
    var request = new GuestOrderRequest("Tamu Uji", PaymentMethod.Qris,
        [new CheckoutItem(menu.Id, 2)], Guid.NewGuid());
    var order = Value(await demo.Kiosk.PlaceOrderAsync(request), "tamu memesan tanpa login");
    Check(Value(await demo.Kiosk.PlaceOrderAsync(request), "retry pesanan").Id == order.Id,
        "pesanan kiosk idempoten");
    Check((await demo.Kiosk.GetPendingOrdersAsync()).Error == ErrorCode.Unauthorized,
        "antrean memerlukan login kasir");
    Check(Value(await demo.Auth.LoginAsync("kasir", "kasir"), "login kasir").Role == UserRole.Kasir,
        "kasir/kasir dapat masuk");
    Check(Value(await demo.Kiosk.GetPendingOrdersAsync(), "antrean kasir").Any(x => x.Id == order.Id),
        "pesanan tamu tampil di antrean");
    Check(Value(await demo.Kiosk.RejectOrderAsync(order.Id), "tolak pesanan"), "kasir dapat menolak");
    Check(!Value(await demo.Kiosk.GetPendingOrdersAsync(), "antrean sesudah tolak").Any(x => x.Id == order.Id),
        "pesanan ditolak hilang dari antrean");
    Check((await demo.Kiosk.CompleteOrderAsync(order.Id)).Error == ErrorCode.Conflict,
        "pesanan ditolak tidak bisa menjadi transaksi");
    var paidOrder = Value(await demo.Kiosk.PlaceOrderAsync(new("Tamu Bayar", PaymentMethod.Tunai,
        [new CheckoutItem(menu.Id, 1)], Guid.NewGuid())), "pesanan untuk dibayar");
    var guestSale = Value(await demo.Kiosk.CompleteOrderAsync(paidOrder.Id), "kasir terima pembayaran");
    Check(guestSale.CustomerName == "Tamu Bayar" && guestSale.Total == menu.Price,
        "transaksi tamu menyimpan nama dan harga");
    Check((await demo.Kiosk.RejectOrderAsync(paidOrder.Id)).Error == ErrorCode.Conflict,
        "pesanan yang sudah dibayar tidak dapat ditolak");
    var customers = Value(await demo.Transactions.GetCustomerOptionsAsync(), "daftar pelanggan");
    var purchaseRequest = Guid.NewGuid();
    var sale = Value(await demo.Transactions.CheckoutAsync(new(customers[0].Id, PaymentMethod.Qris,
        [new CheckoutItem(menu.Id, 3)], purchaseRequest)), "checkout kasir");
    Check(sale.Total == menu.Price * 3, "total dihitung dari harga SQLite");
    Check((await demo.Transactions.CheckoutAsync(new(customers[0].Id, PaymentMethod.Qris,
        [new CheckoutItem(menu.Id, 3)], purchaseRequest))).Error == ErrorCode.Conflict,
        "checkout ulang tidak menggandakan transaksi");
    Check(Value(await demo.Transactions.GetByRequestIdAsync(purchaseRequest), "cari request checkout").Id == sale.Id,
        "transaksi lama ditemukan dari request id");
    Check(Value(await demo.Transactions.GetTransactionsPageAsync(1, 2), "riwayat kasir").Items.Count == 2,
        "riwayat kasir berpaginasi");
    await demo.Auth.LogoutAsync();
    Check(Value(await demo.Auth.LoginAsync("pelanggan", "pelanggan"), "login pelanggan").Role == UserRole.Pelanggan,
        "pelanggan demo dapat masuk");
    var own = Value(await demo.Transactions.GetTransactionsAsync(), "riwayat pelanggan");
    Check(own.Any(x => x.Id == sale.Id) && own.All(x => x.Id != guestSale.Id),
        "pelanggan hanya melihat transaksinya");
    Check((await demo.Dashboard.GetTodayAsync()).Error == ErrorCode.Forbidden,
        "pelanggan tidak dapat membuka dashboard admin");
    await demo.Auth.LogoutAsync();

    using var reopened = new PosDemoServices(path);
    Check(Value(await reopened.Auth.LoginAsync("admin", "admin"), "buka ulang database").Role == UserRole.Admin,
        "akun demo bertahan setelah aplikasi dibuka ulang");
    Check(Value(await reopened.Transactions.GetDetailAsync(sale.Id), "detail setelah buka ulang").Total == sale.Total,
        "transaksi bertahan setelah aplikasi dibuka ulang");
    Check(Value(await reopened.Dashboard.GetTodayAsync(), "dashboard setelah checkout").Revenue ==
        initial.Revenue + guestSale.Total + sale.Total,
        "dashboard menjumlah penjualan asli tanpa pesanan ditolak");
    Console.WriteLine("Semua uji demo offline selesai.");
    return 0;
}
finally
{
    if (Directory.Exists(folder)) Directory.Delete(folder, true);
}
