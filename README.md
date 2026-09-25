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
- Pilih kualitas langsung di panel browser (resolusi, ukuran nyata, audio
  saja) — unduhan YouTube selalu berakhir sebagai **satu file ter-merge**
  (video+audio) berkat ffmpeg otomatis
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

- **Dari source** (satu-satunya cara saat ini — belum ada rilis biner):
  `dotnet build ifredrixDownloadManager.sln -c Release` (syarat: Windows +
  .NET 8 SDK), lalu jalankan hasilnya di `bin\Release\net8.0-windows\`.
  Paket MSI per-machine (`installer/product.wxs`, butuh WiX v7 + persetujuan
  admin/UAC) dan arsip portabel dibuat dari hasil build tersebut.
- Bila kelak mengunduh rilis jadi, verifikasi dengan `SHA256SUMS.txt`
  pada rilis yang sama.

## Cara penggunaan

1. **Unduh manual**: tombol New Download (tautan `http(s)/ftp`, `magnet:`,
   atau file `.torrent`), atau tempel URL — clipboard berisi tautan
   otomatis ditawarkan. Dialog menampilkan nama, ukuran, dan dukungan
   resume sebelum masuk antrean.
2. **Tangkap dari browser**: muat ekstensi (`BrowserExtension/<nama-browser>`)
   sebagai *unpacked extension* (aturan keamanan browser: tak bisa
   auto-install), pastikan aplikasi berjalan, lalu:
   - klik tombol "⇩ ifredrix" pada video → panel kualitas (resolusi,
     perkiraan ukuran, audio saja) → klik satu baris, berkas masuk
     antrean dan jendela progres terbuka;
   - atau biarkan autodownload browser diambil alih aplikasi
     (menu konteks "Send to ifredrix" juga bisa).
3. **Kelola antrean**: Jeda/Lanjutkan/Batal/Hapus per baris, klik-kolom
   untuk jendela progres, Refresh untuk memperbarui alamat yang basi,
   toggle Tor per unduhan, batas kecepatan global maupun per unduhan.
4. **Saat pertama butuh**: yt-dlp, ffmpeg, dan Tor bundle diunduh otomatis
   sekali dari sumber resmi (lihat bawah) — tanpa instalasi terpisah.
5. **Lainnya**: perencana jadwal + auto-shutdown, asosiasi `.torrent`,
   checksum SHA256/SHA1/MD5, ekspor/impor antrean, pola pengecualian URL.

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

## Komponen pihak ketiga

**Proyek ini bukan fork dari proyek mana pun.** Seluruh kode di repo ini
ditulis untuk aplikasi ini; komponen eksternal dipakai apa adanya dari
rilis/paket resmi (bukan dari fork), sebagian diunduh otomatis saat
pertama dibutuhkan:

| Komponen | Sumber | Lisensi | Dipakai untuk |
|---|---|---|---|
| yt-dlp | `github.com/yt-dlp/yt-dlp` (rilis exe resmi) | Unlicense | daftar kualitas + unduh stream |
| ffmpeg | `github.com/BtbN/FFmpeg-Builds` (cadangan: gyan.dev) | GPL | gabung video+audio, remux |
| Tor expert bundle | `torproject.org` | lisensi Tor/BSD | rute Tor terkelola |
| MonoTorrent 3.0.2 | NuGet (`mono/monotorrent` di GitHub) | MIT | unduhan BitTorrent |
| WiX Toolset v7 | `github.com/wiX-Toolset/wix` | MS-RL | membangun MSI saja (build-time) |
| .NET 8 SDK | Microsoft | MIT | runtime + build |

## Catatan

- Ekstensi browser dipasang manual (aturan keamanan browser).
- `mms://` hanya di-rewrite ke `http://`; protokol MMS biner tidak ditulis ulang.
- Batas kecepatan torrent berlaku per sesi torrent, bukan per file di dalamnya.
