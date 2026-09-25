#!/usr/bin/env python3
"""probe.py - Lapisan 1 mesin sendiri: temukan kandidat media dari satu halaman.

Pemakaian:
    python probe.py <URL-atau-berkas-lokal>

Keluaran: JSON { "direct": [...], "manifest": [...], "embed": [...] }.
  direct   = berkas progresif (.mp4 dkk) -> HttpDownloader 32-koneksi.
  manifest = playlist HLS/DASH (.m3u8/.mpd) -> Lapisan 2 (belum ada).
  embed    = halaman embed/iframe yang butuh Lapisan 3 (extractor situs).

Stdlib-only. Tanpa JS rendering: halaman yang merender player via JS
berat tanpa data di markup akan lapor embed/temuan parsial - jujur,
bukan dipaksakan.
"""
import html.parser
import json
import re
import sys
import urllib.parse
import urllib.request

DIRECT_EXTS = (".mp4", ".m4v", ".mkv", ".webm", ".mov", ".avi", ".flv",
               ".wmv", ".mpg", ".mpeg", ".3gp", ".mp3", ".m4a", ".aac",
               ".ogg", ".opus", ".flac", ".wav")
MANIFEST_EXTS = (".m3u8", ".mpd")


def bagi(url):
    """Pisahkan URL jadi (bersih, jenis) atau (None, None) bila bukan media."""
    if not url or not isinstance(url, str):
        return None, None
    url = url.strip()
    if url.startswith("//"):
        url = "https:" + url
    if not re.match(r"^https?://", url, re.I):
        return None, None
    path = urllib.parse.urlsplit(url).path.lower()
    if path.endswith(MANIFEST_EXTS):
        return url, "manifest"
    if path.endswith(DIRECT_EXTS):
        return url, "direct"
    return None, None


class Pengumpul(html.parser.HTMLParser):
    """Kumpulkan <video>/<source>, meta video, dan iframe dari markup."""

    def __init__(self):
        super().__init__(convert_charrefs=True)
        self.media = []    # (url, dari)
        self.bingkai = []  # src iframe
        self._dalam_ld = False
        self._buf_ld = []

    def handle_starttag(self, tag, attrs):
        a = dict(attrs)
        if tag in ("video", "source") and a.get("src"):
            self.media.append((a["src"], tag))
        if tag == "meta" and a.get("content"):
            prop = (a.get("property") or a.get("name") or "").lower()
            if prop in ("og:video", "og:video:url", "og:video:secure_url",
                        "twitter:player:stream"):
                self.media.append((a["content"], "meta:" + prop))
        if tag == "iframe" and a.get("src"):
            self.bingkai.append(a["src"])
        if tag == "script" and a.get("type", "").lower() == "application/ld+json":
            self._dalam_ld = True
            self._buf_ld = []

    def handle_data(self, data):
        if self._dalam_ld:
            self._buf_ld.append(data)

    def handle_endtag(self, tag):
        if tag == "script" and self._dalam_ld:
            self._dalam_ld = False
            try:
                doc = json.loads("".join(self._buf_ld))
            except (ValueError, TypeError):
                return
            docs = doc if isinstance(doc, list) else [doc]
            for d in docs:
                if not isinstance(d, dict):
                    continue
                if "VideoObject" not in json.dumps(d.get("@type", "")):
                    continue
                for kunci in ("contentUrl", "embedUrl"):
                    val = d.get(kunci)
                    urls = val if isinstance(val, list) else [val]
                    for u in urls:
                        if u:
                            self.media.append((u, "json-ld:" + kunci))


def gali_teks_js(html):
    """URL media mentah yang tertanam di JavaScript/inline JSON."""
    pola = re.compile(
        r"""(?:"|')((?:https?:)?//[^"'\\\s<>]+?\.(?:m3u8|mpd|mp4|m4v|webm|mov|mkv|m4a|mp3)(?:\?[^"'\\\s<>]*)?)(?:"|')""",
        re.I)
    return [("js:" + m.group(1), m.group(1)) for m in pola.finditer(html)]


def selidiki(html, dasar=""):
    """Inti Lapisan 1: kembalikan dict kandidat terdedup, urutan stabil."""
    parser = Pengumpul()
    parser.feed(html)
    mentah = [(u, s) for (u, s) in parser.media]
    mentah += [(u, "js") for (_, u) in gali_teks_js(html)]
    langsung, manifest, tanam = [], [], []
    lihat = set()
    for url, _sumber in mentah:
        url = url.strip()
        if url.startswith("//"):
            url = "https:" + url  # relatif-protokol -> https sebelum urljoin.
        penuh = urllib.parse.urljoin(dasar, url)
        bersih, jenis = bagi(penuh)
        if not bersih or bersih in lihat:
            continue
        lihat.add(bersih)
        (langsung if jenis == "direct" else manifest).append(bersih)
    for src in parser.bingkai:
        penuh = urllib.parse.urljoin(dasar, src)
        if re.match(r"^https?://", penuh, re.I) and penuh not in lihat:
            lihat.add(penuh)
            tanam.append(penuh)
    return {"direct": langsung, "manifest": manifest, "embed": tanam}


def ambil(sumber):
    """Baca dari berkas lokal atau unduh via HTTP(S) dengan UA browser."""
    try:
        with open(sumber, "r", encoding="utf-8") as f:
            return f.read(), "file://" + sumber
    except (OSError, UnicodeError):
        pass
    req = urllib.request.Request(
        sumber, headers={"User-Agent": "Mozilla/5.0 (Windows NT 10.0; Win64; x64)"})
    with urllib.request.urlopen(req, timeout=25) as r:
        charset = r.headers.get_content_charset() or "utf-8"
        return r.read().decode(charset, errors="replace"), r.url


def main(argv):
    if len(argv) != 2:
        print("pakai: python probe.py <URL-atau-berkas-lokal>", file=sys.stderr)
        return 2
    try:
        html, final = ambil(argv[1])
    except Exception as ex:
        print(json.dumps({"error": str(ex)}))
        return 1
    hasil = selidiki(html, dasar=final)
    hasil["page"] = final
    print(json.dumps(hasil, indent=2, ensure_ascii=False))
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
