import { getCurrentUserToken } from "/auth.js";

const apiBase = new URL("../../api", window.location.href).pathname.replace(/\/$/, "");
const rankingListEl = document.querySelector("[data-ranking-list]");
const myStatsEl = document.querySelector("[data-my-stats]");
const cacheNextEl = document.querySelector("[data-cache-next]");
let cacheMeta = null;
let refreshTimer = null;
let refreshing = false;

function escapeHtml(value) {
  return String(value ?? "")
    .replaceAll("&", "&amp;")
    .replaceAll("<", "&lt;")
    .replaceAll(">", "&gt;")
    .replaceAll('"', "&quot;")
    .replaceAll("'", "&#39;");
}

function percent(value) {
  return `${Math.round((Number(value) || 0) * 100)}%`;
}

function displayWinRate(player) {
  return Number(player.gamesStarted || 0) < 3 ? "-" : percent(player.winRate);
}

function pluralize(value, singular, plural) {
  return `${value} ${value === 1 ? singular : plural}`;
}

function timeText(ms) {
  const totalSeconds = Math.max(0, Math.ceil(ms / 1000));
  const minutes = Math.floor(totalSeconds / 60);
  const seconds = totalSeconds % 60;

  if (minutes <= 0) return `${seconds}s`;
  if (seconds === 0) return `${minutes}min`;
  return `${minutes}min ${seconds}s`;
}

async function request(path, options = {}) {
  const response = await fetch(`${apiBase}${path}`, options);
  if (!response.ok) throw new Error(`HTTP ${response.status}`);
  return response.json();
}

function renderRanking(data) {
  cacheMeta = {
    generatedAt: Date.parse(data.generatedAt),
    expiresAt: Date.parse(data.expiresAt)
  };
  updateCacheClock();

  if (!data.players?.length) {
    rankingListEl.innerHTML = `<div class="empty">Nenhuma partida ranqueada ainda.</div>`;
    return;
  }

  rankingListEl.innerHTML = data.players.map((player, index) => `
    <div class="ranking-row">
      <strong>${index + 1}</strong>
      <div class="player">
        <div class="name">${escapeHtml(player.displayName || "Jogador")}</div>
      </div>
      <div class="number">${player.gamesStarted ?? 0}</div>
      <div class="number">${player.wins ?? 0}</div>
      <div class="rate">${displayWinRate(player)}</div>
    </div>
  `).join("");
}

function updateCacheClock() {
  if (!cacheMeta?.generatedAt || !cacheMeta?.expiresAt) return;

  const now = Date.now();
  const remainingMs = cacheMeta.expiresAt - now;

  if (remainingMs > 0) {
    cacheNextEl.textContent = `Atualiza em ${timeText(remainingMs)}`;
    return;
  }

  cacheNextEl.textContent = "Atualizando...";
  if (!refreshing) {
    loadRanking();
  }
}

async function renderMyStats() {
  try {
    const token = await getCurrentUserToken();
    if (!token) return;

    const player = await request("/ranking/me", {
      headers: {
        authorization: `Bearer ${token}`
      }
    });

    if (!player) return;

    const games = Number(player.gamesStarted ?? 0);
    const wins = Number(player.wins ?? 0);

    myStatsEl.innerHTML = `
      <span>Minha conta</span>
      <strong>${escapeHtml(player.displayName || "Jogador")}: ${pluralize(games, "partida", "partidas")}, ${pluralize(wins, "vitoria", "vitorias")}, ${displayWinRate(player)} de aproveitamento.</strong>
    `;
    myStatsEl.hidden = false;
  } catch {
    myStatsEl.hidden = true;
  }
}

async function loadRanking() {
  refreshing = true;
  try {
    const ranking = await request("/ranking");
    renderRanking(ranking);
    await renderMyStats();
  } catch (error) {
    rankingListEl.innerHTML = `<div class="error">Nao consegui carregar o ranking agora.</div>`;
  } finally {
    refreshing = false;
  }
}

loadRanking();
refreshTimer = window.setInterval(updateCacheClock, 1000);
window.addEventListener("pagehide", () => {
  window.clearInterval(refreshTimer);
});
