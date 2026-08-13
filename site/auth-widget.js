import { authConfigured, subscribeAuth } from "./auth.js";

const authLinks = document.querySelectorAll("[data-auth-link]");

function firstName(user) {
  return user.displayName?.split(" ").filter(Boolean)[0] || "Conta";
}

subscribeAuth((user) => {
  authLinks.forEach((link) => {
    if (!authConfigured()) {
      link.textContent = "Entrar";
      return;
    }

    if (user) {
      link.textContent = firstName(user);
      link.classList.add("signed-in");
      return;
    }

    link.textContent = "Entrar";
    link.classList.remove("signed-in");
  });
});
