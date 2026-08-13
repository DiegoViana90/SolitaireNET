(() => {
  const statusEls = document.querySelectorAll("[data-api-status]");
  if (statusEls.length === 0) return;

  const endpoint = new URL("/api/health", window.location.origin);

  function setStatus(text, title = text) {
    statusEls.forEach((element) => {
      element.textContent = text;
      element.title = title;
    });
  }

  async function refreshApiStatus() {
    try {
      const response = await fetch(endpoint, { cache: "no-store" });
      if (!response.ok) throw new Error(`HTTP ${response.status}`);

      const data = await response.json();
      const usage = data.usage || {};
      const activePlayers = Number(usage.activePlayers ?? 0);
      const gamesInMemory = Number(usage.gamesInMemory ?? 0);
      const wins = Number(usage.wins ?? 0);

      setStatus(
        `API online | ${activePlayers} jogador${activePlayers === 1 ? "" : "es"} online | ${gamesInMemory} jogo${gamesInMemory === 1 ? "" : "s"} na memoria`,
        JSON.stringify({ ok: data.ok, activePlayers, gamesInMemory, wins })
      );
    } catch (error) {
      setStatus("API indisponivel", String(error));
    }
  }

  refreshApiStatus();
  window.setInterval(refreshApiStatus, 30_000);
})();
