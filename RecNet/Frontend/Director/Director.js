const BASE = "/vannet/api/director";
const SESSION_DURATION_MS = 45 * 60 * 1000;
const REDIRECT_URL = "/vannet/Director";

let currentPlayer = null;
let currentRoom = null;
let allSkins = [];
let sessionToken = null;
let sessionExpiry = 0;
let timerInterval = null;
let sessionWs = null;

function safeParse(text) {
    const fixed = text.replace(/(?<!["'])(-?[0-9]{15,})(?=[ \t]*[,\]\}\r\n])/g, '"$1"');
    return JSON.parse(fixed);
}

(function checkAuth() {
    sessionToken = sessionStorage.getItem("director_token");
    const expiry = parseInt(sessionStorage.getItem("director_token_expiry") || "0", 10);

    if (!sessionToken || Date.now() > expiry) {
        sessionStorage.removeItem("director_token");
        sessionStorage.removeItem("director_token_expiry");
        window.location.href = REDIRECT_URL;
        return;
    }

    sessionExpiry = expiry;
    const isMobile = /Mobi|Android|iPhone|iPad|iPod|BlackBerry|IEMobile|Opera Mini/i.test(navigator.userAgent) || window.innerWidth <= 768;
    const app = document.getElementById("app");
    if (isMobile) app.classList.add("mobile");
    app.style.display = "flex";
    initApp();
    startSessionTimer();
    openSessionWs();
})();

function startSessionTimer() {
    if (timerInterval) clearInterval(timerInterval);
    timerInterval = setInterval(() => {
        const remaining = sessionExpiry - Date.now();
        if (remaining <= 0) {
            clearInterval(timerInterval);
            forceLogout("Your session has expired. Please re-authorize.");
            return;
        }
        const mins = Math.floor(remaining / 60000);
        const secs = Math.floor((remaining % 60000) / 1000);
        const display = `${mins}:${secs.toString().padStart(2, "0")}`;
        const v1 = document.getElementById("timer-val");
        const v2 = document.getElementById("session-big-timer");
        if (v1) v1.textContent = display;
        if (v2) v2.textContent = display;
    }, 1000);
}

function extendSession() {
    sessionExpiry = Date.now() + SESSION_DURATION_MS;
    sessionStorage.setItem("director_token_expiry", sessionExpiry);
}

function forceLogout(msg) {
    clearInterval(timerInterval);
    if (sessionWs) { try { sessionWs.close(); } catch (_) { } }
    sessionStorage.removeItem("director_token");
    sessionStorage.removeItem("director_token_expiry");
    const overlay = document.createElement("div");
    overlay.style.cssText = "position:fixed;inset:0;background:rgba(0,0,0,0.92);z-index:999999;display:flex;align-items:center;justify-content:center;flex-direction:column;gap:16px;color:#e8e8f0;font-family:system-ui;text-align:center;padding:24px";
    overlay.innerHTML = `<div style="font-size:36px">⬡</div><div style="font-size:16px;font-weight:600">${msg || "Session ended."}</div><div style="color:#7a7a9a;font-size:13px">Redirecting to authorization…</div>`;
    document.body.appendChild(overlay);
    setTimeout(() => { window.location.href = REDIRECT_URL; }, 2000);
}

function openSessionWs() {
    if (!sessionToken) return;
    const proto = location.protocol === "https:" ? "wss:" : "ws:";
    const wsUrl = `${proto}//${location.host}/vannet/ws/director-session?token=${encodeURIComponent(sessionToken)}`;
    try {
        sessionWs = new WebSocket(wsUrl);
        sessionWs.onmessage = (e) => {
            try {
                const data = JSON.parse(e.data);
                if (data.type === "session_expired" || data.type === "force_logout") {
                    forceLogout(data.message || "Session was terminated by the server.");
                }
            } catch (_) { }
        };
        sessionWs.onclose = () => {
            if (Date.now() < sessionExpiry) {
                setTimeout(openSessionWs, 3000);
            }
        };
    } catch (_) { }
}

function gv(obj, ...keys) {
    for (const k of keys) {
        if (obj[k] !== undefined && obj[k] !== null) return obj[k];
        const lower = k.charAt(0).toLowerCase() + k.slice(1);
        if (obj[lower] !== undefined && obj[lower] !== null) return obj[lower];
        const upper = k.charAt(0).toUpperCase() + k.slice(1);
        if (obj[upper] !== undefined && obj[upper] !== null) return obj[upper];
    }
    return null;
}

function initApp() {
    document.querySelectorAll(".nav-btn").forEach(btn => {
        btn.addEventListener("click", () => {
            document.querySelectorAll(".nav-btn").forEach(b => b.classList.remove("active"));
            document.querySelectorAll(".panel").forEach(p => p.classList.remove("active"));
            btn.classList.add("active");
            document.getElementById("panel-" + btn.dataset.panel).classList.add("active");
            extendSession();
        });
    });

    setupPlayerPanel();
    setupRoomPanel();
    setupImagesPanel();
    setupDatabasePanel();
    setupModerationPanel();
    setupModBoardPanel();
    setupAntiCheatPanel();
    setupServerPanel();
    setupGitPanel();
    setupSessionPanel();
    setupRoomImport();
    loadSkins();
    renderSessionInfo();
    initUploadPreviews();
}

function setupGitPanel() {
    const diffBtn = document.getElementById("git-diff");
    const statusBtn = document.getElementById("git-status");
    const pushBtn = document.getElementById("git-push");
    const log = document.getElementById("git-log");
    if (!diffBtn || !statusBtn || !pushBtn || !log) return;

    diffBtn.addEventListener("click", async () => {
        diffBtn.disabled = true;
        log.value = "Running git diff HEAD...";

        const res = await api("/git/diff");
        if (!res.ok) {
            const err = typeof res.data === "string"
                ? res.data
                : res.data?.error || "Failed to run git diff.";
            log.value = err;
            toast("Git diff failed", "err");
            diffBtn.disabled = false;
            return;
        }

        const exitCode = (typeof res.data?.exitCode === "number") ? res.data.exitCode : null;
        const output = res.data?.output;
        const body = (typeof output === "string" && output.length > 0)
            ? output
            : "No diff output.";
        log.value = (exitCode !== null ? `Exit code: ${exitCode}\n\n` : "") + body;
        diffBtn.disabled = false;
    });

    statusBtn.addEventListener("click", async () => {
        statusBtn.disabled = true;
        log.value = "Running git status -sb...";

        const res = await api("/git/status");
        if (!res.ok) {
            const err = typeof res.data === "string"
                ? res.data
                : res.data?.error || "Failed to run git status.";
            log.value = err;
            toast("Git status failed", "err");
            statusBtn.disabled = false;
            return;
        }

        const exitCode = (typeof res.data?.exitCode === "number") ? res.data.exitCode : null;
        const output = res.data?.output;
        const body = (typeof output === "string" && output.length > 0)
            ? output
            : "No status output.";
        log.value = (exitCode !== null ? `Exit code: ${exitCode}\n\n` : "") + body;
        statusBtn.disabled = false;
    });
    pushBtn.addEventListener("click", async () => {
        pushBtn.disabled = true;
        log.value = "Running git commit and push...";

        const res = await api("/git/push");
        if (!res.ok) {
            const err = typeof res.data === "string"
                ? res.data
                : res.data?.error || "Failed to run git push.";
            log.value = err;
            toast("Git push failed", "err");
            pushBtn.disabled = false;
            return;
        }

        const exitCode = (typeof res.data?.exitCode === "number") ? res.data.exitCode : null;
        const output = res.data?.output;
        const body = (typeof output === "string" && output.length > 0)
            ? output
            : "No push output.";
        log.value = (exitCode !== null ? `Exit code: ${exitCode}\n\n` : "") + body;
        pushBtn.disabled = false;
    });
}

async function api(path, method = "GET", body = null) {
    extendSession();
    const opts = {
        method,
        headers: {
            "Content-Type": "application/json",
            "X-Director-Token": sessionToken || ""
        }
    };
    if (body) opts.body = JSON.stringify(body);
    try {
        const res = await fetch(BASE + path, opts);
        if (res.status === 401 || res.status === 403) {
            forceLogout("Your session is no longer valid.");
            return { ok: false, status: res.status, data: null };
        }
        const text = await res.text();
        try { return { ok: res.ok, status: res.status, data: safeParse(text) }; }
        catch { return { ok: res.ok, status: res.status, data: text }; }
    } catch (e) {
        return { ok: false, status: 0, data: e.message };
    }
}

async function apiUpload(path, formData) {
    extendSession();
    try {
        const res = await fetch(BASE + path, {
            method: "POST",
            headers: { "X-Director-Token": sessionToken || "" },
            body: formData
        });
        if (res.status === 401 || res.status === 403) {
            forceLogout("Your session is no longer valid.");
            return { ok: false, status: res.status, data: null };
        }
        const text = await res.text();
        try { return { ok: res.ok, status: res.status, data: safeParse(text) }; }
        catch { return { ok: res.ok, status: res.status, data: text }; }
    } catch (e) {
        return { ok: false, status: 0, data: e.message };
    }
}

function toast(msg, type = "info") {
    const el = document.createElement("div");
    el.className = "toast" + (type === "err" ? " toast-err" : type === "ok" ? " toast-ok" : "");
    el.textContent = msg;
    document.getElementById("toast-container").appendChild(el);
    requestAnimationFrame(() => el.classList.add("show"));
    setTimeout(() => { el.classList.remove("show"); setTimeout(() => el.remove(), 300); }, 3200);
}

function showConfirm(title, body, cb) {
    document.getElementById("modal-title").textContent = title;
    document.getElementById("modal-body").textContent = body;
    document.getElementById("modal-overlay").style.display = "flex";
    const yes = document.getElementById("modal-confirm");
    const no = document.getElementById("modal-cancel");
    const cleanup = () => {
        document.getElementById("modal-overlay").style.display = "none";
        yes.replaceWith(yes.cloneNode(true));
        no.replaceWith(no.cloneNode(true));
    };
    document.getElementById("modal-confirm").addEventListener("click", () => { cleanup(); cb(); });
    document.getElementById("modal-cancel").addEventListener("click", cleanup);
}

function infoGrid(obj, containerId) {
    const el = document.getElementById(containerId);
    el.innerHTML = "";
    for (const [k, v] of Object.entries(obj)) {
        const item = document.createElement("div");
        item.className = "info-item";
        item.innerHTML = `<div class="info-label">${k}</div><div class="info-val">${v ?? "-"}</div>`;
        el.appendChild(item);
    }
}

function pfpUrl(img) { return img ? `/imageserver/${img}` : "/imageserver/DefaultPFP.png"; }
function roomImgUrl(img) { return img ? `/imageserver/${img}` : "/imageserver/DefaultRoomImage.png"; }

function setupDetailTabs(containerSelector) {
    document.querySelectorAll(containerSelector + " .dtab").forEach(tab => {
        tab.addEventListener("click", () => {
            const parent = tab.closest(".detail-pane");
            parent.querySelectorAll(".dtab").forEach(t => t.classList.remove("active"));
            parent.querySelectorAll(".dtab-content").forEach(c => c.classList.remove("active"));
            tab.classList.add("active");
            document.getElementById(tab.dataset.tab).classList.add("active");
        });
    });
}

function renderSessionInfo() {
    const grid = document.getElementById("session-info-grid");
    if (!grid) return;
    infoGrid({
        "Token": sessionToken ? sessionToken.substring(0, 20) + "…" : "-",
        "Expires": new Date(sessionExpiry).toLocaleTimeString(),
        "Duration": "45 minutes"
    }, "session-info-grid");
}

function wireUploadPreview(inputId, previewId, isCircle) {
    const input = document.getElementById(inputId);
    const container = document.getElementById(previewId);
    if (!input || !container) return;

    input.addEventListener("change", () => {
        const file = input.files[0];
        if (!file) { container.style.display = "none"; return; }
        const url = URL.createObjectURL(file);
        container.style.display = "flex";
        const img = container.querySelector("img");
        const strong = container.querySelector("strong");
        const span = container.querySelector(".upload-preview-meta span");
        img.src = url;
        img.style.borderRadius = isCircle ? "50%" : "var(--radius-sm)";
        strong.textContent = file.name;
        span.textContent = (file.size / 1024).toFixed(1) + " KB · " + file.type;
    });
}

function initUploadPreviews() {
    const pfpInput = document.getElementById("pa-pfp-upload");
    if (pfpInput && !document.getElementById("pfp-upload-preview")) {
        pfpInput.insertAdjacentHTML("afterend", `
      <div class="upload-preview" id="pfp-upload-preview" style="display:none">
        <img src="" alt="preview">
        <div class="upload-preview-meta">
          <strong></strong>
          <span></span>
          <div style="color:var(--accent2);font-size:11px;margin-top:2px">Ready to upload</div>
        </div>
      </div>`);
        wireUploadPreview("pa-pfp-upload", "pfp-upload-preview", true);
    }

    const roomInput = document.getElementById("ra-image-upload");
    if (roomInput && !document.getElementById("room-img-upload-preview")) {
        roomInput.insertAdjacentHTML("afterend", `
      <div class="upload-preview" id="room-img-upload-preview" style="display:none">
        <img src="" alt="preview">
        <div class="upload-preview-meta">
          <strong></strong>
          <span></span>
          <div style="color:var(--accent2);font-size:11px;margin-top:2px">Ready to upload</div>
        </div>
      </div>`);
        wireUploadPreview("ra-image-upload", "room-img-upload-preview", false);
    }
}

function setupSessionPanel() {
    document.getElementById("session-extend-btn").addEventListener("click", () => {
        extendSession();
        renderSessionInfo();
        toast("Session extended by 45 minutes", "ok");
    });

    document.getElementById("session-logout-btn").addEventListener("click", () => {
        showConfirm("Sign Out", "End your session and return to the authorization page?", () => {
            forceLogout("Signed out successfully.");
        });
    });
}

function setupPlayerPanel() {
    document.getElementById("player-search-btn").addEventListener("click", playerSearch);
    document.getElementById("player-search-input").addEventListener("keydown", e => { if (e.key === "Enter") playerSearch(); });
    document.getElementById("player-list-all-btn").addEventListener("click", playerListAll);
    document.getElementById("player-list-devs-btn").addEventListener("click", playerListDevelopers);
    document.getElementById("player-list-mods-btn").addEventListener("click", playerListModerators);
    document.getElementById("pd-close").addEventListener("click", () => {
        document.getElementById("player-detail").style.display = "none";
        currentPlayer = null;
    });

    document.getElementById("player-create-toggle-btn").addEventListener("click", () => {
        const section = document.getElementById("player-create-section");
        section.style.display = section.style.display === "none" ? "block" : "none";
    });
    document.getElementById("pc-create-btn").addEventListener("click", createPlayer);

    setupDetailTabs("#player-detail");
    setupPlayerActions();
}

async function createPlayer() {
    const idInput = document.getElementById("pc-player-id");
    const playerId = parseInt(idInput.value, 10);
    if (!playerId || playerId <= 0) { toast("Enter a valid Player ID", "err"); return; }

    const username = document.getElementById("pc-username").value.trim();
    const isJunior = document.getElementById("pc-junior").checked;

    const res = await api("/players/create", "POST", {
        PlayerId: playerId,
        Username: username || null,
        IsJunior: isJunior
    });

    if (!res.ok) {
        toast((res.data && res.data.error) || "Failed to create player", "err");
        return;
    }

    toast(`Created player ${res.data.playerId} (${res.data.username})`, "ok");
    idInput.value = "";
    document.getElementById("pc-username").value = "";
    document.getElementById("pc-junior").checked = false;
    document.getElementById("player-create-section").style.display = "none";
    loadPlayerDetail(res.data.playerId);
}

async function playerSearch() {
    const q = document.getElementById("player-search-input").value.trim();
    if (!q) return;
    extendSession();
    try {
        const res = await fetch(`/acc/account/search?name=${encodeURIComponent(q)}`, {
            headers: { "X-Director-Token": sessionToken || "" }
        });
        if (!res.ok) { toast("Search failed", "err"); return; }
        const data = await res.json();
        renderPlayerResults(data.Results || data.results || data);
    } catch (e) {
        toast("Search failed", "err");
    }
}

async function playerListAll() {
    const res = await api("/players/search?take=100");
    if (!res.ok) { toast("Failed to load players", "err"); return; }
    renderPlayerResults(res.data.Results || res.data.results || res.data);
}

async function playerListDevelopers() {
    const res = await api("/players/list-developers");
    if (!res.ok) { toast("Failed to load developers", "err"); return; }
    renderPlayerResults(res.data.Results || res.data.results || res.data);
}

async function playerListModerators() {
    const res = await api("/players/list-moderators");
    if (!res.ok) { toast("Failed to load moderators", "err"); return; }
    renderPlayerResults(res.data.Results || res.data.results || res.data);
}

function renderPlayerResults(players) {
    const el = document.getElementById("player-results");
    el.innerHTML = "";
    if (!players || players.length === 0) {
        el.innerHTML = '<div class="empty-state">No players found.</div>';
        return;
    }
    const grid = document.createElement("div");
    grid.className = "results-grid";
    players.forEach(p => {
        const id = gv(p, "accountId", "playerId", "PlayerId", "AccountId");
        const name = gv(p, "displayName", "username", "DisplayName", "Username") || "Unknown";
        const username = gv(p, "username", "Username") || "?";
        const img = gv(p, "profileImage", "ProfileImage");
        const isOnline = gv(p, "isOnline") || gv(p, "heartbeat")?.isOnline || false;
        const level = gv(p, "level", "Level") || 1;

        const card = document.createElement("div");
        card.className = "grid-card";
        card.innerHTML = `
      <img class="grid-card-thumb" src="${pfpUrl(img)}" onerror="this.src='/imageserver/DefaultPFP.png'" style="border-radius:0">
      <div class="grid-card-badge"><span class="status-dot ${isOnline ? "status-dot-online" : "status-dot-offline"}"></span></div>
      <div class="grid-card-body">
        <div class="grid-card-name">${name}</div>
        <div class="grid-card-sub">@${username} · Lv ${level}</div>
        <div class="grid-card-sub">ID: ${id}</div>
      </div>`;
        card.addEventListener("click", () => loadPlayerDetail(id));
        grid.appendChild(card);
    });
    el.appendChild(grid);
}

async function loadPlayerDetail(id) {
    const res = await api(`/players/${id}`);
    if (!res.ok) { toast("Failed to load player", "err"); return; }
    currentPlayer = res.data;
    currentPlayer._id = gv(res.data, "accountId", "playerId", "AccountId", "PlayerId");
    renderPlayerDetail(res.data);
    document.getElementById("player-detail").style.display = "block";
    document.getElementById("player-detail").scrollIntoView({ behavior: "smooth", block: "start" });
}

function renderPlayerDetail(p) {
    const id = p._id || gv(p, "accountId", "playerId", "AccountId", "PlayerId");
    const name = gv(p, "displayName", "username", "DisplayName", "Username") || "Unknown";
    const username = gv(p, "username", "Username") || "?";
    const img = gv(p, "profileImage", "ProfileImage");

    document.getElementById("pd-pfp").src = pfpUrl(img);
    document.getElementById("pd-name").textContent = name;
    document.getElementById("pd-username").textContent = "@" + username;
    document.getElementById("pd-id").textContent = "ID: " + id;

    const hb = gv(p, "heartbeat") || {};
    const bd = gv(p, "moderationBlockDetails") || {};
    const roles = gv(p, "roles", "playerRoles") || [];

    infoGrid({
        "Player ID": id,
        "Username": gv(p, "username", "Username"),
        "Display Name": gv(p, "displayName", "DisplayName"),
        "Level": gv(p, "level", "Level"),
        "XP": gv(p, "xp", "XP"),
        "Created At": fmtDate(gv(p, "createdAt", "CreatedAt")),
        "Last Login": fmtDate(gv(p, "lastLoginAt", "LastLoginAt")),
        "Is Junior": gv(p, "isJunior", "IsJunior") ? "Yes" : "No",
        "Profile Image": img,
        "Dorm Room ID": gv(p, "dormRoomId", "DormRoomId"),
        "Bio": gv(p, "bio", "Bio") || "-",
        "Status": hb.isOnline ? "Online" : "Offline",
        "Current Room": hb.roomInstance?.roomId || "None",
        "Banned": bd.isBan || bd.IsBan ? "Yes" : "No",
        "Discord": discordLabel(p),
        "Roles": (Array.isArray(roles) ? roles : []).join(", ") || "None"
    }, "pd-info-grid");

    const pa_username = document.getElementById("pa-username");
    if (pa_username) pa_username.value = gv(p, "username", "Username") || "";

    renderPlayerRoles(p);
    renderPlayerBanStatus(p);
    loadPlayerDetailRooms(id);
    loadOwnedSkins(p);
}

function renderPlayerRoles(p) {
    const roles = gv(p, "roles", "playerRoles", "PlayerRoles") || [];
    const grid = document.getElementById("pd-roles-grid");
    grid.innerHTML = "";
    if (!roles.length) {
        grid.innerHTML = '<span class="muted">No roles assigned.</span>';
    } else {
        roles.forEach(r => {
            const chip = document.createElement("span");
            chip.className = "role-chip";
            chip.textContent = r;
            grid.appendChild(chip);
        });
    }
    document.querySelectorAll(".btn-role").forEach(btn => {
        btn.classList.toggle("has-role", roles.includes(btn.dataset.role));
    });
}

function discordLabel(p) {
    const linked = gv(p, "discordLinked", "DiscordLinked");
    const uname = gv(p, "discordUsername", "DiscordUsername");
    const uid = gv(p, "discordUserId", "DiscordUserId");
    if (!linked && !uid) return "Not linked";
    const who = uname ? `@${uname}` : "";
    const idPart = uid ? `(${uid})` : "";
    return `${who} ${idPart}`.trim() || "Linked";
}

function renderPlayerBanStatus(p) {
    const bd = gv(p, "moderationBlockDetails", "ModerationBlockDetails") || {};
    const el = document.getElementById("pa-ban-status");
    const isBanned = bd.isBan || bd.IsBan;
    if (!isBanned) {
        el.innerHTML = '<span class="badge badge-offline">Not Banned</span>';
    } else {
        el.innerHTML = `<span class="badge badge-banned">Banned</span>
      <div class="muted" style="margin-top:6px">Reason: ${bd.message || bd.Message || "-"}</div>
      <div class="muted">Duration: ${bd.duration || bd.Duration || "-"} days</div>`;
    }

    const toxEl = document.getElementById("pa-toxmod-status");
    if (toxEl) {
        const tm = gv(p, "toxMod", "ToxMod") || {};
        const strikes = tm.strikes ?? tm.Strikes ?? 0;
        const banned = bd.isVoiceModAutoban || bd.IsVoiceModAutoban;
        const category = tm.activeBanCategory || tm.ActiveBanCategory;
        const expiresAt = tm.activeBanExpiresUnixTime ?? tm.ActiveBanExpiresUnixTime ?? 0;
        let html = banned
            ? '<span class="badge badge-banned">Voice Banned</span>'
            : '<span class="badge badge-offline">Voice OK</span>';
        if (banned) {
            if (category) html += `<div class="muted" style="margin-top:6px">Offense: ${category}</div>`;
            html += `<div class="muted">Expires: ${expiresAt ? fmtToxModRemaining(expiresAt) : "never (manual lift required)"}</div>`;
        }
        html += `<div class="muted" style="margin-top:${banned ? 0 : 6}px">ToxMod strikes: ${strikes}</div>`;
        toxEl.innerHTML = html;
    }

    const juniorEl = document.getElementById("pa-junior-status");
    if (juniorEl) {
        const isJr = gv(p, "isJunior", "IsJunior");
        let html = isJr
            ? '<span class="badge badge-banned">Comms Restricted (Junior)</span>'
            : '<span class="badge badge-offline">Not Junior</span>';
        if (isJr) {
            const js = gv(p, "juniorStatus", "JuniorStatus") || {};
            const reason = js.reason || js.Reason;
            const by = js.setBy || js.SetBy;
            const at = js.setAt || js.SetAt;
            if (reason) html += `<div class="muted" style="margin-top:6px">Reason: ${reason}</div>`;
            if (by || at) html += `<div class="muted">By: ${by || "?"}${at ? " · " + fmtDate(at) : ""}</div>`;
        }
        juniorEl.innerHTML = html;
    }

    const nameLockEl = document.getElementById("pa-namelock-status");
    if (nameLockEl) {
        const locked = gv(p, "nameLocked", "NameLocked");
        nameLockEl.innerHTML = locked
            ? '<span class="badge badge-banned">Name Locked</span>'
            : '<span class="badge badge-offline">Name Unlocked</span>';
    }

    const rrplusEl = document.getElementById("pa-rrplus-status");
    if (rrplusEl) {
        const has = gv(p, "recRoomPlus", "RecRoomPlus");
        rrplusEl.innerHTML = has
            ? '<span class="badge badge-online">Rec Room Plus</span>'
            : '<span class="badge badge-offline">No RR+</span>';
    }
}

function fmtToxModRemaining(expiresUnixTime) {
    const secs = expiresUnixTime - Math.floor(Date.now() / 1000);
    if (secs <= 0) return "expired (lifts on next check)";
    const d = Math.floor(secs / 86400), h = Math.floor((secs % 86400) / 3600), m = Math.floor((secs % 3600) / 60);
    const parts = [];
    if (d) parts.push(d + "d");
    if (h) parts.push(h + "h");
    parts.push(m + "m");
    return `${new Date(expiresUnixTime * 1000).toLocaleString()} (in ${parts.join(" ")})`;
}

async function loadPlayerDetailRooms(playerId) {
    const res = await api(`/players/${playerId}/rooms`);
    const el = document.getElementById("pd-rooms-list");
    el.innerHTML = "";
    const rooms = res.ok ? (res.data.rooms || res.data.Rooms || res.data || []) : [];
    if (!rooms.length) {
        el.innerHTML = '<div class="empty-state">No rooms found.</div>';
        return;
    }
    const grid = document.createElement("div");
    grid.className = "pd-rooms-grid";
    rooms.forEach(r => {
        const rid = gv(r, "roomId", "RoomId");
        const rname = gv(r, "name", "Name", "displayName", "DisplayName") || "Unknown";
        const rimg = gv(r, "imageName", "ImageName");
        const visits = gv(r, "stats", "Stats")?.visitCount ?? gv(r, "stats", "Stats")?.VisitCount ?? 0;
        const card = document.createElement("div");
        card.className = "grid-card";
        card.innerHTML = `
      <img class="grid-card-thumb" src="${roomImgUrl(rimg)}" style="border-radius:0" onerror="this.src='/imageserver/DefaultRoomImage.png'">
      <div class="grid-card-body">
        <div class="grid-card-name">${rname}</div>
        <div class="grid-card-sub">ID: ${rid} · ${visits} visits</div>
      </div>`;
        card.addEventListener("click", () => { loadRoomDetail(rid); switchPanel("rooms"); });
        grid.appendChild(card);
    });
    el.appendChild(grid);
}

function setupPlayerActions() {
    const pid = () => currentPlayer?._id;

    document.getElementById("pa-rename-btn").addEventListener("click", async () => {
        if (!pid()) return;
        const name = document.getElementById("pa-username").value.trim();
        if (!name) return;
        const res = await api(`/players/${pid()}/rename`, "POST", { username: name });
        if (res.ok) { toast("Renamed to " + name, "ok"); await loadPlayerDetail(pid()); }
        else toast("Rename failed: " + (res.data?.error || res.status), "err");
    });

    document.getElementById("pa-clearname-btn").addEventListener("click", async () => {
        if (!pid()) return;
        const res = await api(`/players/${pid()}/clear-name`, "POST", {});
        if (res.ok) { toast("Name cleared: " + (res.data?.newName || "?"), "ok"); await loadPlayerDetail(pid()); }
        else toast("Failed: " + (res.data?.error || res.status), "err");
    });

    document.getElementById("pa-namelock-btn").addEventListener("click", async () => {
        if (!pid()) return;
        const res = await api(`/players/${pid()}/name-lock`, "POST", { locked: true });
        if (res.ok) { toast("Name locked", "ok"); await loadPlayerDetail(pid()); }
        else toast("Failed: " + (res.data?.error || res.status), "err");
    });

    document.getElementById("pa-nameunlock-btn").addEventListener("click", async () => {
        if (!pid()) return;
        const res = await api(`/players/${pid()}/name-lock`, "POST", { locked: false });
        if (res.ok) { toast("Name unlocked", "ok"); await loadPlayerDetail(pid()); }
        else toast("Failed: " + (res.data?.error || res.status), "err");
    });

    document.getElementById("pa-bio-btn").addEventListener("click", async () => {
        if (!pid()) return;
        const bio = document.getElementById("pa-bio").value;
        const res = await api(`/players/${pid()}/set-bio`, "POST", { bio });
        if (res.ok) { toast("Bio updated", "ok"); await loadPlayerDetail(pid()); }
        else toast("Failed: " + (res.data?.error || res.status), "err");
    });

      document.getElementById("pa-password-btn").addEventListener("click", async () => {
    if (!pid()) return;
    const password = document.getElementById("pa-password").value;
    const res = await api(`/players/${pid()}/set-password`, "POST", { password });
    if (res.ok) { toast("Password updated", "ok"); await loadPlayerDetail(pid()); }
    else toast("Failed: " + (res.data?.error || res.status), "err");
  });


    document.getElementById("pa-pfp-set-btn").addEventListener("click", async () => {
        if (!pid()) return;
        const img = document.getElementById("pa-pfp-name").value.trim();
        if (!img) return;
        const res = await api(`/players/${pid()}/set-pfp`, "POST", { imageName: img });
        if (res.ok) { toast("PFP updated", "ok"); await loadPlayerDetail(pid()); }
        else toast("Failed: " + (res.data?.error || res.status), "err");
    });

    document.getElementById("pa-pfp-upload-btn").addEventListener("click", async () => {
        if (!pid()) return;
        const fileInput = document.getElementById("pa-pfp-upload");
        if (!fileInput.files[0]) { toast("Select a file first", "err"); return; }
        const fd = new FormData();
        fd.append("file", fileInput.files[0]);
        const res = await apiUpload(`/players/${pid()}/upload-pfp`, fd);
        if (res.ok) {
            toast("PFP uploaded", "ok");
            fileInput.value = "";
            const preview = document.getElementById("pfp-upload-preview");
            if (preview) preview.style.display = "none";
            await loadPlayerDetail(pid());
        } else toast("Failed: " + (res.data?.error || res.status), "err");
    });

    document.getElementById("pa-pfp-reset-btn").addEventListener("click", () => {
        if (!pid()) return;
        showConfirm("Reset PFP", "Reset this player's profile picture to default?", async () => {
            const res = await api(`/players/${pid()}/reset-pfp`, "POST", {});
            if (res.ok) { toast("PFP reset", "ok"); await loadPlayerDetail(pid()); }
            else toast("Failed", "err");
        });
    });

    document.getElementById("pa-level-btn").addEventListener("click", async () => {
        if (!pid()) return;
        const level = parseInt(document.getElementById("pa-level").value);
        const xpVal = document.getElementById("pa-xp").value;
        const xp = xpVal ? parseInt(xpVal) : null;
        if (!level) return;
        const res = await api(`/players/${pid()}/set-level`, "POST", { level, xp });
        if (res.ok) { toast("Level set to " + level, "ok"); await loadPlayerDetail(pid()); }
        else toast("Failed: " + (res.data?.error || res.status), "err");
    });

    document.getElementById("pa-platformid-btn").addEventListener("click", async () => {
        if (!pid()) return;
        const platformId = document.getElementById("pa-platformid").value.trim();
        if (!platformId) return;
        const res = await api(`/players/${pid()}/set-platform-id`, "POST", { platformId });
        if (res.ok) { toast("Platform ID updated", "ok"); await loadPlayerDetail(pid()); }
        else toast("Failed: " + (res.data?.error || res.status), "err");
    });

    document.getElementById("pa-rrplus-add-btn").addEventListener("click", async () => {
        if (!pid()) return;
        const res = await api(`/players/${pid()}/rrplus`, "POST", { enabled: true });
        if (res.ok) { toast("Rec Room Plus granted", "ok"); await loadPlayerDetail(pid()); }
        else toast("Failed: " + (res.data?.error || res.status), "err");
    });

    document.getElementById("pa-rrplus-remove-btn").addEventListener("click", async () => {
        if (!pid()) return;
        const res = await api(`/players/${pid()}/rrplus`, "POST", { enabled: false });
        if (res.ok) { toast("Rec Room Plus removed", "ok"); await loadPlayerDetail(pid()); }
        else toast("Failed: " + (res.data?.error || res.status), "err");
    });

    document.getElementById("pa-junior-set-btn").addEventListener("click", async () => {
        if (!pid()) return;
        const reason = document.getElementById("pa-junior-reason").value.trim() || null;
        const res = await api(`/players/${pid()}/set-junior`, "POST", { isJunior: true, reason });
        if (res.ok) {
            toast("Set as Junior - player notified", "ok");
            document.getElementById("pa-junior-reason").value = "";
            await loadPlayerDetail(pid());
        }
        else toast("Failed: " + (res.data?.error || res.status), "err");
    });

    document.getElementById("pa-junior-unset-btn").addEventListener("click", async () => {
        if (!pid()) return;
        const res = await api(`/players/${pid()}/set-junior`, "POST", { isJunior: false });
        if (res.ok) { toast("Junior removed", "ok"); await loadPlayerDetail(pid()); }
        else toast("Failed: " + (res.data?.error || res.status), "err");
    });

    document.getElementById("pa-toxmod-ban-btn").addEventListener("click", async () => {
        if (!pid()) return;
        const category = document.getElementById("pa-toxmod-category").value;
        const durVal = document.getElementById("pa-toxmod-duration").value;
        const note = document.getElementById("pa-toxmod-note").value.trim();
        const loud = document.getElementById("pa-toxmod-mode").value === "loud";
        const res = await api(`/players/${pid()}/toxmod/ban`, "POST", {
            category,
            note: note || null,
            durationSeconds: durVal ? parseInt(durVal) : null,
            loud
        });
        if (res.ok) { toast(loud ? "ToxMod voice ban issued (loud)" : "ToxMod voice ban issued (quiet)", "ok"); await loadPlayerDetail(pid()); }
        else toast("Failed: " + (res.data?.error || res.status), "err");
    });

    document.getElementById("pa-toxmod-unban-btn").addEventListener("click", async () => {
        if (!pid()) return;
        const res = await api(`/players/${pid()}/toxmod/unban`, "POST", { resetStrikes: false });
        if (res.ok) { toast("Voice ban lifted", "ok"); await loadPlayerDetail(pid()); }
        else toast("Failed: " + (res.data?.error || res.status), "err");
    });

    document.getElementById("pa-toxmod-reset-btn").addEventListener("click", async () => {
        if (!pid()) return;
        const res = await api(`/players/${pid()}/toxmod/unban`, "POST", { resetStrikes: true });
        if (res.ok) { toast("Voice ban lifted and strikes reset", "ok"); await loadPlayerDetail(pid()); }
        else toast("Failed: " + (res.data?.error || res.status), "err");
    });

    document.getElementById("pa-hb-clear-btn").addEventListener("click", () => {
        if (!pid()) return;
        showConfirm("Clear Heartbeat", "Clear heartbeat for this player?", async () => {
            const res = await api(`/players/${pid()}/heartbeat-clear`, "POST", {});
            if (res.ok) { toast("Heartbeat cleared", "ok"); await loadPlayerDetail(pid()); }
            else toast("Failed", "err");
        });
    });

    document.getElementById("pa-hb-view-btn").addEventListener("click", async () => {
        if (!pid()) return;
        const res = await api(`/players/${pid()}/heartbeat`);
        if (res.ok) toast(JSON.stringify(res.data), "info");
        else toast("Failed", "err");
    });

document.getElementById("pa-bring-btn").addEventListener("click", async () => {
    if (!pid()) return;
    const username = document.getElementById("pa-bring-username").value.trim();
    if (!username) return;
    const res = await api(`/players/${pid()}/force-bring`, "POST", { username });
    if (res.ok) toast("Force brought to " + username + "'s room", "ok");
    else toast("Failed: " + (res.data?.error || res.status), "err");
});

    document.getElementById("pa-transfer-btn").addEventListener("click", () => {
        if (!pid()) return;
        const target = parseInt(document.getElementById("pa-transfer-target").value);
        if (!target) return;
        const keepCoOwner = document.getElementById("pa-transfer-coowner").checked;
        const extra = keepCoOwner ? " The original owner will stay as co-owner." : "";
        showConfirm("Transfer Rooms", `Transfer all rooms from player ${pid()} to player ${target}?${extra}`, async () => {
            const res = await api(`/players/${pid()}/transfer-rooms`, "POST", { newPlayerId: target, keepCoOwner });
            if (res.ok) toast(keepCoOwner ? "Rooms transferred, old owner kept as co-owner" : "Rooms transferred", "ok");
            else toast("Failed: " + (res.data?.error || res.status), "err");
        });
    });

    document.getElementById("pa-sendws-btn").addEventListener("click", async () => {
        if (!pid()) return;
        const raw = (document.getElementById("pa-sendws")?.value || "").trim();
        if (!raw) { toast("Enter a JSON payload first", "err"); return; }
        try { JSON.parse(raw); } catch { toast("Invalid JSON", "err"); return; }
        const res = await api(`/players/${pid()}/send-ws-json`, "POST", { Json: raw });
        if (res.ok) toast("Sent to player websocket", "ok");
        else toast("Failed: " + (res.data?.error || res.status), "err");
    });

    document.getElementById("pa-repair-btn").addEventListener("click", () => {
        if (!pid()) return;
        showConfirm("Repair Account", "Repair account for this player?", async () => {
            const res = await api(`/players/${pid()}/repair`, "POST", {});
            if (res.ok) { toast("Account repaired", "ok"); await loadPlayerDetail(pid()); }
            else toast("Failed", "err");
        });
    });

    document.getElementById("pa-del-images-btn").addEventListener("click", () => {
        if (!pid()) return;
        showConfirm("Delete All Images", "Delete ALL images for this player? This cannot be undone.", async () => {
            const res = await api(`/players/${pid()}/delete-all-images`, "POST", {});
            if (res.ok) toast("Images deleted", "ok");
            else toast("Failed", "err");
        });
    });

    document.getElementById("pa-del-rooms-btn").addEventListener("click", () => {
        if (!pid()) return;
        showConfirm("Delete All Rooms", "Delete ALL rooms for this player? This cannot be undone.", async () => {
            const res = await api(`/players/${pid()}/delete-all-rooms`, "POST", {});
            if (res.ok) toast("Rooms deleted", "ok");
            else toast("Failed", "err");
        });
    });

    document.getElementById("pa-delete-btn").addEventListener("click", () => {
        if (!pid()) return;
        showConfirm("Delete Account", "Permanently delete this account? This cannot be undone.", async () => {
            const res = await api(`/players/${pid()}/delete`, "DELETE");
            if (res.ok) {
                toast("Account deleted", "ok");
                document.getElementById("player-detail").style.display = "none";
                currentPlayer = null;
            } else toast("Failed", "err");
        });
    });

    document.querySelectorAll(".btn-role").forEach(btn => {
        btn.addEventListener("click", async () => {
            if (!pid()) return;
            const role = btn.dataset.role;
            const hasRole = btn.classList.contains("has-role");
            const action = hasRole ? "remove" : "add";
            const res = await api(`/players/${pid()}/roles`, "POST", { role, action });
            if (res.ok) { toast("Role " + action + "ed: " + role, "ok"); await loadPlayerDetail(pid()); }
            else toast("Failed: " + (res.data?.error || res.status), "err");
        });
    });

    document.getElementById("pa-influencer-add").addEventListener("click", async () => {
        if (!pid()) return;
        const res = await api(`/players/${pid()}/influencer`, "POST", { action: "add" });
        if (res.ok) { toast("Influencer granted", "ok"); await loadPlayerDetail(pid()); }
        else toast("Failed", "err");
    });

    document.getElementById("pa-influencer-remove").addEventListener("click", async () => {
        if (!pid()) return;
        const res = await api(`/players/${pid()}/influencer`, "POST", { action: "remove" });
        if (res.ok) { toast("Influencer removed", "ok"); await loadPlayerDetail(pid()); }
        else toast("Failed", "err");
    });

    document.getElementById("pa-ban-btn").addEventListener("click", () => {
        if (!pid()) return;
        const reason = document.getElementById("pa-ban-reason").value.trim() || "Banned by admin.";
        const duration = parseInt(document.getElementById("pa-ban-duration").value) || 99999;
        showConfirm("Ban Player", "Ban this player?", async () => {
            const res = await api(`/players/${pid()}/ban`, "POST", { reason, duration });
            if (res.ok) { toast("Player banned", "ok"); await loadPlayerDetail(pid()); }
            else toast("Failed: " + (res.data?.error || res.status), "err");
        });
    });

    document.getElementById("pa-perm-ban-btn").addEventListener("click", () => {
        if (!pid()) return;
        const reason = document.getElementById("pa-ban-reason").value.trim() || "Banned by admin.";
        showConfirm("Permanently Ban Player", "Permanently ban this player? Duration will be set to 2147483647 days.", async () => {
            const res = await api(`/players/${pid()}/ban`, "POST", { reason, duration: 2147483647 });
            if (res.ok) { toast("Player permanently banned", "ok"); await loadPlayerDetail(pid()); }
            else toast("Failed: " + (res.data?.error || res.status), "err");
        });
    });

    document.getElementById("pa-unban-btn").addEventListener("click", () => {
        if (!pid()) return;
        showConfirm("Unban Player", "Unban this player?", async () => {
            const res = await api(`/players/${pid()}/unban`, "POST", {});
            if (res.ok) { toast("Player unbanned", "ok"); await loadPlayerDetail(pid()); }
            else toast("Failed", "err");
        });
    });

    document.getElementById("pa-close-game-btn").addEventListener("click", () => {
        if (!pid()) return;
        showConfirm("Close Game", "Close this users game?", async () => {
            const res = await api(`/players/${pid()}/close-game`, "POST", {});
            if (res.ok) toast("Close game sent", "ok");
            else toast("Failed: " + (res.data?.error || res.status), "err");
        });
    });
    
    document.getElementById("pa-logout-btn").addEventListener("click", () => {
    if (!pid()) return;
    showConfirm("Logout Player", "Logout this user?", async () => {
        const res = await api(`/players/${pid()}/logout`, "POST", {});
        if (res.ok) toast("Logged Out (dummy message)", "ok");
        else toast("Failed: " + (res.data?.error || res.status), "err");
    });
});

    document.getElementById("pa-player-coach-btn").addEventListener("click", async () => {
        if (!pid()) return;
        const message = document.getElementById("pa-player-coach-msg").value.trim();
        if (!message) { toast("Enter a message", "err"); return; }
        const res = await api(`/players/${pid()}/send-coach-message`, "POST", { message });
        if (res.ok) {
            toast("Coach message sent", "ok");
            document.getElementById("pa-player-coach-msg").value = "";
        } else toast("Failed: " + (res.data?.error || res.status), "err");
    });

    document.getElementById("pa-token-btn").addEventListener("click", async () => {
        if (!pid()) return;
        const amount = parseInt(document.getElementById("pa-token-amount").value);
        const balanceType = parseInt(document.getElementById("pa-token-type").value);
        if (isNaN(amount)) return;
        const res = await api(`/players/${pid()}/set-tokens`, "POST", { amount, balanceType });
        if (res.ok) { toast("Tokens set to " + amount, "ok"); await loadPlayerDetail(pid()); }
        else toast("Failed: " + (res.data?.error || res.status), "err");
    });

    document.getElementById("pa-skin-search").addEventListener("input", function () {
        renderSkinList(this.value);
    });

    document.getElementById("pa-rmd-btn").addEventListener("click", () => {
        if (!pid()) return;
        const reporterVal = document.getElementById("pa-rmd-reporter").value.trim();
        const timeoutVal = document.getElementById("pa-rmd-timeoutstartedat").value;
        const body = {
            reportCategory: parseInt(document.getElementById("pa-rmd-category").value),
            duration: parseInt(document.getElementById("pa-rmd-duration").value) || 0,
            gameSessionId: parseInt(document.getElementById("pa-rmd-gamesessionid").value) || 0,
            message: document.getElementById("pa-rmd-message").value.trim() || null,
            playerIdReporter: reporterVal ? parseInt(reporterVal) : null,
            voteKickReason: document.getElementById("pa-rmd-votekickreason").value.trim() || null,
            timeoutStartedAt: timeoutVal ? new Date(timeoutVal).toISOString() : null,
            associatedAccountUsername: document.getElementById("pa-rmd-assocusername").value.trim() || null,
            isBan: document.getElementById("pa-rmd-isban").value === "true",
            isHostKick: document.getElementById("pa-rmd-ishostkick").value === "true",
            isVoiceModAutoban: document.getElementById("pa-rmd-isvoicemodautoban").value === "true",
            isDeviceBan: document.getElementById("pa-rmd-isdeviceban").value === "true",
            isWarning: document.getElementById("pa-rmd-iswarning").value === "true",
            bootToDorm: document.getElementById("pa-rmd-boottodorm").value === "true"
        };
        showConfirm("Apply Raw Mod Details", "Apply raw moderation details to this player?", async () => {
            const res = await api(`/players/${pid()}/raw-mod-details`, "POST", body);
            if (res.ok) { toast("Raw mod details applied", "ok"); await loadPlayerDetail(pid()); }
            else toast("Failed: " + (res.data?.error || res.status), "err");
        });
    });
}

async function loadSkins() {
    const res = await api("/skins");
    if (res.ok) allSkins = res.data || [];
}

function renderSkinList(filter = "") {
    const el = document.getElementById("pd-skin-list");
    el.innerHTML = "";
    const skins = filter ? allSkins.filter(s => (s.friendlyName || s.FriendlyName)?.toLowerCase().includes(filter.toLowerCase())) : allSkins.slice(0, 60);
    skins.forEach(skin => {
        const name = gv(skin, "friendlyName", "FriendlyName") || "Unknown";
        const item = document.createElement("div");
        item.className = "skin-item";
        item.innerHTML = `<span>${name}</span><button class="btn-action" style="font-size:11px;padding:4px 8px">Give</button>`;
        item.querySelector("button").addEventListener("click", async () => {
            if (!currentPlayer?._id) return;
            const res = await api(`/players/${currentPlayer._id}/give-skin`, "POST", { skin: name, action: "add" });
            if (res.ok) { toast("Skin granted: " + name, "ok"); loadOwnedSkins(currentPlayer); }
            else toast("Failed: " + (res.data?.error || res.status), "err");
        });
        el.appendChild(item);
    });
}

function loadOwnedSkins(p) {
    const extra = gv(p, "playerExtra", "PlayerExtra") || {};
    const owned = gv(extra, "ownedEquipment", "OwnedEquipment") || gv(p, "ownedEquipment", "OwnedEquipment") || [];
    const el = document.getElementById("pd-owned-list");
    el.innerHTML = "";
    owned.forEach(s => {
        const name = gv(s, "friendlyName", "FriendlyName") || gv(s, "prefabName", "PrefabName") || "Unknown";
        const chip = document.createElement("div");
        chip.className = "owned-skin-chip";
        chip.innerHTML = `${name} <span>✕</span>`;
        chip.addEventListener("click", () => {
            showConfirm("Remove Skin", "Remove " + name + "?", async () => {
                const res = await api(`/players/${currentPlayer._id}/give-skin`, "POST", { skin: name, action: "remove" });
                if (res.ok) { toast("Skin removed", "ok"); await loadPlayerDetail(currentPlayer._id); }
                else toast("Failed", "err");
            });
        });
        el.appendChild(chip);
    });
    if (!owned.length) el.innerHTML = '<span class="muted">No skins owned.</span>';
}

function setupRoomPanel() {
    document.getElementById("room-search-btn").addEventListener("click", roomSearch);
    document.getElementById("room-search-input").addEventListener("keydown", e => { if (e.key === "Enter") roomSearch(); });
    document.getElementById("room-hot-btn").addEventListener("click", roomLoadHot);
    document.getElementById("rd-close").addEventListener("click", () => {
        document.getElementById("room-detail").style.display = "none";
        currentRoom = null;
    });

    setupDetailTabs("#room-detail");
    setupRoomActions();
}

async function roomSearch() {
    const q = document.getElementById("room-search-input").value.trim();
    if (!q) return;
    if (/^\d+$/.test(q)) {
        const res = await api(`/rooms/${BigInt(q)}`);
        if (res.ok) { renderRoomResults([res.data]); return; }
        toast("Room not found", "err"); return;
    }
    extendSession();
    try {
        const res = await api(`/rooms/search?q=${encodeURIComponent(q)}&skip=0&take=30`);
        if (!res.ok) { toast("Search failed", "err"); return; }
        renderRoomResults(res.data.Results || res.data.results || res.data);
    } catch (e) {
        toast("Search failed", "err");
        return;
    }
}

async function roomLoadHot() {
    const res = await api("/rooms/hot?take=50");
    if (!res.ok) { toast("Failed to load rooms", "err"); return; }
    renderRoomResults(res.data.Results || res.data.results || res.data);
}

function renderRoomResults(rooms) {
    const el = document.getElementById("room-results");
    el.innerHTML = "";
    if (!rooms || rooms.length === 0) {
        el.innerHTML = '<div class="empty-state">No rooms found.</div>';
        return;
    }
    const grid = document.createElement("div");
    grid.className = "results-grid";
    rooms.forEach(r => {
        const rid = gv(r, "roomId", "RoomId");
        const rname = gv(r, "name", "Name", "displayName", "DisplayName") || "Unknown";
        const rimg = gv(r, "imageName", "ImageName");
        const creator = gv(r, "creatorAccountId", "CreatorAccountId") || "?";
        const statsObj = gv(r, "stats", "Stats") || {};
        const visits = statsObj.visitCount ?? statsObj.VisitCount ?? 0;

        const card = document.createElement("div");
        card.className = "grid-card";
        card.innerHTML = `
      <img class="grid-card-thumb" src="${roomImgUrl(rimg)}" style="border-radius:0;border-bottom:1px solid var(--border)" onerror="this.src='/imageserver/DefaultRoomImage.png'">
      <div class="grid-card-visits">${visits} visits</div>
      <div class="grid-card-body">
        <div class="grid-card-name">${rname}</div>
        <div class="grid-card-sub">ID: ${rid} · Creator: ${creator}</div>
      </div>`;
        card.addEventListener("click", () => loadRoomDetail(rid));
        grid.appendChild(card);
    });
    el.appendChild(grid);
}

async function loadRoomDetail(id) {
    const res = await api(`/rooms/${id}`);
    if (!res.ok) { toast("Failed to load room", "err"); return; }
    currentRoom = res.data;
    currentRoom._id = gv(res.data, "roomId", "RoomId");
    renderRoomDetail(res.data);
    document.getElementById("room-detail").style.display = "block";
    document.getElementById("room-detail").scrollIntoView({ behavior: "smooth", block: "start" });
}

function populateSubroomSelect(r) {
    const sel = document.getElementById("ra-blob-subroom-select");
    sel.innerHTML = '<option value="">Select SubRoom…</option>';
    const subs = gv(r, "subRooms", "SubRooms") || [];
    subs.forEach(s => {
        const sid = String(gv(s, "subRoomId", "SubRoomId"));
        const sname = gv(s, "name", "Name") || "SubRoom";
        const opt = document.createElement("option");
        opt.value = sid;
        opt.textContent = `${sname} (ID: ${sid})`;
        sel.appendChild(opt);
    });
}

function renderRoomDetail(r) {
    const rid = r._id || gv(r, "roomId", "RoomId");
    const rname = gv(r, "displayName", "DisplayName", "name", "Name") || "Unknown";
    const rimg = gv(r, "imageName", "ImageName");
    const creator = gv(r, "creatorAccountId", "CreatorAccountId") || "?";
    const creatorUser = gv(r, "creatorUsername", "CreatorUsername");

    document.getElementById("rd-img").src = roomImgUrl(rimg);
    document.getElementById("rd-name").textContent = rname;
    document.getElementById("rd-id").textContent = "ID: " + rid;
    document.getElementById("rd-creator").textContent = "Creator ID: " + creator + (creatorUser ? " (@" + creatorUser + ")" : "");

    const statsObj = gv(r, "stats", "Stats") || {};
    const subRooms = gv(r, "subRooms", "SubRooms") || [];

    infoGrid({
        "Room ID": rid,
        "Name": gv(r, "name", "Name"),
        "Display Name": gv(r, "displayName", "DisplayName"),
        "Description": gv(r, "description", "Description"),
        "Creator ID": creator,
        "Creator Username": creatorUser || "-",
        "Max Players": gv(r, "maxPlayers", "MaxPlayers"),
        "Accessibility": accessibilityName(gv(r, "accessibility", "Accessibility")),
        "State": gv(r, "state", "State"),
        "Age Rating": gv(r, "ageRating", "AgeRating"),
        "Is RRO": (gv(r, "isRRO", "IsRRO")) ? "Yes" : "No",
        "Cloning": (gv(r, "cloningAllowed", "CloningAllowed")) ? "Allowed" : "Disabled",
        "Supports Mobile": (gv(r, "supportsMobile", "SupportsMobile")) ? "Yes" : "No",
        "Created At": fmtDate(gv(r, "createdAt", "CreatedAt")),
        "SubRooms": subRooms.length
    }, "rd-info-grid");

    const statsGrid = document.getElementById("rd-stats-grid");
    if (statsGrid) {
        statsGrid.innerHTML = "";
        const statsData = {
            "Visits": statsObj.visitCount ?? statsObj.VisitCount ?? 0,
            "Cheers": statsObj.cheerCount ?? statsObj.CheerCount ?? 0,
            "Favorites": statsObj.favoriteCount ?? statsObj.FavoriteCount ?? 0
        };
        for (const [k, v] of Object.entries(statsData)) {
            const item = document.createElement("div");
            item.className = "info-item";
            item.innerHTML = `<div class="info-label">${k}</div><div class="info-val">${v}</div>`;
            statsGrid.appendChild(item);
        }
    }

    renderRoomTags(r);
    renderSubRooms(r);
    populateSubroomSelect(r);

    const ra_name = document.getElementById("ra-name");
    const ra_desc = document.getElementById("ra-desc");
    if (ra_name) ra_name.value = gv(r, "name", "Name") || "";
    if (ra_desc) ra_desc.value = gv(r, "description", "Description") || "";

    const accessSel = document.getElementById("ra-access");
    const currAccess = gv(r, "accessibility", "Accessibility");
    if (accessSel && currAccess !== null) accessSel.value = currAccess;

    const cloneSel = document.getElementById("ra-cloning");
    if (cloneSel) cloneSel.value = (gv(r, "cloningAllowed", "CloningAllowed")) ? "true" : "false";

    const ageSel = document.getElementById("ra-agerating");
    if (ageSel) ageSel.value = gv(r, "ageRating", "AgeRating") ?? 0;

    const maxEl = document.getElementById("ra-maxplayers");
    if (maxEl) maxEl.value = gv(r, "maxPlayers", "MaxPlayers") ?? 8;

    const mobileSel = document.getElementById("ra-mobile");
    if (mobileSel) mobileSel.value = (gv(r, "supportsMobile", "SupportsMobile")) ? "true" : "false";

    const screensSel = document.getElementById("ra-screens");
    if (screensSel) screensSel.value = (gv(r, "supportsScreens", "SupportsScreens")) ? "true" : "false";

    const walkvrSel = document.getElementById("ra-walkvr");
    if (walkvrSel) walkvrSel.value = (gv(r, "supportsWalkVR", "SupportsWalkVR")) ? "true" : "false";

    const teleportvrSel = document.getElementById("ra-teleportvr");
    if (teleportvrSel) teleportvrSel.value = (gv(r, "supportsTeleportVR", "SupportsTeleportVR")) ? "true" : "false";

    const juniorsSel = document.getElementById("ra-juniors");
    if (juniorsSel) juniorsSel.value = (gv(r, "supportsJuniors", "SupportsJuniors")) ? "true" : "false";
}

const TAG_TYPE_NAMES = { 0: "General", 1: "Auto", 2: "AG Only", 3: "Banned" };

function renderRoomTags(r) {
    const tags = gv(r, "tags", "Tags") || [];
    const el = document.getElementById("rd-current-tags");
    el.innerHTML = "";
    if (!tags.length) {
        el.innerHTML = '<span class="muted">No tags.</span>';
        return;
    }
    tags.forEach(t => {
        const tagName = gv(t, "tag", "Tag") || t;
        const tagType = t.type ?? t.Type ?? 0;
        const typeLabel = TAG_TYPE_NAMES[tagType] || `Type ${tagType}`;

        const chip = document.createElement("span");
        chip.className = "tag-chip";
        chip.innerHTML = `${tagName}<span class="tag-type-badge type-${tagType}">${typeLabel}</span><button class="tag-remove-btn" title="Remove tag" data-tag="${tagName}">✕</button>`;
        chip.querySelector(".tag-remove-btn").addEventListener("click", async (e) => {
            e.stopPropagation();
            const tag = e.currentTarget.dataset.tag;
            const rid = currentRoom?._id;
            if (!rid) return;
            const res = await api(`/rooms/${rid}/tags`, "POST", { tags: [tag], action: "remove" });
            if (res.ok) { toast("Tag removed", "ok"); await loadRoomDetail(rid); }
            else toast("Failed to remove tag", "err");
        });
        el.appendChild(chip);
    });
}

function renderSubRooms(r) {
    const subs = gv(r, "subRooms", "SubRooms") || [];
    const rid = r._id || gv(r, "roomId", "RoomId");
    const el = document.getElementById("rd-subrooms-list");
    el.innerHTML = "";
    if (!subs.length) {
        el.innerHTML = '<div class="empty-state">No subrooms.</div>';
        return;
    }
    subs.forEach(s => {
        const sid = gv(s, "subRoomId", "SubRoomId");
        const card = document.createElement("div");
        card.className = "subroom-card";
        card.innerHTML = `
      <div>
        <div class="sr-name">${gv(s, "name", "Name") || "SubRoom"}</div>
        <div class="sr-meta">ID: ${sid} · Max: ${gv(s, "maxPlayers", "MaxPlayers")} · ${accessibilityName(gv(s, "accessibility", "Accessibility"))}</div>
        <div class="sr-meta">DataBlob: ${gv(s, "dataBlob", "DataBlob") || "none"}</div>
        <div class="sr-meta sr-instances" data-subroom="${sid}">Instances: …</div>
      </div>
      <div style="display:flex;flex-direction:column;gap:6px">
        <button class="btn-danger sr-refresh-btn" data-subroom="${sid}">Refresh All Public Instances</button>
        <button class="btn-action sr-makeprivate-btn" data-subroom="${sid}">Make All Instances Private</button>
        <button class="btn-action sr-makepublic-btn" data-subroom="${sid}">Make All Instances Public</button>
      </div>`;

        card.querySelector(".sr-refresh-btn").addEventListener("click", () => {
            const srName = gv(s, "name", "Name") || `SubRoom ${sid}`;
            showConfirm(
                "Refresh All Public Instances",
                `Everyone in a public instance of "${srName}" will be moved into a brand new instance. Players currently together stay together. Private instances are untouched.`,
                async () => {
                    const res = await api(`/rooms/${rid}/refresh-instances`, "POST", { subRoomId: sid });
                    if (res.ok && res.data?.success) {
                        const moved = res.data.playersMoved ?? 0;
                        const inst = res.data.instancesRefreshed ?? 0;
                        toast(inst ? `Refreshed ${inst} instance(s), moved ${moved} player(s)` : "No public instances to refresh", inst ? "ok" : "info");
                        loadSubRoomInstanceCounts(rid);
                    } else {
                        toast(res.data?.error || "Failed to refresh instances", "err");
                    }
                }
            );
        });

        card.querySelector(".sr-makeprivate-btn").addEventListener("click", () => {
            const srName = gv(s, "name", "Name") || `SubRoom ${sid}`;
            showConfirm(
                "Make All Instances Private",
                `Every public instance of "${srName}" will be switched to private, and players inside will stay together. Already-private instances are untouched.`,
                async () => {
                    const res = await setSubRoomInstancesPrivacy(rid, sid, true);
                    if (res.ok && res.data?.success) {
                        const changed = res.data.instancesChanged ?? 0;
                        toast(changed ? `Made ${changed} instance(s) private` : "No public instances to change", changed ? "ok" : "info");
                        loadSubRoomInstanceCounts(rid);
                    } else {
                        toast(res.data?.error || "Failed to update instances", "err");
                    }
                }
            );
        });

        card.querySelector(".sr-makepublic-btn").addEventListener("click", () => {
            const srName = gv(s, "name", "Name") || `SubRoom ${sid}`;
            showConfirm(
                "Make All Instances Public",
                `Every private instance of "${srName}" will be switched to public, and players inside will stay together. Already-public instances are untouched.`,
                async () => {
                    const res = await setSubRoomInstancesPrivacy(rid, sid, false);
                    if (res.ok && res.data?.success) {
                        const changed = res.data.instancesChanged ?? 0;
                        toast(changed ? `Made ${changed} instance(s) public` : "No private instances to change", changed ? "ok" : "info");
                        loadSubRoomInstanceCounts(rid);
                    } else {
                        toast(res.data?.error || "Failed to update instances", "err");
                    }
                }
            );
        });

        el.appendChild(card);
    });

    loadSubRoomInstanceCounts(rid);
}

async function setSubRoomInstancesPrivacy(roomId, subRoomId, isPrivate) {
    return api(`/rooms/${roomId}/set-instances-privacy`, "POST", { subRoomId, isPrivate });
}

async function loadSubRoomInstanceCounts(roomId) {
    const cells = document.querySelectorAll("#rd-subrooms-list .sr-instances");
    if (!cells.length) return;

    const res = await api(`/rooms/${roomId}/instances`);
    if (!res.ok) {
        cells.forEach(c => { c.textContent = "Instances: unavailable"; });
        return;
    }

    const stats = {};
    (res.data?.subRooms || []).forEach(x => { stats[String(x.subRoomId)] = x; });

    cells.forEach(c => {
        const st = stats[c.dataset.subroom];
        const pub = st?.publicInstances ?? 0;
        const pubPlayers = st?.publicPlayers ?? 0;
        const priv = st?.privateInstances ?? 0;
        const privPlayers = st?.privatePlayers ?? 0;
        c.textContent = `Public: ${pub} instance${pub === 1 ? "" : "s"} (${pubPlayers} player${pubPlayers === 1 ? "" : "s"}) · Private: ${priv} instance${priv === 1 ? "" : "s"} (${privPlayers} player${privPlayers === 1 ? "" : "s"})`;

        const card = c.closest(".subroom-card");
        const refreshBtn = card?.querySelector(".sr-refresh-btn");
        if (refreshBtn) refreshBtn.disabled = pub === 0;
        const makePrivateBtn = card?.querySelector(".sr-makeprivate-btn");
        if (makePrivateBtn) makePrivateBtn.disabled = pub === 0;
        const makePublicBtn = card?.querySelector(".sr-makepublic-btn");
        if (makePublicBtn) makePublicBtn.disabled = priv === 0;
    });
}

function setupRoomImport() {
    function wireImporter(triggerBtnId, endpoint, title) {
        const modal = document.getElementById("import-room-modal");
        const status = document.getElementById("import-status");
        const nameInput = document.getElementById("import-room-name");
        const submitBtn = document.getElementById("import-room-submit");

        const resetModal = () => {
            modal.style.display = "none";
            status.style.display = "none";
            status.textContent = "";
            nameInput.value = "";
            submitBtn.disabled = false;
            submitBtn.onclick = null;
        };

        document.getElementById(triggerBtnId).addEventListener("click", () => {
            modal.style.display = "flex";
            document.getElementById("import-modal-title").textContent = title;

            submitBtn.onclick = async () => {
                const name = nameInput.value.trim();
                if (!name) { toast("Room name is required", "err"); return; }

                submitBtn.disabled = true;
                status.style.display = "block";
                status.textContent = "Importing…";
                status.style.color = "var(--text-muted)";

                try {
                    const res = await fetch(`/vannet/api/director/rooms/${endpoint}?name=${encodeURIComponent(name)}`, {
                        method: "POST",
                        headers: { "X-Director-Token": sessionToken || "" }
                    });

                    const data = await res.text().then(t => { try { return safeParse(t); } catch (_) { return {}; } }).catch(() => ({}));

                    if (res.ok && data.success) {
                        status.textContent = `Done - ${data.subRoomsImported} subroom(s) imported. Room ID: ${data.roomId} (${data.name})`;
                        status.style.color = "var(--success)";
                        toast("Room imported successfully", "ok");
                        nameInput.value = "";
                    } else {
                        status.textContent = "Error: " + (data.error || res.status);
                        status.style.color = "var(--danger-hover)";
                        toast("Import failed: " + (data.error || res.status), "err");
                    }
                } catch (e) {
                    status.textContent = "Network error: " + e.message;
                    status.style.color = "var(--danger-hover)";
                }

                submitBtn.disabled = false;
            };
        });

        document.getElementById("import-modal-close").addEventListener("click", resetModal);
        modal.addEventListener("click", (e) => { if (e.target === modal) resetModal(); });
        nameInput.addEventListener("keydown", (e) => { if (e.key === "Enter") submitBtn.click(); });
    }

    wireImporter("room-import-btn", "import-meownet", "Meow.Net Room Importer");
    wireImporter("room-import-epicquest-btn", "import-epicquest", "EpicQuest Room Importer");
}

function setupRoomActions() {
    const rid = () => currentRoom?._id;

    document.getElementById("ra-name-btn").addEventListener("click", async () => {
        if (!rid()) return;
        const name = document.getElementById("ra-name").value.trim();
        if (!name) return;
        const res = await api(`/rooms/${rid()}/set-name`, "POST", { name });
        if (res.ok) { toast("Name updated", "ok"); await loadRoomDetail(rid()); }
        else toast("Failed: " + (res.data?.error || res.status), "err");
    });

    document.getElementById("ra-desc-btn").addEventListener("click", async () => {
        if (!rid()) return;
        const description = document.getElementById("ra-desc").value;
        const res = await api(`/rooms/${rid()}/set-description`, "POST", { description });
        if (res.ok) { toast("Description updated", "ok"); await loadRoomDetail(rid()); }
        else toast("Failed", "err");
    });

    document.getElementById("ra-image-btn").addEventListener("click", async () => {
        if (!rid()) return;
        const imageName = document.getElementById("ra-image").value.trim();
        if (!imageName) return;
        const res = await api(`/rooms/${rid()}/set-image`, "POST", { imageName });
        if (res.ok) { toast("Image updated", "ok"); await loadRoomDetail(rid()); }
        else toast("Failed", "err");
    });

    document.getElementById("ra-image-upload-btn").addEventListener("click", async () => {
        if (!rid()) return;
        const fileInput = document.getElementById("ra-image-upload");
        if (!fileInput.files[0]) { toast("Select a file first", "err"); return; }
        const fd = new FormData();
        fd.append("file", fileInput.files[0]);
        const res = await apiUpload(`/rooms/${rid()}/upload-image`, fd);
        if (res.ok) {
            toast("Image uploaded", "ok");
            fileInput.value = "";
            const preview = document.getElementById("room-img-upload-preview");
            if (preview) preview.style.display = "none";
            await loadRoomDetail(rid());
        } else toast("Failed: " + (res.data?.error || res.status), "err");
    });

    document.getElementById("ra-image-reset-btn").addEventListener("click", () => {
        if (!rid()) return;
        showConfirm("Reset Image", "Reset room image to default?", async () => {
            const res = await api(`/rooms/${rid()}/reset-image`, "POST", {});
            if (res.ok) { toast("Image reset", "ok"); await loadRoomDetail(rid()); }
            else toast("Failed", "err");
        });
    });

    document.getElementById("ra-access-btn").addEventListener("click", async () => {
        if (!rid()) return;
        const accessibility = parseInt(document.getElementById("ra-access").value);
        const res = await api(`/rooms/${rid()}/set-accessibility`, "POST", { accessibility });
        if (res.ok) { toast("Accessibility updated", "ok"); await loadRoomDetail(rid()); }
        else toast("Failed", "err");
    });

    document.getElementById("ra-cloning-btn").addEventListener("click", async () => {
        if (!rid()) return;
        const allowed = document.getElementById("ra-cloning").value === "true";
        const res = await api(`/rooms/${rid()}/set-cloning`, "POST", { allowed });
        if (res.ok) { toast("Cloning updated", "ok"); await loadRoomDetail(rid()); }
        else toast("Failed", "err");
    });

    document.getElementById("ra-restrictions-btn").addEventListener("click", async () => {
        if (!rid()) return;
        const supportsScreens = document.getElementById("ra-screens").value === "true";
        const supportsWalkVR = document.getElementById("ra-walkvr").value === "true";
        const supportsTeleportVR = document.getElementById("ra-teleportvr").value === "true";
        const supportsJuniors = document.getElementById("ra-juniors").value === "true";
        const supportsMobile = document.getElementById("ra-mobile").value === "true";
        const res = await api(`/rooms/${rid()}/set-restrictions`, "POST", { supportsScreens, supportsWalkVR, supportsTeleportVR, supportsJuniors, supportsMobile });
        if (res.ok) { toast("Platform settings saved", "ok"); await loadRoomDetail(rid()); }
        else toast("Failed: " + (res.data?.error || res.status), "err");
    });

    document.getElementById("ra-agerating-btn").addEventListener("click", async () => {
        if (!rid()) return;
        const ageRating = parseInt(document.getElementById("ra-agerating").value);
        const res = await api(`/rooms/${rid()}/set-age-rating`, "POST", { ageRating });
        if (res.ok) { toast("Age rating updated", "ok"); await loadRoomDetail(rid()); }
        else toast("Failed", "err");
    });

    document.getElementById("ra-maxplayers-btn").addEventListener("click", async () => {
        if (!rid()) return;
        const maxPlayers = parseInt(document.getElementById("ra-maxplayers").value);
        if (!maxPlayers) return;
        const res = await api(`/rooms/${rid()}/set-max-players`, "POST", { maxPlayers });
        if (res.ok) { toast("Max players updated", "ok"); await loadRoomDetail(rid()); }
        else toast("Failed", "err");
    });

    document.getElementById("ra-owner-btn").addEventListener("click", () => {
        if (!rid()) return;
        const newOwnerId = parseInt(document.getElementById("ra-owner").value);
        if (!newOwnerId) return;
        showConfirm("Transfer Ownership", `Transfer room ${rid()} to player ${newOwnerId}?`, async () => {
            const res = await api(`/rooms/${rid()}/change-owner`, "POST", { newOwnerId });
            if (res.ok) { toast("Ownership transferred", "ok"); await loadRoomDetail(rid()); }
            else toast("Failed", "err");
        });
    });

    document.getElementById("ra-role-btn").addEventListener("click", async () => {
        if (!rid()) return;
        const playerId = parseInt(document.getElementById("ra-role-player").value);
        const role = parseInt(document.getElementById("ra-role-val").value);
        if (!playerId) return;
        const res = await api(`/rooms/${rid()}/set-role`, "POST", { playerId, role });
        if (res.ok) { toast("Role set", "ok"); await loadRoomDetail(rid()); }
        else toast("Failed", "err");
    });

    document.getElementById("ra-beta-btn").addEventListener("click", async () => {
        if (!rid()) return;
        const res = await api(`/rooms/${rid()}/toggle-beta`, "POST", {});
        if (res.ok) { toast("Beta tag toggled", "ok"); await loadRoomDetail(rid()); }
        else toast("Failed", "err");
    });

    document.getElementById("ra-blob-btn").addEventListener("click", async () => {
        if (!rid()) return;
        const subRoomId = document.getElementById("ra-blob-subroom-select").value;
        const datablob = document.getElementById("ra-blob-name").value.trim();
        if (!subRoomId || !datablob) { toast("Select a subroom and enter a datablob filename", "err"); return; }
        const res = await api(`/rooms/${rid()}/change-datablob`, "POST", { subRoomId: subRoomId.toString(), datablob });
        if (res.ok) { toast("DataBlob imported", "ok"); await loadRoomDetail(rid()); }
        else toast("Failed: " + (res.data?.error || res.status), "err");
    });

    document.getElementById("ra-blob-upload").addEventListener("change", async () => {
        if (!rid()) return;
        const subRoomId = document.getElementById("ra-blob-subroom-select").value;
        const fileInput = document.getElementById("ra-blob-upload");
        if (!subRoomId) { toast("Select a subroom first", "err"); fileInput.value = ""; return; }
        if (!fileInput.files[0]) return;
        const fd = new FormData();
        fd.append("file", fileInput.files[0]);
        fd.append("subRoomId", subRoomId.toString());
        const res = await apiUpload(`/rooms/${rid()}/upload-datablob`, fd);
        if (res.ok) {
            toast("DataBlob uploaded: " + res.data?.fileName, "ok");
            fileInput.value = "";
            await loadRoomDetail(rid());
        } else toast("Failed: " + (res.data?.error || res.status), "err");
    });

    document.getElementById("ra-tag-add-btn").addEventListener("click", async () => {
        if (!rid()) return;
        const tag = document.getElementById("ra-tag-input").value.trim();
        if (!tag) return;
        const type = parseInt(document.getElementById("ra-tag-type").value);
        const res = await api(`/rooms/${rid()}/tags`, "POST", { tags: [tag], action: "add", type });
        if (res.ok) { toast("Tag added", "ok"); await loadRoomDetail(rid()); }
        else toast("Failed", "err");
    });

    document.getElementById("ra-tag-remove-btn").addEventListener("click", async () => {
        if (!rid()) return;
        const tag = document.getElementById("ra-tag-input").value.trim();
        if (!tag) return;
        const res = await api(`/rooms/${rid()}/tags`, "POST", { tags: [tag], action: "remove" });
        if (res.ok) { toast("Tag removed", "ok"); await loadRoomDetail(rid()); }
        else toast("Failed", "err");
    });
    document.getElementById("ra-set-modern-persistence-btn").addEventListener("click", async () => {
        if (!rid()) return;
        const res = await api(`/rooms/${rid()}/set-modern-persistence`, "POST", {});
        if (res.ok) { toast("Modern persistence values set", "ok"); await loadRoomDetail(rid()); }
        else toast("Failed", "err");
    });

    document.getElementById("ra-roomtype-btn").addEventListener("click", async () => {
        if (!rid()) return;
        const roomType = document.getElementById("ra-roomtype").value === "true";
        const res = await api(`/rooms/${rid()}/set-room-type`, "POST", { roomType });
        if (res.ok) { toast("Room type updated", "ok"); await loadRoomDetail(rid()); }
        else toast("Failed", "err");
    });

    document.getElementById("ra-delete-btn").addEventListener("click", () => {
        if (!rid()) return;
        showConfirm("Delete Room", "Permanently delete this room? This cannot be undone.", async () => {
            const res = await api(`/rooms/${rid()}/delete`, "DELETE");
            if (res.ok) {
                toast("Room deleted", "ok");
                document.getElementById("room-detail").style.display = "none";
                currentRoom = null;
            } else toast("Failed", "err");
        });
    });
}

let currentImage = null;
let lastLoadedImages = [];
let showPrivateImages = false;

const IMAGE_TYPE_NAMES = {
    0: "None",
    1: "Share Camera",
    2: "Outfit Thumbnail",
    3: "Room Thumbnail",
    4: "Profile Thumbnail",
    5: "Invention Thumbnail",
    6: "Player Event Thumbnail",
    7: "Room Load Screen"
};

function imageTypeName(v) {
    return IMAGE_TYPE_NAMES[v] ?? ("Type " + v);
}

function setupImagesPanel() {
    document.getElementById("img-show-private").addEventListener("change", function () {
        showPrivateImages = this.checked;
        renderImages(lastLoadedImages);
    });

    document.getElementById("img-search-btn").addEventListener("click", async () => {
        const pid = document.getElementById("img-player-id").value.trim();
        if (!pid) return;
        const res = await api(`/Images/player/${pid}`);
        if (!res.ok) { toast("Failed to load images", "err"); return; }
        lastLoadedImages = res.data || [];
        renderImages(lastLoadedImages);
    });

    document.getElementById("img-global-btn").addEventListener("click", async () => {
        const res = await api("/Images/global");
        if (!res.ok) { toast("Failed", "err"); return; }
        lastLoadedImages = res.data || [];
        renderImages(lastLoadedImages);
    });

    document.getElementById("img-detail-close").addEventListener("click", closeImageDetail);
    document.getElementById("img-detail-overlay").addEventListener("click", e => {
        if (e.target === document.getElementById("img-detail-overlay")) closeImageDetail();
    });

    document.getElementById("imgd-access-btn").addEventListener("click", async () => {
        if (!currentImage) return;
        const id = currentImage.id ?? currentImage.Id ?? currentImage.savedImageId ?? currentImage.SavedImageId;
        const val = parseInt(document.getElementById("imgd-access-select").value);
        const res = await api(`/Images/${id}/set-accessibility`, "POST", { accessibility: val });
        if (res.ok) { toast("Accessibility updated", "ok"); await reloadCurrentImage(id); }
        else toast("Failed: " + (res.data?.error || res.status), "err");
    });

    document.getElementById("imgd-lock-btn").addEventListener("click", async () => {
        if (!currentImage) return;
        const id = currentImage.id ?? currentImage.Id ?? currentImage.savedImageId ?? currentImage.SavedImageId;
        const res = await api(`/Images/${id}/set-accessibility-lock`, "POST", { locked: true });
        if (res.ok) { toast("Accessibility locked", "ok"); await reloadCurrentImage(id); }
        else toast("Failed: " + (res.data?.error || res.status), "err");
    });

    document.getElementById("imgd-unlock-btn").addEventListener("click", async () => {
        if (!currentImage) return;
        const id = currentImage.id ?? currentImage.Id ?? currentImage.savedImageId ?? currentImage.SavedImageId;
        const res = await api(`/Images/${id}/set-accessibility-lock`, "POST", { locked: false });
        if (res.ok) { toast("Accessibility unlocked", "ok"); await reloadCurrentImage(id); }
        else toast("Failed: " + (res.data?.error || res.status), "err");
    });

    document.getElementById("imgd-desc-btn").addEventListener("click", async () => {
        if (!currentImage) return;
        const id = currentImage.id ?? currentImage.Id ?? currentImage.savedImageId ?? currentImage.SavedImageId;
        const desc = document.getElementById("imgd-desc-input").value;
        const res = await api(`/Images/${id}/set-description`, "POST", { description: desc || null });
        if (res.ok) { toast("Description updated", "ok"); await reloadCurrentImage(id); }
        else toast("Failed: " + (res.data?.error || res.status), "err");
    });

    document.getElementById("imgd-reset-cheers-btn").addEventListener("click", () => {
        if (!currentImage) return;
        const id = currentImage.id ?? currentImage.Id ?? currentImage.savedImageId ?? currentImage.SavedImageId;
        showConfirm("Reset Cheers", "Reset all cheers on this image to 0?", async () => {
            const res = await api(`/Images/${id}/reset-cheers`, "POST", {});
            if (res.ok) { toast("Cheers reset", "ok"); await reloadCurrentImage(id); }
            else toast("Failed: " + (res.data?.error || res.status), "err");
        });
    });

    document.getElementById("imgd-json-btn").addEventListener("click", () => {
        const block = document.getElementById("imgd-json-block");
        if (block.style.display === "none") {
            block.textContent = JSON.stringify(currentImage, null, 2);
            block.style.display = "block";
            document.getElementById("imgd-json-btn").textContent = "Hide JSON";
        } else {
            block.style.display = "none";
            document.getElementById("imgd-json-btn").textContent = "View Raw JSON";
        }
    });

    document.getElementById("imgd-devlock-btn").addEventListener("click", async () => {
        if (!currentImage) return;
        const id = currentImage.id ?? currentImage.Id ?? currentImage.savedImageId ?? currentImage.SavedImageId;
        showConfirm("Enable Dev Lock", "Lock this image so only developers can modify it bro?", async () => {
            const res = await api(`/Images/${id}/set-dev-lock`, "POST", { devLocked: true });
            if (res.ok) { toast("Dev lock enabled", "ok"); await reloadCurrentImage(id); }
            else toast("Failed: " + (res.data?.error || res.status), "err");
        });
    });

    document.getElementById("imgd-devunlock-btn").addEventListener("click", async () => {
        if (!currentImage) return;
        const id = currentImage.id ?? currentImage.Id ?? currentImage.savedImageId ?? currentImage.SavedImageId;
        const res = await api(`/Images/${id}/set-dev-lock`, "POST", { devLocked: false });
        if (res.ok) { toast("Dev lock removed", "ok"); await reloadCurrentImage(id); }
        else toast("Failed: " + (res.data?.error || res.status), "err");
    });

    document.getElementById("imgd-transfer-btn").addEventListener("click", () => {
        if (!currentImage) return;
        const id = currentImage.id ?? currentImage.Id ?? currentImage.savedImageId ?? currentImage.SavedImageId;
        const newPlayerId = parseInt(document.getElementById("imgd-transfer-id").value);
        if (!newPlayerId) { toast("Enter a valid player ID", "err"); return; }
        showConfirm("Transfer Image", `Transfer image ${id} to player ${newPlayerId}? The image will appear in their gallery.`, async () => {
            const res = await api(`/Images/${id}/transfer`, "POST", { newPlayerId });
            if (res.ok) {
                toast("Image transferred to player " + newPlayerId, "ok");
                document.getElementById("imgd-transfer-id").value = "";
                await reloadCurrentImage(id);
            } else toast("Failed: " + (res.data?.error || res.status), "err");
        });
    });

    document.getElementById("imgd-copy-name-btn").addEventListener("click", () => {
        if (!currentImage) return;
        const name = gv(currentImage, "imageName", "ImageName") ?? "";
        if (!name) { toast("No file name available", "err"); return; }
        navigator.clipboard.writeText(name).then(() => {
            toast("Copied: " + name, "ok");
        }).catch(() => {
            toast("Copy failed - try manually", "err");
        });
    });

    document.getElementById("imgd-delete-btn").addEventListener("click", () => {
        if (!currentImage) return;
        const id = currentImage.id ?? currentImage.Id ?? currentImage.savedImageId ?? currentImage.SavedImageId;
        showConfirm("Delete Image", "Permanently delete this image and all its cheers? This cannot be undone.", async () => {
            const res = await api(`/Images/${id}/delete`, "POST", {});
            if (res.ok) {
                toast("Image deleted", "ok");
                closeImageDetail();
                const card = document.querySelector(`.img-card[data-id="${id}"]`);
                if (card) card.remove();
            } else toast("Failed: " + (res.data?.error || res.status), "err");
        });
    });
}

async function reloadCurrentImage(id) {
    const res = await api(`/Images/${id}`);
    if (res.ok) {
        currentImage = res.data;
        renderImageDetail(res.data);
    }
}

function closeImageDetail() {
    document.getElementById("img-detail-overlay").style.display = "none";
    currentImage = null;
}

async function openImageDetail(imgData) {
    const id = imgData.id ?? imgData.Id ?? imgData.savedImageId ?? imgData.SavedImageId;
    const res = await api(`/Images/${id}`);
    const full = res.ok ? res.data : imgData;
    currentImage = full;
    renderImageDetail(full);
    const overlay = document.getElementById("img-detail-overlay");
    overlay.style.display = "flex";
}

function renderImageDetail(img) {
    const name = gv(img, "imageName", "ImageName");
    const id = img.id ?? img.Id ?? img.savedImageId ?? img.SavedImageId ?? "?";
    const playerId = gv(img, "playerId", "PlayerId") ?? "?";
    const roomId = gv(img, "roomId", "RoomId") ?? "?";
    const type = img.type ?? img.Type ?? 0;
    const access = img.accessibility ?? img.Accessibility ?? 0;
    const locked = img.accessibilityLocked ?? img.AccessibilityLocked ?? false;
    const cheerCount = img.cheerCount ?? img.CheerCount ?? 0;
    const commentCount = img.commentCount ?? img.CommentCount ?? 0;
    const createdAt = img.createdAt ?? img.CreatedAt;
    const desc = img.description ?? img.Description;
    const tagged = img.taggedPlayerIds ?? img.TaggedPlayerIds ?? [];
    const playerEventId = gv(img, "playerEventId", "PlayerEventId");

    document.getElementById("imgd-preview").src = `/imageserver/${name}`;

    const typeBadge = document.getElementById("imgd-type-badge");
    typeBadge.textContent = imageTypeName(type);

    const accessBadge = document.getElementById("imgd-access-badge");
    if (access === 1) {
        accessBadge.textContent = "Public";
        accessBadge.style.background = "rgba(39,174,96,0.18)";
        accessBadge.style.border = "1px solid rgba(39,174,96,0.4)";
        accessBadge.style.color = "var(--success)";
    } else {
        accessBadge.textContent = "Private";
        accessBadge.style.background = "rgba(192,57,43,0.12)";
        accessBadge.style.border = "1px solid rgba(192,57,43,0.3)";
        accessBadge.style.color = "var(--danger)";
    }

    infoGrid({
        "Image ID": id,
        "Player ID": playerId,
        "Room ID": roomId,
        "Player Event ID": playerEventId ?? "-",
        "Cheers": cheerCount,
        "Comments": commentCount,
        "Created": fmtDate(createdAt),
        "Description": desc ?? "-"
    }, "imgd-info-grid");

    const accessSel = document.getElementById("imgd-access-select");
    accessSel.value = String(access);

    const lockStatus = document.getElementById("imgd-lock-status");
    lockStatus.innerHTML = locked
        ? '<span class="badge badge-banned">Locked - player cannot change accessibility</span>'
        : '<span class="badge badge-offline">Unlocked</span>';

    document.getElementById("imgd-desc-input").value = desc ?? "";

    const devLocked = img.devLocked ?? img.DevLocked ?? false;
    const devLockEl = document.getElementById("imgd-devlock-status");
    if (devLockEl) {
        devLockEl.innerHTML = devLocked
            ? '<span class="badge badge-banned">Dev Locked - only developers can modify this image</span>'
            : '<span class="badge badge-offline">No Dev Lock</span>';
    }

    const filenameEl = document.getElementById("imgd-filename-display");
    if (filenameEl) {
        filenameEl.textContent = name ? name : "-";
    }

    const taggedEl = document.getElementById("imgd-tagged-players");
    taggedEl.innerHTML = "";
    if (tagged.length) {
        tagged.forEach(tpid => {
            const chip = document.createElement("span");
            chip.className = "chip";
            chip.textContent = tpid;
            chip.style.cursor = "pointer";
            chip.title = "Open player profile";
            chip.addEventListener("click", () => {
                closeImageDetail();
                loadPlayerDetail(tpid);
                switchPanel("players");
            });
            taggedEl.appendChild(chip);
        });
    } else {
        taggedEl.textContent = "No tagged players.";
    }

    const roomEl = document.getElementById("imgd-room-info");
    if (roomId && roomId !== "?" && roomId !== 0) {
        const btn = document.createElement("button");
        btn.className = "btn-ghost";
        btn.style.fontSize = "12px";
        btn.textContent = "Room ID: " + roomId + " - Open Room";
        btn.addEventListener("click", () => {
            closeImageDetail();
            loadRoomDetail(roomId);
            switchPanel("rooms");
        });
        roomEl.innerHTML = "";
        roomEl.appendChild(btn);
    } else {
        roomEl.textContent = "No room associated.";
    }

    const cheerEl = document.getElementById("imgd-cheer-info");
    cheerEl.textContent = cheerCount + " cheer" + (cheerCount === 1 ? "" : "s");

    const jsonBlock = document.getElementById("imgd-json-block");
    jsonBlock.style.display = "none";
    document.getElementById("imgd-json-btn").textContent = "View Raw JSON";
}

function renderImages(images) {
    const el = document.getElementById("img-results");
    el.innerHTML = "";

    const sorted = [...images].sort((a, b) => {
        const da = new Date(a.createdAt ?? a.CreatedAt ?? 0).getTime();
        const db = new Date(b.createdAt ?? b.CreatedAt ?? 0).getTime();
        return db - da;
    });

    const filtered = showPrivateImages
        ? sorted
        : sorted.filter(img => (img.accessibility ?? img.Accessibility ?? 0) === 1);

    if (!filtered.length) {
        el.innerHTML = '<div class="empty-state">No images found.</div>';
        return;
    }
    filtered.forEach(img => {
        const name = gv(img, "imageName", "ImageName", "fileName", "FileName");
        const id = img.savedImageId ?? img.SavedImageId ?? img.id ?? img.Id ?? "?";
        const playerId = gv(img, "playerId", "PlayerId") ?? "?";
        const access = img.accessibility ?? img.Accessibility ?? 0;
        const type = img.type ?? img.Type ?? img.savedImageType ?? img.SavedImageType ?? 0;
        const cheerCount = img.cheerCount ?? img.CheerCount ?? 0;
        const locked = img.accessibilityLocked ?? img.AccessibilityLocked ?? false;

        const card = document.createElement("div");
        card.className = "img-card";
        card.dataset.id = id;
        card.innerHTML = `
      <img src="/imageserver/${name}" onerror="this.src='/imageserver/DefaultPFP.png'" loading="lazy">
      <div class="img-card-meta">
        <div style="font-weight:700;color:var(--text);margin-bottom:2px">${imageTypeName(type)}</div>
        <div>ID: ${id} · Player: ${playerId}</div>
        <div style="display:flex;gap:4px;margin-top:4px;flex-wrap:wrap">
          <span style="padding:2px 5px;border-radius:3px;font-size:10px;background:${access === 1 ? "rgba(39,174,96,0.18)" : "rgba(192,57,43,0.12)"};color:${access === 1 ? "var(--success)" : "var(--danger)"};">${access === 1 ? "Public" : "Private"}</span>
          ${locked ? '<span style="padding:2px 5px;border-radius:3px;font-size:10px;background:rgba(0,0,0,0.1)">Locked</span>' : ""}
          ${cheerCount > 0 ? `<span style="padding:2px 5px;border-radius:3px;font-size:10px;background:rgba(242,101,34,0.12);color:var(--accent)">♥ ${cheerCount}</span>` : ""}
        </div>
      </div>`;
        card.addEventListener("click", () => openImageDetail(img));
        el.appendChild(card);
    });
}

function setupDatabasePanel() {
    const refreshBtn = document.getElementById("db-refresh-btn");
    if (refreshBtn) refreshBtn.addEventListener("click", loadDbStats);

    const execBtn = document.getElementById("db-exec-btn");
    if (execBtn) execBtn.addEventListener("click", async () => {
        const expression = document.getElementById("db-expr-input").value.trim();
        if (!expression) return;
        const res = await api("/db/execute", "POST", { expression });
        const block = document.getElementById("db-exec-result");
        block.classList.add("visible");
        block.textContent = res.ok ? JSON.stringify(res.data.result ?? res.data, null, 2) : (res.data?.error || "Error: " + res.status);
    });

    const giftBtn = document.getElementById("db-gift-tokens-btn");
    if (giftBtn) giftBtn.addEventListener("click", () => {
        const amount = parseInt(document.getElementById("db-gift-amount").value);
        const message = document.getElementById("db-gift-msg").value.trim() || null;
        if (!amount || amount <= 0) { toast("Enter a valid amount", "err"); return; }
        showConfirm("Gift All Players", `This lags the server a bit and also risky (Amount: ${amount})`, async () => {
            const res = await api("/players/gift-all-tokens", "POST", { amount, message });
            if (res.ok) toast(`Gifted ${amount} tokens to ${res.data?.playersGifted ?? "?"} players`, "ok");
            else toast("Failed: " + (res.data?.error || res.status), "err");
        });
    });

    loadDbStats();
}

async function loadDbStats() {
    const res = await api("/db/info");
    if (!res.ok) return;
    const d = res.data;
    document.getElementById("dbs-players").textContent = d.players ?? "-";
    document.getElementById("dbs-rooms").textContent = d.rooms ?? "-";
    document.getElementById("dbs-saves").textContent = d.saves ?? "-";
    document.getElementById("dbs-inventions").textContent = d.inventions ?? "-";
}

function setupModerationPanel() {
    document.getElementById("mod-ban-btn").addEventListener("click", () => {
        const id = parseInt(document.getElementById("mod-ban-id").value);
        const reason = document.getElementById("mod-ban-reason").value.trim() || "Banned by admin.";
        const duration = parseInt(document.getElementById("mod-ban-days").value) || 99999;
        if (!id) return;
        showConfirm("Ban Player", `Ban player ${id}?`, async () => {
            const res = await api(`/players/${id}/ban`, "POST", { reason, duration });
            if (res.ok) toast("Player " + id + " banned", "ok");
            else toast("Failed: " + (res.data?.error || res.status), "err");
        });
    });

    document.getElementById("mod-unban-btn").addEventListener("click", () => {
        const id = parseInt(document.getElementById("mod-unban-id").value);
        if (!id) return;
        showConfirm("Unban Player", `Unban player ${id}?`, async () => {
            const res = await api(`/players/${id}/unban`, "POST", {});
            if (res.ok) toast("Player " + id + " unbanned", "ok");
            else toast("Failed", "err");
        });
    });

    document.getElementById("mod-shove-btn").addEventListener("click", async () => {
        const pid = parseInt(document.getElementById("mod-shove-player").value);
        const roomId = parseInt(document.getElementById("mod-shove-room").value);
        const subRoomIdVal = document.getElementById("mod-shove-subroom").value;
        const subRoomId = subRoomIdVal ? parseInt(subRoomIdVal) : null;
        const instanceId = document.getElementById("mod-shove-instance").value.trim() || null;
        if (!pid || !roomId) return;
        const res = await api(`/players/${pid}/shove`, "POST", { roomId, subRoomId, instanceId });
        if (res.ok) toast(`Player ${pid} shoved to room ${roomId}`, "ok");
        else toast("Failed: " + (res.data?.error || res.status), "err");
    });

    document.getElementById("mod-hb-btn").addEventListener("click", () => {
        const id = parseInt(document.getElementById("mod-hb-id").value);
        if (!id) return;
        showConfirm("Clear Heartbeat", `Clear heartbeat for player ${id}?`, async () => {
            const res = await api(`/players/${id}/heartbeat-clear`, "POST", {});
            if (res.ok) toast("Heartbeat cleared", "ok");
            else toast("Failed", "err");
        });
    });

    document.getElementById("mod-dragall-btn").addEventListener("click", () => {
        const roomId = parseInt(document.getElementById("mod-dragall-room").value);
        const subRoomIdVal = document.getElementById("mod-dragall-subroom").value;
        const subRoomId = subRoomIdVal ? parseInt(subRoomIdVal) : null;
        const instanceId = document.getElementById("mod-dragall-instance").value.trim() || null;
        if (!roomId) return;
        showConfirm("Drag All Online", `Drag all online players to room ${roomId}?`, async () => {
            const res = await api("/players/shove-all", "POST", { roomId, subRoomId, instanceId });
            if (res.ok) toast(`Dragged ${res.data?.playersShoved ?? "?"} players to room ${roomId}`, "ok");
            else toast("Failed: " + (res.data?.error || res.status), "err");
        });
    });
}

let modBoardPlayer = null;

function setupModBoardPanel() {
    const mbId = () => modBoardPlayer?._id;

    async function mbLoadPlayer(id) {
        const res = await api(`/players/${id}`);
        if (!res.ok) { toast("Player not found", "err"); return; }
        modBoardPlayer = res.data;
        modBoardPlayer._id = gv(res.data, "accountId", "playerId", "AccountId", "PlayerId");
        renderModBoardCard(res.data);
    }

    async function mbLookup() {
        const q = document.getElementById("mb-lookup-input").value.trim();
        if (!q) return;
        if (/^\d+$/.test(q)) { await mbLoadPlayer(q); return; }
        const res = await api(`/players/search?q=${encodeURIComponent(q)}&take=50`);
        if (!res.ok) { toast("Search failed", "err"); return; }
        const results = res.data.Results || res.data.results || res.data || [];
        const exact = results.find(r => (gv(r, "username", "Username") || "").toLowerCase() === q.toLowerCase());
        const pick = exact || results[0];
        if (!pick) { toast("No player found", "err"); return; }
        await mbLoadPlayer(gv(pick, "accountId", "playerId", "AccountId", "PlayerId"));
    }

    document.getElementById("mb-lookup-btn").addEventListener("click", mbLookup);
    document.getElementById("mb-lookup-input").addEventListener("keydown", e => {
        if (e.key === "Enter") mbLookup();
    });

    document.getElementById("mb-close").addEventListener("click", () => {
        modBoardPlayer = null;
        document.getElementById("mb-player-card").style.display = "none";
    });

    document.getElementById("mb-voiceban-btn").addEventListener("click", async () => {
        if (!mbId()) return;
        const res = await api(`/players/${mbId()}/toxmod/ban`, "POST", { category: "Other", loud: false });
        if (res.ok) { toast("Voice ban issued", "ok"); await mbLoadPlayer(mbId()); }
        else toast("Failed: " + (res.data?.error || res.status), "err");
    });

    document.getElementById("mb-voiceunban-btn").addEventListener("click", async () => {
        if (!mbId()) return;
        const res = await api(`/players/${mbId()}/toxmod/unban`, "POST", { resetStrikes: false });
        if (res.ok) { toast("Voice ban lifted", "ok"); await mbLoadPlayer(mbId()); }
        else toast("Failed: " + (res.data?.error || res.status), "err");
    });

    document.getElementById("mb-ban-btn").addEventListener("click", () => {
        if (!mbId()) return;
        showConfirm("Ban Player", `Ban player ${mbId()}?`, async () => {
            const res = await api(`/players/${mbId()}/ban`, "POST", { reason: "Banned by a moderator.", duration: 99999 });
            if (res.ok) { toast("Player banned", "ok"); await mbLoadPlayer(mbId()); }
            else toast("Failed: " + (res.data?.error || res.status), "err");
        });
    });

    document.getElementById("mb-unban-btn").addEventListener("click", async () => {
        if (!mbId()) return;
        const res = await api(`/players/${mbId()}/unban`, "POST", {});
        if (res.ok) { toast("Player unbanned", "ok"); await mbLoadPlayer(mbId()); }
        else toast("Failed: " + (res.data?.error || res.status), "err");
    });

    document.getElementById("mb-junior-btn").addEventListener("click", async () => {
        if (!mbId()) return;
        const reason = document.getElementById("mb-junior-reason").value.trim() || null;
        const res = await api(`/players/${mbId()}/set-junior`, "POST", { isJunior: true, reason });
        if (res.ok) {
            toast("Comms revoked - player notified", "ok");
            document.getElementById("mb-junior-reason").value = "";
            await mbLoadPlayer(mbId());
        }
        else toast("Failed: " + (res.data?.error || res.status), "err");
    });

    document.getElementById("mb-unjunior-btn").addEventListener("click", async () => {
        if (!mbId()) return;
        const res = await api(`/players/${mbId()}/set-junior`, "POST", { isJunior: false });
        if (res.ok) { toast("Comms restored", "ok"); await mbLoadPlayer(mbId()); }
        else toast("Failed: " + (res.data?.error || res.status), "err");
    });

    document.getElementById("mb-close-game-btn").addEventListener("click", async () => {
        if (!mbId()) return;
        const res = await api(`/players/${mbId()}/close-game`, "POST", {});
        if (res.ok) toast("Game closed", "ok");
        else toast("Failed: " + (res.data?.error || res.status), "err");
    });

    document.getElementById("mb-hb-clear-btn").addEventListener("click", () => {
        if (!mbId()) return;
        showConfirm("Clear Heartbeat", `Clear heartbeat for player ${mbId()}?`, async () => {
            const res = await api(`/players/${mbId()}/heartbeat-clear`, "POST", {});
            if (res.ok) { toast("Heartbeat cleared", "ok"); await mbLoadPlayer(mbId()); }
            else toast("Failed", "err");
        });
    });

    document.getElementById("mb-full-profile-btn").addEventListener("click", async () => {
        if (!mbId()) return;
        await loadPlayerDetail(mbId());
        switchPanel("players");
    });

    document.getElementById("mb-dm-send-btn").addEventListener("click", async () => {
        if (!mbId()) return;
        const msg = document.getElementById("mb-dm-text").value.trim();
        if (!msg) { toast("Enter a message first", "err"); return; }
        const res = await api(`/players/${mbId()}/coach-dm`, "POST", { message: msg });
        if (res.ok) { toast("DM sent as Coach", "ok"); document.getElementById("mb-dm-text").value = ""; }
        else toast("Failed: " + (res.data?.error || res.status), "err");
    });

    document.getElementById("mb-coach-popup-btn").addEventListener("click", async () => {
        if (!mbId()) return;
        const msg = document.getElementById("mb-dm-text").value.trim();
        if (!msg) { toast("Enter a message first", "err"); return; }
        const res = await api(`/players/${mbId()}/send-coach-message`, "POST", { message: msg });
        if (res.ok) { toast("Popup sent as Coach", "ok"); document.getElementById("mb-dm-text").value = ""; }
        else toast("Failed: " + (res.data?.error || res.status), "err");
    });
}

function renderModBoardCard(p) {
    const id = p._id || gv(p, "accountId", "playerId", "AccountId", "PlayerId");
    const name = gv(p, "displayName", "username", "DisplayName", "Username") || "Unknown";
    const username = gv(p, "username", "Username") || "?";
    const img = gv(p, "profileImage", "ProfileImage");
    const hb = gv(p, "heartbeat") || {};
    const bd = gv(p, "moderationBlockDetails", "ModerationBlockDetails") || {};
    const tm = gv(p, "toxMod", "ToxMod") || {};

    document.getElementById("mb-pfp").src = pfpUrl(img);
    document.getElementById("mb-name").textContent = name;
    document.getElementById("mb-username").textContent = "@" + username;
    document.getElementById("mb-id").textContent = "ID: " + id;

    const badges = [];
    badges.push(hb.isOnline
        ? '<span class="badge badge-online">Online</span>'
        : '<span class="badge badge-offline">Offline</span>');
    if (bd.isBan || bd.IsBan) badges.push('<span class="badge badge-banned">Banned</span>');
    if (bd.isVoiceModAutoban || bd.IsVoiceModAutoban) badges.push('<span class="badge badge-banned">Voice Banned</span>');
    if (gv(p, "isJunior", "IsJunior")) badges.push('<span class="badge badge-banned">Comms Revoked (Junior)</span>');
    const strikes = tm.strikes ?? tm.Strikes ?? 0;
    if (strikes) badges.push(`<span class="badge badge-offline">${strikes} strike${strikes === 1 ? "" : "s"}</span>`);
    if (gv(p, "discordLinked", "DiscordLinked") || gv(p, "discordUserId", "DiscordUserId"))
        badges.push(`<span class="badge badge-online">Discord: ${discordLabel(p)}</span>`);
    document.getElementById("mb-badges").innerHTML = badges.join(" ");

    document.getElementById("mb-player-card").style.display = "block";
}

async function loadAnnouncements() {
    const el = document.getElementById("ann-list");
    if (!el) return;
    const res = await api("/announcements");
    const list = res.ok && Array.isArray(res.data) ? res.data : [];
    el.innerHTML = "";
    if (!list.length) {
        el.innerHTML = '<div class="muted">No active announcements.</div>';
        return;
    }
    list.forEach(a => {
        const id = gv(a, "announcementId", "AnnouncementId");
        const title = gv(a, "title", "Title") || "(no title)";
        const body = gv(a, "body", "Body") || "";
        const row = document.createElement("div");
        row.className = "field-row";
        row.style.alignItems = "center";

        const info = document.createElement("div");
        info.style.flex = "1";
        const t = document.createElement("strong");
        t.textContent = title;
        const b = document.createElement("div");
        b.className = "muted";
        b.style.fontSize = "12px";
        b.textContent = body.length > 120 ? body.slice(0, 120) + "…" : body;
        info.appendChild(t);
        info.appendChild(b);

        const btn = document.createElement("button");
        btn.className = "btn-danger";
        btn.textContent = "Cancel";
        btn.addEventListener("click", async () => {
            const r = await api(`/announcement/delete/${id}`, "POST", {});
            if (r.ok) { toast("Announcement cancelled", "ok"); await loadAnnouncements(); }
            else toast("Failed: " + (r.data?.error || r.status), "err");
        });

        row.appendChild(info);
        row.appendChild(btn);
        el.appendChild(row);
    });
}

function setupServerPanel() {
    document.getElementById("ann-send-btn").addEventListener("click", async () => {
        const title = document.getElementById("ann-title").value.trim();
        const body = document.getElementById("ann-body").value.trim();
        if (!title && !body) { toast("Title or body required", "err"); return; }
        const res = await api("/announcement", "POST", {
            AnnouncementType: document.getElementById("ann-type").value,
            Title: document.getElementById("ann-title").value.trim() || null,
            Body: document.getElementById("ann-body").value.trim() || null,
            ImageName: document.getElementById("ann-image").value.trim() || null,
            LinkType: document.getElementById("ann-linktype").value,
            LinkName: document.getElementById("ann-linkname").value.trim() || null,
            LinkUri: document.getElementById("ann-linkuri").value.trim() || null
        });
        if (res.ok) { toast("Announcement published", "ok"); await loadAnnouncements(); }
        else toast("Failed: " + (res.data?.error || res.status), "err");
    });
    document.getElementById("ann-clear-btn").addEventListener("click", () => {
        showConfirm("Clear Announcement", "Remove all announcements so players see none?", async () => {
            const res = await api("/announcement", "POST", { clear: true });
            if (res.ok) { toast("Announcement cleared", "ok"); await loadAnnouncements(); }
            else toast("Failed", "err");
        });
    });
    document.getElementById("ann-refresh-btn").addEventListener("click", loadAnnouncements);
    loadAnnouncements();

    (async () => {
        const res = await api("/community-banner");
        if (res.ok) {
            document.getElementById("cbb-message").value = res.data?.message || "";
            document.getElementById("cbb-url").value = res.data?.moreInfoUrl || "";
        }
    })();

    (async () => {
        const res = await api("/system-prompt");
        if (res.ok) {
            document.getElementById("ccsp-message").value = res.data?.message || "";
        }
    })();
    
	document.getElementById("crash-all-btn").addEventListener("click", () => {
    	showConfirm("Crash Everyone", "Send a crash websocket event to all online non-developer players?", async () => {
        	const res = await api("/players/close-all", "POST", {});
        	if (res.ok) toast(`Closed ${res.data?.playersCrashed ?? "?"} player(s)`, "ok");
        	else toast("Failed: " + (res.data?.error || res.status), "err");
    	});
	});

    document.getElementById("ccsp-send-btn").addEventListener("click", async () => {
        extendSession();
        const message = document.getElementById("ccsp-message").value.trim();
        try {
            const res = await fetch(BASE + "/system-prompt", {
                method: "POST",
                headers: {
                    "Content-Type": "application/json",
                    "X-Director-Token": sessionToken || ""
                },
                body: JSON.stringify(message)
            });
            if (res.status === 401 || res.status === 403) { forceLogout("Your session is no longer valid."); return; }
            if (res.ok) toast("System prompt updated", "ok");
            else toast("Failed: " + res.status, "err");
        } catch (e) {
            toast("Failed: " + e.message, "err");
        }
    });

    document.getElementById("cbb-send-btn").addEventListener("click", async () => {
        const message = document.getElementById("cbb-message").value.trim();
        const moreInfoUrl = document.getElementById("cbb-url").value.trim();
        const res = await api("/community-banner", "POST", { message, moreInfoUrl });
        if (res.ok) toast("Community board banner updated", "ok");
        else toast("Failed: " + (res.data?.error || res.status), "err");
    });

    document.getElementById("db-maint-btn").addEventListener("click", () => {
        const mins = parseInt(document.getElementById("db-maint-minutes").value) || 10;
        showConfirm("Send Maintenance Alert", `Broadcast maintenance alert for ${mins} minutes to all connected clients?`, async () => {
            const res = await api("/maintenance/schedule", "POST", { minutes: mins });
            if (res.ok) toast("Maintenance alert sent", "ok");
            else toast("Failed", "err");
        });
    });

    document.getElementById("db-clear-rooms-btn").addEventListener("click", () => {
        showConfirm("Clear Rooms", "Clear all non-dorm rooms and re-import from ImportRooms.json?", async () => {
            const res = await api("/rooms/clear", "POST", {});
            if (res.ok) toast("Rooms cleared and re-imported", "ok");
            else toast("Failed", "err");
        });
    });

    document.getElementById("db-reset-btn").addEventListener("click", () => {
        showConfirm("WIPE Database", "This will permanently wipe ALL players, rooms, inventions, friends, and events. This cannot be undone.", async () => {
            const res = await api("/db/reset", "POST", {});
            if (res.ok) { toast("Database wiped", "ok"); await loadDbStats(); }
            else toast("Failed", "err");
        });
    });

    document.getElementById("coach-btn").addEventListener("click", async () => {
        const coachMsgValue = document.getElementById("coach-msg").value;
        const res = await api("/coach/send-msg", "POST", { message: coachMsgValue });
        if (res.ok) {
            toast("Message sent!", "ok");
            document.getElementById("coach-msg").value = "";
            await loadDbStats();
        } else {
            toast("Failed", "err");
        }
    });
}

function switchPanel(name) {
    document.querySelectorAll(".nav-btn").forEach(b => b.classList.remove("active"));
    document.querySelectorAll(".panel").forEach(p => p.classList.remove("active"));
    document.querySelector(`.nav-btn[data-panel="${name}"]`)?.classList.add("active");
    document.getElementById("panel-" + name)?.classList.add("active");
}

function fmtDate(val) {
    if (!val) return "-";
    try { return new Date(val).toLocaleString(); } catch { return val; }
}

function accessibilityName(v) {
    const m = { 0: "Private", 1: "Public", 2: "Unlisted", 3: "Dev Only", 4: "Dev Unlisted" };
    return m[v] ?? v ?? "-";
}
// --- Anti-Cheat ---------------------------------------------------------------
// Hardware bans, and the machine link table behind them. The panel is built around
// one idea: a machine ID is shared by everyone who has used that computer, so the
// linked account list is shown first and the ban button sits underneath it.

function setupAntiCheatPanel() {
    document.getElementById("ac-search-btn").addEventListener("click", acSearch);
    document.getElementById("ac-search-input").addEventListener("keydown", e => {
        if (e.key === "Enter") acSearch();
    });
    document.getElementById("ac-list-banned-btn").addEventListener("click", acListBanned);

    document.getElementById("ac-ban-btn").addEventListener("click", () => {
        const hwid = document.getElementById("ac-ban-hwid").value.trim();
        const playerId = parseInt(document.getElementById("ac-ban-player").value) || null;
        const reason = document.getElementById("ac-ban-reason").value.trim();
        const daysVal = document.getElementById("ac-ban-days").value.trim();
        const durationDays = daysVal ? parseInt(daysVal) : null;

        if (!hwid && !playerId) { toast("Give a machine ID or a player ID", "err"); return; }
        if (!reason) { toast("A reason is required", "err"); return; }

        const what = hwid ? `machine ${acShort(hwid)}` : `every machine player ${playerId} uses`;
        const term = durationDays ? `${durationDays} day(s)` : "permanently";

        showConfirm("Ban Machine",
            `Ban ${what} ${term}? Every account linked to it will be banned too.`,
            () => acBan({ hwid: hwid || null, playerId, reason, durationDays }));
    });

    document.getElementById("ac-unban-btn").addEventListener("click", () => {
        const hwid = document.getElementById("ac-unban-hwid").value.trim();
        if (!hwid) return;

        showConfirm("Lift Hardware Ban",
            `Lift the ban on machine ${acShort(hwid)}? Accounts banned with it stay banned.`,
            async () => {
                const res = await api("/anticheat/machines/unban", "POST", { hwid });
                if (res.ok) { toast("Hardware ban lifted", "ok"); acListBanned(); }
                else toast("Failed: " + (res.data?.error || res.status), "err");
            });
    });
}

async function acBan(body) {
    const res = await api("/anticheat/machines/ban", "POST", body);

    if (!res.ok) { toast("Failed: " + (res.data?.error || res.status), "err"); return; }

    const machines = res.data?.hwids?.length ?? 0;
    const accounts = res.data?.bannedAccounts?.length ?? 0;
    toast(`Banned ${machines} machine(s), ${accounts} account(s)`, "ok");

    acListBanned();
}

async function acSearch() {
    const raw = document.getElementById("ac-search-input").value.trim();
    if (!raw) return;

    // A machine ID is a long hash and a player ID is a number, so the box takes either
    // and works out which one it was given.
    const query = /^\d+$/.test(raw) ? `playerId=${raw}` : `hwid=${encodeURIComponent(raw)}`;

    const res = await api(`/anticheat/machines?${query}`);
    if (!res.ok) { toast("Lookup failed: " + (res.data?.error || res.status), "err"); return; }

    acRenderMachines(res.data, "No machine on record for that.");
}

async function acListBanned() {
    const res = await api("/anticheat/machines/banned");
    if (!res.ok) { toast("Failed to load banned machines", "err"); return; }

    acRenderMachines(res.data, "No machines are hardware banned.");
}

function acShort(hwid) {
    return !hwid ? "?" : (hwid.length > 20 ? hwid.slice(0, 10) + "…" + hwid.slice(-6) : hwid);
}

function acDate(v) {
    if (!v) return "-";
    const d = new Date(v);
    return isNaN(d) ? "-" : d.toLocaleString();
}

function acRenderMachines(machines, emptyText) {
    const el = document.getElementById("ac-results");
    el.innerHTML = "";

    if (!machines || machines.length === 0) {
        el.innerHTML = `<div class="empty-state">${emptyText}</div>`;
        return;
    }

    const list = document.createElement("div");
    list.className = "results-list";

    machines.forEach(m => list.appendChild(acMachineCard(m)));
    el.appendChild(list);
}

function acMachineCard(m) {
    const card = document.createElement("div");
    card.className = "ac-machine" + (m.banned ? " ac-machine-banned" : "");

    const players = m.players || [];
    const bannedCount = players.filter(p => p.banned).length;

    const head = document.createElement("div");
    head.className = "ac-machine-head";
    head.innerHTML = `
      <div>
        <div class="ac-hwid" title="${m.hwid || ""}">${acShort(m.hwid)}</div>
        <div class="muted">Seen ${acDate(m.firstSeen)} → ${acDate(m.lastSeen)}</div>
      </div>
      <div class="ac-machine-badges">
        ${m.banned ? '<span class="badge badge-banned">Banned</span>' : ""}
        <span class="chip">${players.length} account${players.length === 1 ? "" : "s"}</span>
        ${bannedCount ? `<span class="chip">${bannedCount} already banned</span>` : ""}
      </div>`;
    card.appendChild(head);

    if (m.banned) {
        const ban = document.createElement("div");
        ban.className = "ac-ban-detail";
        ban.innerHTML = `
          <div><strong>Reason:</strong> ${m.banReason || "-"}</div>
          <div><strong>By:</strong> ${m.bannedBy || "-"} · ${acDate(m.bannedAt)}</div>
          <div><strong>Expires:</strong> ${m.banExpiresAt ? acDate(m.banExpiresAt) : "never"}</div>`;
        card.appendChild(ban);
    }

    const accounts = document.createElement("div");
    accounts.className = "ac-accounts";

    if (players.length === 0) {
        accounts.innerHTML = '<div class="muted">No accounts linked yet.</div>';
    } else {
        players.forEach(p => {
            const chip = document.createElement("button");
            chip.className = "ac-account" + (p.banned ? " ac-account-banned" : "");
            chip.innerHTML = `
              <img src="${pfpUrl(p.profileImage)}" onerror="this.src='/imageserver/DefaultPFP.png'">
              <span>${p.username || "Unknown"}</span>
              <span class="muted">${p.playerId}</span>`;
            // Straight through to the player, because the next thing a moderator wants after
            // seeing a name on a machine is that account.
            chip.addEventListener("click", () => {
                document.querySelector('.nav-btn[data-panel="players"]').click();
                loadPlayerDetail(p.playerId);
            });
            accounts.appendChild(chip);
        });
    }

    card.appendChild(accounts);
    card.appendChild(acMachineActions(m));

    return card;
}

function acMachineActions(m) {
    const row = document.createElement("div");
    row.className = "ac-machine-actions";

    const fill = document.createElement("button");
    fill.className = "btn-ghost";
    fill.textContent = "Copy ID to form";
    fill.addEventListener("click", () => {
        document.getElementById("ac-ban-hwid").value = m.hwid;
        document.getElementById("ac-unban-hwid").value = m.hwid;
        toast("Machine ID copied into the ban form");
    });
    row.appendChild(fill);

    if (m.banned) {
        const unban = document.createElement("button");
        unban.className = "btn-action";
        unban.textContent = "Lift ban";
        unban.addEventListener("click", () => {
            showConfirm("Lift Hardware Ban",
                `Lift the ban on machine ${acShort(m.hwid)}? Accounts banned with it stay banned.`,
                async () => {
                    const res = await api("/anticheat/machines/unban", "POST", { hwid: m.hwid });
                    if (res.ok) { toast("Hardware ban lifted", "ok"); acListBanned(); }
                    else toast("Failed: " + (res.data?.error || res.status), "err");
                });
        });
        row.appendChild(unban);
    } else {
        const ban = document.createElement("button");
        ban.className = "btn-danger";
        ban.textContent = "Ban this machine";
        ban.addEventListener("click", () => {
            const reason = document.getElementById("ac-ban-reason").value.trim();
            if (!reason) {
                toast("Fill in a reason below first", "err");
                document.getElementById("ac-ban-reason").focus();
                return;
            }

            const daysVal = document.getElementById("ac-ban-days").value.trim();
            const durationDays = daysVal ? parseInt(daysVal) : null;
            const count = (m.players || []).length;

            showConfirm("Ban Machine",
                `Ban machine ${acShort(m.hwid)}? ${count} linked account(s) will be banned with it.`,
                () => acBan({ hwid: m.hwid, playerId: null, reason, durationDays }));
        });
        row.appendChild(ban);
    }

    return row;
}
