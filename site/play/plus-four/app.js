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
  simulated: false,
  localGame: null,
  localOpponentCount: 1,
  animating: false,
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
const simulateTableEl = document.querySelector("#simulate-table");
const botCountButtons = Array.from(document.querySelectorAll("[data-bot-count]"));
const roomCodeEl = document.querySelector("#room-code");
const joinCodeEl = document.querySelector("#join-code");
const leaveRoomEl = document.querySelector("#leave-room");
const drawCardEl = document.querySelector("#draw-card");
const drawCountEl = document.querySelector("#draw-count");
const discardCardEl = document.querySelector("#discard-card");
const handEl = document.querySelector("#hand");
const opponentCardsEl = document.querySelector("#opponent-cards");
const opponentCountEl = document.querySelector("#opponent-count");
const opponentScoreLabelEl = document.querySelector("#opponent-score-label");
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
const myScoreEl = document.querySelector("#my-score");
const opponentScoreEl = document.querySelector("#opponent-score");
const roundNumberEl = document.querySelector("#round-number");
const nextRoundEl = document.querySelector("#next-round");
const colorPickerEl = document.querySelector("#color-picker");
const colorModalEl = document.querySelector("#color-modal");
const colorModalCardEl = document.querySelector("#color-modal-card");
const colorModalCloseEl = document.querySelector("#color-modal-close");
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
  if (state.pendingColorCardId && !game.hand.some((card) => card.id === state.pendingColorCardId)) {
    state.pendingColorCardId = null;
  }

  roomInfoEl.textContent = state.simulated
    ? `Partida local | ${localOpponents().length} IA${localOpponents().length === 1 ? "" : "s"} adversaria${localOpponents().length === 1 ? "" : "s"}`
    : `Sala ${state.roomCode} | Voce e Jogador ${state.playerSide === "one" ? "1" : "2"}`;
  drawCountEl.textContent = game.drawCount;
  myScoreEl.textContent = state.playerSide === "one" ? game.oneScore : game.twoScore;
  opponentScoreLabelEl.textContent = state.simulated && localOpponents().length > 1 ? "IAs" : "Adversario";
  opponentScoreEl.textContent = state.playerSide === "one" ? game.twoScore : game.oneScore;
  roundNumberEl.textContent = game.round;
  nextRoundEl.hidden = !game.roundWinner || game.matchWinner;
  drawCardEl.disabled = !canAct();

  discardCardEl.replaceChildren(cardEl(game.topCard, { large: true }));
  handEl.replaceChildren(...game.hand.map((card) => cardEl(card)));
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
    topOpponentLabelEl.textContent = "Adversario";
    opponentCountEl.textContent = `${state.game.opponentCount} carta${state.game.opponentCount === 1 ? "" : "s"}`;
    opponentCardsEl.replaceChildren(...cardBacks(state.game.opponentCount));
    topOpponentSeatEl.classList.toggle("current-turn", state.game.turn !== state.playerSide);
    renderSideOpponent(null, {
      seat: leftSeatEl,
      label: leftSeatLabelEl,
      cards: leftOpponentCardsEl,
      count: leftOpponentCountEl
    });
    renderSideOpponent(null, {
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
    const playable = canAct() && canPlay(card);
    if (playable) el.classList.add("playable");
    el.type = "button";
    el.disabled = !playable;
    el.addEventListener("click", () => playCard(card, el));
  }

  return el;
}

async function playCard(card, sourceEl) {
  if (!canAct() || !canPlay(card)) return;
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
  await playCardWithMotion(card, null, sourceEl);
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
  await playCardWithMotion(card, color, findHandCardEl(card.id));
}

async function playCardWithMotion(card, color, sourceEl) {
  state.animating = true;
  try {
    await animateCardToDiscard(color, sourceEl);
  } finally {
    state.animating = false;
  }

  await sendAction({
    type: "play",
    cardId: card.id,
    color
  });
}

function findHandCardEl(cardId) {
  return Array.from(handEl.querySelectorAll("[data-card-id]"))
    .find((item) => item.dataset.cardId === cardId);
}

function needsColorChoice(card) {
  return card.color === "wild" || card.value === "Inverte";
}

async function animateCardToDiscard(color, sourceEl, options = {}) {
  if (!sourceEl || window.matchMedia("(prefers-reduced-motion: reduce)").matches) return;

  const from = sourceEl.getBoundingClientRect();
  const to = discardCardEl.getBoundingClientRect();
  if (!from.width || !to.width) return;
  const flightFrom = getFlightRect(sourceEl, from, options);

  const ghost = options.card
    ? cardEl({ ...options.card, playedColor: color || options.card.playedColor }, { large: true })
    : sourceEl.cloneNode(true);
  ghost.classList.add("flying-card");
  ghost.classList.remove("playable", "pending-color");
  ghost.removeAttribute("disabled");
  if (color) ghost.classList.add(`chosen-${color}`);

  Object.assign(ghost.style, {
    left: `${flightFrom.left}px`,
    top: `${flightFrom.top}px`,
    width: `${flightFrom.width}px`,
    height: `${flightFrom.height}px`
  });

  document.body.append(ghost);

  const deltaX = to.left + (to.width / 2) - (flightFrom.left + (flightFrom.width / 2));
  const deltaY = to.top + (to.height / 2) - (flightFrom.top + (flightFrom.height / 2));
  const scale = Math.min(1.65, Math.max(1.08, to.height / flightFrom.height));

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
    turnOrder: localTurnOrderFor(opponents)
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
    const card = drawLocalCard();
    if (!card) {
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

    state.game.hand.push(card);
    state.game.turn = localNextSide("one");
    syncLocalPublicState();
    render();
    scheduleLocalBotTurn();
    return;
  }

  if (action.type === "play") {
    const card = state.game.hand.find((item) => item.id === action.cardId);
    if (!card || !canPlay(card)) return;

    state.game.hand = state.game.hand.filter((item) => item.id !== action.cardId);
    state.localGame.discardPile.push({ ...card, playedColor: action.color || card.playedColor });
    state.game.topCard = state.localGame.discardPile.at(-1);
    state.game.currentColor = action.color || card.color;
    state.game.turn = await nextLocalTurnAfter(card, "one");
    finishLocalRoundIfNeeded("one");
    syncLocalPublicState();
    render();

    scheduleLocalBotTurn();
  }
}

async function simulateBotTurn() {
  if (!state.simulated || !state.game || !state.localGame || state.game.turn === "one") return;

  const opponent = localOpponentById(state.game.turn);
  if (!opponent) return;

  const card = chooseBotCard(opponent);
  if (!card) {
    const drawn = drawLocalCard();
    if (drawn) {
      state.animating = true;
      try {
        await animateDrawTo(getOpponentCardsEl(opponent.id));
      } finally {
        state.animating = false;
      }

      opponent.hand.push(drawn);
      showToast(`${opponent.name} comprou uma carta.`);
    } else {
      showToast(`${opponent.name} passou.`);
    }

    state.game.turn = localNextSide(opponent.id);
    syncLocalPublicState();
    render();
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
  state.game.currentColor = chosenColor || card.color;
  state.game.topCard = state.localGame.discardPile.at(-1);
  state.game.turn = await nextLocalTurnAfter(card, opponent.id);
  finishLocalRoundIfNeeded(opponent.id);
  syncLocalPublicState();
  showToast(`${opponent.name} jogou ${card.value}.`);
  render();

  scheduleLocalBotTurn();
}

async function animateOpponentCardToDiscard(opponentId, card, color) {
  const sourceEl = findOpponentCardEl(opponentId);
  await animateCardToDiscard(color, sourceEl, { card });
}

function findOpponentCardEl(opponentId) {
  const container = getOpponentCardsEl(opponentId);
  const cards = Array.from(container.querySelectorAll(".card-back"));
  return cards.at(-1) || container;
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
  if (card.value === "+2") {
    await drawLocalCardsWithMotion(next, 2);
    return localNextSide(next);
  }

  if (card.value === "+4") {
    await drawLocalCardsWithMotion(next, 4);
    return localNextSide(next);
  }

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

function canPlay(card) {
  const top = state.game?.topCard;
  if (!top) return false;
  return card.color === "wild" ||
    card.value === "Inverte" ||
    card.color === state.game.currentColor ||
    card.value === top.value;
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
  if (state.game.turn === state.playerSide) return "Sua vez.";
  if (state.simulated) return `Vez da ${currentLocalOpponentName()}.`;
  return "Vez do adversario.";
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
  state.animating = false;
  colorModalEl.hidden = true;
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
document.addEventListener("keydown", (event) => {
  if (event.key !== "Escape" || colorModalEl.hidden) return;
  state.pendingColorCardId = null;
  colorModalEl.hidden = true;
  render();
});

setLocalOpponentCount(state.localOpponentCount);
restoreSession();
