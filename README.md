# ifredrix Download Manager

Download manager untuk Windows (WinForms, .NET 8): antrean multi-koneksi,
tangkap tautan otomatis, dukungan torrent & stream, Tor, dan penjadwal —
dengan tampilan jendela klasik, tema Light/Dark, serta UI Indonesia dan
Inggris.

## Fitur utama

- Antrean unduh multi-koneksi (segmentasi dinamis, HTTP/2, resume, retry)
- Tangkap tautan otomatis dari clipboard (URL biasa, `magnet:`, `.torrent`, YouTube/HLS)
- Torrent & stream (yt-dlp + ffmpeg, auto-fetch sekali saat dibutuhkan)
- Proxy (None/System/Custom + auth), cookie, login situs per-host (Basic auth)
- Tor: Off / External / Managed (batas 4 koneksi HTTP, 2 FTP)
- FTP multi-koneksi + FTPS eksplisit (`ftps://`)
- Perencana jadwal, batas kecepatan global & per-unduhan, auto-shutdown
- Ekspor/impor antrean, riwayat persisten, verifikasi checksum (SHA256/SHA1/MD5)
- Tema Light/Dark + 4 aksen, kolom & toolbar dapat dikustom, UI Indonesia/English
- Asosiasi `.torrent`, pemasang Windows (`installer/`), pola pengecualian URL

## Build

```powershell
dotnet build ifredrixDownloadManager.sln -c Release
```

Syarat: Windows + .NET 8 SDK.

## Pembaruan mandiri

Menu **Help → Check for updates...** membaca manifest dari URL berikut
(dapat diubah di dialog):

```
https://api.github.com/repos/ifredrix/DownloadManager/releases/latest
```

Manifest juga bisa berupa JSON generik: `{"version","url","sha256","notes"}`.
Paket yang diunduh diverifikasi SHA256-nya sebelum dijalankan.

## Catatan

- Ekstensi browser dipasang manual (aturan keamanan browser).
- `mms://` hanya di-rewrite ke `http://`; protokol MMS biner tidak ditulis ulang.
- Batas kecepatan torrent berlaku per sesi torrent, bukan per file di dalamnya.
