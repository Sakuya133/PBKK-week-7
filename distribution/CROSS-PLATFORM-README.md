# POS Desktop — panduan paket

Paket ini menargetkan **Windows x64** atau **Ubuntu/Linux x64** dan berisi aplikasi Avalonia self-contained, launcher, setup tool, SQL scripts, serta API lokal opsional. SQL Server tidak disertakan dan harus tersedia sebagai service lokal/jaringan.

## Ubuntu/Linux

Prasyarat sistem: sesi desktop X11 atau XWayland dan paket `libx11-6`, `libice6`, `libsm6`, `libfontconfig1`. `curl` diperlukan untuk API lokal opsional; `x11-utils` memberi pengecekan display awal:

~~~bash
sudo apt install libx11-6 libice6 libsm6 libfontconfig1 curl x11-utils
~~~

Ekstrak TAR, lalu di terminal desktop Ubuntu jalankan:

~~~bash
tar -xzf POS-Desktop-linux-x64.tar.gz
cd POS-Desktop-linux-x64
./install-desktop.sh
./launch-posdesktop.sh
~~~

Shortcut ada di `~/.local/share/applications/posdesktop.desktop`. Perintah langsung yang dijalankan launcher: `./launch-posdesktop.sh`. Launcher menangani path dengan spasi. Config API ada di `~/.config/posdesktop/pos-client.json` (atau `$XDG_CONFIG_HOME/posdesktop/pos-client.json`); log ada di `~/.local/state/posdesktop/logs/posdesktop.log`.

Avalonia memakai X11/XWayland default. Jika `XOpenDisplay failed`, jalankan dari terminal sesi desktop yang sama. Wayland perlu XWayland dan `DISPLAY`; launcher tidak menebak DISPLAY maupun mengubah otorisasi `XAUTHORITY`. SSH/headless tanpa X11 forwarding yang diotorisasi tidak dapat membuka jendela.

## Windows

Ekstrak `POS-Desktop-win-x64.zip` ke folder biasa. Jalankan `Launch-POS-Desktop.cmd`; executable utama juga tersedia sebagai `PosDesktop.exe`. Path dengan spasi didukung. Config API: `%APPDATA%\posdesktop\pos-client.json`; log: `%LOCALAPPDATA%\POS Desktop\Logs\posdesktop.log`.

## API dan database

Atur alamat API dari file konfigurasi atau environment `POS_API_URL`. API yang berada di jaringan harus menggunakan HTTPS. Untuk menjalankan API dari paket yang sama, aktifkan launcher opsional dan sediakan connection string hanya di host API:

Linux:

~~~bash
export POS_CONNECTION_STRING='Server=localhost;Database=PosDesktop;User ID=sa;Password=GANTI_PASSWORD;Encrypt=true;TrustServerCertificate=true'
POS_START_LOCAL_API=true ./launch-posdesktop.sh
~~~

Windows PowerShell: set `POS_START_LOCAL_API=1` dan `POS_CONNECTION_STRING`, lalu jalankan `Launch-POS-Desktop.cmd`. API lokal dibatasi ke loopback dan development HTTP. Launcher menunggu health check, hanya menghentikan proses yang dimulainya, dan tetap membuka login jika API gagal. Connection string tidak diteruskan ke proses desktop.

Setup schema dan seed dijalankan dari root folder paket di terminal tepercaya setelah mengatur `POS_CONNECTION_STRING`:

~~~bash
./Setup/PosSetup --demo
~~~

Pada Windows jalankan `.\Setup\PosSetup.exe --demo` dari PowerShell. Contoh akun demo: Admin `admin` / `Admin123!`, Kasir `kasir` / `Kasir123!`, Pelanggan `pelanggan` / `Pelanggan123!`, Pelanggan kedua `pelanggan2` / `Pelanggan234!`. Hanya untuk database pengembangan.

## Pembuatan ulang dan verifikasi

Dari source repository, `python3 scripts/package_cross_platform.py` mempublish client/API/setup self-contained untuk `linux-x64` dan `win-x64`, lalu membuat TAR/ZIP. Build Windows dari Linux adalah cross-publish; untuk memastikan runtime Windows, buka paket di Windows native. SQL integration memerlukan SQL Server terpisah.
