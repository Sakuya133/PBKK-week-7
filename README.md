# POS Desktop — backend dan API

Backend POS untuk tugas Framework Programming. Frontend WPF belum ada di repository. MVP: login tiga role, katalog, kelola produk oleh Admin, checkout oleh Kasir, riwayat sesuai hak akses, dan dashboard harian. Tunai/QRIS hanya dicatat sesudah kasir mengonfirmasi pembayaran secara manual. Registrasi, edit profil, CRUD kategori, grafik, dan pengelolaan pelanggan lengkap belum tersedia.

Arsitektur produksi: **WPF → HTTPS → PosServer → SQL Server**. Kredensial SQL hanya berada di host server. `PosClient` memberi frontend interface service yang sama dengan backend. Library `PosBackend` berisi aturan bisnis dan ADO.NET; jangan dipakai untuk koneksi SQL langsung dari WPF. SQL Server tetap merupakan kebutuhan terpisah dari executable WPF maupun API.

## Langkah berikutnya untuk tim frontend

Buat proyek WPF `net10.0-windows` di `src/PosWpf/` (atau gunakan proyek tim yang sudah ada) dan tambahkan ke solution. Referensikan **hanya** `src/PosClient/PosClient.csproj`; kontrak dan DTO ada di `src/PosContracts/Contracts.cs`.

- Saat startup, buat satu `PosRemoteServices` dengan URL API dan berikan `Auth`, `Products`, `Transactions`, serta `Dashboard` ke ViewModels. Contoh: [integrasi WPF](#contoh-integrasi-wpf).
- Buat Views/XAML dan ViewModels untuk login; produk, transaksi, dan dashboard Admin; checkout dan riwayat Kasir; katalog dan riwayat Pelanggan. Tampilkan `ServiceResult<T>.Message` saat operasi gagal.
- Saat checkout, konfirmasi pembayaran manual, gunakan `CustomerId=null` untuk “Umum”, dan simpan `RequestId` yang sama untuk retry.

Jalankan API dan SQL Server sesuai [panduan setup](#setup-sql-server). WPF hanya menyimpan URL API, bukan kredensial SQL.

## Teknologi dan struktur

Target `net10.0` dipilih karena SDK 10.0.112 tersedia di lingkungan ini dan .NET 10 adalah [rilis LTS](https://learn.microsoft.com/en-us/dotnet/core/releases-and-support). WPF nanti memakai `net10.0-windows`. Server menggunakan ASP.NET Core Minimal API. Akses data memakai Microsoft.Data.SqlClient 7.1.1; password memakai Microsoft.AspNetCore.Identity PasswordHasher (PBKDF2 Identity V3, 600.000 iterasi, salt acak). SQL Server 2019+ atau Express diperlukan. Build WPF/publish `win-x64` memerlukan Windows dan .NET 10 SDK.

| Folder/file | Fungsi |
|---|---|
| `src/PosContracts` | DTO, interface service, dan pola hasil/error; tanpa SQL |
| `src/PosBackend` | Service bisnis, repository ADO.NET, hashing; hanya untuk server/setup |
| `src/PosServer` | HTTPS API, bearer session, pembatasan login |
| `src/PosClient` | Adaptor HTTP untuk interface service pada ViewModel WPF |
| `database/schema.sql` | Lima tabel utama, constraint, indeks, migrasi sederhana |
| `database/server.sql` | Tabel tambahan `AuthSessions` |
| `database/seed.sql`, `demo.sql` | Kategori awal; produk/akun demo opsional |
| `database/permissions.sql` | Role SQL `pos_api_role` untuk akun runtime API |
| `tools/PosSetup` | Setup/seed/akun awal dari terminal tepercaya |
| `tests/PosBackend.Tests`, `tests/PosClient.Tests`, `tests/api_smoke.py` | Tes bisnis/SQL, client, dan API |

Backend menangani login, izin, validasi, harga, transaksi atomik, statistik, dan error. Tim frontend menangani Views/XAML, ViewModels, styling, navigasi, konfirmasi pembayaran, dan binding hasil service. Backend tidak memakai kontrol WPF.

## Hak akses

| Operasi | Admin | Kasir | Pelanggan |
|---|:---:|:---:|:---:|
| Login/logout, katalog aktif, kategori | ✓ | ✓ | ✓ |
| Semua produk termasuk nonaktif; tambah/ubah/nonaktifkan | ✓ | — | — |
| Daftar pelanggan aktif, checkout | — | ✓ | — |
| Daftar/detail transaksi | Semua | Yang dicatat sendiri | Yang terhubung ke akunnya |
| Omzet/jumlah/top 5 hari ini | ✓ | — | — |

`CustomerId = null` berarti “Umum”; pembelian itu tidak muncul pada riwayat pelanggan. Semua endpoint selain login dan health memerlukan token. Role/status akun dibaca ulang dari database pada tiap permintaan; service juga memeriksa izin dan memfilter transaksi di SQL. Role dan identitas kasir berasal dari sesi, bukan dari input checkout. Detail transaksi di luar hak akses mengembalikan `NotFound`.

## Database dan transaksi

| Tabel | Kolom utama dan aturan |
|---|---|
| `Users` | `Id`, username unik, `PasswordHash`, nama, role, aktif, hitungan login gagal, batas lockout |
| `Categories` | `Id`, nama unik; seed kategori awal |
| `Products` | `Id`, nama, FK kategori, harga `decimal(18,2)` positif, aktif |
| `Transactions` | `Id`, nomor dan `RequestId` unik, FK kasir, FK pelanggan nullable, waktu UTC, metode, total `decimal(18,2)` |
| `TransactionItems` | FK transaksi/produk, snapshot nama/harga, kuantitas positif, subtotal; produk unik per transaksi |
| `AuthSessions` | Tabel tambahan server: FK pengguna, SHA-256 hash token unik, waktu dibuat/kedaluwarsa/dicabut |

Query berparameter. Saat checkout, service memvalidasi produk/pelanggan aktif, membaca ulang harga dari SQL, menghitung subtotal/total, lalu menyimpan header dan item dalam satu SQL transaction. Gagal berarti rollback. `RequestId` mencegah duplikasi ketika koneksi putus; gunakan `GetByRequestIdAsync` untuk memeriksa hasil yang ambigu. Snapshot item menjaga transaksi lama saat produk berubah. Lima login gagal mengunci akun 15 menit. Token acak 256 bit berlaku 8 jam, disimpan sebagai hash di SQL, dan dicabut saat logout. Dashboard memakai jam UTC SQL Server dan batas hari Asia/Jakarta.

## Setup SQL Server

1. Siapkan SQL Server; gunakan akun setup dengan hak membuat database. Isi `POS_CONNECTION_STRING` **di environment host PosSetup/PosServer**, bukan di source atau mesin WPF. `.env.example` dan `.env.production.example` hanya contoh, tidak dimuat otomatis. Untuk pengembangan lokal dengan sertifikat SQL belum tepercaya:

   ```powershell
   $env:POS_CONNECTION_STRING = 'Server=localhost;Database=PosDesktop;Integrated Security=true;Encrypt=true;TrustServerCertificate=true'
   ```

2. Jalankan setup dan buat Admin awal. Password dibaca tanpa echo jika terminal interaktif; untuk otomasi gunakan `POS_INITIAL_ADMIN_PASSWORD` sementara. Setup boleh diulang tanpa menghapus transaksi.

   ```powershell
   dotnet run --project tools/PosSetup
   dotnet run --project tools/PosSetup -- --create-admin admin_utama
   ```

3. Buat akun Kasir/Pelanggan dari terminal tepercaya (password tanpa echo, atau `POS_NEW_USER_PASSWORD` sementara):

   ```powershell
   dotnet run --project tools/PosSetup -- --create-user Kasir kasir_1 "Nama Kasir"
   dotnet run --project tools/PosSetup -- --create-user Pelanggan pelanggan_1 "Nama Pelanggan"
   ```

4. DBA membuat login/user SQL **khusus runtime API**, memasukkannya ke `pos_api_role` di database `PosDesktop`, tanpa `db_owner` atau `db_datawriter`. Akun setup terpisah. Akses port SQL hanya dari host API; jangan buka SQL ke terminal WPF. `permissions.sql` juga mencabut hak role lama `pos_app_role` bila ada. Pasang sertifikat SQL yang tepercaya dan gunakan `Encrypt=true;TrustServerCertificate=false` pada server production. Jangan beri frontend connection string.

5. Khusus database pengembangan: `dotnet run --project tools/PosSetup -- --demo`. Seed ini idempotent dan menambah produk serta akun demo: `admin` / `Admin123!`, `kasir` / `Kasir123!`, `pelanggan` / `Pelanggan123!`, `pelanggan2` / `Pelanggan234!`. **Jangan jalankan `--demo` di production.**

## Menjalankan API

Di host server, set `POS_CONNECTION_STRING`, `ASPNETCORE_ENVIRONMENT=Production`, `ASPNETCORE_URLS=https://0.0.0.0:8443`, dan sertifikat HTTPS Kestrel yang dipercaya klien (misalnya via `ASPNETCORE_Kestrel__Certificates__Default__Path` dan `...__Password` pada secret host). Jalankan `dotnet run --project src/PosServer` untuk pengembangan atau publish server untuk deployment. API menolak HTTP di mode normal. Untuk development lokal saja, set `ASPNETCORE_ENVIRONMENT=Development`, `POS_API_ALLOW_HTTP_DEV=true`, dan `ASPNETCORE_URLS=http://127.0.0.1:5077`; HTTP hanya diterima dari loopback. Endpoint `/health` hanya menyatakan proses hidup, tidak memeriksa database.

Akun SQL runtime API boleh membaca tabel bisnis dan menyimpan transaksi/sesi sesuai `pos_api_role`; tidak boleh membuat akun produk lewat hak DBA. Lindungi file konfigurasi, secret, sertifikat, jaringan, dan backup di host server. Gunakan sertifikat HTTPS valid pada klien. Aplikasi ini belum menyediakan pembayaran elektronik nyata, audit log terpusat, backup otomatis, atau pengelolaan akun mandiri; operasional production memerlukan prosedur di luar repository.

## Kontrak service untuk frontend

Frontend mereferensikan `src/PosClient/PosClient.csproj` (DTO/interface berasal dari referensi transitif `PosContracts`, tanpa paket SQL). Namespace kontrak tetap `PosBackend` untuk pemanggilan `using PosBackend;`. `PosRemoteServices` menyediakan `Auth`, `Products`, `Transactions`, `Dashboard`. Semua operasi async menerima `CancellationToken` opsional. `ServiceResult<T>` berisi `Success`, `Value`, `Error`, `Message`; saat gagal, `Value` null/default. `ErrorCode`: `Validation`, `Unauthorized`, `Forbidden`, `NotFound`, `Conflict`, `DatabaseUnavailable`, `DatabaseError`, `InternalError`, `ServiceUnavailable`, `RateLimited`; `None` untuk sukses. Koneksi HTTP gagal/timeout menghasilkan `ServiceUnavailable`; terlalu banyak percobaan login menghasilkan `RateLimited`. Error SQL diberi pesan umum tanpa kredensial. Pembatalan oleh caller melempar `OperationCanceledException`.

| Interface dan method | Parameter | Hasil | Role |
|---|---|---|---|
| `IAuthService.CurrentUser` | — | `UserDto?` dari login lokal | Semua |
| `LoginAsync(username, password)` | string, string | `UserDto` | Semua; login salah `Unauthorized` |
| `LogoutAsync()` | — | `bool` | Semua; mencabut token di server dan membersihkan sesi lokal |
| `IProductService.GetCatalogAsync()` | — | `IReadOnlyList<ProductDto>` aktif | Semua login |
| `GetCategoriesAsync()` | — | `IReadOnlyList<CategoryDto>` | Semua login |
| `GetAllAsync()` | — | `IReadOnlyList<ProductDto>` semua | Admin |
| `CreateAsync(ProductInput)` | nama, kategori, harga, aktif | `ProductDto` | Admin |
| `UpdateAsync(id, ProductInput)` | ID dan input | `ProductDto` | Admin |
| `DeactivateAsync(id)` | ID | `bool` | Admin |
| `ITransactionService.GetCustomerOptionsAsync()` | — | `IReadOnlyList<CustomerOptionDto>` | Kasir |
| `CheckoutAsync(CheckoutRequest)` | pelanggan nullable, pembayaran, item, RequestId | `TransactionDto` | Kasir |
| `GetByRequestIdAsync(Guid)` | RequestId checkout | `TransactionDto` | Kasir pencatat |
| `GetTransactionsAsync()` | — | 100 transaksi terbaru | Semua, sesuai scope |
| `GetTransactionsPageAsync(page, pageSize=50)` | halaman mulai 1; ukuran 1–100 | `TransactionPageDto`, `HasMore` | Semua, sesuai scope |
| `GetDetailAsync(id)` | ID transaksi | `TransactionDto` beserta item | Semua, sesuai scope |
| `IDashboardService.GetTodayAsync()` | — | `DailySalesDto` | Admin |

`ProductInput` memerlukan harga positif, maksimal dua angka desimal. `CheckoutRequest` memakai `PaymentMethod.Tunai`/`Qris`, `CustomerId=null` untuk Umum, 1–100 `CheckoutItem` dengan produk unik dan kuantitas integer positif, serta `RequestId=Guid.NewGuid()`. Gunakan **Guid yang sama** pada retry. Contoh error: input buruk `Validation`; tidak login/token kedaluwarsa `Unauthorized`; role salah `Forbidden`; transaksi di luar scope `NotFound`; RequestId duplikat `Conflict`; SQL gagal `DatabaseUnavailable`/`DatabaseError`; API tidak terjangkau `ServiceUnavailable`. Respons API memakai bentuk `ServiceResult<T>` dan status HTTP yang sesuai; login mengirim token hanya ke client service, tidak ke ViewModel.

### Contoh integrasi WPF

```csharp
using PosBackend;
using PosClient;

// Buat satu instance pada startup WPF, inject ke ViewModels.
using var services = new PosRemoteServices(new Uri("https://pos.example.com:8443/"));
var login = await services.Auth.LoginAsync(enteredUsername, enteredPassword);
if (!login.Success) { ErrorText = login.Message; return; }

var catalog = await services.Products.GetCatalogAsync();
if (!catalog.Success) { ErrorText = catalog.Message; return; }
Products.Clear();
foreach (var product in catalog.Value!) Products.Add(product);

// Kasir mengonfirmasi pembayaran manual terlebih dahulu.
var requestId = Guid.NewGuid(); // simpan di state ViewModel untuk retry
var checkout = await services.Transactions.CheckoutAsync(
    new CheckoutRequest(null, PaymentMethod.Tunai,
        new[] { new CheckoutItem(catalog.Value![0].Id, 2) }, requestId));
if (!checkout.Success) { ErrorText = checkout.Message; return; }

var history = await services.Transactions.GetTransactionsPageAsync(1, 50);
if (!history.Success) { ErrorText = history.Message; return; }
Transactions.Clear();
foreach (var row in history.Value!.Items) Transactions.Add(row);
```

Di ViewModel, simpan interface service lewat constructor injection sederhana; bind `Message` ke properti error, daftar ke `ObservableCollection<T>`, dan panggil operasi dari async command dengan loading state. Tampilkan opsi “Umum” sebagai `CustomerId=null`. Simpan `RequestId` sampai hasil checkout dipastikan; bila respons hilang, panggil `GetByRequestIdAsync`. Muat ulang koleksi setelah ganti akun. Simpan `PosRemoteServices` selama sesi aplikasi; token hanya di memori. Untuk tes lokal di loopback HTTP, gunakan `PosRemoteServices.CreateForDevelopment(new Uri("http://127.0.0.1:5077/"))`.

## Build dan pengujian

```bash
dotnet restore PosDesktop.slnx
dotnet build PosDesktop.slnx
dotnet run --project tests/PosBackend.Tests
dotnet run --project tests/PosClient.Tests
```

Runner C# tanpa `POS_TEST_CONNECTION_STRING` menguji hash, TLS SQL, dan akses sebelum login, lalu melewati tes SQL. Dengan `POS_TEST_CONNECTION_STRING` yang menunjuk **database khusus bernama `PosDesktopTest`** dan hak membuat database, runner menguji izin `pos_api_role`, login benar/salah, lockout, migrasi hash, role, isolasi pelanggan, retry, snapshot harga, pagination, rollback dengan trigger uji, dan dashboard. Jangan arahkan ke production. Runner client menguji HTTPS, bearer token, deserialisasi DTO, logout, dan error koneksi tanpa SQL. Tes API: siapkan database development dengan `--demo`, jalankan PosServer pada loopback HTTP dev, lalu `python3 tests/api_smoke.py`. Tes itu membuat transaksi demo dan memeriksa login, RBAC, checkout, idempotensi, isolasi pelanggan, dashboard, logout, dan pembatasan login.

Verifikasi 18 Oktober 2026: build solution **0 warning, 0 error**; runner bisnis/SQL, runner client, dan API smoke lulus pada SQL Server 2022 container sementara; setup normal berhasil dijalankan ulang. `PosClient` tidak memiliki paket NuGet SQL transitif. Publish API self-contained `linux-x64` berhasil. Ketersediaan Windows/WPF dan publish GUI belum diuji di lingkungan ini. Pengujian beban, penetrasi, pemulihan backup, dan konfigurasi sertifikat production belum dilakukan.

## Publish Windows dan server

Belum ada proyek WPF, jadi publish GUI adalah **langkah integrasi berikutnya**. Setelah frontend dibuat dan mereferensikan `PosClient`:

```powershell
dotnet publish path/ke/ProyekWpf.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
dotnet publish src/PosServer/PosServer.csproj -c Release -r win-x64 --self-contained true -o publish-server
```

Bagikan seluruh isi folder publish WPF ke komputer pengguna, beserta alamat API yang dikonfigurasi di luar source. **Jangan bagikan connection string/sertifikat server** ke klien. Deploy folder `publish-server` hanya pada host API bersama secret SQL dan sertifikat HTTPS yang dijaga; siapkan SQL Server terpisah. Class library backend sendiri tidak menghasilkan aplikasi GUI executable. Untuk layanan Windows permanen, pengaturan service account, auto-start, firewall, sertifikat, log, dan backup harus disiapkan operator.

## Troubleshooting

| Gejala | Pemeriksaan |
|---|---|
| API menolak start | `POS_CONNECTION_STRING` belum diatur, enkripsi SQL nonaktif, atau `TrustServerCertificate=true` di mode normal. |
| `ServiceUnavailable` di WPF | Periksa alamat API, proses PosServer, firewall, sertifikat HTTPS, dan jaringan. |
| `DatabaseUnavailable` | Periksa SQL Server, port, database, akun `pos_api_role`, sertifikat SQL, dan environment server. |
| Login gagal/terkunci | Akun harus dibuat melalui setup; periksa status aktif dan tunggu lockout 15 menit setelah lima kegagalan. |
| `Unauthorized` setelah logout | Token telah dicabut; login ulang. |
| `Forbidden` | Role tidak punya izin; lihat matriks akses. |
| `NotFound` detail transaksi | ID tidak ada atau tidak dimiliki akun. |
| HTTP 400 “HTTPS wajib” | Pakai URL HTTPS; HTTP hanya boleh development loopback dengan `POS_API_ALLOW_HTTP_DEV=true`. |
| Setup SQL gagal | Gunakan akun setup berhak `CREATE DATABASE`; pisahkan dari akun runtime API. |
| Restore/build gagal | Periksa SDK .NET 10 dan akses NuGet. |
