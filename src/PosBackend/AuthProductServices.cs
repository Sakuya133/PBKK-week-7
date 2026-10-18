using System.Data;
using Microsoft.Data.SqlClient;

namespace PosBackend;

internal sealed class AuthService(Database db, Session session) : IAuthService
{
    private readonly UserRepository users = new(db);
    public UserDto? CurrentUser => session.Current;

    public Task<ServiceResult<UserDto>> LoginAsync(string username, string password, CancellationToken cancellationToken = default)
        => Execute.Run(async () =>
        {
            session.Set(null);
            Guard.That(!string.IsNullOrWhiteSpace(username) && username.Trim().Length <= 80
                && !string.IsNullOrEmpty(password) && password.Length <= 128,
                ErrorCode.Validation, "Username dan password wajib diisi.");
            var found = await users.TryLoginAsync(username.Trim(), password, cancellationToken);
            Guard.That(found is not null, ErrorCode.Unauthorized,
                "Username atau password salah, atau akun tidak aktif.");
            session.Set(found);
            return found!;
        });

    public Task<ServiceResult<bool>> LogoutAsync(CancellationToken cancellationToken = default)
    {
        session.Set(null);
        return Task.FromResult(ServiceResult<bool>.Ok(true));
    }
}

internal sealed class ProductService(Database db, Session session) : IProductService
{
    public Task<ServiceResult<IReadOnlyList<ProductDto>>> GetCatalogAsync(CancellationToken cancellationToken = default)
        => GetProductsAsync(false, cancellationToken);
    public Task<ServiceResult<IReadOnlyList<ProductDto>>> GetAllAsync(CancellationToken cancellationToken = default)
        => GetProductsAsync(true, cancellationToken);

    private Task<ServiceResult<IReadOnlyList<ProductDto>>> GetProductsAsync(bool all, CancellationToken ct)
        => Execute.Run<IReadOnlyList<ProductDto>>(async () =>
        {
            await session.RequireAsync(db, ct, all ? [UserRole.Admin] : [UserRole.Admin, UserRole.Kasir, UserRole.Pelanggan]);
            await using var connection = await db.OpenAsync(ct);
            await using var command = new SqlCommand(
                "SELECT p.Id, p.Name, p.CategoryId, c.Name, p.Price, p.IsActive FROM dbo.Products p " +
                "JOIN dbo.Categories c ON c.Id=p.CategoryId WHERE (@all=1 OR p.IsActive=1) ORDER BY p.Name", connection);
            command.Parameters.Add("@all", SqlDbType.Bit).Value = all;
            var products = new List<ProductDto>();
            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct)) products.Add(ReadProduct(reader));
            return products;
        });

    public Task<ServiceResult<IReadOnlyList<CategoryDto>>> GetCategoriesAsync(CancellationToken cancellationToken = default)
        => Execute.Run<IReadOnlyList<CategoryDto>>(async () =>
        {
            await session.RequireAsync(db, cancellationToken, UserRole.Admin, UserRole.Kasir, UserRole.Pelanggan);
            await using var connection = await db.OpenAsync(cancellationToken);
            await using var command = new SqlCommand("SELECT Id, Name FROM dbo.Categories ORDER BY Name", connection);
            var categories = new List<CategoryDto>();
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken)) categories.Add(new(reader.GetInt32(0), reader.GetString(1)));
            return categories;
        });

    public Task<ServiceResult<ProductDto>> CreateAsync(ProductInput input, CancellationToken cancellationToken = default)
        => SaveAsync(null, input, cancellationToken);
    public Task<ServiceResult<ProductDto>> UpdateAsync(int id, ProductInput input, CancellationToken cancellationToken = default)
        => SaveAsync(id, input, cancellationToken);

    private Task<ServiceResult<ProductDto>> SaveAsync(int? id, ProductInput input, CancellationToken ct)
        => Execute.Run(async () =>
        {
            await session.RequireAsync(db, ct, UserRole.Admin);
            Guard.That(input is not null && !string.IsNullOrWhiteSpace(input.Name) && input.Name.Trim().Length <= 160
                && input.CategoryId > 0 && input.Price > 0 && input.Price <= 999_999_999_999m
                && decimal.Round(input.Price, 2) == input.Price,
                ErrorCode.Validation, "Nama, kategori, atau harga produk tidak valid.");
            await using var connection = await db.OpenAsync(ct);
            await using var command = new SqlCommand(id is null
                ? "INSERT dbo.Products (Name, CategoryId, Price, IsActive) OUTPUT INSERTED.Id VALUES (@name,@category,@price,@active)"
                : "UPDATE dbo.Products SET Name=@name,CategoryId=@category,Price=@price,IsActive=@active " +
                  "OUTPUT INSERTED.Id WHERE Id=@id", connection);
            command.Parameters.Add("@name", SqlDbType.NVarChar, 160).Value = input!.Name.Trim();
            command.Parameters.Add("@category", SqlDbType.Int).Value = input.CategoryId;
            var price = command.Parameters.Add("@price", SqlDbType.Decimal);
            price.Precision = 18; price.Scale = 2; price.Value = input.Price;
            command.Parameters.Add("@active", SqlDbType.Bit).Value = input.IsActive;
            if (id is not null) command.Parameters.Add("@id", SqlDbType.Int).Value = id.Value;
            try
            {
                var value = await command.ExecuteScalarAsync(ct);
                Guard.That(value is not null, ErrorCode.NotFound, "Produk tidak ditemukan.");
                return (await GetByIdAsync(connection, (int)value!, ct))!;
            }
            catch (SqlException e) when (e.Number == 547)
            { throw new ServiceFault(ErrorCode.Validation, "Kategori tidak ditemukan."); }
        });

    public Task<ServiceResult<bool>> DeactivateAsync(int id, CancellationToken cancellationToken = default)
        => Execute.Run(async () =>
        {
            await session.RequireAsync(db, cancellationToken, UserRole.Admin);
            Guard.That(id > 0, ErrorCode.Validation, "Id produk tidak valid.");
            await using var connection = await db.OpenAsync(cancellationToken);
            await using var command = new SqlCommand("UPDATE dbo.Products SET IsActive=0 WHERE Id=@id", connection);
            command.Parameters.Add("@id", SqlDbType.Int).Value = id;
            Guard.That(await command.ExecuteNonQueryAsync(cancellationToken) > 0, ErrorCode.NotFound, "Produk tidak ditemukan.");
            return true;
        });

    private static async Task<ProductDto?> GetByIdAsync(SqlConnection connection, int id, CancellationToken ct)
    {
        await using var command = new SqlCommand("SELECT p.Id,p.Name,p.CategoryId,c.Name,p.Price,p.IsActive " +
            "FROM dbo.Products p JOIN dbo.Categories c ON c.Id=p.CategoryId WHERE p.Id=@id", connection);
        command.Parameters.Add("@id", SqlDbType.Int).Value = id;
        await using var reader = await command.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? ReadProduct(reader) : null;
    }
    private static ProductDto ReadProduct(SqlDataReader reader) => new(reader.GetInt32(0), reader.GetString(1),
        reader.GetInt32(2), reader.GetString(3), reader.GetDecimal(4), reader.GetBoolean(5));
}
