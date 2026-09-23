const board = document.querySelector(".sudoku-board");
const difficultyScreen = document.querySelector("#difficulty-screen");
const gameWindow = document.querySelector("#game-window");
const gameActions = document.querySelector("#game-actions");
const noteMode = document.querySelector("#note-mode");
const timeEl = document.querySelector(".stat strong");
const errorsEl = document.querySelector(".mistakes strong");
const difficultyEl = document.querySelector(".difficulty");
const levels = { "Fácil": 42, "Médio": 34, "Difícil": 28 };

let solution = [], puzzle = [], current = [], wrong = new Set(), selected = -1;
let notes = false, errors = 0, elapsed = 0, timer = null, paused = false, level = "Médio";

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
  shuffled([...Array(81).keys()]).slice(levels[level]).forEach((index) => { puzzle[index] = 0; });
  current = [...puzzle]; wrong = new Set(); selected = -1; errors = 0; elapsed = 0; paused = false;
  difficultyScreen.hidden = true; gameWindow.hidden = false; gameActions.hidden = false;
  document.querySelector(".sudoku-shell").classList.add("playing");
  difficultyEl.innerHTML = `<span class="dot"></span> ${level}`;
  document.querySelector(".primary").textContent = "Pausar";
  render(); clearInterval(timer);
  timer = setInterval(() => { if (!paused) { elapsed += 1; updateStats(); } }, 1000);
}

function render() {
  board.innerHTML = "";
  current.forEach((value, index) => {
    const cell = document.createElement("button");
    cell.type = "button";
    cell.className = `cell${puzzle[index] ? " given" : ""}${selected === index ? " selected" : ""}`;
    cell.dataset.index = index; cell.setAttribute("role", "gridcell");
    if (value) cell.textContent = value;
    if (!puzzle[index] && value) cell.classList.add(wrong.has(index) ? "error" : "user");
    cell.addEventListener("click", () => { if (!paused && !puzzle[index]) { selected = index; render(); } });
    board.append(cell);
  });
  updateStats();
}

function updateStats() {
  timeEl.textContent = `${String(Math.floor(elapsed / 60)).padStart(2, "0")}:${String(elapsed % 60).padStart(2, "0")}`;
  errorsEl.textContent = `${errors} / 3`;
}

function enter(value) {
  if (paused || selected < 0 || puzzle[selected]) return;
  if (notes) return toggleNote(value);
  if (value !== solution.flat()[selected]) {
    current[selected] = value; wrong.add(selected); errors += 1; render();
    if (errors >= 3) { paused = true; alert("Você atingiu o limite de 3 erros. Comece uma nova partida."); }
    return;
  }
  current[selected] = value; wrong.delete(selected); render();
  if (current.every((item, index) => item === solution.flat()[index])) {
    paused = true; clearInterval(timer);
    setTimeout(() => alert("Parabéns! Sudoku concluído."), 30);
  }
}

function toggleNote(value) {
  const cell = board.children[selected];
  const set = new Set((cell.dataset.notes || "").split(",").filter(Boolean));
  set.has(String(value)) ? set.delete(String(value)) : set.add(String(value));
  cell.dataset.notes = [...set].sort().join(",");
  cell.innerHTML = set.size ? `<span class="notes">${[...set].sort().map((item) => `<i>${item}</i>`).join("")}</span>` : "";
  cell.classList.toggle("has-notes", set.size > 0);
}

document.querySelectorAll(".difficulty-option").forEach((button) => button.addEventListener("click", () => startGame(button.querySelector("strong").textContent.trim())));
document.querySelectorAll(".number-pad button").forEach((button) => button.addEventListener("click", () => enter(Number(button.textContent))));
noteMode.addEventListener("click", () => { notes = !notes; noteMode.classList.toggle("on", notes); noteMode.setAttribute("aria-pressed", String(notes)); });
document.querySelector(".erase").addEventListener("click", () => { if (selected >= 0 && !puzzle[selected] && !paused) { current[selected] = 0; wrong.delete(selected); render(); } });
document.querySelector(".secondary").addEventListener("click", () => startGame(level));
document.querySelector(".primary").addEventListener("click", (event) => { paused = !paused; event.currentTarget.textContent = paused ? "Continuar" : "Pausar"; gameWindow.classList.toggle("is-paused", paused); });
document.addEventListener("keydown", (event) => { if (/^[1-9]$/.test(event.key)) enter(Number(event.key)); if (event.key === "Backspace" || event.key === "Delete") document.querySelector(".erase").click(); });
