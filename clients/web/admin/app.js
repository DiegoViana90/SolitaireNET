import { signInWithGoogle, getCurrentUserToken, waitForAuthReady, currentUser } from "../auth.js?v=3";
const status = document.querySelector("#status"), login = document.querySelector("#login"), content = document.querySelector("#content");
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
  const data = await response.json(); content.hidden = false; document.querySelector("#summary").textContent = `${data.total} requisições recentes · ${data.uniqueIps} IPs únicos`;
  const escapeHtml = value => String(value ?? "-").replace(/[&<>\"']/g, char => ({"&":"&amp;","<":"&lt;",">":"&gt;",'"':"&quot;","'":"&#39;"}[char]));
  const cell = value => { const text = escapeHtml(value); return `<td title="${text}">${text}</td>`; };
  document.querySelector("#rows").innerHTML = data.visits.map(v => `<tr>${cell(v.time)}${cell(v.ip)}${cell(v.method)}${cell(v.path)}${cell(v.status)}${cell(v.referer || "-")}${cell(v.userAgent)}</tr>`).join("");
  login.hidden = true;
}
load().catch(e => status.textContent = e.message);
