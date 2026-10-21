using System.Globalization;
using PosDesktop.ViewModels;
using PosDemo;

static void Check(bool condition, string name)
{
    if (!condition) throw new Exception("GAGAL: " + name);
    Console.WriteLine("OK: " + name);
}

var indonesia = CultureInfo.GetCultureInfo("id-ID");
var english = CultureInfo.GetCultureInfo("en-US");

foreach (var culture in new[] { indonesia, english })
{
    foreach (var amount in new[] { 25000m, 25000.5m, 25000.75m })
    {
        var text = ProductPriceText.Format(amount, culture);
        Check(ProductPriceText.TryParse(text, culture, out var parsed) && parsed == amount,
            $"harga {amount} tetap sama sesudah ditampilkan dan dibaca ({culture.Name})");
    }
}

Check(ProductPriceText.Format(25000m, indonesia) == "25000", "harga bulat tidak memakai pemisah ambigu");
Check(ProductPriceText.Format(25000.5m, indonesia) == "25000,5", "pecahan memakai koma Indonesia");
Check(ProductPriceText.TryParse("25000.00", indonesia, out var legacy) && legacy == 25000m,
    "harga format lama tidak berubah menjadi seratus kali lipat");
Check(!ProductPriceText.TryParse("25.000,00", indonesia, out _),
    "pemisah ribuan ditolak agar harga tidak salah tafsir");

static async Task WaitUntilAsync(Func<bool> predicate)
{
    var deadline = DateTime.UtcNow.AddSeconds(5);
    while (!predicate())
    {
        if (DateTime.UtcNow >= deadline) throw new Exception("GAGAL: operasi editor produk tidak selesai");
        await Task.Delay(10);
    }
}

var originalCulture = CultureInfo.CurrentCulture;
var folder = Path.Combine(Path.GetTempPath(), "mikugomi-editor-test-" + Guid.NewGuid().ToString("N"));
try
{
    CultureInfo.CurrentCulture = indonesia;
    using var demo = new PosDemoServices(Path.Combine(folder, "demo.sqlite3"));
    var editor = new MainViewModel(demo, "SQLite lokal", offlineDemo: true)
    {
        Username = "admin",
        Password = "admin"
    };
    await editor.InitializeAsync();
    editor.LoginCommand.Execute(null);
    await WaitUntilAsync(() => editor.IsAdmin && !editor.IsBusy);
    editor.NavigateCommand.Execute("Products");
    await WaitUntilAsync(() => editor.IsProductsPage && !editor.IsBusy && editor.Products.Count > 0);
    var product = editor.Products[0];
    editor.SelectedProduct = product;
    Check(ProductPriceText.TryParse(editor.ProductPriceInput, indonesia, out var shownPrice) &&
        shownPrice == product.Price, "panel edit menampilkan harga yang benar");
    editor.SaveProductCommand.Execute(null);
    await WaitUntilAsync(() => editor.SuccessMessage == "Tidak ada perubahan pada produk.");
    Check(editor.EditingProductId == product.Id, "simpan tanpa perubahan tidak menutup editor");
    var afterSave = await demo.Products.GetAllAsync();
    Check(afterSave.Success && afterSave.Value?.First(x => x.Id == product.Id).Price == product.Price,
        "simpan tanpa perubahan menjaga harga di database");
    var updatedPrice = product.Price + 500m;
    editor.ProductPriceInput = ProductPriceText.Format(updatedPrice, indonesia);
    editor.SaveProductCommand.Execute(null);
    await WaitUntilAsync(() => editor.SuccessMessage == "Produk berhasil diperbarui." && !editor.IsBusy);
    var afterEdit = await demo.Products.GetAllAsync();
    Check(afterEdit.Success && afterEdit.Value?.First(x => x.Id == product.Id).Price == updatedPrice,
        "perubahan harga yang disengaja tersimpan sesuai input");
}
finally
{
    CultureInfo.CurrentCulture = originalCulture;
    if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
}

Console.WriteLine("Semua uji harga editor produk selesai.");
