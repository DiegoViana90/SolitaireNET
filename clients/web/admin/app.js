import { signInWithGoogle, getCurrentUserToken, waitForAuthReady, currentUser } from "../auth.js?v=3";
const status = document.querySelector("#status"), login = document.querySelector("#login"), content = document.querySelector("#content");
let allVisits = [];
login.onclick = async () => {
  if (login.disabled) return;
  login.disabled = true;
  login.textContent = "Entrando...";
  try { await signInWithGoogle(); await load(); }
  catch (e) { status.textContent = e.message; }
  finally { login.disabled = false; login.textContent = "Entrar com Google"; }
};
async function load() {
  login.disabled = true;
  login.textContent = "Carregando...";
  await waitForAuthReady(); const user = currentUser();
  if (!user) { status.textContent = "Entre com sua conta Google."; login.disabled = false; login.textContent = "Entrar com Google"; return; }
  status.textContent = `Usuário: ${user.email}`;
  const response = await fetch("/api/admin/visits", { headers: { Authorization: `Bearer ${await getCurrentUserToken()}` } });
  if (!response.ok) { status.textContent = response.status === 403 ? `Acesso negado para ${user.email || "esta conta"}.` : "Não foi possível carregar os acessos."; return; }
  const data = await response.json(); allVisits = data.visits; content.hidden = false;
  ["period", "from", "to", "ip", "path", "statusCode"].forEach(id => document.querySelector(`#${id}`).addEventListener("input", renderVisits));
  renderVisits();
  login.hidden = true;
}
function renderVisits() {
  const period = document.querySelector("#period").value, now = new Date(), today = new Date(now.getFullYear(), now.getMonth(), now.getDate());
  const start = period === "today" ? today : period === "yesterday" ? new Date(today.getTime() - 86400000) : period === "7days" ? new Date(today.getTime() - 6 * 86400000) : document.querySelector("#from").valueAsDate;
  const end = period === "yesterday" ? today : period === "custom" && document.querySelector("#to").value ? new Date(document.querySelector("#to").value + "T23:59:59") : null;
  const ip = document.querySelector("#ip").value.toLowerCase(), path = document.querySelector("#path").value.toLowerCase(), code = document.querySelector("#statusCode").value;
  const visits = allVisits.filter(v => {
    const value = `${v.time} ${v.ip} ${v.path} ${v.method} ${v.status} ${v.referer} ${v.userAgent}`.toLowerCase();
    const parsed = new Date(v.time.replace(/\//g, " ").replace(/:/, " "));
    return (!start || parsed >= start) && (!end || parsed <= end) && (!ip || v.ip.toLowerCase().includes(ip)) && (!path || v.path.toLowerCase().includes(path)) && (!code || String(v.status) === code) && (!ip || value.includes(ip));
  });
  document.querySelector("#summary").textContent = `${visits.length} requisições filtradas · ${new Set(visits.map(v => v.ip)).size} IPs únicos`;
  const escapeHtml = value => String(value ?? "-").replace(/[&<>\"']/g, char => ({"&":"&amp;","<":"&lt;",">":"&gt;",'"':"&quot;","'":"&#39;"}[char]));
  const cell = value => { const text = escapeHtml(value); return `<td title="${text}">${text}</td>`; };
  document.querySelector("#rows").innerHTML = visits.map(v => `<tr>${cell(v.time)}${cell(v.ip)}${cell(v.method)}${cell(v.path)}${cell(v.status)}${cell(v.referer || "-")}${cell(v.userAgent)}</tr>`).join("");
}
load().catch(e => status.textContent = e.message);
