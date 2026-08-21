const apiBase = new URL("../../api", window.location.href).pathname.replace(/\/$/, "");
const sessionKey = "paciencia-plus-four-session";

const colors = {
  red: "Vermelho",
  blue: "Azul",
  green: "Verde",
  yellow: "Amarelo",
  wild: "Troca cor"
};
const playColors = ["red", "blue", "green", "yellow"];
let localCardSequence = 0;

const localOpponentSlots = [
  { id: "bot-top", name: "Adversario", shortName: "IA 1" },
  { id: "bot-left", name: "Adversario 2", shortName: "IA 2" },
  { id: "bot-right", name: "Adversario 3", shortName: "IA 3" }
];

const state = {
  roomCode: null,
  playerId: null,
  playerSide: null,
  game: null,
  selectedColor: "red",
  pendingColorCardId: null,
  selectedCardIds: [],
  simulated: false,
  localGame: null,
  localOpponentCount: 1,
  animating: false,
  busy: false,
  localBotBusy: false,
  pollTimer: null,
  eventSource: null,
  usingPollingFallback: false,
  eventWatchdog: null,
  message: ""
};

const lobbyEl = document.querySelector("#lobby");
const tableEl = document.querySelector("#table");
const myAreaEl = document.querySelector("#my-area");
const roomInfoEl = document.querySelector("#room-info");
const statusEl = document.querySelector("#status");
const createRoomEl = document.querySelector("#create-room");
const randomRoomEl = document.querySelector("#random-room");
const simulateTableEl = document.querySelector("#simulate-table");
const botCountButtons = Array.from(document.querySelectorAll("[data-bot-count]"));
const roomCodeEl = document.querySelector("#room-code");
const joinCodeEl = document.querySelector("#join-code");
const leaveRoomEl = document.querySelector("#leave-room");
const aiLobbyControlEl = document.querySelector("#ai-lobby-control");
const addAiEl = document.querySelector("#add-ai");
const seatStatusEl = document.querySelector("#seat-status");
const drawCardEl = document.querySelector("#draw-card");
const drawCountEl = document.querySelector("#draw-count");
const discardCardEl = document.querySelector("#discard-card");
const handEl = document.querySelector("#hand");
const myCardCountEl = document.querySelector("#my-card-count");
const opponentCardsEl = document.querySelector("#opponent-cards");
const opponentCountEl = document.querySelector("#opponent-count");
const topOpponentLabelEl = document.querySelector("#top-opponent-label");
const topOpponentSeatEl = document.querySelector(".top-player");
const leftSeatEl = document.querySelector("#left-seat");
const leftSeatLabelEl = document.querySelector("#left-seat-label");
const leftOpponentCardsEl = document.querySelector("#left-opponent-cards");
const leftOpponentCountEl = document.querySelector("#left-opponent-count");
const rightSeatEl = document.querySelector("#right-seat");
const rightSeatLabelEl = document.querySelector("#right-seat-label");
const rightOpponentCardsEl = document.querySelector("#right-opponent-cards");
const rightOpponentCountEl = document.querySelector("#right-opponent-count");
const nextRoundEl = document.querySelector("#next-round");
const colorPickerEl = document.querySelector("#color-picker");
const colorModalEl = document.querySelector("#color-modal");
const colorModalCardEl = document.querySelector("#color-modal-card");
const colorModalCloseEl = document.querySelector("#color-modal-close");
const toastStackEl = document.querySelector("#toast-stack");
const rulesModalEl = document.querySelector("#rules-modal");
const howToPlayEl = document.querySelector("#how-to-play");
const rulesModalCloseEl = document.querySelector("#rules-modal-close");

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
    render();
  }
}

function applyJoinResult(result, options = {}) {
  state.roomCode = result.roomCode;
  state.playerId = result.playerId;
  state.playerSide = result.playerSide;
  state.game = result.state;

  if (result.waiting) {
    setMessage(result.playerSide
      ? "Aguardando outro jogador entrar."
      : "Voce entrara na proxima rodada. Aguarde uma vaga liberar.");
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
  state.usingPollingFallback = true;
  state.pollTimer = window.setInterval(refreshRoom, 1200);
}

function stopPolling() {
  if (state.eventSource) {
    state.eventSource.close();
    state.eventSource = null;
  }
  if (state.pollTimer) {
    window.clearInterval(state.pollTimer);
    state.pollTimer = null;
  }
  state.usingPollingFallback = false;
  if (state.eventWatchdog) {
    window.clearTimeout(state.eventWatchdog);
    state.eventWatchdog = null;
  }
}

function applyRoomUpdate(result) {
  const previousEvent = state.game?.lastEvent?.id;
  if (!result?.state) return;
  state.game = result.state;
  state.playerSide = result.playerSide;
  if (state.game.lastEvent?.playerSide && state.game.lastEvent.playerSide !== state.playerSide) {
    state.busy = false;
    state.animating = false;
  }
  if (state.game?.lastEvent?.id && state.game.lastEvent.id !== previousEvent) {
    showEventToast(state.game.lastEvent);
  }
  if (state.game?.canceled) {
    clearSession();
    stopPolling();
  }
  render();
}

async function refreshRoom() {
  if (!state.roomCode || !state.playerId || state.busy) return;

  try {
    const result = await request(`/plus-four/rooms/${encodeURIComponent(state.roomCode)}?playerId=${encodeURIComponent(state.playerId)}`);
    applyRoomUpdate(result);
  } catch (error) {
    setMessage(error.message);
    if (error.message.toLowerCase().includes("sala nao encontrada") ||
        error.message.toLowerCase().includes("sala não encontrada")) {
      stopPolling();
      showToast("A sala foi encerrada ou o servidor foi reiniciado.");
    }
    render();
  }
}

function render() {
  const inRoom = Boolean(state.roomCode && state.playerId && state.game);
  lobbyEl.hidden = inRoom;
  tableEl.hidden = !inRoom;
  roomInfoEl.hidden = !inRoom;
  leaveRoomEl.hidden = !inRoom;
  seatStatusEl.hidden = !inRoom || state.simulated;
  aiLobbyControlEl.hidden = !inRoom || state.simulated || state.playerSide !== "one";
  if (inRoom && !state.simulated) {
    const seats = state.game?.seats || [];
    const occupied = seats.filter(seat => seat.occupied || seat.addPending).length;
    seatStatusEl.textContent = `Assentos ${occupied}/4`;
    addAiEl.disabled = occupied >= 4;
  }
  statusEl.textContent = getStatus();

  if (!inRoom) return;

  const game = state.game;
  state.selectedCardIds = state.selectedCardIds.filter(id => game.hand.some(card => card.id === id));
  if (state.pendingColorCardId && !game.hand.some((card) => card.id === state.pendingColorCardId)) {
    state.pendingColorCardId = null;
  }

  roomInfoEl.textContent = state.simulated
    ? `Partida local | ${localOpponents().length} IA${localOpponents().length === 1 ? "" : "s"} adversaria${localOpponents().length === 1 ? "" : "s"}`
    : `Sala ${state.roomCode} | Voce e ${sideLabel(state.playerSide)}`;
  drawCountEl.textContent = game.drawCount;
  const isMyTurn = game.ready && game.turn === state.playerSide;
  tableEl.classList.toggle("current-turn", isMyTurn);
  myAreaEl.classList.toggle("current-turn", isMyTurn);
  nextRoundEl.hidden = !game.roundWinner || game.matchWinner;
  drawCardEl.disabled = !canAct();

  discardCardEl.replaceChildren(cardEl(game.topCard, { large: true }));
  handEl.replaceChildren(...game.hand.map((card) => cardEl(card)));
  myCardCountEl.textContent = `${game.hand.length} carta${game.hand.length === 1 ? "" : "s"}`;
  renderOpponents();

  colorModalEl.hidden = !state.pendingColorCardId;
  const pendingCard = game.hand.find((card) => card.id === state.pendingColorCardId);
  colorModalCardEl.replaceChildren(pendingCard
    ? cardEl({ ...pendingCard, playedColor: state.selectedColor }, { large: true })
    : document.createDocumentFragment());
  colorPickerEl.querySelectorAll("button").forEach((button) => {
    button.classList.toggle("selected", button.dataset.color === state.selectedColor);
  });
}

function renderOpponents() {
  if (!state.simulated) {
    const seats = state.game.seats || [];
    const top = seats.find(seat => seat.side === "three");
    const left = seats.find(seat => seat.side === "two");
    const right = seats.find(seat => seat.side === "four");
    renderServerOpponent(top, { seat: topOpponentSeatEl, label: topOpponentLabelEl, cards: opponentCardsEl, count: opponentCountEl });
    renderServerOpponent(left, {
      seat: leftSeatEl,
      label: leftSeatLabelEl,
      cards: leftOpponentCardsEl,
      count: leftOpponentCountEl
    });
    renderServerOpponent(right, {
      seat: rightSeatEl,
      label: rightSeatLabelEl,
      cards: rightOpponentCardsEl,
      count: rightOpponentCountEl
    });
    return;
  }

  const [topOpponent, leftOpponent, rightOpponent] = localOpponents();
  renderTopOpponent(topOpponent);
  renderSideOpponent(leftOpponent, {
    seat: leftSeatEl,
    label: leftSeatLabelEl,
    cards: leftOpponentCardsEl,
    count: leftOpponentCountEl
  });
  renderSideOpponent(rightOpponent, {
    seat: rightSeatEl,
    label: rightSeatLabelEl,
    cards: rightOpponentCardsEl,
    count: rightOpponentCountEl
  });
}

function renderServerOpponent(seat, elements) {
  elements.seat.querySelectorAll(".ai-seat-control").forEach(control => control.remove());
  const active = seat?.occupied;
  elements.seat.classList.toggle("active-opponent", Boolean(active));
  elements.seat.classList.toggle("current-turn", state.game?.turn === seat?.side);
  elements.label.textContent = seat?.isAi ? `IA ${seat.side === "two" ? "1" : seat.side === "three" ? "2" : "3"}` : active ? "Jogador" : "Vazio";
  if (active && seat.isAi && state.playerSide === "one") {
    const remove = document.createElement("button");
    remove.className = "add-ai-seat remove-ai-seat ai-seat-control";
    remove.type = "button";
    remove.textContent = seat.removePending ? "Sera removida na proxima rodada" : "Remover IA";
    remove.disabled = seat.removePending;
    remove.addEventListener("click", () => sendAction({ type: "remove-ai", aiSide: seat.side }));
    elements.cards.replaceChildren(...cardBacks(7));
    elements.seat.append(remove);
    elements.cards.classList.toggle("ai-removing", seat.removePending);
  } else {
    elements.cards.replaceChildren(...cardBacks(active ? (seat.handCount ?? (seat.isAi ? 7 : 0)) : 0));
    elements.cards.classList.remove("ai-removing");
  }
  elements.count.textContent = seat?.removePending
    ? "Sera removida na proxima rodada"
    : active
      ? `${seat.handCount ?? (seat.isAi ? 7 : 0)} carta${(seat.handCount ?? (seat.isAi ? 7 : 0)) === 1 ? "" : "s"}`
      : "";
  elements.seat.classList.toggle("current-turn", Boolean(state.game?.ready && state.game?.turn === seat?.side));
}

function renderTopOpponent(opponent) {
  const count = opponent?.hand.length || 0;
  topOpponentLabelEl.textContent = opponent?.name || "Adversario";
  opponentCountEl.textContent = `${count} carta${count === 1 ? "" : "s"}`;
  opponentCardsEl.replaceChildren(...cardBacks(count));
  topOpponentSeatEl.classList.toggle("current-turn", state.game.turn === opponent?.id);
}

function renderSideOpponent(opponent, elements) {
  elements.seat.classList.toggle("active-opponent", Boolean(opponent));
  elements.seat.classList.toggle("current-turn", state.game?.turn === opponent?.id);
  elements.label.textContent = opponent?.shortName || "Vazio";
  elements.cards.replaceChildren(...cardBacks(opponent?.hand.length || 0));
  elements.count.textContent = opponent
    ? `${opponent.hand.length} carta${opponent.hand.length === 1 ? "" : "s"}`
    : "";
}

function cardBackEl() {
  const el = document.createElement("div");
  el.className = "card-back";
  el.setAttribute("aria-hidden", "true");
  return el;
}

function cardBacks(count) {
  return Array.from({ length: Math.min(count, 7) }, () => cardBackEl());
}

function cardEl(card, options = {}) {
  const el = document.createElement(options.large ? "div" : "button");
  el.className = `card ${card.color}`;
  if (needsColorChoice(card) && !card.playedColor) el.classList.add("color-action");
  if (card.playedColor) el.classList.add(`chosen-${card.playedColor}`);
  if (card.id === state.pendingColorCardId) el.classList.add("pending-color");
  el.dataset.cardId = card.id;
  el.dataset.color = card.color;
  el.dataset.value = card.value;
  el.setAttribute("aria-label", `${card.value} ${label(card)}`);
  el.innerHTML = `<strong>${displayValue(card)}</strong>`;

  if (!options.large) {
    const isCutCandidate = Boolean(state.game?.canCut && (card.value === "Pula" || card.value === "Inverte") && card.color !== "wild");
    const playable = (canAct() || isCutCandidate) && canPlay(card, isCutCandidate);
    if (playable) el.classList.add("playable");
    if (isCutCandidate) el.classList.add("cut-available");
    if (state.selectedCardIds.includes(card.id)) el.classList.add("selected");
    el.type = "button";
    el.disabled = !playable;
    el.addEventListener("click", (event) => playCard(card, el, event));
  }

  return el;
}

async function playCard(card, sourceEl, event = null) {
  const cut = Boolean(state.game?.canCut && state.game.turn !== state.playerSide);
  if ((!canAct() && !cut) || !canPlay(card, cut)) return;
  if (!cut && event?.shiftKey) {
    const selected = state.game.hand.find(item => item.id === card.id);
    if (selected && state.selectedCardIds.length > 0 && state.selectedCardIds.every(id => {
      const item = state.game.hand.find(candidate => candidate.id === id);
      return item && item.color === selected.color && item.value === selected.value;
    })) {
      state.selectedCardIds = [...new Set([...state.selectedCardIds, card.id])];
      setMessage(`${state.selectedCardIds.length} cartas selecionadas. Clique sem Shift para jogar juntas.`);
      render();
    } else {
      state.selectedCardIds = [card.id];
      setMessage("Carta selecionada. Segure Shift para selecionar outra identica.");
      render();
    }
    return;
  }
  if (!cut && state.selectedCardIds.length > 0 && state.selectedCardIds.every(id => {
    const selected = state.game.hand.find(item => item.id === id);
    return selected && selected.color === card.color && selected.value === card.value;
  })) {
    state.selectedCardIds = [...new Set([...state.selectedCardIds, card.id])];
    if (state.selectedCardIds.length === 1) return;
  } else if (!cut && !needsColorChoice(card) && state.selectedCardIds.length === 0) {
    state.selectedCardIds = [card.id];
  }
  if (needsColorChoice(card)) {
    state.pendingColorCardId = card.id;
    state.selectedColor = state.game.currentColor && state.game.currentColor !== "wild"
      ? state.game.currentColor
      : "red";
    setMessage("Escolha a cor para jogar essa carta.");
    render();
    return;
  }

  state.pendingColorCardId = null;
  state.selectedCardIds = [];
  await playCardWithMotion(card, null, sourceEl, cut);
}

async function playPendingColorCard(color) {
  const card = state.game?.hand.find((item) => item.id === state.pendingColorCardId);
  if (!card || !needsColorChoice(card) || !canAct() || !canPlay(card)) {
    state.pendingColorCardId = null;
    render();
    return;
  }

  state.selectedColor = color;
  state.pendingColorCardId = null;
  colorModalEl.hidden = true;
  await playCardWithMotion(card, color, findHandCardEl(card.id), false);
}

async function playCardWithMotion(card, color, sourceEl, cut = false) {
  state.animating = true;
  try {
    await animateCardToDiscard(color, sourceEl);
  } finally {
    state.animating = false;
  }

  const cardIds = state.selectedCardIds.length > 1 ? [...state.selectedCardIds] : [card.id];
  state.selectedCardIds = [];
  await sendAction({
    type: "play",
    cardId: card.id,
    cardIds,
    color
  });
}

function findHandCardEl(cardId) {
  return Array.from(handEl.querySelectorAll("[data-card-id]"))
    .find((item) => item.dataset.cardId === cardId);
}

function needsColorChoice(card) {
  return card.color === "wild";
}

async function animateCardToDiscard(color, sourceEl, options = {}) {
  if (!sourceEl || window.matchMedia("(prefers-reduced-motion: reduce)").matches) return;

  const from = sourceEl.getBoundingClientRect();
  const to = discardCardEl.getBoundingClientRect();
  if (!from.width || !to.width) return;
  const hasKnownCard = Boolean(options.card);
  // A origem da IA e um card-back empilhado; o retangulo dele pode incluir a
  // area visual da pilha. Para a carta revelada, use sempre o tamanho real de
  // uma carta, sem herdar a dimensao do descarte ou da pilha.
  const flightFrom = hasKnownCard ? getFlightRect(sourceEl, from, options) : from;

  const ghost = options.card
    ? cardEl({ ...options.card, playedColor: color || options.card.playedColor })
    : sourceEl.cloneNode(true);
  ghost.classList.add("flying-card");
  ghost.classList.remove("playable", "pending-color");
  ghost.removeAttribute("disabled");
  if (color) ghost.classList.add(`chosen-${color}`);

  ghost.style.left = `${flightFrom.left}px`;
  ghost.style.top = `${flightFrom.top}px`;
  ghost.style.setProperty("width", `${flightFrom.width}px`, "important");
  ghost.style.setProperty("height", `${flightFrom.height}px`, "important");
  ghost.style.setProperty("min-width", `${flightFrom.width}px`, "important");
  ghost.style.setProperty("min-height", `${flightFrom.height}px`, "important");

  document.body.append(ghost);

  const deltaX = to.left + (to.width / 2) - (flightFrom.left + (flightFrom.width / 2));
  const deltaY = to.top + (to.height / 2) - (flightFrom.top + (flightFrom.height / 2));
  const scale = 1;

  try {
    await ghost.animate([
      { transform: "translate(0, 0) scale(1)", opacity: 1 },
      { transform: `translate(${deltaX}px, ${deltaY}px) scale(${scale})`, opacity: 0.96 }
    ], {
      duration: 340,
      easing: "cubic-bezier(0.18, 0.82, 0.2, 1)"
    }).finished;
  } finally {
    ghost.remove();
  }
}

function getDiscardSizedFlightRect(sourceRect, discardRect) {
  return {
    left: sourceRect.left + (sourceRect.width / 2) - (discardRect.width / 2),
    top: sourceRect.top + (sourceRect.height / 2) - (discardRect.height / 2),
    width: discardRect.width,
    height: discardRect.height
  };
}

function getFlightRect(sourceEl, rect, options) {
  if (!options.card || !sourceEl.closest?.(".side-cards")) return rect;

  const width = window.matchMedia("(max-width: 620px)").matches ? 34 : 38;
  const height = window.matchMedia("(max-width: 620px)").matches ? 50 : 56;
  return {
    left: rect.left + (rect.width / 2) - (width / 2),
    top: rect.top + (rect.height / 2) - (height / 2),
    width,
    height
  };
}

async function animateDrawTo(targetEl) {
  if (!targetEl || window.matchMedia("(prefers-reduced-motion: reduce)").matches) return;

  const from = drawCardEl.getBoundingClientRect();
  const to = targetEl.getBoundingClientRect();
  if (!from.width || !to.width) return;

  const ghost = document.createElement("div");
  ghost.className = "card-back flying-card";
  Object.assign(ghost.style, {
    left: `${from.left}px`,
    top: `${from.top}px`,
    width: `${Math.min(42, from.width)}px`,
    height: `${Math.min(62, from.height)}px`
  });

  document.body.append(ghost);

  const startX = from.width > 42 ? (from.width - 42) / 2 : 0;
  const startY = from.height > 62 ? (from.height - 62) / 2 : 0;
  const deltaX = to.left + (to.width / 2) - (from.left + startX + 21);
  const deltaY = to.top + (to.height / 2) - (from.top + startY + 31);

  try {
    await ghost.animate([
      { transform: `translate(${startX}px, ${startY}px) scale(1)`, opacity: 1 },
      { transform: `translate(${startX + deltaX}px, ${startY + deltaY}px) scale(0.92)`, opacity: 0.96 }
    ], {
      duration: 320,
      easing: "cubic-bezier(0.18, 0.82, 0.2, 1)"
    }).finished;
  } finally {
    ghost.remove();
  }
}

function simulateTable() {
  stopPolling();
  startLocalRound({
    oneScore: 0,
    twoScore: 0,
    round: 1,
    startingSide: "one",
    opponentCount: state.localOpponentCount
  });
  showToast("Partida local contra IA iniciada.");
  render();
}

function startLocalRound(options) {
  const deck = shuffle(buildDeck());
  const playerHand = [];
  const opponents = localOpponentSlots
    .slice(0, options.opponentCount || state.localOpponentCount || 1)
    .map((slot) => ({ ...slot, hand: [] }));

  for (let index = 0; index < 7; index += 1) {
    playerHand.push(deck.pop());
    for (const opponent of opponents) {
      opponent.hand.push(deck.pop());
    }
  }

  let topCard = deck.pop();
  while (topCard?.color === "wild") {
    deck.unshift(topCard);
    topCard = deck.pop();
  }
  if (topCard?.value === "Inverte") {
    topCard = { ...topCard, playedColor: topCard.color };
  }

  state.simulated = true;
  state.localGame = {
    drawPile: deck,
    discardPile: [topCard],
    opponents,
    direction: 1,
    turnOrder: localTurnOrderFor(opponents),
    pendingDraw: 0,
    pendingAction: null,
    cutOpen: false
  };
  state.roomCode = "BOT";
  state.playerId = "simulated-player";
  state.playerSide = "one";
  state.message = "";
  state.game = {
    ready: true,
    canceled: false,
    turn: options.startingSide,
    currentColor: topCard.color,
    round: options.round,
    oneScore: options.oneScore,
    twoScore: options.twoScore,
    roundWinner: null,
    matchWinner: null,
    drawCount: deck.length,
    topCard,
    hand: playerHand,
    opponentCount: opponents[0]?.hand.length || 0,
    opponents: opponents.map((opponent) => ({
      id: opponent.id,
      name: opponent.name,
      count: opponent.hand.length
    })),
    lastEvent: null
  };

  scheduleLocalBotTurn();
}

async function drawCard() {
  if (!canAct()) return;
  state.pendingColorCardId = null;
  colorModalEl.hidden = true;
  await sendAction({ type: "draw" });
}

async function nextRound() {
  if (!state.game?.roundWinner || state.game?.matchWinner) return;
  await sendAction({ type: "next-round" });
}

async function sendAction(action) {
  if (state.simulated) {
    await applySimulatedAction(action);
    return;
  }

  if (state.busy) return;

  state.busy = true;
  try {
    const previousEventId = state.game?.lastEvent?.id;
    const result = await request(`/plus-four/rooms/${encodeURIComponent(state.roomCode)}/actions`, {
      method: "POST",
      body: JSON.stringify({ playerId: state.playerId, ...action })
    });
    state.game = result.state;
    render();
    if (state.game?.lastEvent?.id && state.game.lastEvent.id !== previousEventId) {
      showEventToast(state.game.lastEvent);
      if ((state.game.lastEvent.type === "draw" || state.game.lastEvent.type === "draw-penalty") &&
          state.game.lastEvent.playerSide === state.playerSide) {
        await animateDrawTo(handEl);
      }
    }
  } catch (error) {
    setMessage(error.message);
    render();
  } finally {
    state.busy = false;
    render();
  }
}

async function applySimulatedAction(action) {
  if (!state.game || !state.localGame) return;

  if (action.type === "next-round") {
    startLocalRound({
      oneScore: state.game.oneScore,
      twoScore: state.game.twoScore,
      round: state.game.round + 1,
      startingSide: state.game.roundWinner === "one" ? localNextSide("one") : "one",
      opponentCount: state.localGame.opponents.length
    });
    render();
    return;
  }

  if (action.type === "draw") {
    const amount = state.localGame.pendingDraw || 1;
    const cards = Array.from({ length: amount }, () => drawLocalCard()).filter(Boolean);
    if (!cards.length) {
      showToast("Monte vazio.");
      render();
      return;
    }

    state.animating = true;
    try {
      await animateDrawTo(handEl);
    } finally {
      state.animating = false;
    }

    state.game.hand.push(...cards);
    state.localGame.pendingDraw = 0;
    state.localGame.pendingAction = null;
    state.localGame.cutOpen = false;
    state.game.turn = localNextSide("one");
    syncLocalPublicState();
    render();
    scheduleLocalBotTurn();
    return;
  }

  if (action.type === "play") {
    const ids = action.cardIds?.length ? action.cardIds : [action.cardId];
    const cards = ids.map((id) => state.game.hand.find((item) => item.id === id)).filter(Boolean);
    const card = cards[0];
    if (!card || cards.length !== ids.length || cards.some(item => item.color !== card.color || item.value !== card.value) ||
      (cards.length > 1 && !sameAsTop(card))) return;
    if (!canPlay(card)) return;

    state.game.hand = state.game.hand.filter((item) => !ids.includes(item.id));
    for (const played of cards) state.localGame.discardPile.push({ ...played, playedColor: action.color || played.playedColor });
    state.localGame.cutOpen = true;
    if (card.value === "+2" || card.value === "+4") {
      state.localGame.pendingDraw += card.value === "+2" ? 2 * cards.length : 4 * cards.length;
      state.localGame.pendingAction = card.value;
    }
    state.game.topCard = state.localGame.discardPile.at(-1);
    state.game.currentColor = action.color || card.color;
    for (const played of cards) {
      state.game.turn = await nextLocalTurnAfter(played, state.game.turn === "one" ? "one" : state.game.turn);
    }
    finishLocalRoundIfNeeded("one");
    syncLocalPublicState();
    render();

    await tryLocalCut();
    scheduleLocalBotTurn();
  }
}

async function simulateBotTurn() {
  if (!state.simulated || !state.game || !state.localGame || state.game.turn === "one" || state.localBotBusy) return;
  state.localBotBusy = true;

  const opponent = localOpponentById(state.game.turn);
  if (!opponent) return;

  const card = chooseBotCard(opponent);
  if (!card) {
    const amount = state.localGame.pendingDraw || 1;
    const drawn = Array.from({ length: amount }, () => drawLocalCard()).filter(Boolean);
    if (drawn.length) {
      state.animating = true;
      try {
        await animateDrawTo(getOpponentCardsEl(opponent.id));
      } finally {
        state.animating = false;
      }

      opponent.hand.push(...drawn);
      showToast(`${opponent.name} comprou uma carta.`);
    } else {
      showToast(`${opponent.name} passou.`);
    }

    state.localGame.pendingDraw = 0;
    state.localGame.pendingAction = null;
    state.localGame.cutOpen = false;
    state.game.turn = localNextSide(opponent.id);
    syncLocalPublicState();
    render();
    state.localBotBusy = false;
    scheduleLocalBotTurn();
    return;
  }

  opponent.hand = opponent.hand.filter((item) => item.id !== card.id);
  const chosenColor = needsColorChoice(card) ? chooseBotColor(opponent) : null;
  state.animating = true;
  try {
    await animateOpponentCardToDiscard(opponent.id, card, chosenColor);
  } finally {
    state.animating = false;
  }

  state.localGame.discardPile.push({ ...card, playedColor: chosenColor || card.playedColor });
  state.localGame.cutOpen = true;
  if (card.value === "+2" || card.value === "+4") {
    state.localGame.pendingDraw += card.value === "+2" ? 2 : 4;
    state.localGame.pendingAction = card.value;
  }
  state.game.currentColor = chosenColor || card.color;
  state.game.topCard = state.localGame.discardPile.at(-1);
  state.game.turn = await nextLocalTurnAfter(card, opponent.id);
  finishLocalRoundIfNeeded(opponent.id);
  syncLocalPublicState();
  showToast(`${opponent.name} jogou ${card.value}.`);
  render();

  await tryLocalCut();
  state.localBotBusy = false;
  scheduleLocalBotTurn();
}

async function animateOpponentCardToDiscard(opponentId, card, color) {
  const sourceEl = findOpponentCardEl(opponentId);
  await animateCardToDiscard(color, sourceEl, { card });
}

function findOpponentCardEl(opponentId) {
  const container = getOpponentCardsEl(opponentId);
  const cards = Array.from(container.querySelectorAll(".card-back"));
  return cards.at(-1) || null;
}

function getOpponentCardsEl(opponentId) {
  if (opponentId === "bot-left") return leftOpponentCardsEl;
  if (opponentId === "bot-right") return rightOpponentCardsEl;
  return opponentCardsEl;
}

function scheduleLocalBotTurn(delay = 700) {
  if (!state.simulated || !state.game || state.game.roundWinner || state.game.turn === "one") return;
  window.setTimeout(() => {
    void simulateBotTurn();
  }, delay);
}

function localOpponents() {
  return state.localGame?.opponents || [];
}

function localOpponentById(id) {
  return localOpponents().find((opponent) => opponent.id === id) || null;
}

function localNextSide(side) {
  const turnOrder = state.localGame?.turnOrder || ["one", "bot-top"];
  const currentIndex = turnOrder.indexOf(side);
  if (currentIndex < 0) return turnOrder[0];
  const direction = state.localGame?.direction || 1;
  return turnOrder[(currentIndex + direction + turnOrder.length) % turnOrder.length];
}

function localTurnOrderFor(opponents) {
  const activeIds = new Set(opponents.map((opponent) => opponent.id));
  return ["one", "bot-left", "bot-top", "bot-right"].filter((side) => side === "one" || activeIds.has(side));
}

function currentLocalOpponentName() {
  return localOpponentById(state.game?.turn)?.name || "IA adversaria";
}

function buildDeck() {
  const deck = [];
  for (const color of playColors) {
    deck.push(newLocalCard(color, "0"));
    for (let copy = 0; copy < 2; copy += 1) {
      for (let value = 1; value <= 9; value += 1) {
        deck.push(newLocalCard(color, value.toString()));
      }
      deck.push(newLocalCard(color, "Pula"));
      deck.push(newLocalCard(color, "+2"));
      deck.push(newLocalCard(color, "Inverte"));
    }
  }

  for (let index = 0; index < 4; index += 1) {
    deck.push(newLocalCard("wild", "Cor"));
    deck.push(newLocalCard("wild", "+4"));
  }

  return deck;
}

function newLocalCard(color, value) {
  localCardSequence += 1;
  return {
    id: `local-${Date.now()}-${localCardSequence}`,
    color,
    value,
    playedColor: null
  };
}

function shuffle(cards) {
  const deck = [...cards];
  for (let index = deck.length - 1; index > 0; index -= 1) {
    const target = Math.floor(Math.random() * (index + 1));
    [deck[index], deck[target]] = [deck[target], deck[index]];
  }
  return deck;
}

function drawLocalCard() {
  ensureLocalDrawPile();
  const card = state.localGame?.drawPile.pop() || null;
  syncLocalPublicState();
  return card;
}

function ensureLocalDrawPile() {
  if (!state.localGame || state.localGame.drawPile.length > 0 || state.localGame.discardPile.length <= 1) {
    return;
  }

  const topCard = state.localGame.discardPile.at(-1);
  const recycled = state.localGame.discardPile
    .slice(0, -1)
    .map((card) => ({ ...card, playedColor: null }));

  state.localGame.discardPile = [topCard];
  state.localGame.drawPile = shuffle(recycled);
}

function chooseBotCard(opponent) {
  if (!opponent) return null;
  return [...opponent.hand]
    .filter((card) => canPlayLocal(card))
    .sort((left, right) => botCardWeight(right) - botCardWeight(left))[0] || null;
}

function canPlayLocal(card) {
  const top = state.game?.topCard;
  if (!top) return false;
  if (state.localGame?.pendingDraw > 0 && card.value !== state.localGame.pendingAction) return false;
  return card.color === "wild" ||
    card.value === "Inverte" ||
    card.color === state.game.currentColor ||
    card.value === top.value;
}

function botCardWeight(card) {
  if (card.value === "+4") return 90;
  if (card.value === "+2") return 70;
  if (card.value === "Pula" || card.value === "Inverte") return 60;
  if (card.color === state.game?.currentColor) return 40;
  if (card.color === "wild") return 30;
  return Number.parseInt(card.value, 10) || 10;
}

function chooseBotColor(opponent) {
  if (!opponent) return "red";
  const counts = Object.fromEntries(playColors.map((color) => [color, 0]));
  for (const card of opponent.hand) {
    if (counts[card.color] !== undefined) counts[card.color] += 1;
  }

  return playColors
    .map((color) => ({ color, count: counts[color] }))
    .sort((left, right) => right.count - left.count)[0]?.color || "red";
}

async function nextLocalTurnAfter(card, side) {
  const next = localNextSide(side);
  if (card.value === "Pula") return localNextSide(next);
  if (card.value === "Inverte") {
    state.localGame.direction *= -1;
    return state.localGame.turnOrder.length === 2 ? side : localNextSide(side);
  }

  return next;
}

function drawLocalCards(side, count) {
  for (let index = 0; index < count; index += 1) {
    const card = drawLocalCard();
    if (!card) return;
    if (side === "one") {
      state.game.hand.push(card);
    } else {
      localOpponentById(side)?.hand.push(card);
    }
  }
}

async function drawLocalCardsWithMotion(side, count) {
  for (let index = 0; index < count; index += 1) {
    const card = drawLocalCard();
    if (!card) return;

    state.animating = true;
    try {
      await animateDrawTo(side === "one" ? handEl : getOpponentCardsEl(side));
    } finally {
      state.animating = false;
    }

    if (side === "one") {
      state.game.hand.push(card);
    } else {
      localOpponentById(side)?.hand.push(card);
    }

    syncLocalPublicState();
    render();
  }
}

function finishLocalRoundIfNeeded(side) {
  if (!state.game || !state.localGame) return;
  const winnerOpponent = side === "one" ? null : localOpponentById(side);
  const winnerHandCount = side === "one"
    ? state.game.hand.length
    : winnerOpponent?.hand.length;

  if (winnerHandCount > 0) return;

  state.game.roundWinner = side;
  state.game.turn = side;
  if (side === "one") {
    state.game.oneScore += localOpponents()
      .flatMap((opponent) => opponent.hand)
      .reduce((total, card) => total + cardPoints(card), 0);
    showToast("Voce venceu a rodada.");
  } else {
    const remainingOpponentCards = localOpponents()
      .filter((opponent) => opponent.id !== side)
      .flatMap((opponent) => opponent.hand);
    state.game.twoScore += [...state.game.hand, ...remainingOpponentCards]
      .reduce((total, card) => total + cardPoints(card), 0);
    showToast(`${winnerOpponent?.name || "IA adversaria"} venceu a rodada.`);
  }
}

function cardPoints(card) {
  if (card.value === "+4") return 50;
  if (card.value === "Cor") return 40;
  if (card.value === "+2" || card.value === "Pula" || card.value === "Inverte") return 20;
  return Number.parseInt(card.value, 10) || 10;
}

function syncLocalPublicState() {
  if (!state.game || !state.localGame) return;
  state.game.drawCount = state.localGame.drawPile.length;
  state.game.opponentCount = state.localGame.opponents[0]?.hand.length || 0;
  state.game.opponents = state.localGame.opponents.map((opponent) => ({
    id: opponent.id,
    name: opponent.name,
    count: opponent.hand.length
  }));
  state.game.topCard = state.localGame.discardPile.at(-1);
  state.game.direction = state.localGame.direction === 1 ? "normal" : "inverted";
  state.game.pendingDraw = state.localGame.pendingDraw;
  state.game.pendingAction = state.localGame.pendingAction;
  state.game.canCut = Boolean(state.localGame.cutOpen && state.game.turn !== "one" && state.game.hand?.some(card => {
    const top = state.game.topCard;
    return (top.value === "Pula" || top.value === "Inverte") && card.value === top.value && card.color === top.color;
  }));
}

async function tryLocalCut() {
  if (!state.localGame?.cutOpen || state.game?.roundWinner) return;
  const top = state.localGame.discardPile.at(-1);
  const topId = top.id;
  await new Promise(resolve => window.setTimeout(resolve, 1000 + Math.random() * 2000));
  if (!state.localGame?.cutOpen || state.game?.roundWinner || state.localGame.discardPile.at(-1)?.id !== topId) return;
  const cutter = localOpponents().find(opponent => opponent.hand.some(card =>
    (top.value === "Pula" || top.value === "Inverte") && card.value === top.value && card.color === top.color));
  if (!cutter) return;
  const card = cutter.hand.find(item => item.value === top.value && item.color === top.color);
  cutter.hand = cutter.hand.filter(item => item.id !== card.id);
  state.localGame.discardPile.push({ ...card, playedColor: card.color });
  state.localGame.cutOpen = false;
  state.game.turn = cutter.id;
  showToast(`${cutter.name} cortou a jogada.`);
  syncLocalPublicState();
  render();
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
    !state.animating &&
    !state.busy);
}

function canPlay(card, cut = false) {
  const top = state.game?.topCard;
  if (!top) return false;
  if ((state.game.pendingDraw || state.localGame?.pendingDraw) > 0 && card.value !== (state.game.pendingAction || state.localGame?.pendingAction || top.value)) return false;
  if (cut) return Boolean(state.game.canCut && (card.value === "Pula" || card.value === "Inverte") && card.color !== "wild");
  return card.color === "wild" ||
    card.color === state.game.currentColor ||
    card.value === top.value;
}

function sameAsTop(card) {
  const top = state.game?.topCard;
  return Boolean(top && card.color === top.color && card.value === top.value);
}

function getStatus() {
  if (!state.game) return state.message || "Crie uma sala ou procure uma partida.";
  if (state.game.canceled) return "Sala encerrada.";
  if (!state.game.ready) return `${state.message || "Aguardando outro jogador entrar."} Codigo: ${state.roomCode}`;
  if (state.game.matchWinner) return state.game.matchWinner === state.playerSide ? "Voce venceu a partida!" : "Adversario venceu a partida.";
  if (state.game.roundWinner) {
    if (state.game.roundWinner === state.playerSide) return "Voce venceu a rodada.";
    return state.simulated ? `${localOpponentById(state.game.roundWinner)?.name || "IA adversaria"} venceu a rodada.` : "Adversario venceu a rodada.";
  }
  if (state.pendingColorCardId) return "Escolha uma cor para jogar essa carta.";
  if (state.game.pendingDraw > 0 && state.game.turn === state.playerSide) {
    return `Sua vez: acumule ${state.game.pendingAction || "a penalidade"} ou compre ${state.game.pendingDraw} cartas.`;
  }
  if (state.game.turn === state.playerSide) return "Sua vez.";
  if (state.game.canCut) return "Corte disponivel: jogue sua carta identica antes do proximo jogador.";
  if (state.simulated) return `Vez da ${currentLocalOpponentName()}.`;
  return "Vez do adversario.";
}

function sideLabel(side) {
  return ({ one: "Jogador 1", two: "Jogador 2", three: "Jogador 3", four: "Jogador 4" })[side] || "Jogador";
}

function label(card) {
  if (card.color === "wild") return card.playedColor ? colors[card.playedColor] : "Troca cor";
  return colors[card.color];
}

function displayValue(card) {
  if (card.value === "Inverte") return "\u21bb";
  return card.value;
}

function setMessage(text) {
  state.message = text;
}

function setControlsEnabled(enabled) {
  createRoomEl.disabled = !enabled;
  randomRoomEl.disabled = !enabled;
  simulateTableEl.disabled = !enabled;
  botCountButtons.forEach((button) => {
    button.disabled = !enabled;
  });
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
  state.pendingColorCardId = null;
  state.simulated = false;
  state.localGame = null;
  state.localBotBusy = false;
  state.animating = false;
  colorModalEl.hidden = true;
}

function showEventToast(event) {
  if (event.playerSide === state.playerSide) return;
  if (event.type === "play" && event.card) {
    const opponentId = opponentDomId(event.playerSide);
    const opponent = localOpponentSlots.find(item => item.id === opponentId);
    showToast(`${opponent?.shortName || sideLabel(event.playerSide)} jogou ${event.card.value}.`);
    flashOpponentSeat(opponentId);
    window.setTimeout(() => {
      void animateOpponentCardToDiscard(opponentId, event.card, event.color)
        .finally(() => clearOpponentSeat(opponentId));
    }, 30);
    return;
  }
  if (event.type === "draw" || event.type === "draw-penalty") {
    const opponentId = opponentDomId(event.playerSide);
    flashOpponentSeat(opponentId);
    window.setTimeout(() => {
      void animateDrawTo(getOpponentCardsEl(opponentId))
        .finally(() => clearOpponentSeat(opponentId));
    }, 30);
  }
  if (event.message) showToast(event.message);
  if (event.type === "cut") showToast("Um jogador cortou a jogada antes de voce.");
  if (event.type === "play") showToast("Adversario jogou uma carta.");
  if (event.type === "draw") showToast("Adversario comprou uma carta.");
  if (event.type === "round-win") showToast("Rodada encerrada.");
}

function opponentDomId(side) {
  return ({ two: "bot-left", three: "bot-top", four: "bot-right" })[side] || "bot-top";
}

function flashOpponentSeat(opponentId) {
  const seat = opponentId === "bot-top" ? topOpponentSeatEl : opponentId === "bot-left" ? leftSeatEl : rightSeatEl;
  seat.classList.remove("ai-acted");
  void seat.offsetWidth;
  seat.classList.add("ai-acted");
  window.setTimeout(() => seat.classList.remove("ai-acted"), 1500);
}

function clearOpponentSeat(opponentId) {
  const seat = opponentId === "bot-top" ? topOpponentSeatEl : opponentId === "bot-left" ? leftSeatEl : rightSeatEl;
  seat.classList.remove("ai-acted");
}

function showToast(text) {
  const currentToasts = Array.from(toastStackEl.children);
  if (currentToasts.at(-1)?.textContent === text) return;
  // Mantem apenas as tres mensagens mais recentes; a nova sempre fica visivel.
  while (toastStackEl.children.length >= 3) toastStackEl.firstElementChild.remove();
  const toast = document.createElement("div");
  toast.className = "toast";
  toast.textContent = text;
  toastStackEl.append(toast);
  window.setTimeout(() => toast.remove(), 2200);
}

function setLocalOpponentCount(count) {
  state.localOpponentCount = Math.min(3, Math.max(1, count));
  botCountButtons.forEach((button) => {
    const selected = Number.parseInt(button.dataset.botCount, 10) === state.localOpponentCount;
    button.classList.toggle("selected", selected);
    button.setAttribute("aria-pressed", selected ? "true" : "false");
  });
}

createRoomEl.addEventListener("click", createRoom);
randomRoomEl.addEventListener("click", findRandomRoom);
simulateTableEl.addEventListener("click", simulateTable);
botCountButtons.forEach((button) => {
  button.addEventListener("click", () => {
    setLocalOpponentCount(Number.parseInt(button.dataset.botCount, 10));
  });
});
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
addAiEl.addEventListener("click", () => sendAction({ type: "add-ai" }));
colorPickerEl.addEventListener("click", (event) => {
  const button = event.target.closest("button[data-color]");
  if (!button) return;
  playPendingColorCard(button.dataset.color);
});
colorModalCloseEl.addEventListener("click", () => {
  state.pendingColorCardId = null;
  colorModalEl.hidden = true;
  render();
});
howToPlayEl.addEventListener("click", () => {
  rulesModalEl.hidden = false;
});
rulesModalCloseEl.addEventListener("click", () => {
  rulesModalEl.hidden = true;
});
rulesModalEl.addEventListener("click", (event) => {
  if (event.target === rulesModalEl) rulesModalEl.hidden = true;
});
document.addEventListener("keydown", (event) => {
  if (event.key === "Escape" && !rulesModalEl.hidden) rulesModalEl.hidden = true;
  if (event.key !== "Escape" || colorModalEl.hidden) return;
  state.pendingColorCardId = null;
  colorModalEl.hidden = true;
  render();
});

setLocalOpponentCount(state.localOpponentCount);
restoreSession();
