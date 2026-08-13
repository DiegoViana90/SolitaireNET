(() => {
  const buildLabel = "__BUILD_LABEL__";
  const localPlaceholder = ["__BUILD", "_LABEL__"].join("");
  const text = buildLabel === localPlaceholder
    ? "Versão: desenvolvimento local"
    : buildLabel;

  document.querySelectorAll("[data-build-version]").forEach((element) => {
    element.textContent = text;
  });
})();
