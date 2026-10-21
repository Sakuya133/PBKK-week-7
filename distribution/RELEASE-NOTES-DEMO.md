# MiKuGoMi POS — demo offline

Unduh paket sesuai sistem operasi, ekstrak seluruh isi arsip, lalu buka aplikasi:

- Windows x64: `POS-Desktop-win-x64.zip` → `PosDesktop.exe`
- Ubuntu/Linux x64: `POS-Desktop-linux-x64.tar.gz` → `launch-posdesktop.sh`

Tidak perlu memasang SQL Server atau menjalankan API. SQLite lokal dan data contoh dibuat otomatis saat pertama dibuka.

Login demo: `admin/admin`, `kasir/kasir`, atau `pelanggan/pelanggan`. Tamu dapat memesan tanpa login. Kasir dapat menerima pembayaran atau menolak pesanan di antrean kiosk.

Perbaikan: membuka produk di panel admin lalu menekan Simpan tanpa mengubahnya tidak lagi menaikkan harga.

Data demo disimpan hanya di komputer pengguna dan tidak disinkronkan antar perangkat. Panduan lengkap ada di `README.md` dalam paket.
