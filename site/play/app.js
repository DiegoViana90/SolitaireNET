const suits = ["S", "H", "D", "C"];
const suitText = { S: "\u2660", H: "\u2665", D: "\u2666", C: "\u2663" };
const rankText = {
  1: "A",
  11: "J",
  12: "Q",
  13: "K"
};

const state = {
  stock: [],
  waste: [],
  tableau: Array.from({ length: 7 }, () => []),
  foundations: Array.from({ length: 4 }, () => []),
  selected: null,
  lastClick: { id: null, at: 0 },
  pointer: null,
  justDragged: false
};

const board = document.querySelector("#tableau");
const stockEl = document.querySelector("#stock");
const wasteEl = document.querySelector("#waste");
const statusEl = document.querySelector("#status");
const newGameEl = document.querySelector("#new-game");
const foundationEls = [...document.querySelectorAll("[data-foundation]")];

function makeDeck() {
  const deck = [];
  for (const suit of suits) {
    for (let rank = 1; rank <= 13; rank += 1) {
      deck.push({ id: `${suit}${rank}`, suit, rank, faceUp: false });
    }
  }
  return deck;
}

function shuffle(cards) {
  for (let i = cards.length - 1; i > 0; i -= 1) {
    const j = Math.floor(Math.random() * (i + 1));
    [cards[i], cards[j]] = [cards[j], cards[i]];
  }
  return cards;
}

function newGame() {
  state.stock = shuffle(makeDeck());
  state.waste = [];
  state.tableau = Array.from({ length: 7 }, () => []);
  state.foundations = Array.from({ length: 4 }, () => []);
  state.selected = null;

  for (let col = 0; col < 7; col += 1) {
    for (let row = 0; row <= col; row += 1) {
      const card = state.stock.pop();
      card.faceUp = row === col;
      state.tableau[col].push(card);
    }
  }

  save();
  render();
}

function allPiles() {
  return [
    state.stock,
    state.waste,
    ...state.tableau,
    ...state.foundations
  ];
}

function isCard(card) {
  return Boolean(
    card &&
    suits.includes(card.suit) &&
    Number.isInteger(card.rank) &&
    card.rank >= 1 &&
    card.rank <= 13 &&
    typeof card.faceUp === "boolean"
  );
}

function hasValidState() {
  if (!Array.isArray(state.stock) || !Array.isArray(state.waste)) return false;
  if (!Array.isArray(state.tableau) || state.tableau.length !== 7) return false;
  if (!Array.isArray(state.foundations) || state.foundations.length !== 4) return false;

  const cards = allPiles().flat();
  const ids = new Set(cards.map((card) => card?.id));

  return cards.length === 52 &&
    ids.size === 52 &&
    cards.every(isCard) &&
    state.tableau.every(Array.isArray) &&
    state.foundations.every(Array.isArray);
}

function save() {
  localStorage.setItem("solitairenet-web", JSON.stringify({
    stock: state.stock,
    waste: state.waste,
    tableau: state.tableau,
    foundations: state.foundations
  }));
}

function load() {
  const raw = localStorage.getItem("solitairenet-web");
  if (!raw) {
    newGame();
    return;
  }

  try {
    const saved = JSON.parse(raw);
    state.stock = saved.stock || [];
    state.waste = saved.waste || [];
    state.tableau = saved.tableau || Array.from({ length: 7 }, () => []);
    state.foundations = saved.foundations || Array.from({ length: 4 }, () => []);

    if (!hasValidState()) {
      newGame();
      return;
    }

    render();
  } catch {
    newGame();
  }
}

function render() {
  stockEl.classList.toggle("has-cards", state.stock.length > 0);
  stockEl.innerHTML = "";
  wasteEl.innerHTML = "";
  board.innerHTML = "";

  if (state.waste.length > 0) {
    wasteEl.append(cardEl(topCard(state.waste), { source: "waste", index: 0 }));
  }

  foundationEls.forEach((slot, index) => {
    slot.innerHTML = "";
    const card = topCard(state.foundations[index]);
    if (card) {
      slot.append(cardEl(card, { source: "foundation", index }));
    }
  });

  state.tableau.forEach((pile, col) => {
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

  const won = state.foundations.reduce((sum, pile) => sum + pile.length, 0) === 52;
  const selected = state.selected?.cards?.length
    ? ` | Selecionada: ${state.selected.cards.length}`
    : "";
  statusEl.textContent = won
    ? "Vitoria."
    : `Monte: ${state.stock.length} | Lixo: ${state.waste.length}${selected}`;
}

function cardEl(card, meta) {
  const el = document.createElement("button");
  el.type = "button";
  el.className = "card";
  el.dataset.card = card.id;

  if (!card.faceUp) {
    el.classList.add("back");
    el.setAttribute("aria-label", "Carta fechada");
  } else {
    if (isRed(card)) el.classList.add("red");
    if (isSelected(card)) el.classList.add("selected");

    const label = `${rankText[card.rank] || card.rank}${suitText[card.suit]}`;
    el.innerHTML = `<span class="corner">${label}</span><span class="pip">${suitText[card.suit]}</span><span class="corner bottom">${label}</span>`;
    el.setAttribute("aria-label", label);
  }

  el.addEventListener("click", (event) => {
    event.stopPropagation();
    if (state.justDragged) {
      state.justDragged = false;
      return;
    }

    onCard(card, meta);
  });

  el.addEventListener("pointerdown", (event) => {
    if (event.button !== 0 || !card.faceUp) return;
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
    document.querySelectorAll(`[data-card="${pointer.card.id}"]`).forEach((el) => {
      el.classList.add("drag-source");
    });
  }

  if (pointer.dragging) {
    event.preventDefault();
    moveGhost(pointer.ghost, pointer.x, pointer.y);
  }
}

function endPointer(event) {
  const pointer = state.pointer;
  cleanupPointerTarget(event.currentTarget, event.pointerId);

  if (!pointer || pointer.id !== event.pointerId) return;

  if (pointer.dragging) {
    event.preventDefault();
    state.justDragged = true;
    const dropped = dropAt(pointer.x, pointer.y);

    removeGhost(pointer.ghost);
    document.body.classList.remove("dragging");

    if (dropped) {
      afterMove();
    } else {
      clearSelection();
      render();
    }
  }

  state.pointer = null;
}

function cancelPointer(event) {
  cleanupPointerTarget(event.currentTarget, event.pointerId);
  if (state.pointer?.ghost) removeGhost(state.pointer.ghost);
  document.body.classList.remove("dragging");
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
  el.className = `card ${isRed(card) ? "red" : ""}`;
  const label = `${rankText[card.rank] || card.rank}${suitText[card.suit]}`;
  el.innerHTML = `<span class="corner">${label}</span><span class="pip">${suitText[card.suit]}</span><span class="corner bottom">${label}</span>`;
  return el;
}

function moveGhost(ghost, x, y) {
  if (!ghost) return;
  ghost.style.transform = `translate(${x - 18}px, ${y - 24}px)`;
}

function removeGhost(ghost) {
  if (ghost) ghost.remove();
}

function dropAt(x, y) {
  const ghost = state.pointer?.ghost;
  if (ghost) ghost.style.display = "none";
  const target = document.elementFromPoint(x, y);
  if (ghost) ghost.style.display = "";

  const foundation = target?.closest?.("[data-foundation]");
  if (foundation) {
    return moveToFoundation(Number(foundation.dataset.foundation));
  }

  const column = target?.closest?.("[data-column]");
  if (column) {
    return moveToTableau(Number(column.dataset.column));
  }

  return false;
}

function onStock() {
  clearSelection();

  if (state.stock.length > 0) {
    const card = state.stock.pop();
    card.faceUp = true;
    state.waste.push(card);
  } else if (state.waste.length > 0) {
    while (state.waste.length > 0) {
      const card = state.waste.pop();
      card.faceUp = false;
      state.stock.push(card);
    }
  }

  save();
  render();
}

function onCard(card, meta) {
  if (!card.faceUp) {
    flipIfTopTableau(card, meta);
    return;
  }

  const isDouble = state.lastClick.id === card.id && Date.now() - state.lastClick.at < 360;
  state.lastClick = { id: card.id, at: Date.now() };

  if (isDouble && autoMove(card, meta)) {
    afterMove();
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

  if (meta.source === "tableau" && moveToTableau(meta.index)) {
    afterMove();
    return;
  }

  if (meta.source === "foundation" && moveToFoundation(meta.index)) {
    afterMove();
    return;
  }

  select(card, meta);
  render();
}

function onTableauSlot(index) {
  if (state.selected && moveToTableau(index)) {
    afterMove();
  }
}

function select(card, meta) {
  if (meta.source === "tableau") {
    const pile = state.tableau[meta.index];
    state.selected = {
      source: meta.source,
      index: meta.index,
      row: meta.row,
      cards: pile.slice(meta.row)
    };
    return;
  }

  state.selected = {
    source: meta.source,
    index: meta.index,
    row: 0,
    cards: [card]
  };
}

function clearSelection() {
  state.selected = null;
}

function moveToTableau(index) {
  const moving = state.selected?.cards || [];
  if (moving.length === 0) return false;

  const card = moving[0];
  const target = state.tableau[index];
  const targetTop = topCard(target);

  if (target.length === 0 && card.rank !== 13) return false;
  if (targetTop && (!targetTop.faceUp || isRed(targetTop) === isRed(card) || card.rank !== targetTop.rank - 1)) return false;

  removeSelected();
  state.tableau[index].push(...moving);
  flipExposed();
  return true;
}

function moveToFoundation(index) {
  const moving = state.selected?.cards || [];
  if (moving.length !== 1) return false;

  const card = moving[0];
  const foundation = state.foundations[index];
  const foundationTop = topCard(foundation);

  if (!foundationTop && card.rank !== 1) return false;
  if (foundationTop && (foundationTop.suit !== card.suit || card.rank !== foundationTop.rank + 1)) return false;

  removeSelected();
  foundation.push(card);
  flipExposed();
  return true;
}

function autoMove(card, meta) {
  select(card, meta);

  for (let i = 0; i < 4; i += 1) {
    if (moveToFoundation(i)) return true;
  }

  clearSelection();
  return false;
}

function removeSelected() {
  const selected = state.selected;
  if (!selected) return;

  if (selected.source === "waste") {
    state.waste.pop();
  }

  if (selected.source === "foundation") {
    state.foundations[selected.index].pop();
  }

  if (selected.source === "tableau") {
    state.tableau[selected.index].splice(selected.row);
  }

  clearSelection();
}

function flipIfTopTableau(card, meta) {
  if (meta.source !== "tableau") return;
  const pile = state.tableau[meta.index];
  if (topCard(pile)?.id === card.id) {
    card.faceUp = true;
    afterMove();
  }
}

function flipExposed() {
  state.tableau.forEach((pile) => {
    const card = topCard(pile);
    if (card && !card.faceUp) card.faceUp = true;
  });
}

function afterMove() {
  clearSelection();
  save();
  render();
}

function topCard(pile) {
  return pile[pile.length - 1];
}

function isRed(card) {
  return card.suit === "H" || card.suit === "D";
}

function isSelected(card) {
  return Boolean(state.selected?.cards.some((item) => item.id === card.id));
}

stockEl.addEventListener("click", onStock);
foundationEls.forEach((slot, index) => {
  slot.addEventListener("click", () => {
    if (state.selected && moveToFoundation(index)) afterMove();
  });
});
newGameEl.addEventListener("click", newGame);

load();
