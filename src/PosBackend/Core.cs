using System.Security.Cryptography;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

namespace PosBackend;

internal sealed class ServiceFault(ErrorCode code, string message) : Exception(message)
{
    public ErrorCode Code { get; } = code;
}

internal static class Guard
{
    public static void That(bool condition, ErrorCode code, string message)
    {
        if (!condition) throw new ServiceFault(code, message);
    }
}

internal sealed class Session
{
    private readonly object _gate = new();
    private UserDto? _current;
    private long _version;
    public UserDto? Current { get { lock (_gate) return _current; } }
    public void Set(UserDto? user) { lock (_gate) { _current = user; _version++; } }
    public async Task<UserDto> RequireAsync(Database db, CancellationToken ct, params UserRole[] roles)
    {
        UserDto? cached; long version;
        lock (_gate) { cached = _current; version = _version; }
        Guard.That(cached is not null, ErrorCode.Unauthorized, "Silakan login terlebih dahulu.");
        var user = await UserRepository.GetActiveByIdAsync(db, cached!.Id, ct);
        lock (_gate)
        {
            Guard.That(_version == version, ErrorCode.Unauthorized, "Sesi berubah. Ulangi operasi setelah login.");
            if (user is null)
            {
                _current = null;
                _version++;
                throw new ServiceFault(ErrorCode.Unauthorized, "Sesi berakhir. Silakan login kembali.");
            }
            _current = user;
        }
        Guard.That(roles.Length == 0 || roles.Contains(user.Role), ErrorCode.Forbidden, "Akses tidak diizinkan untuk role ini.");
        return user;
    }
}

public static class PasswordHash
{
    private static readonly object User = new();
    private static readonly PasswordHasher<object> Hasher = new(Options.Create(new PasswordHasherOptions
    {
        CompatibilityMode = PasswordHasherCompatibilityMode.IdentityV3,
        IterationCount = 600_000
    }));

    public static string Create(string password)
    {
        if (string.IsNullOrWhiteSpace(password) || password.Length is < 8 or > 128)
            throw new ArgumentException("Panjang password harus 8–128 karakter.", nameof(password));
        return Hasher.HashPassword(User, password);
    }

    public static bool Verify(string password, string encoded)
        => VerifyWithUpgrade(password, encoded).Valid;

    internal static (bool Valid, bool NeedsUpgrade) VerifyWithUpgrade(string password, string encoded)
    {
        if (string.IsNullOrEmpty(password) || password.Length > 128 || string.IsNullOrEmpty(encoded)) return (false, false);
        if (!encoded.StartsWith("PBKDF2-SHA256$", StringComparison.Ordinal))
        {
            try
            {
                var result = Hasher.VerifyHashedPassword(User, encoded, password);
                return (result != PasswordVerificationResult.Failed,
                    result == PasswordVerificationResult.SuccessRehashNeeded && password.Length >= 8);
            }
            catch (FormatException) { return (false, false); }
        }
        var parts = encoded.Split('$');
        if (parts.Length != 4 || parts[0] != "PBKDF2-SHA256" || !int.TryParse(parts[1], out var iterations)
            || iterations is < 100_000 or > 2_000_000) return (false, false);
        try
        {
            var salt = Convert.FromBase64String(parts[2]);
            var expected = Convert.FromBase64String(parts[3]);
            if (salt.Length != 16 || expected.Length != 32) return (false, false);
            var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, 32);
            var valid = CryptographicOperations.FixedTimeEquals(actual, expected);
            return (valid, valid && password.Length >= 8);
        }
        catch (FormatException) { return (false, false); }
    }
}

internal static class Execute
{
    public static async Task<ServiceResult<T>> Run<T>(Func<Task<T>> action)
    {
        try { return ServiceResult<T>.Ok(await action()); }
        catch (ServiceFault e) { return ServiceResult<T>.Fail(e.Code, e.Message); }
        catch (SqlException e) when (e.Number is -2 or 2 or 26 or 40 or 53 or 64 or 4060 or 18456 or 10053 or 10054 or 10060 or 10061)
        { return ServiceResult<T>.Fail(ErrorCode.DatabaseUnavailable, "SQL Server tidak dapat dihubungi. Periksa koneksi dan database."); }
        catch (SqlException)
        { return ServiceResult<T>.Fail(ErrorCode.DatabaseError, "Operasi database gagal. Hubungi pengelola aplikasi."); }
        catch (OverflowException)
        { return ServiceResult<T>.Fail(ErrorCode.Validation, "Nilai transaksi melebihi batas yang diizinkan."); }
        catch (OperationCanceledException) { throw; }
        catch (Exception)
        { return ServiceResult<T>.Fail(ErrorCode.InternalError, "Terjadi kesalahan internal. Hubungi pengelola aplikasi."); }
    }
}

internal sealed class Database(string connectionString)
{
    public async Task<SqlConnection> OpenAsync(CancellationToken ct)
    {
        var connection = new SqlConnection(connectionString);
        try { await connection.OpenAsync(ct); return connection; }
        catch (SqlException)
        {
            await connection.DisposeAsync();
            throw new ServiceFault(ErrorCode.DatabaseUnavailable,
                "SQL Server tidak dapat dihubungi. Periksa koneksi dan database.");
        }
        catch (InvalidOperationException)
        {
            await connection.DisposeAsync();
            throw new ServiceFault(ErrorCode.DatabaseUnavailable,
                "Koneksi database tidak tersedia. Periksa konfigurasi.");
        }
        catch { await connection.DisposeAsync(); throw; }
    }
}

public sealed class PosServices
{
    private readonly Session _session;
    public IAuthService Auth { get; }
    public IProductService Products { get; }
    public ITransactionService Transactions { get; }
    public IDashboardService Dashboard { get; }
    public IKioskService Kiosk { get; }

    public PosServices(string connectionString) : this(connectionString, false) { }

    public static PosServices CreateForDevelopment(string connectionString) => new(connectionString, true);

    internal static PosServices ForAuthenticatedUser(string connectionString, UserDto user, bool development)
    {
        var services = new PosServices(connectionString, development);
        services._session.Set(user);
        return services;
    }

    private PosServices(string connectionString, bool allowUntrustedServerCertificate)
    {
        if (string.IsNullOrWhiteSpace(connectionString)) throw new ArgumentException("Connection string wajib diisi.", nameof(connectionString));
        // Validate format without exposing the connection string in an error.
        SqlConnectionStringBuilder builder;
        try { builder = new SqlConnectionStringBuilder(connectionString); }
        catch (ArgumentException) { throw new ArgumentException("Format connection string tidak valid.", nameof(connectionString)); }
        var encryption = builder.Encrypt.ToString();
        if (!encryption.Equals("Mandatory", StringComparison.OrdinalIgnoreCase)
            && !encryption.Equals("Strict", StringComparison.OrdinalIgnoreCase)
            && !encryption.Equals("True", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Koneksi SQL wajib memakai enkripsi.", nameof(connectionString));
        if (builder.TrustServerCertificate && !allowUntrustedServerCertificate)
            throw new ArgumentException("Sertifikat SQL Server harus divalidasi untuk penggunaan production.", nameof(connectionString));
        var db = new Database(builder.ConnectionString);
        var session = _session = new Session();
        Auth = new AuthService(db, session);
        Products = new ProductService(db, session);
        Transactions = new TransactionService(db, session);
        Dashboard = new DashboardService(db, session);
        Kiosk = new KioskService(db, session);
    }
}
