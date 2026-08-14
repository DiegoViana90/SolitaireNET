const apiBase = new URL("../../api", window.location.href).pathname.replace(/\/$/, "");
const sessionKey = "paciencia-plus-four-session";

const colors = {
  red: "Vermelho",
  blue: "Azul",
  green: "Verde",
  yellow: "Amarelo",
  wild: "Livre"
};

const state = {
  roomCode: null,
  playerId: null,
  playerSide: null,
  game: null,
  selectedColor: "red",
  busy: false,
  pollTimer: null,
  message: ""
};

const lobbyEl = document.querySelector("#lobby");
const tableEl = document.querySelector("#table");
const roomInfoEl = document.querySelector("#room-info");
const statusEl = document.querySelector("#status");
const createRoomEl = document.querySelector("#create-room");
const randomRoomEl = document.querySelector("#random-room");
const roomCodeEl = document.querySelector("#room-code");
const joinCodeEl = document.querySelector("#join-code");
const leaveRoomEl = document.querySelector("#leave-room");
const drawCardEl = document.querySelector("#draw-card");
const drawCountEl = document.querySelector("#draw-count");
const discardCardEl = document.querySelector("#discard-card");
const handEl = document.querySelector("#hand");
const opponentCardsEl = document.querySelector("#opponent-cards");
const opponentCountEl = document.querySelector("#opponent-count");
const myScoreEl = document.querySelector("#my-score");
const opponentScoreEl = document.querySelector("#opponent-score");
const roundNumberEl = document.querySelector("#round-number");
const nextRoundEl = document.querySelector("#next-round");
const colorPickerEl = document.querySelector("#color-picker");
const toastStackEl = document.querySelector("#toast-stack");

async function request(path, options = {}) {
  const response = await fetch(`${apiBase}${path}`, {
    headers: { "content-type": "application/json" },
    ...options
  });

  const body = await response.json().catch(() => ({}));
  if (!response.ok) {
    throw new Error(body.error || `HTTP ${response.status}`);
  }

  return body;
}

async function createRoom() {
  await joinFromResult(request("/plus-four/rooms", { method: "POST" }));
}

async function findRandomRoom() {
  setMessage("Procurando sala aleatoria...");
  render();
  await joinFromResult(request("/plus-four/matchmaking", { method: "POST" }));
}

async function joinRoomByCode() {
  const code = roomCodeEl.value.trim().toUpperCase();
  if (!code) {
    setMessage("Informe o codigo da sala.");
    render();
    return;
  }

  await joinFromResult(request(`/plus-four/rooms/${encodeURIComponent(code)}/join`, { method: "POST" }));
}

async function joinFromResult(promise) {
  if (state.busy) return;

  state.busy = true;
  setControlsEnabled(false);
  try {
    applyJoinResult(await promise);
  } catch (error) {
    setMessage(error.message);
    render();
  } finally {
    state.busy = false;
    setControlsEnabled(true);
  }
}

function applyJoinResult(result, options = {}) {
  state.roomCode = result.roomCode;
  state.playerId = result.playerId;
  state.playerSide = result.playerSide;
  state.game = result.state;

  if (result.waiting) {
    setMessage("Aguardando outro jogador entrar.");
  } else if (options.restored) {
    setMessage("Voce voltou para a sala.");
  } else {
    setMessage("Voce entrou na sala.");
    showToast("Voce entrou na sala.");
  }

  localStorage.setItem(sessionKey, JSON.stringify({
    roomCode: state.roomCode,
    playerId: state.playerId
  }));

  startPolling();
  render();
}

async function restoreSession() {
  const raw = localStorage.getItem(sessionKey);
  if (!raw) {
    render();
    return;
  }

  try {
    const saved = JSON.parse(raw);
    if (!saved.roomCode || !saved.playerId) {
      clearSession();
      render();
      return;
    }

    const result = await request(`/plus-four/rooms/${encodeURIComponent(saved.roomCode)}?playerId=${encodeURIComponent(saved.playerId)}`);
    applyJoinResult(result, { restored: true });
  } catch {
    clearSession();
    render();
  }
}

function startPolling() {
  stopPolling();
  state.pollTimer = window.setInterval(refreshRoom, 1200);
}

function stopPolling() {
  if (state.pollTimer) {
    window.clearInterval(state.pollTimer);
    state.pollTimer = null;
  }
}

async function refreshRoom() {
  if (!state.roomCode || !state.playerId || state.busy) return;

  try {
    const previousEvent = state.game?.lastEvent?.id;
    const result = await request(`/plus-four/rooms/${encodeURIComponent(state.roomCode)}?playerId=${encodeURIComponent(state.playerId)}`);
    state.game = result.state;
    state.playerSide = result.playerSide;

    if (state.game?.lastEvent?.id && state.game.lastEvent.id !== previousEvent) {
      showEventToast(state.game.lastEvent);
    }

    if (state.game?.canceled) {
      clearSession();
      stopPolling();
    }

    render();
  } catch (error) {
    setMessage(error.message);
    render();
  }
}

function render() {
  const inRoom = Boolean(state.roomCode && state.playerId && state.game);
  lobbyEl.hidden = inRoom;
  tableEl.hidden = !inRoom;
  roomInfoEl.hidden = !inRoom;
  leaveRoomEl.hidden = !inRoom;
  statusEl.textContent = getStatus();

  if (!inRoom) return;

  const game = state.game;
  roomInfoEl.textContent = `Sala ${state.roomCode} | Voce e Jogador ${state.playerSide === "one" ? "1" : "2"}`;
  drawCountEl.textContent = game.drawCount;
  opponentCountEl.textContent = `${game.opponentCount} carta${game.opponentCount === 1 ? "" : "s"}`;
  myScoreEl.textContent = state.playerSide === "one" ? game.oneScore : game.twoScore;
  opponentScoreEl.textContent = state.playerSide === "one" ? game.twoScore : game.oneScore;
  roundNumberEl.textContent = game.round;
  nextRoundEl.hidden = !game.roundWinner || game.matchWinner;
  drawCardEl.disabled = !canAct();

  discardCardEl.replaceChildren(cardEl(game.topCard, { large: true }));
  handEl.replaceChildren(...game.hand.map((card) => cardEl(card)));
  opponentCardsEl.replaceChildren(...Array.from({ length: game.opponentCount }, () => cardBackEl()));

  colorPickerEl.querySelectorAll("button").forEach((button) => {
    button.classList.toggle("selected", button.dataset.color === state.selectedColor);
  });
}

function cardBackEl() {
  const el = document.createElement("div");
  el.className = "card-back";
  el.setAttribute("aria-hidden", "true");
  return el;
}

function cardEl(card, options = {}) {
  const el = document.createElement(options.large ? "div" : "button");
  el.className = `card ${card.color}`;
  if (card.playedColor) el.classList.add(`chosen-${card.playedColor}`);
  el.dataset.color = card.color;
  el.dataset.value = card.value;
  el.innerHTML = `<span>${label(card)}</span><strong>${card.value}</strong>`;

  if (!options.large) {
    el.type = "button";
    el.disabled = !canAct() || !canPlay(card);
    el.addEventListener("click", () => playCard(card));
  }

  return el;
}

async function playCard(card) {
  if (!canAct() || !canPlay(card)) return;
  if (card.color === "wild" && !state.selectedColor) {
    setMessage("Escolha uma cor para essa carta.");
    render();
    return;
  }

  await sendAction({
    type: "play",
    cardId: card.id,
    color: card.color === "wild" ? state.selectedColor : null
  });
}

async function drawCard() {
  if (!canAct()) return;
  await sendAction({ type: "draw" });
}

async function nextRound() {
  if (!state.game?.roundWinner || state.game?.matchWinner) return;
  await sendAction({ type: "next-round" });
}

async function sendAction(action) {
  if (state.busy) return;

  state.busy = true;
  try {
    const result = await request(`/plus-four/rooms/${encodeURIComponent(state.roomCode)}/actions`, {
      method: "POST",
      body: JSON.stringify({ playerId: state.playerId, ...action })
    });
    state.game = result.state;
    render();
  } catch (error) {
    setMessage(error.message);
    render();
  } finally {
    state.busy = false;
  }
}

async function leaveRoom() {
  if (!state.roomCode || !state.playerId) return;

  try {
    await request(`/plus-four/rooms/${encodeURIComponent(state.roomCode)}/leave`, {
      method: "POST",
      body: JSON.stringify({ playerId: state.playerId })
    });
  } catch {
    // Leaving should always clear the local session.
  }

  clearSession();
  stopPolling();
  render();
}

function canAct() {
  return Boolean(
    state.game?.ready &&
    !state.game?.canceled &&
    !state.game?.roundWinner &&
    state.game?.turn === state.playerSide &&
    !state.busy);
}

function canPlay(card) {
  const top = state.game?.topCard;
  if (!top) return false;
  return card.color === "wild" ||
    card.color === state.game.currentColor ||
    card.value === top.value;
}

function getStatus() {
  if (!state.game) return state.message || "Crie uma sala ou procure uma partida.";
  if (state.game.canceled) return "Sala encerrada.";
  if (!state.game.ready) return `${state.message || "Aguardando outro jogador entrar."} Codigo: ${state.roomCode}`;
  if (state.game.matchWinner) return state.game.matchWinner === state.playerSide ? "Voce venceu a partida!" : "Adversario venceu a partida.";
  if (state.game.roundWinner) return state.game.roundWinner === state.playerSide ? "Voce venceu a rodada." : "Adversario venceu a rodada.";
  if (state.game.turn === state.playerSide) return "Sua vez.";
  return "Vez do adversario.";
}

function label(card) {
  if (card.color === "wild") return card.playedColor ? colors[card.playedColor] : "Livre";
  return colors[card.color];
}

function setMessage(text) {
  state.message = text;
}

function setControlsEnabled(enabled) {
  createRoomEl.disabled = !enabled;
  randomRoomEl.disabled = !enabled;
  joinCodeEl.disabled = !enabled;
  roomCodeEl.disabled = !enabled;
}

function clearSession() {
  localStorage.removeItem(sessionKey);
  state.roomCode = null;
  state.playerId = null;
  state.playerSide = null;
  state.game = null;
  state.message = "";
}

function showEventToast(event) {
  if (event.playerSide === state.playerSide) return;
  if (event.type === "play") showToast("Adversario jogou uma carta.");
  if (event.type === "draw") showToast("Adversario comprou uma carta.");
  if (event.type === "round-win") showToast("Rodada encerrada.");
}

function showToast(text) {
  const toast = document.createElement("div");
  toast.className = "toast";
  toast.textContent = text;
  toastStackEl.append(toast);
  window.setTimeout(() => toast.remove(), 3200);
}

createRoomEl.addEventListener("click", createRoom);
randomRoomEl.addEventListener("click", findRandomRoom);
joinCodeEl.addEventListener("click", joinRoomByCode);
roomCodeEl.addEventListener("input", () => {
  roomCodeEl.value = roomCodeEl.value.toUpperCase();
});
roomCodeEl.addEventListener("keydown", (event) => {
  if (event.key === "Enter") joinRoomByCode();
});
drawCardEl.addEventListener("click", drawCard);
nextRoundEl.addEventListener("click", nextRound);
leaveRoomEl.addEventListener("click", leaveRoom);
colorPickerEl.addEventListener("click", (event) => {
  const button = event.target.closest("button[data-color]");
  if (!button) return;
  state.selectedColor = button.dataset.color;
  render();
});

restoreSession();
