// ifredrix Download Manager - Firefox capture worker
// Talks to the local capture server bundled with the desktop app.

const PORTS = [8795, 8796, 8797, 8798];
const SOURCE = "firefox";

let base = "http://127.0.0.1:8795";

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
browser.runtime.onInstalled.addListener(() => {
    browser.menus.create({
        id: "send-to-ifredrix",
        title: "Send to ifredrix Download Manager",
        contexts: ["link", "video", "audio"]
    });
});

browser.menus.onClicked.addListener((info) => {
    const url = info.linkUrl || info.srcUrl || info.pageUrl;
    if (!url) return;
    send(url);
});

// In-page video panel (panel.js) forwards clicks here.
browser.runtime.onMessage.addListener((msg) => {
    if (msg && msg.type === "ifre-capture" && msg.url) {
        send(msg.url);
    }
});
