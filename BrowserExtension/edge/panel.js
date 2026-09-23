// ifredrix Download Manager - in-page video panel.
// Shows a small "Download" button over each playable video (floating panel).
// Clicking sends the direct media URL when available, otherwise the page URL
// (the desktop app auto-routes stream pages to yt-dlp).

(function () {
    if (window.__ifrePanelInstalled) return;
    window.__ifrePanelInstalled = true;

    const ext = (typeof chrome !== "undefined" && chrome.runtime) ? chrome
        : (typeof browser !== "undefined" ? browser : null);

    function candidateUrl(video) {
        const src = video.currentSrc || video.src || "";
        if (/^https?:\/\//i.test(src)) return src;
        const source = video.querySelector("source[src]");
        if (source && /^https?:\/\//i.test(source.src)) return source.src;
        return location.href;
    }

    function send(url) {
        if (!ext) return;
        try {
            const p = ext.runtime.sendMessage({ type: "ifre-capture", url: url });
            if (p && p.catch) p.catch(() => {});
        } catch (_) { /* extension reloaded */ }
    }

    const buttons = new Map(); // video -> button

    function makeButton(video) {
        const btn = document.createElement("button");
        btn.textContent = "\u21E9 ifredrix";
        btn.title = "Download with ifredrix Download Manager";
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
            send(candidateUrl(video));
            const old = btn.textContent;
            btn.textContent = "\u2713 Sent";
            setTimeout(() => { btn.textContent = old; }, 1500);
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
