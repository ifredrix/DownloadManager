# ifredrix Download Manager — Browser extensions

Two ways to capture: right-clicking a link/video/audio gives a
**"Send to ifredrix Download Manager"** option, and every playable
`<video>` on a page gets a small **"⇩ ifredrix" panel** (a floating download
button) that
sends the direct media URL — or the page URL for stream sites — into the
running desktop app.

The desktop app runs a tiny local HTTP server on `http://127.0.0.1:8795`.
The extension only ever talks to that loopback address, so it cannot leak
URLs to the internet.

## Install

### Google Chrome / Microsoft Edge / Opera
1. Open `chrome://extensions/` (Edge: `edge://extensions/`, Opera: `opera://extensions`).
2. Turn on **Developer mode**.
3. Click **Load unpacked** and select the matching folder inside this directory:
   * Chrome  → `chrome/`
   * Edge    → `edge/`
   * Opera   → `opera/`
4. The extension appears with the name **ifredrix Download Manager**.

### Mozilla Firefox
1. Open `about:debugging#/runtime/this-firefox`.
2. Click **Load Temporary Add-on…**.
3. Pick the file `firefox/manifest.json` inside this directory.
4. The temporary extension works until Firefox restarts. For a permanent
   install, sign the extension via `web-ext sign` or use Firefox Developer
   Edition.

## How it works

* **On startup** the extension sends a heartbeat (`GET /heartbeat?source=…`)
  to the desktop app and then once every 10 seconds.
* **Video panel** (`panel.js` content script) watches for `<video>`
  elements — including ones added later by the page — and overlays a
  download button. Direct media URLs queue as HTTP downloads; stream pages
  queue as background best-MP4 via `yt-dlp` + `ffmpeg` (fetched once into
  the app's tools folder on the first stream download).
* **On context-menu** the extension sends the URL to the desktop app
  (`POST /capture`), which auto-queues it: regular files as HTTP downloads,
  `.torrent`/magnet as torrents, streaming sites as background best-MP4 via `yt-dlp`.
* The status bar of the desktop app shows a small indicator per browser:
  * Green ✓ — extension installed and sending heartbeats
  * Gray ✗ — not detected

## Captured hosts (recognized streaming sites)

Any URL captured this way is auto-routed. For streaming
sites (YouTube, Vimeo, Twitch, TikTok, etc.) the app queues the best MP4 in
the background via `yt-dlp`. Use the dedicated
**Stream URL** button only to pick a specific format manually.