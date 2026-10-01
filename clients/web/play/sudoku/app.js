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
  "Fácil": { clues: 42, maxErrors: 10, maxNotes: 80 },
  "Médio": { clues: 34, maxErrors: 5, maxNotes: 60 },
  "Difícil": { clues: 28, maxErrors: 3, maxNotes: 30 }
};

let solution = [], puzzle = [], current = [], wrong = new Set(), noteState = [], selected = -1, notesUsed = 0;
let notes = false, errors = 0, elapsed = 0, timer = null, paused = false, level = "Médio";

function getCandidates(index) {
  return [1, 2, 3, 4, 5, 6, 7, 8, 9]
    .filter((item) => !isBlocked(index, String(item)));
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
  current = [...puzzle]; wrong = new Set(); noteState = Array.from({ length: 81 }, () => new Set()); notesUsed = 0; selected = -1; errors = 0; elapsed = 0; paused = false;
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
    // A resposta só fica correta depois que o jogador a informa.
    // O jogo não deve deduzir e travar uma célula apenas porque restou
    // uma possibilidade válida no tabuleiro.
    const correct = !puzzle[index] && value === solution.flat()[index];
    cell.className = `cell${puzzle[index] ? " given" : ""}${correct ? " correct" : ""}${selected === index ? " selected" : ""}`;
    cell.disabled = correct;
    cell.dataset.index = index; cell.setAttribute("role", "gridcell");
    if (value) cell.textContent = value;
    if (!puzzle[index] && value) cell.classList.add(wrong.has(index) ? "error" : "user");
    if (!puzzle[index] && noteState[index].size && !value) {
      cell.innerHTML = `<span class="notes">${[...noteState[index]].sort().map((item) => {
        const blocked = isBlocked(index, item);
        const candidates = getCandidates(index);
        const forced = !blocked && candidates.length === 1 && candidates[0] === Number(item);
        return `<i class="${blocked ? "blocked" : forced ? "forced" : ""}">${item}</i>`;
      }).join("")}</span>`;
    }
    cell.addEventListener("click", () => { if (!paused && !puzzle[index] && !correct) { selected = index; render(); } });
    board.append(cell);
  });
  numberButtons.forEach((button) => {
    const value = button.textContent.trim();
    const marked = notes && selected >= 0 && noteState[selected]?.has(value);
    button.classList.toggle("marked", marked);
    button.setAttribute("aria-pressed", String(marked));
    button.disabled = selected >= 0 && !puzzle[selected] && current[selected] === solution.flat()[selected];
  });
  noteMode.disabled = selected >= 0 && !puzzle[selected] && current[selected] === solution.flat()[selected];
  const remainingNotes = Math.max(0, levels[level].maxNotes - notesUsed);
  noteMode.textContent = `✎ Candidatos (${remainingNotes}x no tabuleiro)`;
  updateStats();
}

function updateStats() {
  timeEl.textContent = `${String(Math.floor(elapsed / 60)).padStart(2, "0")}:${String(elapsed % 60).padStart(2, "0")}`;
  errorsEl.textContent = `${errors} / ${levels[level].maxErrors}`;
}

function enter(value) {
  if (paused || selected < 0 || puzzle[selected] || current[selected] === solution.flat()[selected]) return;
  if (notes) return toggleNote(value);
  if (value !== solution.flat()[selected]) {
    current[selected] = value; wrong.add(selected); notesUsed -= noteState[selected].size; noteState[selected].clear(); errors += 1; render();
    if (errors >= levels[level].maxErrors) {
      paused = true;
      clearInterval(timer);
      exitTitle.textContent = "Limite de erros atingido";
      exitMessage.textContent = `Você atingiu ${levels[level].maxErrors} erros. Deseja sair ou começar um novo jogo?`;
      exitNo.textContent = "Novo jogo";
      exitYes.disabled = false;
      exitYes.textContent = "Sair";
      exitNoAction = () => { exitModal.hidden = true; startGame(level); };
      exitAction = () => { window.location.href = "../"; };
      exitModal.hidden = false;
      return;
    }
    return;
  }
  current[selected] = value; wrong.delete(selected); notesUsed -= noteState[selected].size; noteState[selected].clear(); render();
  if (current.every((item, index) => item === solution.flat()[index])) {
    paused = true; clearInterval(timer);
    setTimeout(() => alert("Parabéns! Sudoku concluído."), 30);
  }
}

function toggleNote(value) {
  // An incorrect entry must not remain visible while the user is adding
  // candidates to the same cell.
  if (current[selected] && wrong.has(selected)) {
    current[selected] = 0;
    wrong.delete(selected);
  }

  const set = noteState[selected];
  const key = String(value);
  if (set.has(key)) {
    set.delete(key);
    notesUsed -= 1;
  } else {
    if (notesUsed >= levels[level].maxNotes) return;
    set.add(key);
    notesUsed += 1;
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
noteMode.addEventListener("click", () => {
  notes = !notes;
  if (notes && selected >= 0 && wrong.has(selected)) {
    current[selected] = 0;
    wrong.delete(selected);
  }
  noteMode.classList.toggle("on", notes);
  noteMode.setAttribute("aria-pressed", String(notes));
  render();
});
document.addEventListener("keydown", (event) => { if (/^[1-9]$/.test(event.key)) enter(Number(event.key)); });

let touchStartX = 0;
let touchStartY = 0;
let touchTracking = false;
document.addEventListener("touchstart", (event) => {
  if (event.touches.length !== 1 || gameWindow.hidden || !exitModal.hidden) return;
  touchStartX = event.touches[0].clientX;
  touchStartY = event.touches[0].clientY;
  touchTracking = true;
}, { passive: true });
document.addEventListener("touchmove", (event) => {
  if (!touchTracking || event.touches.length !== 1) return;
  const deltaY = event.touches[0].clientY - touchStartY;
  const deltaX = event.touches[0].clientX - touchStartX;
  if (Math.abs(deltaY) > 10 && Math.abs(deltaY) > Math.abs(deltaX)) {
    event.preventDefault();
  }
}, { passive: false });
document.addEventListener("touchend", (event) => {
  if (!touchTracking) return;
  touchTracking = false;
  const deltaX = event.changedTouches[0].clientX - touchStartX;
  const deltaY = event.changedTouches[0].clientY - touchStartY;
  if (deltaY > 70 && Math.abs(deltaY) > Math.abs(deltaX)) {
    exitTitle.textContent = "Começar novo jogo?";
    exitMessage.textContent = "A partida atual será perdida.";
    showExitConfirmation(() => { exitModal.hidden = true; startGame(level); }, "Novo jogo");
  }
}, { passive: true });

const exitModal = document.querySelector("#exit-modal");
const exitYes = document.querySelector("#exit-yes");
const exitNo = document.querySelector("#exit-no");
const exitTitle = document.querySelector("#exit-title");
const exitMessage = document.querySelector(".exit-box p");
let exitAction = null;
let exitNoAction = null;
function showExitConfirmation(action, confirmLabel = "Sair") {
  exitAction = action;
  exitNoAction = () => { exitModal.hidden = true; };
  exitNo.textContent = "Continuar";
  exitYes.disabled = true;
  exitYes.textContent = `${confirmLabel} (1s)`;
  exitModal.hidden = false;
  window.setTimeout(() => { exitYes.disabled = false; exitYes.textContent = confirmLabel; }, 1500);
}
document.querySelector("#back-button").addEventListener("click", () => {
  exitTitle.textContent = "Sair da partida?";
  exitMessage.textContent = "Sua progressão será perdida.";
  showExitConfirmation(() => { window.location.href = "../"; });
});
document.querySelector(".secondary").addEventListener("click", () => {
  exitTitle.textContent = "Começar novo jogo?";
  exitMessage.textContent = "Sua progressão atual será perdida.";
  showExitConfirmation(() => { exitModal.hidden = true; startGame(level); }, "Novo jogo");
});
exitNo.addEventListener("click", () => { if (exitNoAction) exitNoAction(); });
exitYes.addEventListener("click", () => { if (!exitYes.disabled && exitAction) exitAction(); });
