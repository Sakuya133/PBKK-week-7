using System.Globalization;
using System.Security.Cryptography;
using Microsoft.Data.Sqlite;
using PosBackend;

namespace PosDemo;

/// <summary>Offline, per-user demo. No HTTP listener or SQL Server process is started.</summary>
public sealed partial class PosDemoServices : IPosAppServices, IAuthService, IProductService,
    ITransactionService, IDashboardService, IKioskService
{
    private readonly object _gate = new();
    private readonly string _databasePath;
    private bool _initialized;
    private UserDto? _current;

    public PosDemoServices(string databasePath) => _databasePath = Path.GetFullPath(databasePath);
    public IAuthService Auth => this;
    public IProductService Products => this;
    public ITransactionService Transactions => this;
    public IDashboardService Dashboard => this;
    public IKioskService Kiosk => this;
    public UserDto? CurrentUser { get { lock (_gate) return _current; } }
    public void Dispose() { }
    public Task<ServiceResult<bool>> CheckHealthAsync(CancellationToken ct = default) => Run(_ => true, ct);

    private Task<ServiceResult<T>> Run<T>(Func<SqliteConnection, T> action, CancellationToken ct)
    {
        lock (_gate)
        {
            try
            {
                ct.ThrowIfCancellationRequested();
                using var db = Open();
                return Task.FromResult(ServiceResult<T>.Ok(action(db)));
            }
            catch (DemoFault e) { return Task.FromResult(ServiceResult<T>.Fail(e.Code, e.Message)); }
            catch (SqliteException) { return Task.FromResult(ServiceResult<T>.Fail(ErrorCode.DatabaseError, "Database demo tidak dapat dibaca atau ditulis.")); }
            catch (IOException) { return Task.FromResult(ServiceResult<T>.Fail(ErrorCode.DatabaseUnavailable, "Folder data demo tidak dapat diakses.")); }
            catch (UnauthorizedAccessException) { return Task.FromResult(ServiceResult<T>.Fail(ErrorCode.DatabaseUnavailable, "Folder data demo tidak dapat ditulis.")); }
            catch (OverflowException) { return Task.FromResult(ServiceResult<T>.Fail(ErrorCode.Validation, "Nilai transaksi terlalu besar.")); }
        }
    }

    private SqliteConnection Open()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_databasePath)!);
        var db = new SqliteConnection(new SqliteConnectionStringBuilder
        { DataSource = _databasePath, Mode = SqliteOpenMode.ReadWriteCreate }.ToString());
        try
        {
            db.Open();
            using (var pragma = Command(db, "PRAGMA foreign_keys=ON; PRAGMA busy_timeout=5000;")) pragma.ExecuteNonQuery();
            if (!_initialized) { Initialize(db); _initialized = true; }
            return db;
        }
        catch { db.Dispose(); throw; }
    }

    private static void Initialize(SqliteConnection db)
    {
        using (var schema = Command(db, """
            CREATE TABLE IF NOT EXISTS Users (
              Id INTEGER PRIMARY KEY, Username TEXT NOT NULL UNIQUE, PasswordHash TEXT NOT NULL,
              FullName TEXT NOT NULL, Role TEXT NOT NULL, IsActive INTEGER NOT NULL DEFAULT 1);
            CREATE TABLE IF NOT EXISTS Categories (Id INTEGER PRIMARY KEY, Name TEXT NOT NULL UNIQUE);
            CREATE TABLE IF NOT EXISTS Products (
              Id INTEGER PRIMARY KEY, Name TEXT NOT NULL, CategoryId INTEGER NOT NULL REFERENCES Categories(Id),
              PriceCents INTEGER NOT NULL CHECK (PriceCents>0), IsActive INTEGER NOT NULL DEFAULT 1);
            CREATE TABLE IF NOT EXISTS Transactions (
              Id INTEGER PRIMARY KEY, Number TEXT NOT NULL UNIQUE, RequestId TEXT NOT NULL UNIQUE,
              CashierId INTEGER NOT NULL REFERENCES Users(Id), CustomerId INTEGER NULL REFERENCES Users(Id),
              GuestName TEXT NULL, CreatedAt INTEGER NOT NULL, PaymentMethod TEXT NOT NULL,
              TotalCents INTEGER NOT NULL CHECK (TotalCents>0));
            CREATE TABLE IF NOT EXISTS TransactionItems (
              Id INTEGER PRIMARY KEY, TransactionId INTEGER NOT NULL REFERENCES Transactions(Id),
              ProductId INTEGER NOT NULL REFERENCES Products(Id), ProductName TEXT NOT NULL,
              UnitPriceCents INTEGER NOT NULL, Quantity INTEGER NOT NULL, SubtotalCents INTEGER NOT NULL);
            CREATE TABLE IF NOT EXISTS GuestOrders (
              Id INTEGER PRIMARY KEY, Number TEXT NOT NULL UNIQUE, RequestId TEXT NOT NULL UNIQUE,
              GuestName TEXT NOT NULL, CreatedAt INTEGER NOT NULL, PaymentMethod TEXT NOT NULL,
              TotalCents INTEGER NOT NULL, Status TEXT NOT NULL CHECK (Status IN ('Menunggu','Selesai','Ditolak')),
              TransactionId INTEGER NULL REFERENCES Transactions(Id), RejectedAt INTEGER NULL,
              RejectedBy INTEGER NULL REFERENCES Users(Id));
            CREATE TABLE IF NOT EXISTS GuestOrderItems (
              Id INTEGER PRIMARY KEY, GuestOrderId INTEGER NOT NULL REFERENCES GuestOrders(Id),
              ProductId INTEGER NOT NULL REFERENCES Products(Id), ProductName TEXT NOT NULL,
              UnitPriceCents INTEGER NOT NULL, Quantity INTEGER NOT NULL, SubtotalCents INTEGER NOT NULL);
            CREATE INDEX IF NOT EXISTS IX_Transactions_CreatedAt ON Transactions(CreatedAt);
            CREATE INDEX IF NOT EXISTS IX_GuestOrders_Status_CreatedAt ON GuestOrders(Status,CreatedAt);
            """)) schema.ExecuteNonQuery();
        using var count = Command(db, "SELECT COUNT(*) FROM Users");
        if ((long)count.ExecuteScalar()! > 0) return;

        using var tx = db.BeginTransaction();
        foreach (var (username, password, name, role) in new[]
        {
            ("admin", "admin", "Admin Demo", "Admin"),
            ("kasir", "kasir", "Kasir Demo", "Kasir"),
            ("pelanggan", "pelanggan", "Pelanggan Demo", "Pelanggan"),
            ("pelanggan2", "pelanggan2", "Pelanggan Dua", "Pelanggan")
        })
        {
            using var cmd = Command(db, "INSERT INTO Users(Username,PasswordHash,FullName,Role) VALUES($u,$h,$n,$r)", tx,
                ("$u", username), ("$h", HashPassword(password)), ("$n", name), ("$r", role));
            cmd.ExecuteNonQuery();
        }
        foreach (var category in new[] { "Makanan", "Minuman", "Snack" })
            using (var cmd = Command(db, "INSERT INTO Categories(Name) VALUES($n)", tx, ("$n", category))) cmd.ExecuteNonQuery();
        foreach (var (name, category, price) in new[]
        {
            ("Nasi Goreng", 1, 2500000L), ("Gacoan", 1, 1500000L),
            ("Es Teh", 2, 800000L), ("Kopi Susu", 2, 1800000L), ("Keripik", 3, 1200000L)
        })
            using (var cmd = Command(db, "INSERT INTO Products(Name,CategoryId,PriceCents) VALUES($n,$c,$p)", tx,
                ("$n", name), ("$c", category), ("$p", price))) cmd.ExecuteNonQuery();

        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        using (var sale = Command(db, "INSERT INTO Transactions(Number,RequestId,CashierId,CustomerId,CreatedAt,PaymentMethod,TotalCents) " +
            "VALUES($n,$r,2,3,$t,'Tunai',3300000)", tx, ("$n", NewNumber("TRX")), ("$r", Guid.NewGuid().ToString()), ("$t", now))) sale.ExecuteNonQuery();
        using (var item = Command(db, "INSERT INTO TransactionItems(TransactionId,ProductId,ProductName,UnitPriceCents,Quantity,SubtotalCents) " +
            "VALUES(last_insert_rowid(),1,'Nasi Goreng',2500000,1,2500000)", tx)) item.ExecuteNonQuery();
        using (var item = Command(db, "INSERT INTO TransactionItems(TransactionId,ProductId,ProductName,UnitPriceCents,Quantity,SubtotalCents) " +
            "VALUES((SELECT MAX(Id) FROM Transactions),3,'Es Teh',800000,1,800000)", tx)) item.ExecuteNonQuery();
        using (var order = Command(db, "INSERT INTO GuestOrders(Number,RequestId,GuestName,CreatedAt,PaymentMethod,TotalCents,Status) " +
            "VALUES($n,$r,'Siti Demo',$t,'Qris',1500000,'Menunggu')", tx,
            ("$n", NewNumber("ORD")), ("$r", Guid.NewGuid().ToString()), ("$t", now))) order.ExecuteNonQuery();
        using (var item = Command(db, "INSERT INTO GuestOrderItems(GuestOrderId,ProductId,ProductName,UnitPriceCents,Quantity,SubtotalCents) " +
            "VALUES(last_insert_rowid(),2,'Gacoan',1500000,1,1500000)", tx)) item.ExecuteNonQuery();
        tx.Commit();
    }

    private static string HashPassword(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, 100_000, HashAlgorithmName.SHA256, 32);
        return Convert.ToBase64String(salt.Concat(hash).ToArray());
    }
    private static bool VerifyPassword(string password, string encoded)
    {
        try
        {
            var stored = Convert.FromBase64String(encoded);
            if (stored.Length != 48) return false;
            var actual = Rfc2898DeriveBytes.Pbkdf2(password, stored[..16], 100_000, HashAlgorithmName.SHA256, 32);
            return CryptographicOperations.FixedTimeEquals(actual, stored[16..]);
        }
        catch (FormatException) { return false; }
    }

    private static SqliteCommand Command(SqliteConnection db, string sql, SqliteTransaction? tx = null,
        params (string Name, object? Value)[] args)
    {
        var cmd = db.CreateCommand();
        cmd.CommandText = sql;
        cmd.Transaction = tx;
        foreach (var (name, value) in args) cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
        return cmd;
    }
    private static long LastId(SqliteConnection db, SqliteTransaction? tx = null)
    {
        using var cmd = Command(db, "SELECT last_insert_rowid()", tx);
        return (long)cmd.ExecuteScalar()!;
    }
    private static void Demand(bool valid, ErrorCode code, string message)
    { if (!valid) throw new DemoFault(code, message); }
    private UserDto Require(SqliteConnection db, params UserRole[] roles)
    {
        Demand(_current is not null, ErrorCode.Unauthorized, "Silakan login terlebih dahulu.");
        using var cmd = Command(db, "SELECT Id,Username,FullName,Role FROM Users WHERE Id=$id AND IsActive=1", null,
            ("$id", _current!.Id));
        using var reader = cmd.ExecuteReader();
        Demand(reader.Read(), ErrorCode.Unauthorized, "Sesi berakhir. Silakan login kembali.");
        var user = new UserDto(reader.GetInt32(0), reader.GetString(1), reader.GetString(2),
            Enum.Parse<UserRole>(reader.GetString(3)));
        Demand(roles.Length == 0 || roles.Contains(user.Role), ErrorCode.Forbidden, "Akses tidak diizinkan untuk role ini.");
        _current = user;
        return user;
    }
    private static string NewNumber(string prefix) => prefix + "-" + DateTime.UtcNow.ToString("yyyyMMddHHmmss") + "-" +
        Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
    private static long Cents(decimal amount) => checked((long)(amount * 100));
    private static decimal Money(long cents) => cents / 100m;
    private static DateTimeOffset When(long value) => DateTimeOffset.FromUnixTimeMilliseconds(value);
    private static PaymentMethod Method(string value) => Enum.Parse<PaymentMethod>(value);
    private sealed class DemoFault(ErrorCode code, string message) : Exception(message)
    { public ErrorCode Code { get; } = code; }

    public Task<ServiceResult<UserDto>> LoginAsync(string username, string password, CancellationToken ct = default)
        => Run(db =>
        {
            _current = null;
            Demand(!string.IsNullOrWhiteSpace(username) && !string.IsNullOrEmpty(password),
                ErrorCode.Validation, "Username dan password wajib diisi.");
            using var cmd = Command(db, "SELECT Id,Username,FullName,Role,PasswordHash FROM Users WHERE Username=$u AND IsActive=1", null,
                ("$u", username.Trim()));
            using var reader = cmd.ExecuteReader();
            Demand(reader.Read() && VerifyPassword(password, reader.GetString(4)), ErrorCode.Unauthorized,
                "Username atau password salah.");
            _current = new UserDto(reader.GetInt32(0), reader.GetString(1), reader.GetString(2),
                Enum.Parse<UserRole>(reader.GetString(3)));
            return _current;
        }, ct);
    public Task<ServiceResult<bool>> LogoutAsync(CancellationToken ct = default)
        => Run(_ => { _current = null; return true; }, ct);

    Task<ServiceResult<IReadOnlyList<ProductDto>>> IProductService.GetCatalogAsync(CancellationToken ct)
        => GetProductsAsync(false, true, ct);
    Task<ServiceResult<IReadOnlyList<ProductDto>>> IProductService.GetAllAsync(CancellationToken ct)
        => GetProductsAsync(true, true, ct);
    Task<ServiceResult<IReadOnlyList<ProductDto>>> IKioskService.GetCatalogAsync(CancellationToken ct)
        => GetProductsAsync(false, false, ct);
    private Task<ServiceResult<IReadOnlyList<ProductDto>>> GetProductsAsync(bool all, bool authenticated, CancellationToken ct)
        => Run<IReadOnlyList<ProductDto>>(db =>
        {
            if (authenticated) Require(db, all ? [UserRole.Admin] : [UserRole.Admin, UserRole.Kasir, UserRole.Pelanggan]);
            using var cmd = Command(db, "SELECT p.Id,p.Name,p.CategoryId,c.Name,p.PriceCents,p.IsActive FROM Products p " +
                "JOIN Categories c ON c.Id=p.CategoryId WHERE ($all=1 OR p.IsActive=1) ORDER BY p.Name", null, ("$all", all ? 1 : 0));
            using var reader = cmd.ExecuteReader();
            var result = new List<ProductDto>();
            while (reader.Read()) result.Add(ReadProduct(reader));
            return result;
        }, ct);
    private static ProductDto ReadProduct(SqliteDataReader reader) => new(reader.GetInt32(0), reader.GetString(1),
        reader.GetInt32(2), reader.GetString(3), Money(reader.GetInt64(4)), reader.GetInt64(5) != 0);
    Task<ServiceResult<IReadOnlyList<CategoryDto>>> IProductService.GetCategoriesAsync(CancellationToken ct)
        => Run<IReadOnlyList<CategoryDto>>(db =>
        {
            Require(db, UserRole.Admin, UserRole.Kasir, UserRole.Pelanggan);
            using var cmd = Command(db, "SELECT Id,Name FROM Categories ORDER BY Name");
            using var reader = cmd.ExecuteReader();
            var result = new List<CategoryDto>();
            while (reader.Read()) result.Add(new(reader.GetInt32(0), reader.GetString(1)));
            return result;
        }, ct);
    Task<ServiceResult<ProductDto>> IProductService.CreateAsync(ProductInput input, CancellationToken ct)
        => SaveProductAsync(null, input, ct);
    Task<ServiceResult<ProductDto>> IProductService.UpdateAsync(int id, ProductInput input, CancellationToken ct)
        => SaveProductAsync(id, input, ct);
    private Task<ServiceResult<ProductDto>> SaveProductAsync(int? id, ProductInput input, CancellationToken ct)
        => Run(db =>
        {
            Require(db, UserRole.Admin);
            Demand(input is not null && !string.IsNullOrWhiteSpace(input.Name) && input.Name.Trim().Length <= 160
                && input.CategoryId > 0 && input.Price > 0 && input.Price <= 999_999_999_999m
                && decimal.Round(input.Price, 2) == input.Price,
                ErrorCode.Validation, "Nama, kategori, atau harga produk tidak valid.");
            using (var category = Command(db, "SELECT COUNT(*) FROM Categories WHERE Id=$id", null, ("$id", input!.CategoryId)))
                Demand((long)category.ExecuteScalar()! == 1, ErrorCode.Validation, "Kategori tidak ditemukan.");
            if (id is null)
            {
                using var insert = Command(db, "INSERT INTO Products(Name,CategoryId,PriceCents,IsActive) VALUES($n,$c,$p,$a)", null,
                    ("$n", input.Name.Trim()), ("$c", input.CategoryId), ("$p", Cents(input.Price)), ("$a", input.IsActive ? 1 : 0));
                insert.ExecuteNonQuery();
                id = checked((int)LastId(db));
            }
            else
            {
                using var update = Command(db, "UPDATE Products SET Name=$n,CategoryId=$c,PriceCents=$p,IsActive=$a WHERE Id=$id", null,
                    ("$n", input.Name.Trim()), ("$c", input.CategoryId), ("$p", Cents(input.Price)),
                    ("$a", input.IsActive ? 1 : 0), ("$id", id));
                Demand(update.ExecuteNonQuery() == 1, ErrorCode.NotFound, "Produk tidak ditemukan.");
            }
            return GetProduct(db, id.Value)!;
        }, ct);
    private static ProductDto? GetProduct(SqliteConnection db, int id)
    {
        using var cmd = Command(db, "SELECT p.Id,p.Name,p.CategoryId,c.Name,p.PriceCents,p.IsActive FROM Products p " +
            "JOIN Categories c ON c.Id=p.CategoryId WHERE p.Id=$id", null, ("$id", id));
        using var reader = cmd.ExecuteReader();
        return reader.Read() ? ReadProduct(reader) : null;
    }
    Task<ServiceResult<bool>> IProductService.DeactivateAsync(int id, CancellationToken ct)
        => Run(db =>
        {
            Require(db, UserRole.Admin);
            Demand(id > 0, ErrorCode.Validation, "Id produk tidak valid.");
            using var cmd = Command(db, "UPDATE Products SET IsActive=0 WHERE Id=$id", null, ("$id", id));
            Demand(cmd.ExecuteNonQuery() == 1, ErrorCode.NotFound, "Produk tidak ditemukan.");
            return true;
        }, ct);
}
