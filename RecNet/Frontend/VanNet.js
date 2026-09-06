const IMAGE_SERVER = "https://reloxa.xyz/imageserver/";

function getParam(name) {
  return new URLSearchParams(window.location.search).get(name);
}

function formatDate(dateStr) {
  const d = new Date(dateStr);
  return d.toLocaleDateString("en-US", { month: "short", day: "numeric", year: "numeric" });
}

function truncate(str, max) {
  if (!str) return "";
  return str.length > max ? str.slice(0, max) + "..." : str;
}

async function loadActionLink(code) {
  const res = await fetch(`/actionlink/${code}`);
  if (!res.ok) return null;
  return await res.json();
}

async function loadRoom(roomId) {
  const res = await fetch(`/rooms/${roomId}`);
  if (!res.ok) return null;
  return await res.json();
}

async function loadPlayer(playerId) {
  const res = await fetch(`/api/playerprofile/bulk-account-by-id?id=${playerId}`);
  if (!res.ok) return null;
  const data = await res.json();
  if (Array.isArray(data)) return data[0] || null;
  return data;
}

function showError() {
  document.getElementById("loading-state").classList.add("hidden");
  document.getElementById("error-state").classList.add("visible");
}

function showMain() {
  document.getElementById("loading-state").classList.add("hidden");
  document.getElementById("main-content").classList.add("visible");
}

async function init() {
  const code = getParam("a");
  if (!code) { showError(); return; }

  let linkData;
  try { linkData = await loadActionLink(code); } catch { showError(); return; }
  if (!linkData || !linkData.isValid) { showError(); return; }

  let parsedData = {};
  try { parsedData = JSON.parse(linkData.data); } catch {}

  const codeType = parsedData.codeType ?? linkData.codeType ?? 0;
  if (codeType !== 5) { showError(); return; }

  const roomId = parsedData.roomId ?? parsedData.extraDataId ?? linkData.extraDataId;
  if (!roomId) { showError(); return; }

  let room;
  try { room = await loadRoom(roomId); } catch { showError(); return; }
  if (!room) { showError(); return; }

  let creator;
  try { creator = await loadPlayer(room.creatorAccountId ?? room.CreatorAccountId); } catch {}

  const roomName = room.name ?? room.Name ?? "Unknown Room";
  const description = room.description ?? room.Description ?? "";
  const imageName = room.imageName ?? room.ImageName ?? "";
  const stats = room.stats ?? room.Stats ?? {};
  const cheerCount = stats.cheerCount ?? stats.CheerCount ?? 0;
  const visitCount = stats.visitCount ?? stats.VisitCount ?? 0;
  const favoriteCount = stats.favoriteCount ?? stats.FavoriteCount ?? 0;
  const createdAt = room.createdAt ?? room.CreatedAt ?? "";
  const creatorName = creator ? (creator.displayName ?? creator.username ?? "") : "";
  const creatorUsername = creator ? (creator.username ?? "") : "";
  const expiresAt = parsedData.expiresAt ?? linkData.expiresAt ?? "";

  document.getElementById("room-thumbnail").src = imageName ? `${IMAGE_SERVER}${imageName}` : "";
  document.getElementById("room-name").textContent = `^${roomName}`;
  document.getElementById("room-description").textContent = truncate(description, 200);
  document.getElementById("cheer-count").textContent = `${cheerCount.toLocaleString()} cheers`;
  document.getElementById("visitor-count").textContent = `${visitCount.toLocaleString()} visitors`;
  document.getElementById("favorite-count").textContent = `${favoriteCount.toLocaleString()} favorites`;

  const metaParts = [];
  if (creatorUsername) metaParts.push(`@${creatorUsername}`);
  if (expiresAt) metaParts.push(`Expires ${formatDate(expiresAt)}`);
  document.getElementById("topbar-meta").innerHTML = metaParts.join("<br>");

  const thumbMeta = [];
  if (createdAt) thumbMeta.push(`(${formatDate(createdAt)})`);
  if (creatorName) thumbMeta.push(`@${creatorName}`);
  document.getElementById("thumbnail-meta").textContent = thumbMeta.join("  ");

  document.getElementById("join-btn").addEventListener("click", () => {
    window.location.href = `recroom://actionlink/${code}`;
  });

  showMain();
}

class ActionLinkWebsiteController {
  constructor() {
    this.code = getParam("a");
  }

  reload() {
    document.getElementById("main-content").classList.remove("visible");
    document.getElementById("error-state").classList.remove("visible");
    document.getElementById("loading-state").classList.remove("hidden");
    init();
  }

  getCode() {
    return this.code;
  }
}

window.ActionLinkWebsiteController = new ActionLinkWebsiteController();

init();