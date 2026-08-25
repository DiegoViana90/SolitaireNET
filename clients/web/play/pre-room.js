(() => {
  const template = `
    <div class="lobby-row">
      <button id="create-room" type="button"><strong>Criar sala</strong><span>Gera um codigo para outro jogador entrar.</span></button>
      <div class="join-code"><input id="room-code" type="text" maxlength="4" autocomplete="off" placeholder="CODIGO DA SALA" aria-label="Codigo da sala"><button id="join-code" type="button">Entrar</button></div>
    </div>
    <button id="random-room" type="button">
      <strong>Procurar sala aleatoria</strong>
      <span>Se nao houver outro jogador, voce fica aguardando.</span>
    </button>`;

  document.querySelectorAll("[data-pre-room]").forEach((room) => {
    room.innerHTML = template;
    room.dataset.ready = "true";
  });
})();
