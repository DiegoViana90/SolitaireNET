(() => {
  const buildLabel = "__BUILD_LABEL__";
  const localPlaceholder = ["__BUILD", "_LABEL__"].join("");
  const buildVersion = buildLabel === localPlaceholder ? "local" : buildLabel;
  const versionKey = "solitairenet-build-version";
  const previousVersion = localStorage.getItem(versionKey);
  localStorage.setItem(versionKey, buildVersion);

  if (previousVersion && previousVersion !== buildVersion && !sessionStorage.getItem(versionKey)) {
    sessionStorage.setItem(versionKey, "reloaded");
    window.location.reload();
    return;
  }
  sessionStorage.removeItem(versionKey);

  const text = buildLabel === localPlaceholder
    ? "Versão: desenvolvimento local"
    : buildLabel;

  document.querySelectorAll("[data-build-version]").forEach((element) => {
    element.textContent = text;
    window.setTimeout(() => element.classList.add("is-hidden"), 4500);
  });
})();
