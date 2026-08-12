const suitText = { S: "\u2660", H: "\u2665", D: "\u2666", C: "\u2663" };
const rankText = {
  1: "A",
  11: "J",
  12: "Q",
  13: "K"
};

const apiBase = new URL("../../api", window.location.href).pathname.replace(/\/$/, "");
const saveKey = "solitairenet-server-game-id";

const state = {
  game: null,
  selected: null,
  lastClick: { id: null, at: 0 },
  pointer: null,
  justDragged: false,
  busy: false,
  pendingFlip: null,
  queuedFlip: null,
  victoryAnimation: null,
  dealAnimation: null,
  dealing: false,
  celebratedGameId: null
};

const gameBoardEl = document.querySelector(".board");
const board = document.querySelector("#tableau");
const stockEl = document.querySelector("#stock");
const wasteEl = document.querySelector("#waste");
const statusEl = document.querySelector("#status");
const menuButtonEl = document.querySelector("#menu-button");
const newGameEl = document.querySelector("#new-game");
const confirmModalEl = document.querySelector("#confirm-modal");
const confirmTitleEl = document.querySelector("#confirm-title");
const confirmMessageEl = document.querySelector("#confirm-message");
const confirmNoEl = document.querySelector("#confirm-no");
const confirmYesEl = document.querySelector("#confirm-yes");
const foundationEls = [...document.querySelectorAll("[data-foundation]")];

async function request(path, options = {}) {
  const response = await fetch(`${apiBase}${path}`, {
    headers: { "content-type": "application/json" },
    ...options
  });

  if (!response.ok) {
    const body = await response.json().catch(() => ({}));
    throw new Error(body.error || `HTTP ${response.status}`);
  }

  return response.json();
}

async function startNewGame() {
  stopVictoryAnimation();
  stopDealAnimation();
  state.celebratedGameId = null;
  state.game = await request("/games", { method: "POST" });
  localStorage.setItem(saveKey, state.game.id);
  clearSelection();
  state.dealing = true;
  render();
  startDealAnimation();
}

async function loadGame() {
  const id = localStorage.getItem(saveKey);

  if (id) {
    try {
      state.game = await request(`/games/${id}`);
      render();
      return;
    } catch {
      localStorage.removeItem(saveKey);
    }
  }

  await startNewGame();
}

async function sendAction(action, options = {}) {
  if (!state.game || state.busy) return false;

  state.busy = true;
  const startedAt = performance.now();
  try {
    state.game = await request(`/games/${state.game.id}/actions`, {
      method: "POST",
      body: JSON.stringify(action)
    });
    state.lastTiming = {
      action: action.type,
      requestMs: performance.now() - startedAt
    };
    return true;
  } catch (error) {
    if (!options.silent) {
      showStatus(error.message);
    }

    return false;
  } finally {
    state.busy = false;
  }
}

function render() {
  if (!state.game) return;

  const startedAt = performance.now();
  gameBoardEl.classList.toggle("dealing", state.dealing);
  stockEl.classList.toggle("has-cards", state.game.stockCount > 0);
  wasteEl.classList.toggle("has-card", Boolean(state.game.wasteTop));
  stockEl.innerHTML = "";
  wasteEl.innerHTML = "";
  board.innerHTML = "";

  if (state.game.wasteTop) {
    wasteEl.append(cardEl(state.game.wasteTop, { source: "waste", index: 0 }));
  }

  foundationEls.forEach((slot, index) => {
    slot.innerHTML = "";
    const card = state.game.foundations[index];
    if (card) {
      slot.append(cardEl(card, { source: "foundation", index }));
    }
  });

  state.game.tableau.forEach((pile, col) => {
    const column = document.createElement("div");
    column.className = "column";
    column.dataset.column = col;
    column.addEventListener("click", () => onTableauSlot(col));

    pile.forEach((card, row) => {
      const el = cardEl(card, { source: "tableau", index: col, row });
      el.style.top = `calc(var(--down) * ${row})`;
      column.append(el);
    });

    column.style.minHeight = `calc(var(--ch) + var(--down) * ${Math.max(0, pile.length - 1)})`;
    board.append(column);
  });

  const selected = state.selected?.cards?.length
    ? ` | Selecionada: ${state.selected.cards.length}`
    : "";
  showStatus(state.game.won
    ? "Vitoria."
    : `Monte: ${state.game.stockCount} | Lixo: ${state.game.wasteCount}${selected}`);

  if (state.game.won && state.celebratedGameId !== state.game.id) {
    state.celebratedGameId = state.game.id;
    startVictoryAnimation();
  }

  state.lastRenderMs = performance.now() - startedAt;
  if (state.lastTiming) {
    state.lastTiming.renderMs = state.lastRenderMs;
    state.lastTiming.totalMs = state.lastTiming.requestMs + state.lastTiming.renderMs;
    console.info(
      `[SolitaireNET] ${state.lastTiming.action}: ` +
      `request=${state.lastTiming.requestMs.toFixed(1)}ms ` +
      `render=${state.lastTiming.renderMs.toFixed(1)}ms ` +
      `total=${state.lastTiming.totalMs.toFixed(1)}ms`);
    state.lastTiming = null;
  }
}

function cardEl(card, meta) {
  const el = document.createElement("button");
  el.type = "button";
  el.className = "card";

  if (!card.faceUp) {
    el.classList.add("back");
    if (isPendingFlip(meta)) {
      el.classList.add("pending-flip");
    }

    el.setAttribute("aria-label", "Carta fechada");
  } else {
    el.dataset.card = card.id;

    if (isRed(card)) el.classList.add("red");
    if (isSelected(card)) el.classList.add("selected");

    const label = `${rankText[card.rank] || card.rank}${suitText[card.suit]}`;
    el.innerHTML = `<span class="corner">${label}</span><span class="pip">${suitText[card.suit]}</span><span class="corner bottom">${label}</span>`;
    el.setAttribute("aria-label", label);
  }

  el.addEventListener("click", async (event) => {
    event.stopPropagation();
    if (state.dealing) {
      return;
    }

    if (state.busy) {
      queueFlipIfPossible(card, meta);
      return;
    }

    if (state.justDragged) {
      state.justDragged = false;
      return;
    }

    await onCard(card, meta);
  });

  el.addEventListener("pointerdown", (event) => {
    if (state.dealing) return;
    if (state.busy) return;
    if (event.button !== 0 || !card.faceUp) return;
    event.preventDefault();
    startPointer(event, card, meta);
  });

  return el;
}

function startPointer(event, card, meta) {
  state.pointer = {
    id: event.pointerId,
    card,
    meta,
    startX: event.clientX,
    startY: event.clientY,
    x: event.clientX,
    y: event.clientY,
    dragging: false,
    ghost: null
  };

  event.currentTarget.setPointerCapture(event.pointerId);
  event.currentTarget.addEventListener("pointermove", movePointer);
  event.currentTarget.addEventListener("pointerup", endPointer);
  event.currentTarget.addEventListener("pointercancel", cancelPointer);
}

function movePointer(event) {
  const pointer = state.pointer;
  if (!pointer || pointer.id !== event.pointerId) return;

  pointer.x = event.clientX;
  pointer.y = event.clientY;

  const moved = Math.hypot(pointer.x - pointer.startX, pointer.y - pointer.startY);
  if (!pointer.dragging && moved > 6) {
    select(pointer.card, pointer.meta);
    pointer.dragging = true;
    pointer.ghost = makeGhost(state.selected.cards, pointer.startX, pointer.startY);
    document.body.classList.add("dragging");

    state.selected.cards.forEach((item) => {
      document.querySelectorAll(`[data-card="${item.id}"]`).forEach((el) => {
        el.classList.add("drag-source");
      });
    });
  }

  if (pointer.dragging) {
    event.preventDefault();
    moveGhost(pointer.ghost, pointer.x, pointer.y);
  }
}

async function endPointer(event) {
  const pointer = state.pointer;
  cleanupPointerTarget(event.currentTarget, event.pointerId);

  if (!pointer || pointer.id !== event.pointerId) return;

  if (pointer.dragging) {
    event.preventDefault();
    state.justDragged = true;
    const dropTarget = resolveDropTarget(pointer.x, pointer.y);

    cleanupDragVisuals(pointer.ghost);

    if (dropTarget) {
      await applyDropTarget(dropTarget);
    } else {
      clearSelection();
      render();
    }
  }

  state.pointer = null;
}

function cancelPointer(event) {
  cleanupPointerTarget(event.currentTarget, event.pointerId);
  cleanupDragVisuals(state.pointer?.ghost);
  state.pointer = null;
  clearSelection();
  render();
}

function cleanupPointerTarget(target, pointerId) {
  try {
    target.releasePointerCapture(pointerId);
  } catch {
    // Pointer capture may already be gone when the browser cancels the gesture.
  }

  target.removeEventListener("pointermove", movePointer);
  target.removeEventListener("pointerup", endPointer);
  target.removeEventListener("pointercancel", cancelPointer);
}

function makeGhost(cards, x, y) {
  const ghost = document.createElement("div");
  ghost.className = "drag-ghost";

  cards.forEach((card, index) => {
    const cardNode = visualCard(card);
    cardNode.style.top = `calc(var(--down) * ${index})`;
    ghost.append(cardNode);
  });

  document.body.append(ghost);
  moveGhost(ghost, x, y);
  return ghost;
}

function visualCard(card) {
  const el = document.createElement("div");
  paintVisualCard(el, card);
  return el;
}

function paintVisualCard(el, card) {
  el.className = "card";

  if (!card.faceUp) {
    el.classList.add("back");
    el.innerHTML = "";
    return;
  }

  if (isRed(card)) {
    el.classList.add("red");
  }

  const label = `${rankText[card.rank] || card.rank}${suitText[card.suit]}`;
  el.innerHTML = `<span class="corner">${label}</span><span class="pip">${suitText[card.suit]}</span><span class="corner bottom">${label}</span>`;
}

function moveGhost(ghost, x, y) {
  if (!ghost) return;
  ghost.style.transform = `translate(${x - 18}px, ${y - 24}px)`;
}

function removeGhost(ghost) {
  if (ghost) ghost.remove();
}

function cleanupDragVisuals(ghost) {
  removeGhost(ghost);
  document.body.classList.remove("dragging");
  document.querySelectorAll(".drag-source").forEach((el) => {
    el.classList.remove("drag-source");
  });
}

function resolveDropTarget(x, y) {
  const ghost = state.pointer?.ghost;
  if (ghost) ghost.style.display = "none";
  const target = document.elementFromPoint(x, y);
  if (ghost) ghost.style.display = "";

  const foundation = target?.closest?.("[data-foundation]");
  if (foundation) {
    return { kind: "foundation", index: Number(foundation.dataset.foundation) };
  }

  const column = target?.closest?.("[data-column]");
  if (column) {
    return { kind: "tableau", index: Number(column.dataset.column) };
  }

  return null;
}

async function applyDropTarget(target) {
  if (target.kind === "foundation") {
    await moveToFoundation(target.index);
    return;
  }

  if (target.kind === "tableau") {
    await moveToTableau(target.index);
  }
}

async function onStock() {
  if (state.dealing) return;

  clearSelection();

  if (state.game.stockCount === 0 && state.game.wasteCount === 0) {
    render();
    return;
  }

  const ok = await sendAction({
    type: state.game.stockCount > 0 ? "drawStock" : "resetStock"
  });

  if (ok) render();
}

async function onCard(card, meta) {
  if (state.dealing) return;

  if (!card.faceUp) {
    await flipIfTopTableau(meta);
    return;
  }

  const isDouble = state.lastClick.id === card.id && Date.now() - state.lastClick.at < 360;
  state.lastClick = { id: card.id, at: Date.now() };

  if (isDouble && await autoMove(card, meta)) {
    clearSelection();
    render();
    return;
  }

  if (!state.selected) {
    select(card, meta);
    render();
    return;
  }

  if (isSelected(card)) {
    clearSelection();
    render();
    return;
  }

  if (meta.source === "tableau" && await moveToTableau(meta.index)) {
    clearSelection();
    render();
    return;
  }

  if (meta.source === "foundation" && await moveToFoundation(meta.index)) {
    clearSelection();
    render();
    return;
  }

  select(card, meta);
  render();
}

async function onTableauSlot(index) {
  if (state.dealing) return;

  if (state.selected && await moveToTableau(index)) {
    clearSelection();
    render();
  }
}

function select(card, meta) {
  if (meta.source === "tableau") {
    const pile = state.game.tableau[meta.index];
    state.selected = {
      source: meta.source,
      index: meta.index,
      row: meta.row,
      cards: pile.slice(meta.row).filter((item) => item.faceUp)
    };
    return;
  }

  state.selected = {
    source: meta.source,
    index: meta.index,
    row: null,
    cards: [card]
  };
}

function clearSelection() {
  state.selected = null;
}

async function moveToTableau(index) {
  return moveTo({ kind: "tableau", index });
}

async function moveToFoundation(index) {
  return moveTo({ kind: "foundation", index });
}

async function moveTo(target) {
  if (!state.selected || state.busy) return false;

  if (!canMoveLocally(target)) {
    return false;
  }

  const action = {
    type: "move",
    source: {
      kind: state.selected.source,
      index: state.selected.index,
      row: state.selected.row
    },
    target
  };

  return sendOptimisticMove(action);
}

async function sendOptimisticMove(action) {
  const previousGame = structuredClone(state.game);
  const startedAt = performance.now();

  state.busy = true;
  applyLocalMove(action.source, action.target);
  clearSelection();
  render();

  try {
    state.game = await request(`/games/${previousGame.id}/actions`, {
      method: "POST",
      body: JSON.stringify(action)
    });
    state.lastTiming = {
      action: action.type,
      requestMs: performance.now() - startedAt
    };
    render();
    state.busy = false;
    await flushQueuedFlip();
    return true;
  } catch (error) {
    state.game = previousGame;
    state.queuedFlip = null;
    render();
    showStatus(error.message);
    return false;
  } finally {
    state.busy = false;
  }
}

function applyLocalMove(source, target) {
  const moving = removeLocalCards(source);

  if (target.kind === "tableau") {
    state.game.tableau[target.index].push(...moving);
  }

  if (target.kind === "foundation") {
    state.game.foundations[target.index] = moving[0];
  }
}

function removeLocalCards(source) {
  if (source.kind === "waste") {
    const card = state.game.wasteTop;
    state.game.wasteTop = null;
    state.game.wasteCount = Math.max(0, state.game.wasteCount - 1);
    return [card];
  }

  if (source.kind === "foundation") {
    const card = state.game.foundations[source.index];
    state.game.foundations[source.index] = card.rank > 1
      ? {
          id: `${card.suit}${card.rank - 1}`,
          rank: card.rank - 1,
          suit: card.suit,
          faceUp: true
        }
      : null;
    return [card];
  }

  if (source.kind === "tableau") {
    return state.game.tableau[source.index].splice(source.row);
  }

  return [];
}

function canMoveLocally(target) {
  const moving = state.selected?.cards || [];
  if (!state.game || moving.length === 0) return false;

  const card = moving[0];
  if (!card.faceUp) return false;

  if (target.kind === "tableau") {
    if (state.selected.source === "tableau" && state.selected.index === target.index) {
      return false;
    }

    const pile = state.game.tableau[target.index];
    const targetTop = topCard(pile);

    if (!targetTop) {
      return card.rank === 13;
    }

    return targetTop.faceUp &&
      isRed(targetTop) !== isRed(card) &&
      card.rank === targetTop.rank - 1;
  }

  if (target.kind === "foundation") {
    if (moving.length !== 1) return false;

    const targetTop = state.game.foundations[target.index];
    if (!targetTop) {
      return card.rank === 1;
    }

    return targetTop.suit === card.suit &&
      card.rank === targetTop.rank + 1;
  }

  return false;
}

async function autoMove(card, meta) {
  select(card, meta);

  for (let i = 0; i < 4; i += 1) {
    if (await moveToFoundation(i)) return true;
  }

  clearSelection();
  return false;
}

async function flipIfTopTableau(meta) {
  if (meta.source !== "tableau") return;
  if (!isTopHiddenTableauCard(meta)) return;

  state.pendingFlip = { index: meta.index, row: meta.row };
  render();
  const ok = await sendAction({
    type: "flipTableau",
    source: { kind: "tableau", index: meta.index, row: meta.row }
  });

  state.pendingFlip = null;
  if (ok) render();
  else render();
}

function queueFlipIfPossible(card, meta) {
  if (card.faceUp || !isTopHiddenTableauCard(meta)) return;

  state.queuedFlip = { index: meta.index };
  state.pendingFlip = { index: meta.index, row: meta.row };
  render();
}

async function flushQueuedFlip() {
  if (!state.queuedFlip || !state.game) return;

  const index = state.queuedFlip.index;
  state.queuedFlip = null;
  const pile = state.game.tableau[index];
  const row = pile.length - 1;

  if (row < 0 || pile[row].faceUp) {
    state.pendingFlip = null;
    render();
    return;
  }

  await flipIfTopTableau({ source: "tableau", index, row });
}

function isTopHiddenTableauCard(meta) {
  if (meta.source !== "tableau" || !state.game) return false;

  const pile = state.game.tableau[meta.index];
  return Boolean(pile && meta.row === pile.length - 1 && !pile[meta.row]?.faceUp);
}

function isPendingFlip(meta) {
  return Boolean(
    state.pendingFlip &&
    meta.source === "tableau" &&
    state.pendingFlip.index === meta.index &&
    state.pendingFlip.row === meta.row);
}

function topCard(pile) {
  return pile[pile.length - 1];
}

function isRed(card) {
  return card.suit === "H" || card.suit === "D";
}

function isSelected(card) {
  return Boolean(card.id && state.selected?.cards.some((item) => item.id === card.id));
}

function showStatus(text) {
  statusEl.textContent = text;
}

function startDealAnimation() {
  stopDealAnimation({ keepDealing: true });

  if (!state.game)
    return;

  const stockRect = stockEl.getBoundingClientRect();
  const elements = [];
  const duration = 330;
  const gapDelay = 38;
  let index = 0;

  state.game.tableau.forEach((pile, col) => {
    const column = board.querySelector(`[data-column="${col}"]`);

    pile.forEach((card, row) => {
      const node = column?.children[row];
      if (!node)
        return;

      const target = node.getBoundingClientRect();
      const delay = index * gapDelay;
      const startX = stockRect.left - target.left;
      const startY = stockRect.top - target.top;
      node.style.setProperty("--deal-x", `${startX}px`);
      node.style.setProperty("--deal-y", `${startY}px`);
      node.style.setProperty("--deal-near-x", `${startX * 0.08}px`);
      node.style.setProperty("--deal-near-y", `${startY * 0.08}px`);
      node.style.setProperty("--deal-delay", `${delay}ms`);
      node.style.setProperty("--deal-duration", `${duration}ms`);
      node.style.zIndex = String(200 + index);
      node.classList.add("deal-tableau-card");

      if (card.faceUp) {
        node.classList.add("deal-reveal-card");
      }

      elements.push(node);
      index += 1;
    });
  });

  const totalMs = Math.max(0, index - 1) * gapDelay + duration + 120;
  const timer = window.setTimeout(() => {
    if (state.dealAnimation?.timer === timer) {
      stopDealAnimation();
      state.dealing = false;
      render();
    }
  }, totalMs);

  state.dealAnimation = {
    elements,
    timer
  };
}

function stopDealAnimation(options = {}) {
  if (!state.dealAnimation)
    return;

  window.clearTimeout(state.dealAnimation.timer);
  state.dealAnimation.elements.forEach(clearDealElement);
  state.dealAnimation = null;

  if (!options.keepDealing) {
    state.dealing = false;
    gameBoardEl.classList.remove("dealing");
  }
}

function clearDealElement(node) {
  node.classList.remove("deal-tableau-card", "deal-reveal-card");
  node.style.removeProperty("--deal-x");
  node.style.removeProperty("--deal-y");
  node.style.removeProperty("--deal-near-x");
  node.style.removeProperty("--deal-near-y");
  node.style.removeProperty("--deal-delay");
  node.style.removeProperty("--deal-duration");
  node.style.removeProperty("z-index");
}

function startVictoryAnimation(options = {}) {
  stopVictoryAnimation();

  const layer = document.createElement("div");
  layer.className = "victory-layer";
  layer.setAttribute("aria-hidden", "true");
  document.body.append(layer);

  const sourceRects = foundationEls
    .map((el) => el.getBoundingClientRect())
    .filter((rect) => rect.width > 0 && rect.height > 0);

  const fallbackRect = stockEl.getBoundingClientRect();
  const tableauRect = board.getBoundingClientRect();
  const cardW = getCardWidth();
  const cardH = getCardHeight();
  const gap = getCssPixel("--gap", 18);
  const down = getCssPixel("--down", 34);
  const animations = [];
  const timers = [];
  const suits = ["S", "H", "D", "C"];
  let cardIndex = 0;

  suits.forEach((suit, columnIndex) => {
    const source = sourceRects[columnIndex] || fallbackRect;
    timers.push(window.setTimeout(() => {
      foundationEls[columnIndex].innerHTML = "";
    }, columnIndex * 920 + 120));

    for (let rank = 13; rank >= 1; rank -= 1) {
      const card = {
        id: `${suit}${rank}`,
        rank,
        suit,
        faceUp: true
      };
      const node = visualCard(card);
      node.classList.add("victory-card");
      node.style.zIndex = String(100 + columnIndex * 20 + rank);
      layer.append(node);

      const rowIndex = 13 - rank;
      const startX = source.left;
      const startY = source.top;
      const targetColumn = cardIndex % 7;
      const targetRow = Math.floor(cardIndex / 7);
      const endX = Math.min(
        window.innerWidth - cardW - 8,
        tableauRect.left + targetColumn * (cardW + gap));
      const endY = Math.min(
        window.innerHeight - cardH - 52,
        tableauRect.top + targetRow * down * 0.92);
      const midX = startX + (endX - startX) * 0.52;
      const arcY = Math.max(8, Math.min(startY, endY) - cardH * 0.72 - targetRow * 7);
      const delay = targetColumn * 920 + targetRow * 165;
      const duration = 1780 + targetRow * 105;
      const rotate = (targetColumn % 2 === 0 ? 1 : -1) * (80 + rowIndex * 8);

      cardIndex += 1;

      node.style.transform =
        `translate3d(${startX}px, ${startY}px, 0) rotate(0deg)`;

      const animation = node.animate([
        {
          transform: `translate3d(${startX}px, ${startY}px, 0) rotate(0deg)`,
          offset: 0
        },
        {
          transform: `translate3d(${midX}px, ${arcY}px, 0) rotate(${rotate * 0.34}deg)`,
          offset: 0.38
        },
        {
          transform: `translate3d(${endX}px, ${endY - cardH * 0.16}px, 0) rotate(${rotate * 0.82}deg)`,
          offset: 0.82
        },
        {
          transform: `translate3d(${endX}px, ${endY}px, 0) rotate(${rotate}deg)`,
          offset: 1
        }
      ], {
        duration,
        delay,
        easing: "cubic-bezier(.18,.72,.24,1)",
        fill: "forwards"
      });

      animations.push(animation);
    }
  });

  if (options.simulated) {
    showStatus("Vitoria simulada.");
  }

  state.victoryAnimation = {
    layer,
    animations,
    timers,
    timer: window.setTimeout(() => {
      if (state.victoryAnimation?.layer === layer) {
        clearFoundationSlots();
        stopVictoryAnimation();
      }
    }, 12800)
  };
}

function stopVictoryAnimation() {
  if (!state.victoryAnimation) return;

  window.clearTimeout(state.victoryAnimation.timer);
  state.victoryAnimation.timers.forEach((timer) => window.clearTimeout(timer));
  state.victoryAnimation.animations.forEach((animation) => animation.cancel());
  state.victoryAnimation.layer.remove();
  state.victoryAnimation = null;

  if (state.game?.won) {
    clearFoundationSlots();
  }
}

function clearFoundationSlots() {
  foundationEls.forEach((slot) => {
    slot.innerHTML = "";
  });
}

function getCardWidth() {
  return getCssPixel("--cw", 82);
}

function getCardHeight() {
  return getCssPixel("--ch", 118);
}

function getCssPixel(name, fallback) {
  return Number.parseFloat(getComputedStyle(document.documentElement).getPropertyValue(name)) || fallback;
}

function confirmDelayed({ title, message, yesText = "Sim", noText = "Nao" }) {
  return new Promise((resolve) => {
    let remaining = 3;
    let timer = null;

    confirmTitleEl.textContent = title;
    confirmMessageEl.textContent = message;
    confirmNoEl.textContent = noText;
    confirmYesEl.disabled = true;
    confirmYesEl.textContent = `${yesText} (${remaining})`;
    confirmModalEl.hidden = false;

    const finish = (value) => {
      window.clearInterval(timer);
      confirmModalEl.hidden = true;
      confirmNoEl.removeEventListener("click", onNo);
      confirmYesEl.removeEventListener("click", onYes);
      resolve(value);
    };

    const onNo = () => finish(false);
    const onYes = () => {
      if (!confirmYesEl.disabled) {
        finish(true);
      }
    };

    confirmNoEl.addEventListener("click", onNo);
    confirmYesEl.addEventListener("click", onYes);

    timer = window.setInterval(() => {
      remaining -= 1;

      if (remaining > 0) {
        confirmYesEl.textContent = `${yesText} (${remaining})`;
        return;
      }

      window.clearInterval(timer);
      confirmYesEl.disabled = false;
      confirmYesEl.textContent = yesText;
    }, 1000);
  });
}

stockEl.addEventListener("click", onStock);
menuButtonEl.addEventListener("click", async () => {
  const ok = await confirmDelayed({
    title: "Retornar ao menu",
    message: "Deseja retornar ao menu?",
    yesText: "Sim",
    noText: "Nao"
  });

  if (ok) {
    window.location.href = "../";
  }
});
wasteEl.addEventListener("click", () => {
  const card = state.game?.wasteTop;
  if (!card) return;

  select(card, { source: "waste", index: 0 });
  render();
});
foundationEls.forEach((slot, index) => {
  slot.addEventListener("click", async () => {
    if (state.selected && await moveToFoundation(index)) {
      clearSelection();
      render();
    }
  });
});
newGameEl.addEventListener("click", async () => {
  const ok = await confirmDelayed({
    title: "Reiniciar partida",
    message: "Deseja reiniciar a partida?",
    yesText: "Sim",
    noText: "Nao"
  });

  if (ok) {
    await startNewGame();
  }
});
document.addEventListener("touchmove", (event) => {
  if (state.pointer?.dragging) {
    event.preventDefault();
  }
}, { passive: false });

window.solitaireDebug = {
  get gameId() {
    return state.game?.id;
  },
  get lastRenderMs() {
    return state.lastRenderMs;
  },
  state: () => state.game
};

loadGame().catch((error) => {
  showStatus(`Servidor indisponivel: ${error.message}`);
});
