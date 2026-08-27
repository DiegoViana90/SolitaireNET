const apiBase = new URL("../../api", window.location.href).pathname.replace(/\/$/, "");
const sessionKey = "solitairenet-checkers-session";
const players = {
  light: { label: "claras", direction: -1 },
  dark: { label: "escuras", direction: 1 }
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
  lastDisconnectedSide: null
};

const lobbyEl = document.querySelector("#lobby");
const roomInfoEl = document.querySelector("#room-info");
const createRoomEl = document.querySelector("#create-room");
const randomRoomEl = document.querySelector("#random-room");
const roomCodeEl = document.querySelector("#room-code");
const joinCodeEl = document.querySelector("#join-code");
const botRoomEl = document.querySelector("#bot-room");
const botDifficultyEl = document.querySelector("#bot-difficulty");
const boardEl = document.querySelector("#board");
const statusEl = document.querySelector("#status");
const scorebarEl = document.querySelector("#scorebar");
const lightScoreEl = document.querySelector("#light-score");
const darkScoreEl = document.querySelector("#dark-score");
const newGameEl = document.querySelector("#new-game");
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
  await joinFromResult(request("/checkers/rooms", { method: "POST" }));
}
async function createBotRoom() { await joinFromResult(request(`/checkers/bot/rooms?difficulty=${botDifficultyEl.value}`, { method: "POST" })); }

async function findRandomRoom() {
  setMessage("Procurando sala aleatoria...");
  render();
  await joinFromResult(request("/checkers/matchmaking", { method: "POST" }));
}

async function joinRoomByCode() {
  const code = roomCodeEl.value.trim().toUpperCase();
  if (!code) {
    setMessage("Informe o codigo da sala.");
    render();
    return;
  }

  await joinFromResult(request(`/checkers/rooms/${encodeURIComponent(code)}/join`, { method: "POST" }));
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
  state.botRoom = Boolean(result.bot);
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

    const result = await request(`/checkers/rooms/${encodeURIComponent(saved.roomCode)}?playerId=${encodeURIComponent(saved.playerId)}`);
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
    const result = await request(`/checkers/rooms/${encodeURIComponent(state.roomCode)}?playerId=${encodeURIComponent(state.playerId)}`);
    const nextMove = result.state?.lastMove;
    const shouldAnimateMove =
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
    if (shouldAnimateMove) {
      animateLastMove(nextMove);
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
    await request(`/checkers/rooms/${encodeURIComponent(state.roomCode)}/leave`, {
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
  state.botRoom = false;
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
  scorebarEl.hidden = !inRoom;
  boardEl.hidden = !inRoom;
  newGameEl.textContent = inRoom ? "Sair" : "Novo";

  if (!inRoom) {
    lightScoreEl.textContent = "0";
    darkScoreEl.textContent = "0";
    statusEl.textContent = currentMessage() || "Crie uma sala ou procure uma partida aleatoria.";
    return;
  }

  roomInfoEl.textContent = `Sala ${state.roomCode} | Voce joga com as ${players[state.playerSide].label}`;

  const movesByPiece = getMovesByPiece(state.playerSide);
  const selectedMoves = state.selected
    ? movesByPiece.get(pieceAt(state.selected.row, state.selected.col)?.id) || []
    : [];
  const selectedTargets = new Map(selectedMoves.map((move) => [key(move.to.row, move.to.col), move]));

  boardEl.innerHTML = "";

  for (let viewRow = 0; viewRow < 8; viewRow += 1) {
    for (let viewCol = 0; viewCol < 8; viewCol += 1) {
      const { row, col } = boardPositionFromView(viewRow, viewCol);
      const square = document.createElement("button");
      square.type = "button";
      square.className = `square ${isDarkSquare(row, col) ? "dark" : "light"}`;
      square.dataset.row = String(viewRow);
      square.dataset.col = String(viewCol);
      square.setAttribute("aria-label", `Linha ${viewRow + 1}, coluna ${viewCol + 1}`);

      const targetMove = selectedTargets.get(key(row, col));
      if (targetMove) {
        square.classList.add(targetMove.captured ? "capture" : "move");
        square.classList.add("playable");
      }

      if (state.selected?.row === row && state.selected?.col === col) {
        square.classList.add("selected");
      }

      const piece = pieceAt(row, col);
      if (piece) {
        square.append(pieceEl(piece, row, col, movesByPiece));
      }

      square.addEventListener("click", () => onSquare(row, col, targetMove, movesByPiece));
      boardEl.append(square);
    }
  }

  const counts = countPieces();
  lightScoreEl.textContent = String(counts.light);
  darkScoreEl.textContent = String(counts.dark);
  statusEl.textContent = getStatus(counts, movesByPiece);
}

function pieceEl(piece, row, col, movesByPiece) {
  const el = document.createElement("span");
  el.className = `piece ${piece.owner === "light" ? "light-piece" : "dark-piece"}`;
  el.dataset.pieceId = piece.id;

  if (piece.king) {
    el.classList.add("king");
  }

  const moves = movesByPiece.get(piece.id) || [];
  if (canPlay() && piece.owner === state.playerSide && moves.some((move) => move.captured)) {
    el.classList.add("must-capture");
  }

  el.setAttribute("aria-label", `${piece.king ? "Dama" : "Peca"} ${players[piece.owner].label}`);
  el.addEventListener("click", (event) => {
    event.stopPropagation();
    onPiece(row, col, movesByPiece);
  });

  return el;
}

function onPiece(row, col, movesByPiece) {
  if (!canPlay()) return;

  const piece = pieceAt(row, col);
  if (!piece || piece.owner !== state.playerSide) return;
  if (state.game.forcedPieceId && piece.id !== state.game.forcedPieceId) return;
  if (!movesByPiece.has(piece.id)) return;

  state.selected = { row, col };
  render();
}

function onSquare(row, col, targetMove, movesByPiece) {
  if (targetMove) {
    sendMove(targetMove);
    return;
  }

  const piece = pieceAt(row, col);
  if (piece?.owner === state.playerSide) {
    onPiece(row, col, movesByPiece);
    return;
  }

  state.selected = null;
  render();
}

async function sendMove(move) {
  if (!canPlay() || state.busy) return;

  state.busy = true;
  try {
    const result = await request(`/checkers/rooms/${encodeURIComponent(state.roomCode)}/actions`, {
      method: "POST",
      body: JSON.stringify({
        playerId: state.playerId,
        from: move.from,
        to: move.to
      })
    });
    state.game = result.state;
    state.lastMoveId = state.game.lastMove?.id || state.lastMoveId;
    state.selected = null;
    setMessage("");
    render();
    if (state.botRoom && state.game.ready && !state.game.ended) {
      const bot = await request(`/checkers/bot/rooms/${encodeURIComponent(state.roomCode)}/move`, { method: "POST" });
      state.game = bot.state; state.lastMoveId = state.game.lastMove?.id || state.lastMoveId; render();
    }
  } catch (error) {
    setMessage(error.message);
    render();
  } finally {
    state.busy = false;
  }
}

function getMovesByPiece(owner) {
  if (!canPlay()) return new Map();

  const captures = new Map();
  const regular = new Map();

  forEachPiece((piece, row, col) => {
    if (piece.owner !== owner) return;
    if (state.game.forcedPieceId && piece.id !== state.game.forcedPieceId) return;

    const captureMoves = getMovesForPiece(row, col, true);
    if (captureMoves.length > 0) {
      captures.set(piece.id, captureMoves);
      return;
    }

    const moves = getMovesForPiece(row, col, false);
    if (moves.length > 0) {
      regular.set(piece.id, moves);
    }
  });

  return captures.size > 0 ? captures : regular;
}

function getMovesForPiece(row, col, onlyCaptures) {
  const piece = pieceAt(row, col);
  if (!piece) return [];

  const moves = [];
  const directions = onlyCaptures
    ? getCaptureDirections(piece)
    : getMoveDirections(piece);

  directions.forEach(([dr, dc]) => {
    const stepRow = row + dr;
    const stepCol = col + dc;
    const jumpRow = row + dr * 2;
    const jumpCol = col + dc * 2;
    const stepPiece = pieceAt(stepRow, stepCol);

    if (stepPiece && stepPiece.owner !== piece.owner && isInside(jumpRow, jumpCol) && !pieceAt(jumpRow, jumpCol)) {
      moves.push({
        from: { row, col },
        to: { row: jumpRow, col: jumpCol },
        captured: { row: stepRow, col: stepCol }
      });
      return;
    }

    if (!onlyCaptures && isInside(stepRow, stepCol) && !stepPiece) {
      moves.push({
        from: { row, col },
        to: { row: stepRow, col: stepCol },
        captured: null
      });
    }
  });

  return moves;
}

function getMoveDirections(piece) {
  if (piece.king) {
    return [[1, 1], [1, -1], [-1, 1], [-1, -1]];
  }

  const dr = players[piece.owner].direction;
  return [[dr, 1], [dr, -1]];
}

function getCaptureDirections(piece) {
  if (piece.king) {
    return getMoveDirections(piece);
  }

  return [[1, 1], [1, -1], [-1, 1], [-1, -1]];
}

function getStatus(counts, movesByPiece) {
  if (state.game.canceled) {
    return state.game.canceledBy === state.playerSide
      ? "Voce saiu. Partida encerrada."
      : "Adversario saiu. Partida encerrada.";
  }

  if (!state.game.ready) {
    return `${currentMessage() || "Aguardando outro jogador entrar."} Codigo: ${state.roomCode}`;
  }

  if (state.game.winner) {
    return state.game.winner === state.playerSide ? "Voce venceu." : "Voce perdeu.";
  }

  const disconnectMessage = getDisconnectMessage();
  if (disconnectMessage) {
    return disconnectMessage;
  }

  const message = currentMessage();
  if (message) {
    return message;
  }

  const captureAvailable = [...movesByPiece.values()].some((moves) => moves.some((move) => move.captured));
  const turnText = state.game.turn === state.playerSide
    ? "Sua vez."
    : "Vez do adversario.";

  return captureAvailable ? `${turnText} Captura obrigatoria.` : turnText;
}

function countPieces() {
  const counts = { light: 0, dark: 0 };
  forEachPiece((piece) => {
    counts[piece.owner] += 1;
  });
  return counts;
}

function canPlay() {
  return Boolean(
    state.game?.ready &&
    !state.game.canceled &&
    !state.game.disconnectedSide &&
    !state.game.winner &&
    state.game.turn === state.playerSide);
}

function forEachPiece(callback) {
  for (let row = 0; row < 8; row += 1) {
    for (let col = 0; col < 8; col += 1) {
      const piece = pieceAt(row, col);
      if (piece) callback(piece, row, col);
    }
  }
}

function pieceAt(row, col) {
  if (!isInside(row, col) || !state.game?.board) return null;
  return state.game.board[row][col];
}

function isInside(row, col) {
  return row >= 0 && row < 8 && col >= 0 && col < 8;
}

function isDarkSquare(row, col) {
  return (row + col) % 2 === 1;
}

function key(row, col) {
  return `${row}:${col}`;
}

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
  if (!state.game?.ready || state.game.winner || state.game.disconnectedSide) return;
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

function animateLastMove(move) {
  if (!move?.piece || !boardEl.isConnected) return;

  const from = viewPositionFromBoard(move.from.row, move.from.col);
  const to = viewPositionFromBoard(move.to.row, move.to.col);
  const boardSize = boardEl.clientWidth;
  if (!boardSize) return;

  const squareSize = boardSize / 8;
  const pieceSize = squareSize * 0.72;
  const offset = (squareSize - pieceSize) / 2;
  const startLeft = from.col * squareSize + offset;
  const startTop = from.row * squareSize + offset;
  const endLeft = to.col * squareSize + offset;
  const endTop = to.row * squareSize + offset;

  const targetPiece = [...boardEl.querySelectorAll(".piece")]
    .find((piece) => piece.dataset.pieceId === move.piece.id);
  targetPiece?.classList.add("animating-target");

  const ghost = document.createElement("span");
  ghost.className = `piece move-ghost ${move.piece.owner === "light" ? "light-piece" : "dark-piece"}`;
  if (move.piece.king) {
    ghost.classList.add("king");
  }

  ghost.style.width = `${pieceSize}px`;
  ghost.style.height = `${pieceSize}px`;
  ghost.style.left = `${startLeft}px`;
  ghost.style.top = `${startTop}px`;
  boardEl.append(ghost);

  const animation = ghost.animate(
    [
      { transform: "translate3d(0, 0, 0)" },
      { transform: `translate3d(${endLeft - startLeft}px, ${endTop - startTop}px, 0)` }
    ],
    {
      duration: 420,
      easing: "cubic-bezier(.22, .76, .24, 1)",
      fill: "forwards"
    });

  animation.onfinish = () => {
    ghost.remove();
    targetPiece?.classList.remove("animating-target");
  };
}

function boardPositionFromView(row, col) {
  if (state.playerSide === "dark") {
    return {
      row: 7 - row,
      col: 7 - col
    };
  }

  return { row, col };
}

function viewPositionFromBoard(row, col) {
  if (state.playerSide === "dark") {
    return {
      row: 7 - row,
      col: 7 - col
    };
  }

  return { row, col };
}

function setControlsEnabled(enabled) {
  createRoomEl.disabled = !enabled;
  randomRoomEl.disabled = !enabled;
  joinCodeEl.disabled = !enabled;
  roomCodeEl.disabled = !enabled;
  botRoomEl.disabled = !enabled; botDifficultyEl.disabled = !enabled;
}

createRoomEl.addEventListener("click", createRoom);
botRoomEl.addEventListener("click", createBotRoom);
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
      `${apiBase}/checkers/rooms/${encodeURIComponent(state.roomCode)}/leave`,
      new Blob([payload], { type: "application/json" }));
    return;
  }

  fetch(`${apiBase}/checkers/rooms/${encodeURIComponent(state.roomCode)}/leave`, {
    method: "POST",
    headers: { "content-type": "application/json" },
    body: payload,
    keepalive: true
  }).catch(() => {});
});

restoreSession();
