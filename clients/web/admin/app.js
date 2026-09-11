import { signInWithGoogle, getCurrentUserToken, waitForAuthReady, currentUser } from "../auth.js?v=3";
const status = document.querySelector("#status"), login = document.querySelector("#login"), content = document.querySelector("#content");
let allVisits = [];
const locations = new Map();
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
  status.textContent = "";
  status.hidden = true;
  const response = await fetch("/api/admin/visits", { headers: { Authorization: `Bearer ${await getCurrentUserToken()}` } });
  if (!response.ok) { status.textContent = response.status === 403 ? `Acesso negado para ${user.email || "esta conta"}.` : "Não foi possível carregar os acessos."; return; }
  const data = await response.json(); allVisits = data.visits; content.hidden = false;
  ["period", "from", "to", "ip", "path", "statusCode"].forEach(id => document.querySelector(`#${id}`).addEventListener("input", renderVisits));
  renderVisits();
  login.hidden = true;
  enableColumnResize();
}

function enableColumnResize() {
  if (!document.querySelector("#column-resize-style")) { const style = document.createElement("style"); style.id = "column-resize-style"; style.textContent = "#sheet th{position:relative}.resize-handle{position:absolute;right:-3px;top:0;width:8px;height:100%;cursor:col-resize;z-index:5}"; document.head.append(style); }
  const headers = [...document.querySelectorAll("#sheet th")];
  const saved = JSON.parse(localStorage.getItem("admin-visits-column-widths") || "null");
  headers.forEach((header, index) => {
    if (saved?.[index]) header.style.width = `${saved[index]}px`;
    if (header.querySelector(".resize-handle")) return;
    const handle = document.createElement("span"); handle.className = "resize-handle"; header.append(handle);
    handle.addEventListener("pointerdown", event => {
      event.preventDefault(); event.stopPropagation(); handle.setPointerCapture(event.pointerId);
      const startX = event.clientX, startWidth = header.getBoundingClientRect().width;
      const move = moveEvent => { header.style.width = `${Math.max(45, startWidth + moveEvent.clientX - startX)}px`; };
      const stop = () => { handle.removeEventListener("pointermove", move); localStorage.setItem("admin-visits-column-widths", JSON.stringify(headers.map(item => item.getBoundingClientRect().width))); };
      handle.addEventListener("pointermove", move); handle.addEventListener("pointerup", stop, { once: true });
    });
  });
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
  document.querySelector("#rows").innerHTML = visits.map(v => `<tr>${cell(v.time)}${cell(v.ip)}${cell(locations.get(v.ip) || "Consultando...")}${cell(v.method)}${cell(v.path)}${cell(v.status)}${cell(v.referer || "-")}${cell(v.userAgent)}</tr>`).join("");
  loadLocations(visits);
}
async function loadLocations(visits) {
  const ips = [...new Set(visits.map(v => v.ip))].filter(ip => !locations.has(ip)).slice(0, 100);
  await Promise.all(ips.map(async (ip, index) => {
    const providers = index % 2 === 0
      ? [`https://ipwho.is/${encodeURIComponent(ip)}`, `https://ipapi.co/${encodeURIComponent(ip)}/json/`]
      : [`https://ipapi.co/${encodeURIComponent(ip)}/json/`, `https://ipwho.is/${encodeURIComponent(ip)}`];
    for (const url of providers) {
      try {
        const data = await (await fetch(url)).json();
        const location = [data.city, data.region || data.region_name, data.country || data.country_name].filter(Boolean).join(", ");
        if (location && !data.error && data.success !== false) { locations.set(ip, location); return; }
      } catch { }
    }
    locations.set(ip, "Indisponível");
  }));
  if (ips.length) renderVisits();
}
load().catch(e => status.textContent = e.message);
