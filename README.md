# POS Desktop

Aplikasi kasir desktop Mikogomi POS untuk Windows/Linux x64. Logo `src/mikogomi.jpeg` dipakai di UI, ikon jendela, executable Windows, dan shortcut; paket membawa PNG/ICO hasil konversi untuk ukuran ikon. UI memakai Avalonia dan MVVM sederhana; client desktop memanggil API ASP.NET Core melalui HTTP(S). API memakai ADO.NET dan SQL Server. SQL Server tidak disertakan dalam executable.

Jendela login tetap terbuka saat API tidak tersedia dan memiliki tombol **Coba hubungkan lagi**. Login, katalog, transaksi, riwayat, dan dashboard perlu API serta database aktif.

## Fitur dan hak akses

| Fitur | Admin | Kasir | Pelanggan | Tamu |
|---|:---:|:---:|:---:|:---:|
| Login/logout | ✓ | ✓ | ✓ | — |
| Lihat katalog aktif | ✓ | ✓ | ✓ | ✓ |
| Pesan menu dan isi nama | — | — | — | ✓ |
| Konfirmasi pembayaran pesanan tamu | — | ✓ | — | — |
| Kelola produk | ✓ | — | — | — |
| Dashboard penjualan | ✓ | — | — | — |
| Checkout di kasir | — | ✓ | — | — |
| Riwayat transaksi | Semua | Transaksi sendiri | Pembelian sendiri | — |

Tamu dapat memesan tanpa akun, mengisi nama, mengatur jumlah, dan memilih Tunai/QRIS. Pesanan disimpan sebagai menunggu pembayaran. Kasir mengonfirmasi pembayaran di antrean kiosk; barulah pesanan menjadi transaksi penjualan. API membaca harga/status produk dari database dan menyimpan snapshot nama/harga secara atomik.

## Teknologi dan struktur

- .NET 10, Avalonia 12.1.4, C#, SQL Server 2019+.
- Microsoft.Data.SqlClient hanya digunakan oleh backend/API; tidak ada koneksi SQL langsung dari desktop.
- Password memakai ASP.NET Core Identity PasswordHasher. Sesi bearer disimpan di server dalam bentuk hash.
- Batas harian laporan menggunakan Asia/Jakarta.

| Path | Isi |
|---|---|
| `src/PosDesktop` | Aplikasi Avalonia, XAML, ViewModel, konfigurasi client |
| `src/PosClient` | Adaptor HTTP service |
| `src/PosContracts` | DTO, interface, hasil/error |
| `src/PosServer` | API ASP.NET Core dan sesi login |
| `src/PosBackend` | Service bisnis, aturan akses, repository ADO.NET |
| `tools/PosSetup` | Setup schema, seed, dan akun awal |
| `database` | Schema, seed, demo, dan izin SQL |
| `tests` | Tes client/backend dan API smoke test |
| `scripts/package_cross_platform.py` | Publish dan arsip paket Linux/Windows |

Alur UI: XAML bind ke MainViewModel → ViewModel memanggil PosClient → PosClient mengirim request ke PosServer → backend memeriksa izin/validasi dan mengakses SQL Server. Kiosk tamu memakai katalog dan endpoint pesanan publik terbatas; antrean dan konfirmasi pembayaran memerlukan login kasir.

## Konfigurasi client

Urutan alamat API: environment `POS_API_URL`, file konfigurasi user, lalu alamat development lokal. `POS_CLIENT_CONFIG` dapat menentukan path file konfigurasi.

- Linux: `$XDG_CONFIG_HOME/posdesktop/pos-client.json`, atau `~/.config/posdesktop/pos-client.json`.
- Windows: `%APPDATA%\posdesktop\pos-client.json`.
- Contoh isi file: `{"apiUrl":"http://127.0.0.1:5077/"}`.

HTTP hanya diterima pada API development loopback. Server pada jaringan harus memakai HTTPS dengan sertifikat tepercaya. Jangan menyimpan connection string SQL pada client.

## Database dan akun demo

SQL Server harus tersedia dan dapat diakses dari host API. Atur connection string hanya di terminal host setup/API. Contoh untuk Linux development:

~~~bash
export POS_CONNECTION_STRING='Server=localhost;Database=PosDesktop;User ID=sa;Password=GANTI_PASSWORD;Encrypt=true;TrustServerCertificate=true'
dotnet run --project tools/PosSetup -- --demo
~~~

Setup idempotent dan tidak menghapus transaksi/pengguna. Untuk production gunakan sertifikat tepercaya (`TrustServerCertificate=false`) dan role runtime minimum dari `database/permissions.sql`.
Schema menambahkan `GuestOrders` dan `GuestOrderItems`; pesanan tamu baru masuk ke penjualan setelah kasir mengonfirmasi pembayaran.

Akun demo khusus development: Admin `admin` / `Admin123!`; Kasir `kasir` / `Kasir123!`; Pelanggan `pelanggan` / `Pelanggan123!`; Pelanggan kedua `pelanggan2` / `Pelanggan234!`. Jangan jalankan seed demo di production.

## Build, jalankan, dan uji

~~~bash
dotnet restore PosDesktop.slnx
dotnet build PosDesktop.slnx -c Release
dotnet run --project tests/PosClient.Tests -c Release
dotnet run --project tests/PosBackend.Tests -c Release
~~~

Untuk development, jalankan API di terminal pertama dengan SQL Server aktif:

~~~bash
export POS_CONNECTION_STRING='Server=localhost;Database=PosDesktop;User ID=sa;Password=GANTI_PASSWORD;Encrypt=true;TrustServerCertificate=true'
ASPNETCORE_ENVIRONMENT=Development POS_API_ALLOW_HTTP_DEV=true ASPNETCORE_URLS=http://127.0.0.1:5077 dotnet run --project src/PosServer
~~~

Jalankan UI dari terminal desktop: `dotnet run --project src/PosDesktop`. Endpoint `/health` hanya memeriksa proses API, bukan koneksi database.

Tes backend tanpa `POS_TEST_CONNECTION_STRING` menjalankan tes non-SQL lalu melewati integrasi database. Tes client memakai HTTP handler simulasi dan bukan bukti integrasi SQL. Untuk SQL/API smoke test, siapkan database pengembangan khusus sesuai petunjuk di `tests/PosBackend.Tests` dan `tests/api_smoke.py`; jangan gunakan database production.

## Paket Windows dan Linux

Prasyarat build: .NET 10 SDK, Python 3, akses NuGet/internet. Dari root repo jalankan:

~~~bash
python3 scripts/package_cross_platform.py
~~~

Output self-contained: `artifacts/POS-Desktop-linux-x64/`, `artifacts/POS-Desktop-linux-x64.tar.gz`, `artifacts/POS-Desktop-win-x64/`, dan `artifacts/POS-Desktop-win-x64.zip`. Paket berisi client, dependency Avalonia, launcher, panduan, skrip SQL, API lokal opsional, dan tool setup. SQL Server tetap kebutuhan terpisah. Cross-publish Windows dari Linux tidak membuktikan runtime Windows.

### Download

Unduh paket terbaru dari [GitHub Releases](https://github.com/Sakuya133/PBKK-week-7/releases/latest).

**Ubuntu:**

~~~bash
curl -fLO https://github.com/Sakuya133/PBKK-week-7/releases/latest/download/POS-Desktop-linux-x64.tar.gz
tar -xzf POS-Desktop-linux-x64.tar.gz
cd POS-Desktop-linux-x64
./install-desktop.sh
~~~

Klik **Mikogomi POS** dari menu aplikasi, atau jalankan `./launch-posdesktop.sh`.

**Windows PowerShell:**

~~~powershell
Invoke-WebRequest https://github.com/Sakuya133/PBKK-week-7/releases/latest/download/POS-Desktop-win-x64.zip -OutFile POS-Desktop-win-x64.zip
Expand-Archive .\POS-Desktop-win-x64.zip -DestinationPath .\POS-Desktop-win-x64 -Force
cd .\POS-Desktop-win-x64
powershell -NoProfile -ExecutionPolicy Bypass -File .\Install-POS-Desktop-Shortcut.ps1
~~~

Klik shortcut **Mikogomi POS** di Desktop atau Start Menu.

### Ubuntu/Linux

Avalonia menggunakan X11 melalui XWayland secara default. Jalankan dalam sesi desktop grafis yang menyediakan `DISPLAY`. Paket sistem yang diperlukan: `libx11-6`, `libice6`, `libsm6`, `libfontconfig1`. `curl` diperlukan jika memakai launcher untuk memulai API lokal; `x11-utils` memberi preflight `xdpyinfo`.

~~~bash
sudo apt update
sudo apt install libx11-6 libice6 libsm6 libfontconfig1 curl x11-utils
~~~

`install-desktop.sh` memasang shortcut Mikogomi POS ke menu aplikasi dan Desktop user. Klik shortcut **Mikogomi POS** untuk membuka aplikasi; launcher mendukung path ber-spasi, memeriksa display tanpa menebak `DISPLAY` atau mengubah `XAUTHORITY`, dan menulis log di `$XDG_STATE_HOME/posdesktop/logs/posdesktop.log` atau `~/.local/state/posdesktop/logs/posdesktop.log`.

API lokal opsional (SQL Server tetap harus aktif):

~~~bash
export POS_CONNECTION_STRING='Server=localhost;Database=PosDesktop;User ID=sa;Password=GANTI_PASSWORD;Encrypt=true;TrustServerCertificate=true'
POS_START_LOCAL_API=true ./launch-posdesktop.sh
~~~

Launcher membatasi API development ke loopback, menunggu `/health`, hanya menghentikan proses yang dimulainya, lalu membuka desktop. Bila API gagal, UI login tetap terbuka.

### Windows

Target Windows x64. Ekstrak ZIP, jalankan `powershell -NoProfile -ExecutionPolicy Bypass -File .\Install-POS-Desktop-Shortcut.ps1` sekali, lalu klik shortcut **Mikogomi POS** di Desktop atau Start Menu. Shortcut membuka launcher tersembunyi tanpa jendela terminal. Bisa juga buka `Launch-POS-Desktop.cmd` atau `PosDesktop.exe` langsung. Launcher mendukung folder ber-spasi. Log: `%LOCALAPPDATA%\POS Desktop\Logs\`; konfigurasi: `%APPDATA%\posdesktop\pos-client.json`. Untuk API lokal, set `POS_START_LOCAL_API=1` dan `POS_CONNECTION_STRING` sebelum launcher; connection string dihapus dari environment sebelum aplikasi desktop dibuka.

## Troubleshooting dan batasan

- `XOpenDisplay failed`: buka launcher dari terminal sesi desktop. Wayland memerlukan XWayland dan `DISPLAY`; SSH/headless tanpa X11 forwarding terotorisasi tidak dapat membuka jendela. Jangan mengisi `DISPLAY=:0` secara manual.
- API tidak terhubung: periksa `POS_API_URL`, file konfigurasi, port/firewall, dan proses PosServer.
- `DatabaseUnavailable`: periksa SQL Server, `POS_CONNECTION_STRING` pada host API, izin runtime, dan sertifikat SQL.
- SQL Server tidak disertakan di paket. Siapkan service SQL Server lokal atau gunakan server jaringan.

## Status verifikasi

- Release build berhasil tanpa warning atau error. Tes client lulus; tes backend validasi/akses lulus.
- API route smoke lulus: status API `200`, antrean kasir tanpa login `401`, input pesanan tidak valid `400`. Katalog publik mencapai database, tetapi mengembalikan `503` karena SQL tidak tersedia.
- Paket self-contained Windows x64 dan Ubuntu/Linux x64 berhasil dibuat; executable, launcher, skrip database, README, dan arsip diperiksa.
- Launcher Linux menampilkan petunjuk display dan keluar dengan kode `2` di shell headless. GUI paket baru belum diuji karena sesi ini tidak memiliki display/Xvfb. Windows dibuat lewat cross-publish; native Windows belum diuji.
- Tes integrasi SQL dilewati karena `POS_TEST_CONNECTION_STRING` belum diatur. Simpan pesanan, konfirmasi kasir, dan perubahan dashboard perlu diuji dengan SQL Server pengembangan.
