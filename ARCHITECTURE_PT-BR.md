# Arquitetura do SolitaireNET

## Visão geral

O SolitaireNET é uma solução multiplataforma composta por três produtos que
compartilham o mesmo domínio funcional:

```text
                         +----------------------+
                         | clients/web/         |
                         | Web estática + jogos |
                         +----------+-----------+
                                    |
                                    v HTTP/JSON
+-------------------+      +-------+----------------+
| App .NET MAUI     |----->| SolitaireNET.WebApi    |
| Android / Windows |      | sessões, ranking, auth |
+---------+---------+      +-------+----------------+
          |                         |
          v                         v
  clients/maui/Games/*         SQLite / PostgreSQL
  Engine + Pages
```

## Organização do código

- `clients/maui/Games/<Jogo>`: telas e engines de apresentação do MAUI; as
  regras da partida online ficam exclusivamente no backend.
- `server/api/SolitaireNET.WebApi`: regras, estado e contratos da API; as
  regras da partida online ficam exclusivamente no backend.
- `clients/maui/Pages`: navegação e composição das telas principais.
- `clients/web`: frontend estático com HTML, CSS e JavaScript.
- `scripts`: publicação e operação do site e da API.

## Fluxo de uma partida online

1. `SolitairePage` carrega ou cria o identificador salvo em `Preferences`.
2. `SolitaireApiClient` ou o cliente JavaScript traduz a intenção para HTTP/JSON.
3. A API mantém o estado autoritativo em `GameStore` e valida a ação.
4. O estado devolvido é aplicado ao engine remoto e a tela é atualizada.
5. Em falha de rede, a tela tenta sincronizar novamente antes da próxima ação.

## Decisões arquiteturais

- A UI não deve conter regra de negócio.
- A API é a autoridade para partidas online e ranking.
- MAUI e site enviam ações e renderizam o estado retornado pela mesma API.
- Validações locais devem ser apenas de interação visual; nunca substituem a
  validação do backend.
- SQLite é o fallback local; PostgreSQL é o armazenamento de produção.
- O site e o app MAUI possuem ciclos de publicação independentes.
- Novos jogos devem seguir `Domain -> Engine -> Pages`.

## Próximos passos

1. Criar projetos `SolitaireNET.Domain`, `SolitaireNET.Application` e
   `SolitaireNET.Infrastructure`.
2. Extrair interfaces para repositórios, ranking e clientes de API.
3. Adicionar testes unitários para regras, vitória e serialização.
4. Validar cada pull request com format, build e testes.
