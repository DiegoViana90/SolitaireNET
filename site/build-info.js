(() => {
  const buildLabel = "__BUILD_LABEL__";
  const text = buildLabel === "__BUILD_LABEL__"
    ? "Versão: desenvolvimento local"
    : buildLabel;

  document.querySelectorAll("[data-build-version]").forEach((element) => {
    element.textContent = text;
  });
})();
