const board = document.querySelector(".sudoku-board");
const noteMode = document.querySelector("#note-mode");
let notes = false;
document.querySelectorAll(".difficulty-option").forEach((button) => button.addEventListener("click", () => {
  document.querySelector("#difficulty-screen").hidden = true;
  document.querySelector("#game-window").hidden = false;
  document.querySelector("#game-actions").hidden = false;
  document.querySelector(".sudoku-shell").classList.add("playing");
}));
document.querySelectorAll(".cell").forEach((cell) => {
  if (cell.textContent.trim()) cell.classList.add("given");
});
document.querySelectorAll(".mode").forEach((button) => button.addEventListener("click", () => {
  document.querySelectorAll(".mode").forEach((item) => item.classList.remove("active"));
  button.classList.add("active"); board.className = `sudoku-board size-${button.dataset.size}`;
  document.querySelector(".difficulty").innerHTML = `<span class="dot"></span> ${button.textContent.trim().split(" ")[0]}`;
}));
noteMode.addEventListener("click", () => { notes = !notes; noteMode.classList.toggle("on", notes); noteMode.setAttribute("aria-pressed", String(notes)); });
document.querySelectorAll(".cell:not(.given)").forEach((cell) => cell.addEventListener("click", () => {
  document.querySelectorAll(".cell").forEach((item) => item.classList.remove("selected")); cell.classList.add("selected");
}));
document.querySelectorAll(".number-pad button").forEach((button) => button.addEventListener("click", () => {
  const cell = document.querySelector(".cell.selected:not(.given)"); if (!cell) return;
  if (!notes) { cell.textContent = button.textContent; cell.classList.add("user"); cell.classList.remove("has-notes"); return; }
  const values = new Set(cell.dataset.notes?.split(",") || []); values.has(button.textContent) ? values.delete(button.textContent) : values.add(button.textContent);
  cell.dataset.notes = [...values].sort().join(","); cell.classList.add("has-notes");
  cell.innerHTML = `<span class="notes">${[...values].sort().map((value) => `<i>${value}</i>`).join("")}</span>`;
}));
document.querySelector(".erase").addEventListener("click", () => { const cell = document.querySelector(".cell.selected:not(.given)"); if (cell) { cell.textContent = ""; cell.dataset.notes = ""; cell.classList.remove("has-notes", "user"); } });
