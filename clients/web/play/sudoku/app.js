const board = document.querySelector(".sudoku-board");
const difficultyScreen = document.querySelector("#difficulty-screen");
const gameWindow = document.querySelector("#game-window");
const gameActions = document.querySelector("#game-actions");
const noteMode = document.querySelector("#note-mode");
const timeEl = document.querySelector(".stat strong");
const errorsEl = document.querySelector(".mistakes strong");
const difficultyEl = document.querySelector(".difficulty");
const numberButtons = [...document.querySelectorAll(".number-pad button")];
const levels = {
  "Fácil": { clues: 42, maxErrors: 10, maxNotes: 120 },
  "Médio": { clues: 34, maxErrors: 5, maxNotes: 90 },
  "Difícil": { clues: 28, maxErrors: 3, maxNotes: 60 }
};

let solution = [], puzzle = [], current = [], wrong = new Set(), noteState = [], selected = -1;
let notes = false, errors = 0, elapsed = 0, timer = null, paused = false, level = "Médio";

function isForced(index) {
  return getCandidates(index).length === 1 || getCandidates(index).some((value) => isSoleCandidate(index, value));
}

function getCandidates(index) {
  return [1, 2, 3, 4, 5, 6, 7, 8, 9]
    .filter((item) => !isBlocked(index, String(item)));
}

function isSoleCandidate(index, value) {
  const row = Math.floor(index / 9);
  const col = index % 9;
  const boxRow = Math.floor(row / 3) * 3;
  const boxCol = Math.floor(col / 3) * 3;
  const units = [
    Array.from({ length: 9 }, (_, item) => row * 9 + item),
    Array.from({ length: 9 }, (_, item) => item * 9 + col),
    Array.from({ length: 3 }, (_, r) => Array.from({ length: 3 }, (_, c) => (boxRow + r) * 9 + boxCol + c)).flat()
  ];
  return units.some((unit) => unit.filter((peer) => {
    if (peer === index || puzzle[peer] || current[peer]) return peer === index;
    return !isBlocked(peer, String(value));
  }).length === 1);
}

function pulseNumber(value) {
  numberButtons.forEach((button) => button.classList.remove("pulse"));
  const button = numberButtons.find((item) => item.textContent.trim() === String(value));
  if (!button) return;
  button.classList.remove("pulse");
  void button.offsetWidth;
  button.classList.add("pulse");
  window.setTimeout(() => button.classList.remove("pulse"), 260);
}

function shuffled(values) {
  const result = [...values];
  for (let i = result.length - 1; i > 0; i -= 1) {
    const j = Math.floor(Math.random() * (i + 1));
    [result[i], result[j]] = [result[j], result[i]];
  }
  return result;
}

function createSolution() {
  const base = (row, col) => (row * 3 + Math.floor(row / 3) + col) % 9;
  const rows = shuffled([0, 1, 2]).flatMap((band) => shuffled([0, 1, 2]).map((row) => band * 3 + row));
  const cols = shuffled([0, 1, 2]).flatMap((stack) => shuffled([0, 1, 2]).map((col) => stack * 3 + col));
  const nums = shuffled([1, 2, 3, 4, 5, 6, 7, 8, 9]);
  return rows.map((row) => cols.map((col) => nums[base(row, col)]));
}

function startGame(chosenLevel = "Médio") {
  level = chosenLevel;
  solution = createSolution();
  puzzle = solution.flat();
  shuffled([...Array(81).keys()]).slice(levels[level].clues).forEach((index) => { puzzle[index] = 0; });
  current = [...puzzle]; wrong = new Set(); noteState = Array.from({ length: 81 }, () => new Set()); selected = -1; errors = 0; elapsed = 0; paused = false;
  difficultyScreen.hidden = true; gameWindow.hidden = false; gameActions.hidden = false;
  document.querySelector(".sudoku-shell").classList.add("playing");
  document.querySelector(".sudoku-shell").classList.remove("level-facil", "level-medio", "level-dificil");
  document.querySelector(".sudoku-shell").classList.add(`level-${level.toLowerCase().normalize("NFD").replace(/[\\u0300-\\u036f]/g, "")}`);
  difficultyEl.innerHTML = `<span class="dot"></span> ${level}`;
  render(); clearInterval(timer);
  timer = setInterval(() => { elapsed += 1; updateStats(); }, 1000);
}

function render() {
  board.innerHTML = "";
  current.forEach((value, index) => {
    const cell = document.createElement("button");
    cell.type = "button";
    const correct = !puzzle[index] && value === solution.flat()[index] && isForced(index);
    cell.className = `cell${puzzle[index] ? " given" : ""}${correct ? " correct" : ""}${selected === index ? " selected" : ""}`;
    cell.disabled = correct;
    cell.dataset.index = index; cell.setAttribute("role", "gridcell");
    if (value) cell.textContent = value;
    if (!puzzle[index] && value) cell.classList.add(wrong.has(index) ? "error" : "user");
    if (!puzzle[index] && noteState[index].size && !value) {
      const availableNotes = [...noteState[index]].filter((item) => !isBlocked(index, item));
      cell.innerHTML = `<span class="notes">${[...noteState[index]].sort().map((item) => {
        const blocked = isBlocked(index, item);
        const candidates = getCandidates(index);
        const forced = !blocked && ((candidates.length === 1 && candidates[0] === Number(item)) || isSoleCandidate(index, Number(item)));
        return `<i class="${blocked ? "blocked" : forced ? "forced" : ""}">${item}</i>`;
      }).join("")}</span>`;
    }
    cell.addEventListener("click", () => { if (!paused && !puzzle[index] && !correct) { selected = index; render(); } });
    board.append(cell);
  });
  numberButtons.forEach((button) => {
    const value = button.textContent.trim();
    button.classList.toggle("marked", selected >= 0 && noteState[selected]?.has(value));
    button.setAttribute("aria-pressed", String(selected >= 0 && noteState[selected]?.has(value)));
  });
  const usedNotes = noteState.reduce((total, set) => total + set.size, 0);
  noteMode.textContent = `✎ Candidatos (${levels[level].maxNotes - usedNotes}x)`;
  updateStats();
}

function updateStats() {
  timeEl.textContent = `${String(Math.floor(elapsed / 60)).padStart(2, "0")}:${String(elapsed % 60).padStart(2, "0")}`;
  errorsEl.textContent = `${errors} / ${levels[level].maxErrors}`;
}

function enter(value) {
  if (paused || selected < 0 || puzzle[selected] || (current[selected] === solution.flat()[selected] && isForced(selected))) return;
  if (notes) return toggleNote(value);
  if (value !== solution.flat()[selected]) {
    current[selected] = value; wrong.add(selected); errors += 1; render();
    if (errors >= levels[level].maxErrors) {
      paused = true;
      alert(`Você atingiu o limite de ${levels[level].maxErrors} erros. Comece uma nova partida.`);
    }
    return;
  }
  current[selected] = value; wrong.delete(selected); render();
  if (current.every((item, index) => item === solution.flat()[index])) {
    paused = true; clearInterval(timer);
    setTimeout(() => alert("Parabéns! Sudoku concluído."), 30);
  }
}

function toggleNote(value) {
  const set = noteState[selected];
  const key = String(value);
  if (set.has(key)) {
    set.delete(key);
  } else {
    const usedNotes = noteState.reduce((total, notesForCell) => total + notesForCell.size, 0);
    if (usedNotes >= levels[level].maxNotes) return;
    set.add(key);
  }
  render();
}

function isBlocked(index, value) {
  const row = Math.floor(index / 9);
  const col = index % 9;
  const boxRow = Math.floor(row / 3) * 3;
  const boxCol = Math.floor(col / 3) * 3;
  for (let peer = 0; peer < 81; peer += 1) {
    const peerRow = Math.floor(peer / 9);
    const peerCol = peer % 9;
    const sameRow = peerRow === row;
    const sameColumn = peerCol === col;
    const sameBox = peerRow >= boxRow && peerRow < boxRow + 3 && peerCol >= boxCol && peerCol < boxCol + 3;
    if ((sameRow || sameColumn || sameBox) && peer !== index && current[peer] === Number(value) && !wrong.has(peer)) return true;
  }
  return false;
}

document.querySelectorAll(".difficulty-option").forEach((button) => button.addEventListener("click", () => startGame(button.querySelector("strong").textContent.trim())));
document.querySelectorAll(".number-pad button").forEach((button) => button.addEventListener("click", () => {
  const value = Number(button.textContent);
  pulseNumber(value);
  enter(value);
}));
noteMode.addEventListener("click", () => { notes = !notes; noteMode.classList.toggle("on", notes); noteMode.setAttribute("aria-pressed", String(notes)); render(); });
document.addEventListener("keydown", (event) => { if (/^[1-9]$/.test(event.key)) enter(Number(event.key)); });

const exitModal = document.querySelector("#exit-modal");
const exitYes = document.querySelector("#exit-yes");
const exitTitle = document.querySelector("#exit-title");
const exitMessage = document.querySelector(".exit-box p");
let exitAction = null;
function showExitConfirmation(action) {
  exitAction = action;
  exitYes.disabled = true;
  exitYes.textContent = "Sair (1s)";
  exitModal.hidden = false;
  window.setTimeout(() => { exitYes.disabled = false; exitYes.textContent = "Sair"; }, 1500);
}
document.querySelector("#back-button").addEventListener("click", () => {
  exitTitle.textContent = "Sair da partida?";
  exitMessage.textContent = "Sua progressão será perdida.";
  showExitConfirmation(() => { window.location.href = "../"; });
});
document.querySelector(".secondary").addEventListener("click", () => {
  exitTitle.textContent = "Começar novo jogo?";
  exitMessage.textContent = "Sua progressão atual será perdida.";
  showExitConfirmation(() => { exitModal.hidden = true; startGame(level); });
});
document.querySelector("#exit-no").addEventListener("click", () => { exitModal.hidden = true; });
exitYes.addEventListener("click", () => { if (!exitYes.disabled && exitAction) exitAction(); });
