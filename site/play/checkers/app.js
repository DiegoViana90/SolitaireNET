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
  message: ""
};

const lobbyEl = document.querySelector("#lobby");
const roomInfoEl = document.querySelector("#room-info");
const createRoomEl = document.querySelector("#create-room");
const randomRoomEl = document.querySelector("#random-room");
const roomCodeEl = document.querySelector("#room-code");
const joinCodeEl = document.querySelector("#join-code");
const boardEl = document.querySelector("#board");
const statusEl = document.querySelector("#status");
const lightScoreEl = document.querySelector("#light-score");
const darkScoreEl = document.querySelector("#dark-score");
const newGameEl = document.querySelector("#new-game");

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

async function findRandomRoom() {
  state.message = "Procurando sala aleatoria...";
  render();
  await joinFromResult(request("/checkers/matchmaking", { method: "POST" }));
}

async function joinRoomByCode() {
  const code = roomCodeEl.value.trim().toUpperCase();
  if (!code) {
    state.message = "Informe o codigo da sala.";
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
    state.message = error.message;
    render();
  } finally {
    state.busy = false;
    setControlsEnabled(true);
  }
}

function applyJoinResult(result) {
  state.roomCode = result.roomCode;
  state.playerId = result.playerId;
  state.playerSide = result.playerSide;
  state.game = result.state;
  state.selected = null;
  state.message = result.waiting
    ? "Aguardando outro jogador entrar."
    : "";

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

    const result = await request(`/checkers/rooms/${encodeURIComponent(saved.roomCode)}?playerId=${encodeURIComponent(saved.playerId)}`);
    applyJoinResult(result);
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
    const result = await request(`/checkers/rooms/${encodeURIComponent(state.roomCode)}?playerId=${encodeURIComponent(state.playerId)}`);
    state.game = result.state;
    state.playerSide = result.playerSide;
    if (state.game?.ready && state.message === "Aguardando outro jogador entrar.") {
      state.message = "";
    }
    render();
  } catch (error) {
    state.message = error.message;
    render();
  }
}

function leaveRoom() {
  clearSession();
  render();
}

function clearSession() {
  stopPolling();
  localStorage.removeItem(sessionKey);
  state.roomCode = null;
  state.playerId = null;
  state.playerSide = null;
  state.game = null;
  state.selected = null;
  state.message = "";
}

function render() {
  const inRoom = Boolean(state.game);
  lobbyEl.hidden = inRoom;
  roomInfoEl.hidden = !inRoom;
  boardEl.hidden = !inRoom;
  newGameEl.textContent = inRoom ? "Sair" : "Novo";

  if (!inRoom) {
    lightScoreEl.textContent = "0";
    darkScoreEl.textContent = "0";
    statusEl.textContent = state.message || "Crie uma sala ou procure uma partida aleatoria.";
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
    state.selected = null;
    state.message = "";
    render();
  } catch (error) {
    state.message = error.message;
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
  if (!state.game.ready) {
    return `${state.message} Codigo: ${state.roomCode}`;
  }

  if (state.game.winner) {
    return state.game.winner === state.playerSide ? "Voce venceu." : "Voce perdeu.";
  }

  if (state.message) {
    return state.message;
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

function boardPositionFromView(row, col) {
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
}

createRoomEl.addEventListener("click", createRoom);
randomRoomEl.addEventListener("click", findRandomRoom);
joinCodeEl.addEventListener("click", joinRoomByCode);
roomCodeEl.addEventListener("keydown", (event) => {
  if (event.key === "Enter") {
    joinRoomByCode();
  }
});
newGameEl.addEventListener("click", () => {
  if (state.game) {
    leaveRoom();
    return;
  }

  createRoom();
});

restoreSession();
