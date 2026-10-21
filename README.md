# MiKuGoMi POS

Aplikasi kasir desktop untuk demo offline di Windows x64 dan Ubuntu/Linux x64. Paket unduhan menjalankan Avalonia dan SQLite lokal tanpa SQL Server, API, akun cloud, atau konfigurasi connection string. Runtime .NET dan pustaka SQLite disertakan di paket masing-masing sistem operasi.

## Jalankan paket demo

- **Windows x64:** unduh `artifacts/POS-Desktop-win-x64.zip`, ekstrak seluruh isi ZIP, lalu buka `POS-Desktop-win-x64/PosDesktop.exe`. `Launch-POS-Desktop.cmd` atau shortcut dari `Install-POS-Desktop-Shortcut.ps1` juga dapat dipakai.
- **Ubuntu/Linux x64:** unduh `artifacts/POS-Desktop-linux-x64.tar.gz`, ekstrak, lalu dari sesi desktop jalankan `POS-Desktop-linux-x64/launch-posdesktop.sh`. `install-desktop.sh` opsional untuk membuat shortcut MiKuGoMi POS.

Database demo dibuat dan diisi otomatis pada pembukaan pertama. Data perubahan produk, pesanan, dan transaksi bertahan setelah aplikasi ditutup. Lokasi file SQLite:

- Windows: `%LOCALAPPDATA%\mikugomi-pos\demo.sqlite3`
- Linux: `${XDG_DATA_HOME:-~/.local/share}/mikugomi-pos/demo.sqlite3`

Jika ingin mengulang demo dari awal, tutup aplikasi lalu pindahkan file `demo.sqlite3` ke tempat cadangan. Pembukaan berikutnya membuat database contoh baru; tindakan ini tidak dilakukan otomatis.

### Akun demo

| Peran | Username | Password |
| --- | --- | --- |
| Admin | `admin` | `admin` |
| Kasir | `kasir` | `kasir` |
| Pelanggan | `pelanggan` | `pelanggan` |
| Pelanggan kedua | `pelanggan2` | `pelanggan2` |

Kredensial ini khusus demo. Tampilan login juga menampilkan tiga akun pertama.

## Yang dapat dicoba

- **Admin:** dashboard omzet dan produk terlaris, tambah/ubah/nonaktifkan produk, kategori, dan riwayat transaksi.
- **Kasir:** transaksi pelanggan atau umum dengan Tunai/QRIS, antrean pesanan kiosk, terima pembayaran atau tolak pesanan, dan riwayat transaksi sendiri.
- **Pelanggan:** katalog produk dan riwayat pembelian sendiri.
- **Tamu:** pesan tanpa login melalui kiosk; kasir dapat memproses pesanan itu di antrean.

Database awal memuat kategori dan produk contoh, satu transaksi untuk dashboard, serta satu pesanan kiosk menunggu. Harga dan nama item disalin saat transaksi dibuat sehingga riwayat tidak berubah ketika produk diedit. Pesanan yang ditolak tetap tercatat di database, tetapi tidak menjadi transaksi.

Paket demo berjalan lokal pada satu komputer. Ia tidak menyinkronkan data antar komputer; setiap klien mempunyai file SQLite sendiri. Di Linux, aplikasi desktop memerlukan sesi X11/XWayland dan pustaka grafis sistem seperti `libx11-6`, `libice6`, `libsm6`, dan `libfontconfig1` yang biasanya sudah ada pada desktop Ubuntu.

## Pengembangan dengan API dan SQL Server

Source masih memiliki `PosServer`, `PosBackend`, `PosSetup`, dan skrip SQL Server. Jalur ini tidak diperlukan untuk paket demo. Untuk memakai jalur tersebut saat pengembangan, jalankan SQL Server dan API seperti sebelumnya, lalu set `POS_DESKTOP_MODE=server` sebelum membuka `PosDesktop`. Dalam mode server, alamat API dibaca dari `POS_API_URL` atau `pos-client.json`; HTTP hanya diterima pada loopback development.

Backend SQL Server dan mode demo SQLite menggunakan kontrak layanan yang sama. Mode demo adalah implementasi lokal tersendiri, karena query T-SQL lama tidak kompatibel jika hanya connection string diganti ke SQLite.

## Build dan uji

```bash
dotnet build PosDesktop.slnx -c Release
dotnet run --project tests/PosDemo.Tests -c Release
python3 scripts/package_cross_platform.py
```

Hasil paket ada di `artifacts/POS-Desktop-linux-x64.tar.gz` dan `artifacts/POS-Desktop-win-x64.zip`. Script mempublish aplikasi self-contained untuk tiap sistem operasi. Build Windows dari Linux adalah cross-publish; jalankan paket di Windows native untuk uji tampilan akhir.
