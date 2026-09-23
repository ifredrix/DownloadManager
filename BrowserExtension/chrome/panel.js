// ifredrix Download Manager - in-page video panel.
// Floating button over each playable video opens a quality/size list fetched
// from the desktop app (yt-dlp -F). Picking a row sends the exact format with
// the URL, so video and audio always land as one merged file.

(function () {
    if (window.__ifrePanelInstalled) return;
    window.__ifrePanelInstalled = true;

    const ext = (typeof chrome !== "undefined" && chrome.runtime) ? chrome
        : (typeof browser !== "undefined" ? browser : null);

    const indonesian = /^id/i.test(navigator.language || "");
    const T = indonesian ? {
        pick: "Pilih kualitas",
        loading: "Memuat daftar kualitas...",
        videoAudio: "Video + audio",
        audioOnly: "Audio saja",
        auto: "Terbaik otomatis (MP4)",
        sent: "Terkirim \u2713",
        failed: "Gagal mengirim",
        close: "Tutup",
        offline: "Aplikasi tidak merespons. Buka ifredrix Download Manager.",
        empty: "Tidak ada pilihan kualitas.",
        retry: "Coba lagi"
    } : {
        pick: "Choose quality",
        loading: "Loading quality list...",
        videoAudio: "Video + audio",
        audioOnly: "Audio only",
        auto: "Best automatic (MP4)",
        sent: "Sent \u2713",
        failed: "Send failed",
        close: "Close",
        offline: "App not responding. Start ifredrix Download Manager.",
        empty: "No quality choices found.",
        retry: "Try again"
    };

    const STYLE_ID = "ifredrix-picker-style";
    if (!document.getElementById(STYLE_ID)) {
        const style = document.createElement("style");
        style.id = STYLE_ID;
        style.textContent = [
            "#ifredrix-picker .ifre-head{display:flex;justify-content:space-between;align-items:center;padding:9px 12px;border-bottom:1px solid rgba(255,255,255,0.14);font-weight:600}",
            "#ifredrix-picker .ifre-close{background:none;border:0;color:#cdd3dc;font-size:13px;cursor:pointer;padding:2px 4px}",
            "#ifredrix-picker .ifre-body{padding:6px}",
            "#ifredrix-picker .ifre-section{font-size:11px;letter-spacing:.4px;text-transform:uppercase;color:#8b93a3;padding:7px 8px 3px}",
            "#ifredrix-picker .ifre-row{display:flex;justify-content:space-between;gap:8px;align-items:center;padding:7px 8px;border-radius:7px;cursor:pointer}",
            "#ifredrix-picker .ifre-row:hover{background:rgba(255,255,255,0.10)}",
            "#ifredrix-picker .ifre-main{font-weight:600}",
            "#ifredrix-picker .ifre-sub{font-size:11px;color:#a9b1c1}",
            "#ifredrix-picker .ifre-size{font-size:12px;color:#dfe5ef;white-space:nowrap;text-align:right}",
            "#ifredrix-picker .ifre-loading,#ifredrix-picker .ifre-error,#ifredrix-picker .ifre-empty{padding:14px 10px;color:#c3cad8}",
            "#ifredrix-picker .ifre-error{color:#ff9b9b}",
            "#ifredrix-picker .ifre-retry{margin:0 10px 12px;padding:5px 10px;border-radius:6px;border:1px solid rgba(255,255,255,.3);background:none;color:#fff;cursor:pointer}",
            "#ifredrix-picker .ifre-row.ifre-busy{opacity:.55;pointer-events:none}",
            "#ifredrix-picker .ifre-row.ifre-ok .ifre-size{color:#7ee2a0}",
            "#ifredrix-picker .ifre-row.ifre-fail .ifre-sub{color:#ff9b9b}"
        ].join("\n");
        document.documentElement.appendChild(style);
    }

    // Media URL for the panel. MSE/blob players (Dailymotion, YouTube ...)
    // expose no http src, so we fall back to the frame URL and then unwrap
    // known embed wrappers into the canonical URL yt-dlp understands
    // (geo.dailymotion.com/player.html?video=ID -> /video/ID).
    function unwrapPageUrl(href) {
        try {
            const u = new URL(href, location.href);
            if (/(^|\.)dailymotion\.com$/i.test(u.hostname)) {
                const id = u.searchParams.get("video");
                if (id && /^[a-zA-Z0-9]+$/.test(id)) {
                    return "https://www.dailymotion.com/video/" + id;
                }
            }
        } catch (_) { /* keep original */ }
        return href;
    }

    function candidateUrl(video) {
        const src = video.currentSrc || video.src || "";
        if (/^https?:\/\//i.test(src)) return unwrapPageUrl(src);
        const source = video.querySelector("source[src]");
        if (source && /^https?:\/\//i.test(source.src)) return unwrapPageUrl(source.src);
        return unwrapPageUrl(location.href);
    }

    // Chrome answers via callback; Firefox may hand back a promise. Support
    // both and never hang the panel: 25 s guard, then report "no answer".
    function sendMessage(payload) {
        return new Promise((resolve) => {
            if (!ext) return resolve(null);
            let done = false;
            const finish = (value) => { if (!done) { done = true; resolve(value); } };
            try {
                const ret = ext.runtime.sendMessage(payload, (response) => {
                    const err = ext.runtime.lastError;
                    finish(err ? null : response);
                });
                if (ret && typeof ret.then === "function") {
                    ret.then(finish).catch(() => finish(null));
                }
                setTimeout(() => finish(null), 25000);
            } catch (_) { finish(null); }
        });
    }

    function fmtSize(bytes) {
        if (!bytes || bytes <= 0) return "";
        const units = ["B", "KB", "MB", "GB"];
        let v = bytes;
        let u = 0;
        while (v >= 1024 && u < units.length - 1) { v /= 1024; u++; }
        const digits = v >= 100 ? 0 : v >= 10 ? 1 : 2;
        return "\u2248 " + v.toFixed(digits) + " " + units[u];
    }

    let card = null;

    function closeCard() {
        if (card) {
            const old = card;
            card = null;
            old.remove();
        }
    }

    function el(tag, className, text) {
        const node = document.createElement(tag);
        if (className) node.className = className;
        if (text != null) node.textContent = text;
        return node;
    }

    async function openPicker(url, anchor) {
        closeCard();

        const node = document.createElement("div");
        node.id = "ifredrix-picker";
        node.style.cssText = [
            "position:fixed", "z-index:2147483647",
            "width:320px", "max-height:60vh", "overflow-y:auto",
            "background:rgba(20,26,36,0.97)", "color:#fff",
            "border:1px solid rgba(255,255,255,0.25)", "border-radius:10px",
            "box-shadow:0 10px 30px rgba(0,0,0,0.45)",
            "font:13px/1.45 -apple-system,'Segoe UI',sans-serif",
            "padding:0"
        ].join(";");
        const rect = anchor && anchor.getBoundingClientRect ? anchor.getBoundingClientRect() : null;
        const top = rect ? Math.max(8, Math.min(innerHeight - 120, rect.bottom + 10)) : 80;
        const left = rect
            ? Math.max(8, Math.min(innerWidth - 330, rect.left))
            : Math.max(8, innerWidth - 340);
        node.style.top = top + "px";
        node.style.left = left + "px";
        node.__url = url;
        node.__anchor = anchor || null;

        const head = el("div", "ifre-head");
        head.appendChild(el("span", null, T.pick));
        const close = el("button", "ifre-close", "\u2715");
        close.title = T.close;
        close.addEventListener("click", (ev) => { ev.stopPropagation(); closeCard(); });
        head.appendChild(close);

        const body = el("div", "ifre-body");
        body.appendChild(el("div", "ifre-loading", T.loading));

        node.appendChild(head);
        node.appendChild(body);
        document.documentElement.appendChild(node);
        card = node;

        const resp = await sendMessage({ type: "ifre-formats", url: url, referer: location.href });
        if (card !== node) return; // user closed it (or reopened) meanwhile

        body.textContent = "";
        renderBody(body, url, anchor, resp);
    }

    function makeRow(opts) {
        const row = el("div", "ifre-row");
        row.dataset.format = opts.format || "";
        const text = el("div");
        text.appendChild(el("div", "ifre-main", opts.main));
        text.appendChild(el("div", "ifre-sub", opts.sub));
        const right = el("div", "ifre-size", opts.size || "");
        row.appendChild(text);
        row.appendChild(right);
        row.addEventListener("click", (ev) => { ev.stopPropagation(); opts.onClick(row); });
        return row;
    }

    function section(body, label) {
        body.appendChild(el("div", "ifre-section", label));
    }

    function sendChoice(url, format, row) {
        row.classList.add("ifre-busy");
        sendMessage({ type: "ifre-capture", url: url, format: format || "", referer: location.href })
            .then((resp) => {
                if (resp && resp.ok) {
                    row.classList.add("ifre-ok");
                    const right = row.querySelector(".ifre-size");
                    if (right) right.textContent = T.sent;
                    setTimeout(closeCard, 800);
                } else {
                    row.classList.remove("ifre-busy");
                    row.classList.add("ifre-fail");
                    const sub = row.querySelector(".ifre-sub");
                    if (sub) {
                        sub.textContent = T.failed +
                            (resp && resp.error ? ": " + resp.error : "");
                    }
                }
            });
    }

    function renderBody(body, url, anchor, resp) {
        if (!resp || resp.ok !== true) {
            body.appendChild(el("div", "ifre-error",
                (resp && resp.error) ? String(resp.error) : T.offline));
            const retry = el("button", "ifre-retry", T.retry);
            retry.addEventListener("click", () => openPicker(url, anchor));
            body.appendChild(retry);
            return;
        }

        const list = Array.isArray(resp.formats) ? resp.formats : [];

        // Quick default: same as before the picker existed.
        body.appendChild(makeRow({
            main: T.auto,
            sub: "",
            size: "",
            format: "",
            onClick: (row) => sendChoice(url, "", row)
        }));

        const videos = list.filter((f) => f && f.kind === "video");
        const audios = list.filter((f) => f && f.kind === "audio");

        if (videos.length) {
            section(body, T.videoAudio);
            for (const f of videos) {
                body.appendChild(makeRow({
                    main: f.label || f.ext || f.id,
                    sub: f.ext || "",
                    size: fmtSize(f.sizeBytes),
                    format: f.id,
                    onClick: (row) => sendChoice(url, f.id, row)
                }));
            }
        }

        if (audios.length) {
            section(body, T.audioOnly);
            for (const f of audios) {
                body.appendChild(makeRow({
                    main: f.ext || T.audioOnly,
                    sub: T.audioOnly + (f.id ? " \u00b7 " + f.id : ""),
                    size: fmtSize(f.sizeBytes),
                    format: f.id,
                    onClick: (row) => sendChoice(url, f.id, row)
                }));
            }
        }

        if (!videos.length && !audios.length) {
            body.appendChild(el("div", "ifre-empty", T.empty));
        }
    }

    const buttons = new Map(); // video -> button

    function makeButton(video) {
        const btn = document.createElement("button");
        btn.id = "ifredrix-panel-button";
        btn.textContent = "\u21E9 ifredrix";
        btn.title = T.pick;
        btn.style.cssText = [
            "position:fixed", "z-index:2147483647",
            "display:none", "cursor:pointer",
            "background:rgba(20,26,36,0.92)", "color:#fff",
            "border:1px solid rgba(255,255,255,0.25)", "border-radius:6px",
            "font:600 12px -apple-system,'Segoe UI',sans-serif",
            "padding:5px 10px", "margin:0"
        ].join(";");
        btn.addEventListener("click", (ev) => {
            ev.stopPropagation();
            ev.preventDefault();
            openPicker(candidateUrl(video), btn);
        });
        document.documentElement.appendChild(btn);
        buttons.set(video, btn);
        return btn;
    }

    function refresh() {
        document.querySelectorAll("video").forEach((video) => {
            const btn = buttons.get(video) || makeButton(video);
            const r = video.getBoundingClientRect();
            const visible = r.width >= 120 && r.height >= 60 &&
                r.bottom > 0 && r.right > 0 &&
                r.top < innerHeight && r.left < innerWidth &&
                getComputedStyle(video).visibility !== "hidden";
            if (!visible) {
                btn.style.display = "none";
                return;
            }
            btn.style.display = "block";
            btn.style.top = Math.max(4, r.top + 8) + "px";
            btn.style.left = Math.max(4, Math.min(innerWidth - 110, r.right - 108)) + "px";
        });
    }

    new MutationObserver(refresh).observe(document.documentElement, {
        childList: true, subtree: true
    });
    addEventListener("scroll", refresh, { passive: true, capture: true });
    addEventListener("resize", refresh);
    setInterval(refresh, 1500);
    refresh();
})();
