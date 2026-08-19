# Plus Four (+4) — Regras e plano de implementação

Este documento consolida as regras combinadas para o jogo Plus Four (+4), também chamado de Mais Quatro.

## 1. Jogadores e direção

- A partida poderá ter até quatro jogadores.
- A ordem normal será:

  ```text
  Jogador 1 → Jogador 2 → Jogador 3 → Jogador 4
  ```

- Ao chegar ao último jogador, a ordem volta para o primeiro.
- A direção só muda quando uma carta `Inverte` for jogada.
- Com a direção invertida, a ordem passa a ser:

  ```text
  Jogador 1 → Jogador 4 → Jogador 3 → Jogador 2
  ```

- Outra carta `Inverte` muda novamente a direção.
- Cartas normais não alteram a direção.

## 2. Jogada normal

O jogador da vez pode jogar uma carta compatível com a carta do topo:

- mesma cor; ou
- mesmo valor; ou
- carta de ação válida; ou
- `+2`; ou
- `+4`.

Exemplo:

```text
Topo: 9 vermelho
Jogada válida: 8 vermelho
Jogada válida: 9 azul
```

Uma jogada normal utiliza uma carta por vez.

## 3. Jogada de várias cartas pelo jogador da vez

O jogador da vez poderá selecionar várias cartas e jogá-las juntas somente quando todas forem exatamente iguais à carta que está no topo antes da jogada.

Exemplo permitido:

```text
Topo: 9 vermelho
Jogador joga: 9 vermelho + 9 vermelho
```

Exemplos proibidos como jogada múltipla:

```text
Topo: 9 vermelho
Jogador tenta: 8 vermelho + 8 vermelho
```

Embora um `8 vermelho` possa ser jogado individualmente, o par de `8 vermelho` não pode ser jogado junto porque a carta do topo ainda era `9 vermelho`.

Também não é permitido misturar cartas diferentes:

```text
9 vermelho + 9 azul
9 vermelho + 8 vermelho
```

## 4. Corte (jump-in)

O corte permite que um jogador fora da vez jogue imediatamente uma carta exatamente igual à última carta jogada.

Exemplo:

```text
Topo: 9 vermelho
Jogador 1 joga: 8 vermelho
Jogador 3 possui: 8 vermelho
```

O Jogador 3 pode jogar o `8 vermelho` em cima da carta recém-jogada, mesmo não sendo sua vez.

Nesse caso:

- o Jogador 3 assume a vez;
- o jogador que seria o próximo perde a vez;
- a ordem continua a partir do Jogador 3;
- a carta usada no corte passa a ser a nova carta do topo.

Depois de cada jogada, a oportunidade de corte ficará aberta enquanto o próximo jogador ainda não tiver jogado ou comprado uma carta.

### Disputa entre cortes

Se dois jogadores tentarem cortar ao mesmo tempo, o servidor decidirá pelo primeiro comando recebido.

O jogador que perder a disputa receberá uma mensagem, por exemplo:

```text
Jogador 4 jogou na sua frente mais rápido.
```

O restante da mesa poderá receber um aviso geral:

```text
Jogador 4 cortou a jogada.
```

O servidor será a autoridade final para validar a jogada e evitar que dois jogadores sejam aceitos simultaneamente.

## 5. Alerta visual do corte

Quando uma carta recém-jogada puder ser usada em um corte, o jogador que possuir a carta receberá um alerta individual, por exemplo:

```text
Você pode cortar! Jogue seu 9 vermelho agora.
```

O alerta poderá aparecer como uma faixa, toast ou aviso animado próximo à mão do jogador.

Além do alerta:

- a carta idêntica ficará destacada na mão dos jogadores que a possuem;
- cada jogador verá apenas o destaque das próprias cartas;
- jogadores que não possuem a carta não verão destaque;
- o destaque ficará ativo enquanto a oportunidade de corte estiver aberta.
- quando a janela terminar, o alerta desaparece e a carta volta ao estado normal.

## 6. Cartas de ação

As ações especiais consideradas serão:

- `Pula`;
- `Inverte`;
- `+2`;
- `+4`.

Cartas de ação podem ser jogadas normalmente quando a regra da carta permitir.

### Cores e empilhamento das cartas de ação

- `Pula`, `Inverte` e `+2` terão cores específicas.
- `+4` será uma carta coringa e permitirá escolher a nova cor.
- Cartas de ação podem ser empilhadas na vez normal somente com outra ação do mesmo tipo.

```text
Pula vermelho sobre Pula vermelho
Inverte azul sobre Inverte azul
+2 verde sobre +2 verde
+4 sobre +4
```

Não é permitido misturar ações diferentes:

```text
Pula + Inverte
Pula + +4
Inverte + +4
+2 + Pula
+2 + +4
```

### Corte de cartas de ação

Somente `Pula` e `Inverte` poderão ser usados para corte fora da vez, sempre com correspondência exata de ação e cor:

```text
Pula vermelho → somente outro Pula vermelho pode cortar
Inverte azul → somente outro Inverte azul pode cortar
```

`+2` e `+4` não poderão ser usados para corte fora da vez. Eles ainda poderão ser empilhados normalmente quando chegar a vez do jogador.

## 7. Carta Pula

- `Pula` faz o próximo jogador perder a vez.
- `Pula` terá uma cor específica.
- Um jogador pode empilhar `Pula` sobre outro `Pula` na sua vez.
- O corte exige `Pula` exatamente igual, incluindo a cor.
- Os efeitos são acumulados conforme a quantidade de cartas.
- Um jogador fora da vez que tiver `Pula` idêntico também poderá cortar durante a janela de corte.

Exemplo:

```text
Jogador 1: Pula
Jogador 2: Pula
```

O próximo jogador será pulado conforme a quantidade acumulada.

## 8. Carta Inverte

- `Inverte` muda a direção da partida.
- `Inverte` terá uma cor específica.
- Uma carta `Inverte` pode ser empilhada sobre outra `Inverte` na vez normal.
- O corte exige `Inverte` exatamente igual, incluindo a cor.
- Cada carta aplicada altera a direção novamente.
- Duas cartas `Inverte` consecutivas resultam na direção original.
- Um jogador com `Inverte` idêntico também poderá cortar durante a janela de corte.

## 9. Carta +4

- `+4` pode ser jogado conforme a regra de carta especial.
- Quem joga `+4` escolhe a nova cor.
- `+4` só pode ser empilhado com outro `+4` na vez normal.
- `+4` não pode ser usado para corte fora da vez.
- Cada `+4` aumenta a penalidade em quatro cartas.
- O jogador que não conseguir continuar o acúmulo compra a penalidade total e perde a vez.

Exemplo:

```text
Jogador 1: +4
Jogador 2: +4
Jogador 3: não possui +4
```

Resultado:

```text
Jogador 3 compra 8 cartas e perde a vez.
```

Com três cartas acumuladas, a penalidade será de 12 cartas; com quatro, 16 cartas, e assim por diante.

Um jogador fora da vez que tiver um `+4` não poderá cortar durante a janela de corte.

## 10. Carta +2

- `+2` terá uma cor específica.
- Quando jogado normalmente, o próximo jogador recebe uma penalidade de duas cartas.
- O próximo jogador poderá empilhar outro `+2` na sua vez, aumentando a penalidade.
- `+2` não poderá ser usado para corte fora da vez.
- A penalidade será acumulada até que um jogador não consiga continuar com outro `+2` permitido.

## 11. Feedback para jogadas rejeitadas

Quando o jogador tentar jogar depois de outro jogador ter vencido um corte, o servidor rejeitará a ação e o front-end mostrará uma mensagem como:

```text
Jogador 4 jogou na sua frente mais rápido.
```

Outros erros também deverão continuar sendo informados normalmente, como:

- carta inválida;
- carta não pertencente à mão do jogador;
- jogada fora da vez;
- sala cheia;
- partida encerrada.

## 12. O que será alterado na implementação

### Backend

- Alterar o modelo da partida para suportar até quatro jogadores.
- Substituir a lógica fixa de dois lados por uma lista ordenada de jogadores.
- Adicionar controle de direção normal ou invertida.
- Adicionar cálculo do próximo jogador considerando a direção.
- Permitir jogadas múltiplas somente com cartas exatamente iguais ao topo.
- Adicionar janela de corte controlada pelo servidor.
- Validar cortes de forma atômica, aceitando apenas o primeiro comando válido.
- Implementar acúmulo de `Pula`, `Inverte` e `+4` somente entre ações do mesmo tipo.
- Aplicar a penalidade acumulada de `+4`.
- Criar eventos para informar cortes, jogadas rejeitadas e vencedor da disputa.

### Front-end

- Renderizar as mãos de até quatro jogadores.
- Destacar cartas que podem ser usadas em corte.
- Permitir selecionar várias cartas idênticas quando a jogada múltipla for válida.
- Mostrar visualmente a direção atual.
- Atualizar a ordem dos lugares quando `Inverte` for jogado.
- Mostrar avisos como “Jogador X jogou na sua frente mais rápido”.
- Atualizar a mão, a carta do topo e a penalidade sem depender apenas do estado local.

## 13. Pontos a confirmar antes do código

- A oportunidade de corte termina quando alguém corta, quando o jogador da vez joga uma carta válida ou quando ele compra uma carta.
- Quantidade máxima de cartas que podem ser jogadas juntas numa única jogada.
- Quais cores serão usadas para `Pula`, `Inverte` e `+2`.
- Se o `+4` poderá ser jogado livremente sobre qualquer carta ou somente quando o jogador não possuir outra jogada válida.
- Se o `+2` poderá ser empilhado somente com a mesma cor ou com qualquer `+2`.
- O formato final do alerta individual de corte.

Nenhuma alteração de código da regra deve ser feita antes da confirmação desses pontos.
