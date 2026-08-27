(() => {
  const template = `
    <div class="lobby-row">
      <button id="create-room" type="button"><strong>Criar sala</strong><span>Gera um codigo para outro jogador entrar.</span></button>
      <div class="join-code"><input id="room-code" type="text" maxlength="4" autocomplete="off" placeholder="CODIGO DA SALA" aria-label="Codigo da sala"><button id="join-code" type="button">Entrar</button></div>
    </div>
    <div class="bot-room"><strong>Jogar contra a IA</strong><select id="bot-difficulty" aria-label="Dificuldade da IA"><option value="easy">Fácil</option><option value="medium" selected>Médio</option><option value="hard">Difícil</option></select><button id="bot-room" type="button">Começar partida</button></div>
    <button id="random-room" type="button">
      <strong>Procurar sala aleatoria</strong>
      <span>Se nao houver outro jogador, voce fica aguardando.</span>
    </button>`;

  document.querySelectorAll("[data-pre-room]").forEach((room) => {
    room.innerHTML = template;
    room.dataset.ready = "true";
  });
})();
