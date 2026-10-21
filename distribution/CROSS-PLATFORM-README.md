# MiKuGoMi POS — demo offline

Pilih paket sesuai sistem operasi. **Tidak perlu memasang SQL Server atau menjalankan API.** Ekstrak seluruh arsip, lalu buka aplikasi.

## Windows x64

Buka `PosDesktop.exe` setelah ZIP diekstrak. Jika ingin shortcut, jalankan `Install-POS-Desktop-Shortcut.ps1` sekali. `Launch-POS-Desktop.cmd` juga tersedia.

## Ubuntu/Linux x64

Dari terminal pada sesi desktop, jalankan `./launch-posdesktop.sh`. `./install-desktop.sh` opsional untuk membuat shortcut. Aplikasi memerlukan X11/XWayland dan pustaka grafis desktop Linux.

## Akun demo

- Admin: `admin` / `admin`
- Kasir: `kasir` / `kasir`
- Pelanggan: `pelanggan` / `pelanggan`
- Pelanggan kedua: `pelanggan2` / `pelanggan2`

SQLite lokal dibuat dan diisi otomatis saat pertama dibuka. Ada produk, satu transaksi, dan satu pesanan kiosk contoh. Perubahan tersimpan pada komputer ini; paket tidak menyinkronkan data ke komputer lain. Lokasi database: `%LOCALAPPDATA%\mikugomi-pos\demo.sqlite3` di Windows atau `${XDG_DATA_HOME:-~/.local/share}/mikugomi-pos/demo.sqlite3` di Linux.

Untuk mulai lagi dari data awal, tutup aplikasi lalu pindahkan file database ke tempat cadangan. Panduan pengembangan SQL Server ada di `README.md`.
