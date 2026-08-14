const apiBase = new URL("../../api", window.location.href).pathname.replace(/\/$/, "");
const sessionKey = "solitairenet-chess-session";
const players = {
  white: { label: "brancas" },
  black: { label: "pretas" }
};
const pieceSymbols = {
  "white-king": "♔",
  "white-queen": "♕",
  "white-rook": "♖",
  "white-bishop": "♗",
  "white-knight": "♘",
  "white-pawn": "♙",
  "black-king": "♚",
  "black-queen": "♛",
  "black-rook": "♜",
  "black-bishop": "♝",
  "black-knight": "♞",
  "black-pawn": "♟"
};

const state = {
  roomCode: null,
  playerId: null,
  playerSide: null,
  game: null,
  selected: null,
  busy: false,
  pollTimer: null,
  message: "",
  noticeUntil: null,
  lastMoveId: null,
  lastTurn: null,
  lastReady: false,
  lastDisconnectedSide: null,
  promotionResolver: null
};

const lobbyEl = document.querySelector("#lobby");
const roomInfoEl = document.querySelector("#room-info");
const createRoomEl = document.querySelector("#create-room");
const randomRoomEl = document.querySelector("#random-room");
const roomCodeEl = document.querySelector("#room-code");
const joinCodeEl = document.querySelector("#join-code");
const boardEl = document.querySelector("#board");
const statusEl = document.querySelector("#status");
const newGameEl = document.querySelector("#new-game");
const toastStackEl = document.querySelector("#toast-stack");
const promotionModalEl = document.querySelector("#promotion-modal");

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
  await joinFromResult(request("/chess/rooms", { method: "POST" }));
}

async function findRandomRoom() {
  setMessage("Procurando sala aleatoria...");
  render();
  await joinFromResult(request("/chess/matchmaking", { method: "POST" }));
}

async function joinRoomByCode() {
  const code = roomCodeEl.value.trim().toUpperCase();
  if (!code) {
    setMessage("Informe o codigo da sala.");
    render();
    return;
  }

  await joinFromResult(request(`/chess/rooms/${encodeURIComponent(code)}/join`, { method: "POST" }));
}

async function joinFromResult(promise) {
  if (state.busy) return;

  state.busy = true;
  setControlsEnabled(false);
  try {
    const result = await promise;
    applyJoinResult(result);
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
  state.selected = null;
  state.lastMoveId = state.game?.lastMove?.id || null;
  state.lastTurn = state.game?.turn || null;
  state.lastReady = Boolean(state.game?.ready);
  state.lastDisconnectedSide = state.game?.disconnectedSide || null;

  if (result.waiting) {
    setMessage("Aguardando outro jogador entrar.");
  } else if (options.restored) {
    setMessage("Voce voltou para a sala.", 4200);
    showToast("Voce voltou para a sala.", "success");
  } else {
    setMessage("Voce entrou na sala.", 4200);
    showToast("Voce entrou na sala.", "success");
  }

  localStorage.setItem(sessionKey, JSON.stringify({
    roomCode: state.roomCode,
    playerId: state.playerId
  }));

  if (state.game?.canceled) {
    handleCanceledRoom();
  } else {
    startPolling();
  }

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

    const result = await request(`/chess/rooms/${encodeURIComponent(saved.roomCode)}?playerId=${encodeURIComponent(saved.playerId)}`);
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
    const wasWaiting = state.game && !state.game.ready;
    const previousReady = Boolean(state.game?.ready);
    const previousTurn = state.lastTurn;
    const previousDisconnectedSide = state.lastDisconnectedSide;
    const result = await request(`/chess/rooms/${encodeURIComponent(state.roomCode)}?playerId=${encodeURIComponent(state.playerId)}`);
    const nextMove = result.state?.lastMove;
    const shouldToastMove =
      nextMove &&
      nextMove.id !== state.lastMoveId &&
      nextMove.playerSide !== state.playerSide;

    state.game = result.state;
    state.playerSide = result.playerSide;
    state.lastMoveId = nextMove?.id || state.lastMoveId;
    state.lastTurn = state.game?.turn || null;
    state.lastReady = Boolean(state.game?.ready);
    state.lastDisconnectedSide = state.game?.disconnectedSide || null;

    if (state.game?.canceled) {
      handleCanceledRoom();
      render();
      return;
    }

    if (wasWaiting && state.game?.ready) {
      setMessage("Jogador entrou na sala.", 4200);
      showToast("Jogador entrou na sala.", "success");
    } else if (state.game?.ready && state.message === "Aguardando outro jogador entrar.") {
      setMessage("");
    }

    announceDisconnectChange(previousDisconnectedSide, state.game?.disconnectedSide || null);
    announceTurnChange(previousReady, previousTurn);

    render();
    if (shouldToastMove) {
      showToast(`Lance do adversario: ${nextMove.san}`, "info");
    }
  } catch (error) {
    setMessage(error.message);
    render();
  }
}

async function leaveRoom() {
  await notifyLeave();
  clearSession();
  render();
}

async function notifyLeave() {
  if (!state.roomCode || !state.playerId || state.game?.canceled) return;

  try {
    await request(`/chess/rooms/${encodeURIComponent(state.roomCode)}/leave`, {
      method: "POST",
      body: JSON.stringify({ playerId: state.playerId })
    });
  } catch {
    // Best effort: local exit should not be blocked by a network hiccup.
  }
}

function clearSession() {
  stopPolling();
  localStorage.removeItem(sessionKey);
  state.roomCode = null;
  state.playerId = null;
  state.playerSide = null;
  state.game = null;
  state.selected = null;
  state.lastMoveId = null;
  state.lastTurn = null;
  state.lastReady = false;
  state.lastDisconnectedSide = null;
  setMessage("");
}

function render() {
  const inRoom = Boolean(state.game);
  lobbyEl.hidden = inRoom;
  roomInfoEl.hidden = !inRoom;
  boardEl.hidden = !inRoom;
  newGameEl.textContent = inRoom ? "Sair" : "Novo";

  if (!inRoom) {
    statusEl.textContent = currentMessage() || "Crie uma sala ou procure uma partida aleatoria.";
    return;
  }

  roomInfoEl.textContent = `Sala ${state.roomCode} | Voce joga com as ${players[state.playerSide].label}`;
  const board = parseFenBoard(state.game.fen);
  const movesByFrom = getMovesByFrom();
  const selectedMoves = state.selected ? movesByFrom.get(state.selected) || [] : [];
  const selectedTargets = new Map(selectedMoves.map((move) => [move.to, move]));

  boardEl.innerHTML = "";
  for (let viewRow = 0; viewRow < 8; viewRow += 1) {
    for (let viewCol = 0; viewCol < 8; viewCol += 1) {
      const squareName = squareFromView(viewRow, viewCol);
      const square = document.createElement("button");
      square.type = "button";
      square.className = `square ${(viewRow + viewCol) % 2 === 0 ? "light" : "dark"}`;
      square.setAttribute("aria-label", squareName);

      const targetMove = selectedTargets.get(squareName);
      if (targetMove) {
        square.classList.add(targetMove.captured ? "capture" : "move");
        square.classList.add("playable");
      }

      if (state.selected === squareName) {
        square.classList.add("selected");
      }

      const piece = board.get(squareName);
      if (piece?.type === "king" && state.game.inCheckSide === piece.side) {
        square.classList.add("check");
      }

      if (piece) {
        square.append(pieceEl(piece, squareName, movesByFrom));
      }

      square.addEventListener("click", () => onSquare(squareName, piece, targetMove, movesByFrom));
      boardEl.append(square);
    }
  }

  statusEl.textContent = getStatus();
}

function pieceEl(piece, square, movesByFrom) {
  const el = document.createElement("span");
  el.className = `piece ${piece.side}-piece`;
  el.textContent = pieceSymbols[`${piece.side}-${piece.type}`];
  el.setAttribute("aria-label", `${pieceLabel(piece.type)} ${players[piece.side].label}`);
  if (canPlay() && piece.side === state.playerSide && movesByFrom.has(square)) {
    el.classList.add("playable");
  }

  el.addEventListener("click", (event) => {
    event.stopPropagation();
    onPiece(square, piece, movesByFrom);
  });

  return el;
}

function onPiece(square, piece, movesByFrom) {
  if (!canPlay()) return;
  if (!piece || piece.side !== state.playerSide) return;
  if (!movesByFrom.has(square)) return;

  state.selected = square;
  render();
}

function onSquare(square, piece, targetMove, movesByFrom) {
  if (targetMove) {
    sendMove(targetMove);
    return;
  }

  if (piece?.side === state.playerSide) {
    onPiece(square, piece, movesByFrom);
    return;
  }

  state.selected = null;
  render();
}

async function sendMove(move) {
  if (!canPlay() || state.busy) return;

  let promotion = move.promotionTo;
  if (move.promotion) {
    promotion = await choosePromotion();
    if (!promotion) return;
  }

  state.busy = true;
  try {
    const result = await request(`/chess/rooms/${encodeURIComponent(state.roomCode)}/actions`, {
      method: "POST",
      body: JSON.stringify({
        playerId: state.playerId,
        from: move.from,
        to: move.to,
        promotion
      })
    });
    state.game = result.state;
    state.lastMoveId = state.game.lastMove?.id || state.lastMoveId;
    state.lastTurn = state.game?.turn || null;
    state.selected = null;
    setMessage("");
    render();
  } catch (error) {
    setMessage(error.message);
    render();
  } finally {
    state.busy = false;
  }
}

function getMovesByFrom() {
  const map = new Map();
  if (!canPlay()) return map;

  state.game.legalMoves
    .filter((move) => pieceSideFromMove(move) === state.playerSide)
    .forEach((move) => {
      if (!map.has(move.from)) map.set(move.from, []);
      map.get(move.from).push(move);
    });
  return map;
}

function pieceSideFromMove(move) {
  return move.piece.split("-")[0];
}

function getStatus() {
  if (state.game.canceled) {
    return state.game.canceledBy === state.playerSide
      ? "Voce saiu. Partida encerrada."
      : "Adversario saiu. Partida encerrada.";
  }

  if (!state.game.ready) {
    return `${currentMessage() || "Aguardando outro jogador entrar."} Codigo: ${state.roomCode}`;
  }

  if (state.game.ended) {
    if (state.game.winner === state.playerSide) return "Xeque-mate. Voce venceu.";
    if (state.game.winner) return "Xeque-mate. Voce perdeu.";
    return `Empate: ${endReasonText(state.game.endedBy)}.`;
  }

  const disconnectMessage = getDisconnectMessage();
  if (disconnectMessage) {
    return disconnectMessage;
  }

  const message = currentMessage();
  if (message) {
    return message;
  }

  const check = state.game.inCheckSide === state.game.turn ? " Xeque." : "";
  return state.game.turn === state.playerSide
    ? `Sua vez.${check}`
    : `Vez do adversario.${check}`;
}

function canPlay() {
  return Boolean(
    state.game?.ready &&
    !state.game.canceled &&
    !state.game.disconnectedSide &&
    !state.game.ended &&
    state.game.turn === state.playerSide);
}

function parseFenBoard(fen) {
  const board = new Map();
  const [placement] = fen.split(" ");
  const rows = placement.split("/");
  rows.forEach((rowText, row) => {
    let file = 0;
    for (const char of rowText) {
      if (/\d/.test(char)) {
        file += Number(char);
        continue;
      }

      const side = char === char.toUpperCase() ? "white" : "black";
      const type = fenPieceType(char.toLowerCase());
      board.set(`${String.fromCharCode(97 + file)}${8 - row}`, { side, type });
      file += 1;
    }
  });
  return board;
}

function fenPieceType(char) {
  return {
    k: "king",
    q: "queen",
    r: "rook",
    b: "bishop",
    n: "knight",
    p: "pawn"
  }[char];
}

function squareFromView(row, col) {
  const boardRow = state.playerSide === "black" ? row : 7 - row;
  const boardCol = state.playerSide === "black" ? 7 - col : col;
  return `${String.fromCharCode(97 + boardCol)}${boardRow + 1}`;
}

function pieceLabel(type) {
  return {
    king: "Rei",
    queen: "Dama",
    rook: "Torre",
    bishop: "Bispo",
    knight: "Cavalo",
    pawn: "Peao"
  }[type];
}

function endReasonText(reason) {
  return {
    Checkmate: "xeque-mate",
    Stalemate: "afogamento",
    InsufficientMaterial: "material insuficiente",
    FiftyMoveRule: "regra dos 50 lances",
    Repetition: "repeticao",
    DrawDeclared: "empate declarado",
    Resigned: "desistencia",
    Timeout: "tempo"
  }[reason] || "partida finalizada";
}

function choosePromotion() {
  promotionModalEl.hidden = false;
  return new Promise((resolve) => {
    state.promotionResolver = resolve;
  });
}

promotionModalEl.addEventListener("click", (event) => {
  const button = event.target.closest("[data-promotion]");
  if (!button) return;

  promotionModalEl.hidden = true;
  const resolver = state.promotionResolver;
  state.promotionResolver = null;
  resolver?.(button.dataset.promotion);
});

function setMessage(message, ttlMs = null) {
  state.message = message;
  state.noticeUntil = ttlMs ? Date.now() + ttlMs : null;
}

function showToast(message, tone = "info") {
  if (!toastStackEl || !message) return;

  const toast = document.createElement("div");
  toast.className = `toast ${tone}`;
  toast.setAttribute("role", "status");
  toast.textContent = message;
  toastStackEl.append(toast);

  window.setTimeout(() => {
    toast.classList.add("leaving");
    toast.addEventListener("animationend", () => toast.remove(), { once: true });
  }, 3600);
}

function announceTurnChange(previousReady, previousTurn) {
  if (!state.game?.ready || state.game.ended || state.game.disconnectedSide) return;
  if (state.game.turn !== state.playerSide) return;

  const becameReady = !previousReady && state.game.ready;
  const becameMyTurn = previousTurn !== state.game.turn || becameReady;
  if (becameMyTurn) {
    showToast("Sua vez.", "turn");
  }
}

function announceDisconnectChange(previousSide, currentSide) {
  if (previousSide === currentSide) return;

  if (currentSide) {
    const message = currentSide === state.playerSide
      ? "Voce saiu da partida. Volte antes do tempo acabar."
      : "Adversario desconectado. Aguardando retorno.";
    showToast(message, "warning");
    return;
  }

  if (previousSide) {
    const message = previousSide === state.playerSide
      ? "Voce reconectou."
      : "Adversario voltou para a partida.";
    showToast(message, "success");
  }
}

function getDisconnectMessage() {
  const side = state.game?.disconnectedSide;
  if (!side) return "";

  const seconds = state.game.disconnectSecondsRemaining ?? 0;
  const time = formatTimer(seconds);
  return side === state.playerSide
    ? `Voce esta reconectando. Tempo restante: ${time}.`
    : `Adversario desconectado. Aguardando retorno: ${time}.`;
}

function formatTimer(seconds) {
  const safeSeconds = Math.max(0, Number(seconds) || 0);
  const minutes = Math.floor(safeSeconds / 60);
  const remainder = String(safeSeconds % 60).padStart(2, "0");
  return `${minutes}:${remainder}`;
}

function currentMessage() {
  if (state.noticeUntil && Date.now() > state.noticeUntil) {
    state.message = "";
    state.noticeUntil = null;
  }

  return state.message;
}

function handleCanceledRoom() {
  stopPolling();
  localStorage.removeItem(sessionKey);
  state.selected = null;
  const message = state.game?.canceledBy === state.playerSide
    ? "Voce saiu. Partida encerrada."
    : "Adversario saiu. Partida encerrada.";
  setMessage(message);
  showToast(message, "warning");
}

function setControlsEnabled(enabled) {
  createRoomEl.disabled = !enabled;
  randomRoomEl.disabled = !enabled;
  joinCodeEl.disabled = !enabled;
  roomCodeEl.disabled = !enabled;
}

createRoomEl.addEventListener("click", createRoom);
randomRoomEl.addEventListener("click", findRandomRoom);
joinCodeEl.addEventListener("click", joinRoomByCode);
roomCodeEl.addEventListener("keydown", (event) => {
  if (event.key === "Enter") {
    joinRoomByCode();
  }
});
newGameEl.addEventListener("click", async () => {
  if (state.game) {
    await leaveRoom();
    return;
  }

  createRoom();
});

window.addEventListener("pagehide", () => {
  if (!state.roomCode || !state.playerId || state.game?.canceled) return;

  const payload = JSON.stringify({ playerId: state.playerId });
  if (navigator.sendBeacon) {
    navigator.sendBeacon(
      `${apiBase}/chess/rooms/${encodeURIComponent(state.roomCode)}/leave`,
      new Blob([payload], { type: "application/json" }));
    return;
  }

  fetch(`${apiBase}/chess/rooms/${encodeURIComponent(state.roomCode)}/leave`, {
    method: "POST",
    headers: { "content-type": "application/json" },
    body: payload,
    keepalive: true
  }).catch(() => {});
});

restoreSession();
