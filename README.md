# ifredrix Download Manager

Download manager untuk Windows (WinForms, .NET 8): antrean multi-koneksi,
tangkap tautan otomatis, dukungan torrent & stream, Tor, dan penjadwal —
dengan tampilan jendela klasik, tema Light/Dark, serta UI Indonesia dan
Inggris.

## Fitur utama

- Antrean unduh multi-koneksi (segmentasi dinamis, HTTP/2, resume, retry)
- Autodownload dari browser apa pun (Chrome, Edge, Opera, Brave, Vivaldi,
  Firefox): klik unduhan diambil alih aplikasi; bila aplikasi tertutup,
  unduhan tetap berjalan di browser
- Tangkap tautan otomatis dari clipboard (URL biasa, `magnet:`, `.torrent`, YouTube/HLS)
- Torrent & stream (yt-dlp + ffmpeg, auto-fetch sekali saat dibutuhkan)
- Proxy (None/System/Custom + auth), cookie, login situs per-host (Basic auth)
- Tor: Off / External / Managed (batas 4 koneksi HTTP, 2 FTP)
- FTP multi-koneksi + FTPS eksplisit (`ftps://`)
- Perencana jadwal, batas kecepatan global & per-unduhan, auto-shutdown
- Ekspor/impor antrean, riwayat persisten, verifikasi checksum (SHA256/SHA1/MD5)
- Tema Light/Dark + 4 aksen, kolom & toolbar dapat dikustom, UI Indonesia/English
- Asosiasi `.torrent`, pola pengecualian URL, ekstensi browser (Chrome, Edge,
  Opera, Brave, Vivaldi, Firefox)

## Instalasi

Unduh dari [Releases](https://github.com/ifredrix/DownloadManager/releases):

- **`.msi`** (disarankan) — pemasang Windows per-user, tanpa admin; pasang lalu
  jalankan dari Start Menu / desktop.
- **`.zip`** — ekstrak, lalu jalankan `ifredrixDownloadManager.exe`
  (self-contained, tanpa perlu .NET terpasang).
- `installer/Install.ps1` tetap tersedia sebagai alternatif baris perintah.

Verifikasi unduhan dengan `SHA256SUMS.txt` pada rilis yang sama.

## Build

```powershell
dotnet build ifredrixDownloadManager.sln -c Release
```

Syarat: Windows + .NET 8 SDK.

## Pembaruan mandiri

Menu **Help → Check for updates...** membaca manifest berikut (dapat diubah
di dialog):

```
https://raw.githubusercontent.com/ifredrix/DownloadManager/main/update/latest.json
```

Manifest berisi `{"version","url","sha256","notes"}`; parser GitHub Releases API
(`https://api.github.com/repos/.../releases/latest`) juga didukung, tetapi tanpa
field SHA256. Paket yang diunduh diverifikasi SHA256-nya sebelum dijalankan.

## Lisensi

MIT License — Copyright (c) 2026 Frederikus Hendra Tingang. Lihat [LICENSE](LICENSE).

## Catatan

- Ekstensi browser dipasang manual (aturan keamanan browser).
- `mms://` hanya di-rewrite ke `http://`; protokol MMS biner tidak ditulis ulang.
- Batas kecepatan torrent berlaku per sesi torrent, bukan per file di dalamnya.
