# test-tools — meja perancangan mesin pengunduh video sendiri

Folder eksperimen, BUKAN bagian aplikasi. Tidak dibundel ke MSI/publish.

## Tujuan

Menggantikan ketergantungan yt-dlp secara bertahap dengan mesin milik
sendiri (`ifre-engine`), mulai dari lapisan generik yang bekerja tanpa
extractor spesifik situs.

## Arsitektur yang dirancang

```
Lapisan 1 - Temu generik (probe.py, tahap ini)
  HTML -> <video>/<source>, meta og:video, JSON-LD VideoObject,
          URL .mp4/.m3u8/.mpd mentah di markup/JS.
  Hasil: direct file (unduh via HttpDownloader 32-koneksi) atau
         manifest HLS/DASH (teruskan ke Lapisan 2).

Lapisan 2 - Manifest terbuka (berikutnya)
  Parser m3u8 (master -> varian per bandwidth/resolusi) dan
  MPD (AdaptationSet/Representation). Hanya konten TANPA DRM:
  kunci AES-128 dari manifest = perilaku player normal, tetapi
  Widevine/FairPlay/PlayReady TIDAK disentuh (lisensi studio).

Lapisan 3 - Extractor per situs (satu-satu, bila Lapisan 1 gagal)
  Satu modul kecil per situs: endpoint API/JSON tersembunyi yang
  dipakai player situs itu. Contoh pertama yang realistis:
  archive.org (metadata API terbuka).

Lapisan 4 - Integrasi (terakhir)
  ifre-engine menjadi alternatif StreamCapture: panel memakai
  daftar kualitas mesin sendiri bila mampu, fallback ke yt-dlp
  bila tidak. yt-dlp TIDAK dicopot sampai mesin ini paritas.
```

## Aturan main

- Tanpa bypass DRM. Tanpa credential harvesting di luar sesi browser
  yang sudah ada. Tanpa autodownload besar tanpa konfirmasi.
- Setiap skrip di sini stdlib-only (tanpa pip install) agar bisa
  dijalankan di mesin mana pun.
- Setiap klaim ("situs X bisa") wajib dibuktikan dengan output
  eksekusi, bukan teori.

## Isi folder

- `probe.py` — Lapisan 1: temukan kandidat media dari satu URL/berkas.
- `fixtures/sample.html` — fixture deterministik untuk uji lokal.
