# Usage telemetry ideas

Ideias para medir uso do SolitaireNET sem implementar ainda.

## Objetivo

Ter uma visao simples de uso do servidor:

- jogadores ativos agora;
- partidas criadas;
- acoes executadas;
- vitorias;
- erros de API/jogada;
- possivel base para ranking futuro.

## Opcao inicial: contador em memoria

A API pode manter contadores simples em memoria:

- `TotalGamesCreated`
- `TotalActions`
- `TotalWins`
- `TotalErrors`
- jogadores ativos por `playerId` anonimo

O navegador criaria um `playerId` anonimo no `localStorage` e enviaria:

- um ping a cada 30 segundos enquanto a pagina estiver aberta;
- o mesmo `playerId` em cada acao de jogo.

Jogador ativo seria quem fez ping/acao nos ultimos 2 a 5 minutos.

## Memoria

Isso deve consumir muito pouco.

Mesmo com muitos jogadores, cada entrada ativa precisa guardar basicamente:

- `playerId`
- ultimo ping/acesso

O ponto mais importante nao e o contador, e sim evitar que partidas antigas fiquem acumuladas para sempre.

## Limpeza automatica

Quando implementar, incluir um processo simples de limpeza:

- remover jogadores sem ping ha mais de 5 minutos;
- remover partidas sem acao ha 6, 12 ou 24 horas;
- rodar essa limpeza a cada poucos minutos.

Estrutura esperada:

```text
players ativos: ConcurrentDictionary<string, DateTime>
games: ConcurrentDictionary<string, GameSession>
limpeza automatica: timer/background service
```

## Limitacoes

Como seria em memoria:

- os numeros zeram se a API reiniciar;
- nao serve para historico serio;
- nao serve sozinho para ranking persistente.

Para ranking real, considerar SQLite ou outro banco simples depois.

## Futuro

Possiveis proximos passos:

- endpoint `GET /api/usage`;
- pagina web `/usage/`;
- persistencia em SQLite;
- ranking por tempo, movimentos ou pontuacao;
- separar metricas anonimas de dados de ranking.

## Login e identidade

Ideia futura: usar Firebase Authentication para login simples.

Possiveis provedores:

- Google;
- email/senha;
- login anonimo que pode ser convertido para conta depois.

Fluxo esperado:

- o front faz login pelo Firebase;
- o front envia o token do Firebase para a API;
- a API valida o token antes de aceitar dados de ranking;
- metricas anonimas continuam funcionando sem login;
- ranking persistente exige usuario autenticado.

Isso separa duas coisas:

- telemetria anonima para saber uso do servidor;
- identidade real para ranking, progresso e historico.

Cuidados:

- nao confiar em `playerId` do `localStorage` para ranking;
- validar token no servidor;
- guardar apenas o minimo necessario do usuario;
- permitir jogar sem login, pelo menos inicialmente;
- definir se ranking sera por usuario, por partida ou por dispositivo.
