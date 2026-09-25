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

// Cookies need the "cookies" permission plus host access to the target;
// any failure degrades to "no cookies" and never blocks the capture.
const cookiesApi =
    (typeof browser !== "undefined" && browser && browser.cookies)
        ? browser.cookies
        : (typeof chrome !== "undefined" && chrome.cookies ? chrome.cookies : null);

// The browser's context for this transfer: its User-Agent plus exactly the
// cookies it would send for these URLs (page first, then the media host).
// Sent ONLY to the local app (127.0.0.1), so the download acts as the same
// session that already plays the video in this browser.
async function pageContext(referer, mediaUrl) {
    const ua = (typeof navigator !== "undefined" && navigator.userAgent) || "";
    const cookies = [];
    if (cookiesApi) {
        const seen = new Set();
        for (const u of [referer, mediaUrl]) {
            if (!u || !/^https?:/i.test(u)) continue;
            try {
                const list = await cookiesApi.getAll({ url: u });
                for (const c of list || []) {
                    const key = (c.domain || "") + "|" + (c.path || "/") + "|" + c.name;
                    if (seen.has(key)) continue;
                    seen.add(key);
                    cookies.push({
                        name: c.name, value: c.value, domain: c.domain || "",
                        path: c.path || "/", hostOnly: !!c.hostOnly,
                        secure: !!c.secure, expires: Math.floor(c.expirationDate || 0)
                    });
                }
            } catch (_) { /* no host permission for this url - skip */ }
        }
    }
    return { ua: ua, cookies: cookies };
}

// Returns true only when the app accepted (or already owns) the URL.
// Offline / excluded / rejected all return false so callers can fall back
// to letting the browser handle the download itself. `format` is the yt-dlp
// selector picked in the quality list ("" = automatic best).
async function send(url, format, referer, ui, sizeBytes) {
    const ctx = await pageContext(referer || "", url);
    const payload = JSON.stringify({
        url: url, source: SOURCE, format: format || "", sizeBytes: sizeBytes || 0,
        referer: referer || "", ua: ctx.ua, cookies: ctx.cookies, ui: !!ui
    });
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

// Asks the app for the quality/size list of a stream URL (yt-dlp -F).
// Resolves to { ok:true, formats:[...] } or { ok:false, error:"..." }.
async function requestFormats(url, referer) {
    const ctx = await pageContext(referer || "", url);
    const payload = JSON.stringify({ url: url, referer: referer || "", ua: ctx.ua, cookies: ctx.cookies });
    const targets = [base];
    const found = await discover();
    if (found && found !== base) targets.push(found);

    let error = "app offline";
    for (const target of targets) {
        try {
            const controller = new AbortController();
            const timer = setTimeout(() => controller.abort(), 20000);
            const r = await fetch(target + "/formats?source=" + SOURCE, {
                method: "POST",
                headers: { "Content-Type": "application/json" },
                body: payload,
                cache: "no-store",
                signal: controller.signal
            });
            clearTimeout(timer);
            if (r.ok) {
                base = target;
                const data = await r.json();
                return { ok: true, formats: (data && data.formats) || [] };
            }
            error = (await r.text().catch(() => "")) || ("HTTP " + r.status);
        } catch (e) {
            error = (e && e.name === "AbortError") ? "timeout" : "app offline";
        }
    }
    return { ok: false, error: error };
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
    send(url, null, info.pageUrl || "");
});

// In-page video panel (panel.js) forwards clicks and quality picks here.
chrome.runtime.onMessage.addListener((msg, sender, sendResponse) => {
    if (!msg || !msg.type) return;

    if (msg.type === "ifre-formats" && msg.url) {
        requestFormats(msg.url, msg.referer)
            .then((r) => { try { sendResponse(r); } catch (_) { } })
            .catch((e) => {
                try { sendResponse({ ok: false, error: String((e && e.message) || e) }); } catch (_) { }
            });
        return true; // keep the channel open for the async answer
    }

    if (msg.type === "ifre-capture" && msg.url) {
        send(msg.url, msg.format, msg.referer, msg.ui, msg.sizeBytes)
            .then((ok) => { try { sendResponse({ ok: ok }); } catch (_) { } })
            .catch(() => { try { sendResponse({ ok: false }); } catch (_) { } });
        return true;
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

        const accepted = await send(url, null, item.referrer || "");
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
