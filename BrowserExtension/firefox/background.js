// ifredrix Download Manager - Firefox capture worker
// Talks to the local capture server bundled with the desktop app.

const PORTS = [8795, 8796, 8797, 8798];
const SOURCE = "firefox";

let base = "http://127.0.0.1:8795";

// Chromium exposes chrome.* (promises in MV3); Firefox exposes browser.*
// (promises). Prefer whichever exists so both runtimes can await calls.
const downloadsApi =
    (typeof browser !== "undefined" && browser && browser.downloads)
        ? browser.downloads
        : (typeof chrome !== "undefined" && chrome.downloads ? chrome.downloads : null);

// The app falls back to the next free port when 8795 is already taken (for
// example a second instance running), so probe the whole range instead of
// hardcoding a single port.
async function discover() {
    for (const port of PORTS) {
        try {
            const r = await fetch("http://127.0.0.1:" + port + "/status", { cache: "no-store" });
            if (r.ok) return "http://127.0.0.1:" + port;
        } catch (_) { /* port closed, try next */ }
    }
    return null;
}

async function heartbeat() {
    try {
        const r = await fetch(base + "/heartbeat?source=" + SOURCE, { cache: "no-store" });
        if (r.ok) return;
    } catch (_) { /* fall through and rediscover */ }

    const found = await discover();
    if (found) {
        base = found;
        try { await fetch(base + "/heartbeat?source=" + SOURCE, { cache: "no-store" }); } catch (_) { }
    }
}

// Returns true only when the app accepted (or already owns) the URL.
// Offline / excluded / rejected all return false so callers can fall back
// to letting the browser handle the download itself.
async function send(url) {
    const payload = JSON.stringify({ url: url, source: SOURCE });
    const targets = [base];
    const found = await discover();
    if (found && found !== base) targets.push(found);

    for (const target of targets) {
        try {
            const r = await fetch(target + "/capture", {
                method: "POST",
                headers: { "Content-Type": "application/json" },
                body: payload
            });
            if (r.ok) { base = target; return true; }
        } catch (_) { /* try next */ }
    }
    return false;
}

heartbeat();
setInterval(heartbeat, 10000);

// Add the "Send to ifredrix" context-menu entry on install/update.
chrome.runtime.onInstalled.addListener(() => {
    chrome.contextMenus.create({
        id: "send-to-ifredrix",
        title: "Send to ifredrix Download Manager",
        contexts: ["link", "video", "audio"]
    });
});

chrome.contextMenus.onClicked.addListener((info) => {
    const url = info.linkUrl || info.srcUrl || info.pageUrl;
    if (!url) return;
    send(url);
});

// In-page video panel (panel.js) forwards clicks here.
chrome.runtime.onMessage.addListener((msg) => {
    if (msg && msg.type === "ifre-capture" && msg.url) {
        send(msg.url);
    }
});

// --- Autodownload: take over every browser download -------------------------
// The moment the browser starts an http(s) download we pause it, hand the
// URL to the app, and then either cancel+erase it (app queued it) or resume
// it (app offline / URL excluded / app rejected it). No download is ever
// dropped: the fallback always returns control to the browser.
if (downloadsApi && downloadsApi.onCreated) {
    downloadsApi.onCreated.addListener((item) => {
        void interceptDownload(item);
    });
}

async function interceptDownload(item) {
    try {
        if (!item || typeof item.id !== "number") return;
        const url = item.finalUrl || item.url;
        if (!url || !/^https?:/i.test(url)) return;          // blob:/data:/magnet: are not ours
        if (item.state && item.state !== "in_progress") return;   // already finished - too late

        // Hold it so nothing completes while we ask the app.
        let held = false;
        try { await downloadsApi.pause(item.id); held = true; } catch (_) { }

        const accepted = await send(url);
        if (accepted) {
            try { await downloadsApi.cancel(item.id); } catch (_) { }
            try { await downloadsApi.erase({ id: item.id }); } catch (_) { }
        } else if (held) {
            // The app is offline, excluded the URL, or rejected it:
            // hand the download straight back to the browser.
            try { await downloadsApi.resume(item.id); } catch (_) { }
        }
    } catch (_) { /* never break the browser */ }
}
