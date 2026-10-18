using System.Data;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using PosBackend;

static void Check(bool condition, string message)
{
    if (!condition) throw new Exception("GAGAL: " + message);
    Console.WriteLine("OK: " + message);
}
static T Value<T>(ServiceResult<T> result, string name) where T : notnull
{
    Check(result.Success && result.Value is not null, name + (result.Success ? "" : $" ({result.Error}: {result.Message})"));
    return result.Value!;
}
static async Task<int> ScalarInt(SqlConnection connection, string sql)
{
    await using var command = new SqlCommand(sql, connection);
    return (int)(await command.ExecuteScalarAsync())!;
}
static async Task<int> InsertUser(SqlConnection connection, string username, string role)
{
    await using var command = new SqlCommand(
        "INSERT dbo.Users (Username,PasswordHash,FullName,Role,IsActive) OUTPUT INSERTED.Id VALUES (@u,@h,@n,@r,1)", connection);
    command.Parameters.Add("@u", SqlDbType.NVarChar, 80).Value = username;
    command.Parameters.Add("@h", SqlDbType.NVarChar, 250).Value = PasswordHash.Create("Test123!");
    command.Parameters.Add("@n", SqlDbType.NVarChar, 160).Value = username;
    command.Parameters.Add("@r", SqlDbType.VarChar, 16).Value = role;
    return (int)(await command.ExecuteScalarAsync())!;
}

try
{
    var hash = PasswordHash.Create("correct horse battery staple");
    Check(PasswordHash.Verify("correct horse battery staple", hash), "hash menerima password benar");
    Check(!PasswordHash.Verify("wrong", hash) && !PasswordHash.Verify("x", "invalid"), "hash menolak password salah");
    Check(hash != PasswordHash.Create("correct horse battery staple"), "salt acak menghasilkan hash berbeda");
    Check(!PasswordHash.Verify("x", "not-base64"), "hash rusak ditolak tanpa crash");
    try
    {
        _ = new PosServices("Server=localhost;Database=PosDesktop;Integrated Security=true;Encrypt=true;TrustServerCertificate=true");
        throw new Exception("GAGAL: sertifikat tidak tervalidasi diterima pada mode production");
    }
    catch (ArgumentException) { Console.WriteLine("OK: mode production menolak sertifikat tidak tervalidasi"); }
    try
    {
        _ = new PosServices("Server=localhost;Database=PosDesktop;Integrated Security=true;Encrypt=false");
        throw new Exception("GAGAL: koneksi tanpa enkripsi diterima");
    }
    catch (ArgumentException) { Console.WriteLine("OK: koneksi tanpa enkripsi ditolak"); }
    var withoutLogin = PosServices.CreateForDevelopment(
        "Server=localhost;Database=PosDesktop;Integrated Security=true;Encrypt=true;TrustServerCertificate=true");
    Check((await withoutLogin.Products.GetCatalogAsync()).Error == ErrorCode.Unauthorized, "katalog memerlukan login");
    Check((await withoutLogin.Transactions.GetTransactionsAsync()).Error == ErrorCode.Unauthorized, "riwayat memerlukan login");
    Check((await withoutLogin.Dashboard.GetTodayAsync()).Error == ErrorCode.Unauthorized, "dashboard memerlukan login");

    var configured = Environment.GetEnvironmentVariable("POS_TEST_CONNECTION_STRING");
    if (string.IsNullOrWhiteSpace(configured))
    {
        Console.WriteLine("SKIP: integrasi SQL Server (POS_TEST_CONNECTION_STRING belum diatur).");
        return 0;
    }
    var builder = new SqlConnectionStringBuilder(configured);
    Check(builder.InitialCatalog.Equals("PosDesktopTest", StringComparison.OrdinalIgnoreCase), "database uji bernama PosDesktopTest");
    builder.InitialCatalog = "master";
    await using (var master = new SqlConnection(builder.ConnectionString))
    {
        await master.OpenAsync();
        foreach (var file in new[] { "schema.sql", "server.sql", "permissions.sql" })
        {
            var script = (await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, file)))
                .Replace("PosDesktop", "PosDesktopTest", StringComparison.Ordinal);
            foreach (var batch in Regex.Split(script, @"^\s*GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(batch)) continue;
                await using var command = new SqlCommand(batch, master);
                await command.ExecuteNonQueryAsync();
            }
        }
    }
    builder.InitialCatalog = "PosDesktopTest";
    await using var db = new SqlConnection(builder.ConnectionString);
    await db.OpenAsync();
    var probeName = "pos_probe_" + Guid.NewGuid().ToString("N")[..12];
    await using (var addProbe = new SqlCommand(
        $"CREATE USER [{probeName}] WITHOUT LOGIN; ALTER ROLE pos_api_role ADD MEMBER [{probeName}]", db))
        await addProbe.ExecuteNonQueryAsync();
    try
    {
        await using var probeConnection = new SqlConnection(builder.ConnectionString);
        await probeConnection.OpenAsync();
        await using var checkRole = new SqlCommand(
            $"EXECUTE AS USER = '{probeName}'; " +
            "SELECT HAS_PERMS_BY_NAME('dbo.Users','OBJECT','SELECT'), " +
            "HAS_PERMS_BY_NAME('dbo.Users','OBJECT','INSERT'), " +
            "HAS_PERMS_BY_NAME('dbo.Users','OBJECT','DELETE'), " +
            "HAS_PERMS_BY_NAME('dbo.Products','OBJECT','UPDATE'), " +
            "HAS_PERMS_BY_NAME('dbo.AuthSessions','OBJECT','INSERT'); REVERT", probeConnection);
        await using var permissionReader = await checkRole.ExecuteReaderAsync();
        var valid = await permissionReader.ReadAsync() && permissionReader.GetInt32(0) == 1
            && permissionReader.GetInt32(1) == 0 && permissionReader.GetInt32(2) == 0
            && permissionReader.GetInt32(3) == 1 && permissionReader.GetInt32(4) == 1;
        while (await permissionReader.NextResultAsync()) { }
        Check(valid, "role SQL runtime hanya memiliki izin yang diperlukan");
    }
    finally
    {
        await using var removeProbe = new SqlCommand(
            $"ALTER ROLE pos_api_role DROP MEMBER [{probeName}]; DROP USER [{probeName}]", db);
        await removeProbe.ExecuteNonQueryAsync();
    }
    var suffix = Guid.NewGuid().ToString("N")[..12];
    var adminName = "admin_" + suffix;
    var cashierName = "cashier_" + suffix;
    var oneName = "customer1_" + suffix;
    var twoName = "customer2_" + suffix;
    var lockedName = "locked_" + suffix;
    var legacyName = "legacy_" + suffix;
    var roleChangedName = "rolechange_" + suffix;
    await InsertUser(db, adminName, "Admin");
    await InsertUser(db, cashierName, "Kasir");
    var customerOne = await InsertUser(db, oneName, "Pelanggan");
    await InsertUser(db, twoName, "Pelanggan");
    await InsertUser(db, lockedName, "Pelanggan");
    await InsertUser(db, roleChangedName, "Kasir");
    var legacyId = await InsertUser(db, legacyName, "Pelanggan");
    var legacySalt = RandomNumberGenerator.GetBytes(16);
    var legacyDerived = Rfc2898DeriveBytes.Pbkdf2("Test123!", legacySalt, 100_000,
        HashAlgorithmName.SHA256, 32);
    var legacyHash = $"PBKDF2-SHA256$100000${Convert.ToBase64String(legacySalt)}${Convert.ToBase64String(legacyDerived)}";
    await using (var legacyUpdate = new SqlCommand("UPDATE dbo.Users SET PasswordHash=@hash WHERE Id=@id", db))
    {
        legacyUpdate.Parameters.Add("@hash", SqlDbType.NVarChar, 250).Value = legacyHash;
        legacyUpdate.Parameters.Add("@id", SqlDbType.Int).Value = legacyId;
        await legacyUpdate.ExecuteNonQueryAsync();
    }
    int categoryId;
    await using (var category = new SqlCommand("INSERT dbo.Categories (Name) OUTPUT INSERTED.Id VALUES (@name)", db))
    {
        category.Parameters.Add("@name", SqlDbType.NVarChar, 100).Value = "Test_" + suffix;
        categoryId = (int)(await category.ExecuteScalarAsync())!;
    }
    int productId;
    await using (var product = new SqlCommand(
        "INSERT dbo.Products (Name,CategoryId,Price,IsActive) OUTPUT INSERTED.Id VALUES (@name,@category,10000.00,1)", db))
    {
        product.Parameters.Add("@name", SqlDbType.NVarChar, 160).Value = "Test product " + suffix;
        product.Parameters.Add("@category", SqlDbType.Int).Value = categoryId;
        productId = (int)(await product.ExecuteScalarAsync())!;
    }
    var services = PosServices.CreateForDevelopment(builder.ConnectionString);
    Value(await services.Auth.LoginAsync(legacyName, "Test123!"), "login hash lama");
    await using (var migrated = new SqlCommand("SELECT PasswordHash FROM dbo.Users WHERE Id=@id", db))
    {
        migrated.Parameters.Add("@id", SqlDbType.Int).Value = legacyId;
        Check(!((string)(await migrated.ExecuteScalarAsync())!).StartsWith("PBKDF2-SHA256$"),
            "hash lama dimigrasi saat login");
    }
    for (var attempt = 0; attempt < 5; attempt++)
        Check((await services.Auth.LoginAsync(lockedName, "wrong")).Error == ErrorCode.Unauthorized,
            $"login salah ke-{attempt + 1} ditolak");
    Check((await services.Auth.LoginAsync(lockedName, "Test123!")).Error == ErrorCode.Unauthorized,
        "akun terkunci setelah lima kegagalan");
    Check((await services.Auth.LoginAsync(adminName, "wrong")).Error == ErrorCode.Unauthorized, "login salah ditolak");
    Value(await services.Auth.LoginAsync(adminName, "Test123!"), "login admin benar");
    Check((await services.Transactions.CheckoutAsync(new(null, PaymentMethod.Tunai,
        [new CheckoutItem(productId, 1)], Guid.NewGuid()))).Error == ErrorCode.Forbidden, "admin tidak dapat checkout");
    var before = Value(await services.Dashboard.GetTodayAsync(), "dashboard awal");
    var unitsForTest = Math.Max(3, before.TopProducts.Count == 0 ? 3 : checked(before.TopProducts.Max(x => x.UnitsSold) + 1));
    Value(await services.Auth.LoginAsync(cashierName, "Test123!"), "login kasir benar");
    Check((await services.Products.CreateAsync(new("Forbidden", categoryId, 100m))).Error == ErrorCode.Forbidden,
        "kasir tidak dapat mengelola produk");
    Check((await services.Transactions.CheckoutAsync(new(999999999, PaymentMethod.Qris,
        [new CheckoutItem(productId, 1)], Guid.NewGuid()))).Error == ErrorCode.Validation, "pelanggan tidak valid ditolak");
    var purchaseRequestId = Guid.NewGuid();
    var purchase = Value(await services.Transactions.CheckoutAsync(new(customerOne, PaymentMethod.Tunai,
        [new CheckoutItem(productId, unitsForTest - 1)], purchaseRequestId)), "checkout pelanggan terdaftar");
    Check(purchase.Total == (unitsForTest - 1) * 10000m && purchase.Items[0].UnitPrice == 10000m,
        "total dihitung dari harga database");
    var general = Value(await services.Transactions.CheckoutAsync(new(null, PaymentMethod.Qris,
        [new CheckoutItem(productId, 1)], Guid.NewGuid())), "checkout Umum");
    Check(general.CustomerId is null, "pembelian Umum tanpa pemilik pelanggan");
    Check((await services.Transactions.CheckoutAsync(new(customerOne, PaymentMethod.Tunai,
        [new CheckoutItem(productId, unitsForTest - 1)], purchaseRequestId))).Error == ErrorCode.Conflict,
        "retry dengan RequestId sama tidak membuat transaksi kedua");
    Check(Value(await services.Transactions.GetByRequestIdAsync(purchaseRequestId), "cari checkout setelah retry").Id == purchase.Id,
        "RequestId menemukan transaksi awal");
    var firstPage = Value(await services.Transactions.GetTransactionsPageAsync(1, 1), "halaman transaksi pertama");
    var secondPage = Value(await services.Transactions.GetTransactionsPageAsync(2, 1), "halaman transaksi kedua");
    Check(firstPage.HasMore && firstPage.Items.Count == 1 && secondPage.Items.Count == 1,
        "riwayat transaksi dapat dipaginasi");
    Value(await services.Auth.LoginAsync(twoName, "Test123!"), "login pelanggan kedua");
    var otherList = Value(await services.Transactions.GetTransactionsAsync(), "riwayat pelanggan kedua");
    Check(otherList.All(x => x.Id != purchase.Id && x.Id != general.Id), "pelanggan lain tidak melihat transaksi");
    Check((await services.Transactions.GetDetailAsync(purchase.Id)).Error == ErrorCode.NotFound,
        "pelanggan lain tidak membaca detail transaksi");
    Value(await services.Auth.LoginAsync(oneName, "Test123!"), "login pelanggan pertama");
    var ownList = Value(await services.Transactions.GetTransactionsAsync(), "riwayat pelanggan pertama");
    Check(ownList.Any(x => x.Id == purchase.Id) && ownList.All(x => x.Id != general.Id),
        "pelanggan hanya melihat pembeliannya sendiri");
    Value(await services.Auth.LoginAsync(adminName, "Test123!"), "login admin ulang");
    Value(await services.Products.UpdateAsync(productId, new("Updated product", categoryId, 15000m)), "ubah harga produk");
    var old = Value(await services.Transactions.GetDetailAsync(purchase.Id), "detail transaksi lama");
    Check(old.Items[0].UnitPrice == 10000m && old.Total == (unitsForTest - 1) * 10000m
        && old.Items[0].ProductName.StartsWith("Test product"),
        "snapshot harga dan nama tetap tersimpan");
    var after = Value(await services.Dashboard.GetTodayAsync(), "dashboard setelah transaksi");
    Check(after.Revenue == before.Revenue + unitsForTest * 10000m && after.TransactionCount == before.TransactionCount + 2
        && after.TopProducts.Any(x => x.ProductId == productId && x.UnitsSold == unitsForTest),
        "omzet, jumlah transaksi, dan top produk sesuai transaksi");

    var triggerName = "TR_TestRollback_" + suffix;
    await using (var trigger = new SqlCommand($"CREATE TRIGGER dbo.{triggerName} ON dbo.TransactionItems AFTER INSERT AS " +
        $"BEGIN IF EXISTS (SELECT 1 FROM inserted WHERE ProductId={productId}) THROW 51000, 'Test rollback', 1; END", db))
        await trigger.ExecuteNonQueryAsync();
    try
    {
        var countBefore = await ScalarInt(db, "SELECT COUNT(*) FROM dbo.Transactions");
        Value(await services.Auth.LoginAsync(cashierName, "Test123!"), "login kasir untuk uji rollback");
        var failed = await services.Transactions.CheckoutAsync(new(customerOne, PaymentMethod.Tunai,
            [new CheckoutItem(productId, 1)], Guid.NewGuid()));
        Check(!failed.Success && failed.Error == ErrorCode.DatabaseError, "kegagalan item dikembalikan sebagai error");
        var countAfter = await ScalarInt(db, "SELECT COUNT(*) FROM dbo.Transactions");
        Check(countBefore == countAfter, "rollback tidak menyisakan header transaksi parsial");
    }
    finally
    {
        await using var drop = new SqlCommand($"DROP TRIGGER dbo.{triggerName}", db);
        await drop.ExecuteNonQueryAsync();
    }
    await using (var disable = new SqlCommand("UPDATE dbo.Users SET IsActive=0 WHERE Username=@name", db))
    {
        disable.Parameters.Add("@name", SqlDbType.NVarChar, 80).Value = cashierName;
        await disable.ExecuteNonQueryAsync();
    }
    Check((await services.Products.GetCatalogAsync()).Error == ErrorCode.Unauthorized,
        "akun yang dinonaktifkan kehilangan akses tanpa login ulang");
    Value(await services.Auth.LoginAsync(roleChangedName, "Test123!"), "login kasir untuk uji perubahan role");
    await using (var changeRole = new SqlCommand("UPDATE dbo.Users SET Role='Pelanggan' WHERE Username=@name", db))
    {
        changeRole.Parameters.Add("@name", SqlDbType.NVarChar, 80).Value = roleChangedName;
        await changeRole.ExecuteNonQueryAsync();
    }
    Check((await services.Transactions.GetCustomerOptionsAsync()).Error == ErrorCode.Forbidden,
        "perubahan role berlaku tanpa login ulang");
    Console.WriteLine("Semua uji integrasi selesai.");
    return 0;
}
catch (Exception exception)
{
    Console.Error.WriteLine(exception.Message);
    return 1;
}
