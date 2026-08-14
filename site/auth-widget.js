import {
  authConfigured,
  getCurrentUserToken,
  signInWithGoogle,
  signOutUser,
  subscribeAuth
} from "./auth.js";

const authLinks = document.querySelectorAll("[data-auth-link]");
const profileCacheKey = "solitairenet.authProfile";
let activeUser = null;

function firstName(user) {
  return user.displayName?.split(" ").filter(Boolean)[0] || "Conta";
}

function readCachedProfile() {
  try {
    return JSON.parse(localStorage.getItem(profileCacheKey) || "null");
  } catch {
    return null;
  }
}

function writeCachedProfile(user) {
  if (!user) {
    localStorage.removeItem(profileCacheKey);
    return;
  }

  localStorage.setItem(profileCacheKey, JSON.stringify({
    name: firstName(user)
  }));
}

function escapeHtml(value) {
  return String(value ?? "")
    .replaceAll("&", "&amp;")
    .replaceAll("<", "&lt;")
    .replaceAll(">", "&gt;")
    .replaceAll('"', "&quot;")
    .replaceAll("'", "&#39;");
}

function safeImageUrl(value) {
  if (!value) return "/appicon.svg";

  try {
    const url = new URL(value, window.location.origin);
    if (url.protocol === "https:" || url.origin === window.location.origin) {
      return url.href;
    }
  } catch {
    return "/appicon.svg";
  }

  return "/appicon.svg";
}

function ensureModal() {
  let modal = document.querySelector("#auth-modal");
  if (modal) return modal;

  const style = document.createElement("style");
  style.textContent = `
    .auth-modal {
      position: fixed;
      inset: 0;
      z-index: 80;
      display: grid;
      place-items: center;
      padding: 18px;
      background: rgba(0, 0, 0, 0.56);
    }

    .auth-modal[hidden] {
      display: none;
    }

    .auth-box {
      width: min(380px, 100%);
      border: 1px solid rgba(255, 246, 229, 0.16);
      border-radius: 8px;
      background: #17241d;
      color: #fff6e5;
      padding: 18px;
      box-shadow: 0 24px 50px rgba(0, 0, 0, 0.44);
    }

    .auth-head {
      display: flex;
      align-items: center;
      justify-content: space-between;
      gap: 12px;
      margin-bottom: 12px;
    }

    .auth-head h2 {
      margin: 0;
      font-size: 1.12rem;
      line-height: 1.2;
    }

    .auth-close {
      width: 32px;
      height: 32px;
      border: 1px solid rgba(255, 246, 229, 0.16);
      border-radius: 6px;
      background: rgba(16, 23, 19, 0.58);
      color: #fff6e5;
      font: inherit;
      font-weight: 900;
      cursor: pointer;
    }

    .auth-copy,
    .auth-status {
      margin: 0;
      color: #cabfa8;
      line-height: 1.4;
      font-size: 0.94rem;
    }

    .auth-status.ok {
      color: #9be7b3;
    }

    .auth-notice {
      border: 1px solid rgba(255, 157, 157, 0.4);
      border-radius: 8px;
      background: rgba(96, 32, 32, 0.28);
      color: #ffb5b5;
      padding: 12px;
      line-height: 1.45;
    }

    .auth-actions {
      display: grid;
      gap: 10px;
      margin-top: 14px;
    }

    .auth-actions button,
    .auth-primary {
      min-height: 40px;
      border: 0;
      border-radius: 6px;
      padding: 8px 12px;
      font: inherit;
      font-weight: 850;
      cursor: pointer;
      text-decoration: none;
      display: grid;
      place-items: center;
    }

    .auth-actions button:disabled {
      opacity: 0.55;
      cursor: wait;
    }

    .auth-provider {
      background: #f2f2f2;
      color: #111111;
    }

    .auth-secondary {
      border: 1px solid rgba(255, 246, 229, 0.16);
      background: rgba(16, 23, 19, 0.5);
      color: #fff6e5;
    }

    .auth-primary {
      background: #d9b955;
      color: #20160b;
    }

    .auth-profile {
      display: grid;
      grid-template-columns: 52px 1fr;
      align-items: center;
      gap: 12px;
      margin-bottom: 14px;
    }

    .auth-avatar {
      width: 52px;
      height: 52px;
      border-radius: 999px;
      background: #203329;
      object-fit: cover;
    }

    .auth-profile strong {
      display: block;
      font-size: 1.08rem;
    }

  `;

  modal = document.createElement("div");
  modal.id = "auth-modal";
  modal.className = "auth-modal";
  modal.hidden = true;
  modal.innerHTML = `
    <div class="auth-box" role="dialog" aria-modal="true" aria-labelledby="auth-title">
      <div class="auth-head">
        <h2 id="auth-title">Entrar</h2>
        <button class="auth-close" type="button" aria-label="Fechar">x</button>
      </div>
      <div data-auth-content></div>
    </div>
  `;

  document.head.append(style);
  document.body.append(modal);

  modal.addEventListener("click", (event) => {
    if (event.target === modal || event.target.closest(".auth-close")) {
      closeModal();
    }
  });

  document.addEventListener("keydown", (event) => {
    if (event.key === "Escape") closeModal();
  });

  return modal;
}

function closeModal() {
  const modal = document.querySelector("#auth-modal");
  if (modal) modal.hidden = true;
}

function openModal() {
  const modal = ensureModal();
  renderModal();
  modal.hidden = false;
}

function setLinkState(user) {
  authLinks.forEach((link) => {
    const signedOutLabel = link.dataset.signedOutLabel || "Entrar";

    if (!authConfigured()) {
      link.textContent = signedOutLabel;
      link.classList.remove("signed-in");
      return;
    }

    if (user) {
      link.textContent = firstName(user);
      link.classList.add("signed-in");
      return;
    }

    link.textContent = signedOutLabel;
    link.classList.remove("signed-in");
  });
}

function hydrateCachedLinkState() {
  if (!authConfigured()) return;

  const cachedProfile = readCachedProfile();
  authLinks.forEach((link) => {
    if (cachedProfile?.name) {
      link.textContent = cachedProfile.name;
      link.classList.add("signed-in");
      return;
    }

    link.textContent = "Conta";
    link.classList.add("signed-in");
  });
}

async function validateApiSession(statusEl) {
  statusEl.textContent = "Validando sessao na API...";
  statusEl.classList.remove("ok");

  try {
    const token = await getCurrentUserToken();
    const response = await fetch("/api/auth/me", {
      headers: {
        authorization: `Bearer ${token}`
      }
    });

    if (!response.ok) throw new Error(`API retornou HTTP ${response.status}`);

    statusEl.textContent = "Sessao validada pela API.";
    statusEl.classList.add("ok");
  } catch (error) {
    statusEl.textContent = error.message || "API ainda nao validou esta sessao.";
  }
}

function renderSetup(content) {
  content.innerHTML = `
    <div class="auth-notice">
      Firebase ainda nao foi configurado neste ambiente. Preencha
      <strong>site/firebase-config.js</strong> e configure <strong>Firebase:ProjectId</strong> na API.
    </div>
  `;
}

function renderSignedOut(content) {
  content.innerHTML = `
    <p class="auth-copy">Entre para preparar seu perfil para ranking online e historico de partidas.</p>
    <div class="auth-actions">
      <button class="auth-provider" data-google-login type="button">Entrar com Google</button>
    </div>
    <div class="auth-status" data-auth-status></div>
  `;

  const statusEl = content.querySelector("[data-auth-status]");
  const buttons = [...content.querySelectorAll("button")];

  async function run(action) {
    statusEl.textContent = "";
    buttons.forEach((button) => { button.disabled = true; });

    try {
      await action();
      closeModal();
    } catch (error) {
      statusEl.textContent = error.message || "Nao consegui entrar agora.";
    } finally {
      buttons.forEach((button) => { button.disabled = false; });
    }
  }

  content.querySelector("[data-google-login]").addEventListener("click", () => run(signInWithGoogle));
}

function renderSignedIn(content) {
  const photoUrl = escapeHtml(safeImageUrl(activeUser.photoURL));
  const name = escapeHtml(activeUser.displayName || "Conta");

  content.innerHTML = `
    <div class="auth-profile">
      <img class="auth-avatar" src="${photoUrl}" alt="">
      <div>
        <strong>${name}</strong>
      </div>
    </div>
    <div class="auth-status" data-api-status>Validando sessao na API...</div>
    <div class="auth-actions">
      <a class="auth-primary" href="/play/">Jogar agora</a>
      <button class="auth-secondary" data-sign-out type="button">Sair da conta</button>
    </div>
  `;

  validateApiSession(content.querySelector("[data-api-status]"));
  content.querySelector("[data-sign-out]").addEventListener("click", async () => {
    await signOutUser();
    renderModal();
  });
}

function renderModal() {
  const modal = ensureModal();
  const content = modal.querySelector("[data-auth-content]");

  if (!authConfigured()) {
    renderSetup(content);
    return;
  }

  if (activeUser) {
    renderSignedIn(content);
    return;
  }

  renderSignedOut(content);
}

authLinks.forEach((link) => {
  link.addEventListener("click", (event) => {
    event.preventDefault();
    openModal();
  });
});

hydrateCachedLinkState();

subscribeAuth((user) => {
  activeUser = user;
  writeCachedProfile(user);
  setLinkState(user);

  const modal = document.querySelector("#auth-modal");
  if (modal && !modal.hidden) {
    renderModal();
  }
});
