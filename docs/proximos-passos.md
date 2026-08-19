# Proximos Passos

Este documento separa o que falta para consolidar o projeto em producao,
monetizar com ads e manter o deploy previsivel.

## 1. Validacao Atual

O plano de ADS esta correto para o estado atual do projeto:

- AdSense web e o caminho mais simples para comecar.
- `paciencia.net.br` ja tem as paginas basicas que ajudam na revisao do dominio:
  home, jogos, como jogar, regras, sobre, privacidade e contato.
- O arquivo `ads.txt` deve ficar em `site/ads.txt`, porque o deploy publica o
  conteudo de `site/` na raiz do dominio.
- A URL publica esperada e `https://paciencia.net.br/ads.txt`.
- Anuncios nao devem ficar dentro da mesa do Solitaire nem perto de acoes como
  `Novo`, arrastar cartas, criar sala ou entrar em partida.
- Auto Ads deve ser usado com cuidado; manual ads e melhor para a primeira
  fase.

Pontos importantes das politicas oficiais do Google:

- Cliques e impressoes artificiais, inclusive cliques proprios, violam as
  politicas do AdSense.
- Jogos exigem cuidado extra com posicionamento porque usuarios clicam e
  interagem rapidamente.
- `ads.txt` e altamente recomendado e precisa conter o publisher ID correto.
- Privacy & messaging/consentimento deve ser revisado se anuncios forem
  exibidos para usuarios em regioes com exigencias especificas, como EEA, UK e
  Suica.

Referencias:

- https://support.google.com/adsense/answer/48182
- https://support.google.com/adsense/answer/1346295
- https://support.google.com/adsense/answer/12171612
- https://support.google.com/adsense/answer/10924669
- https://support.google.com/adsense/answer/9261805

## 2. ADS - Fase 1

Objetivo: aprovar o dominio e comecar com baixo risco de clique acidental.

- Criar ou abrir a conta Google AdSense.
- Adicionar `https://paciencia.net.br` em Sites.
- Usar o metodo de verificacao solicitado pelo AdSense.
- Copiar o publisher ID real.
- Criar `site/ads.txt` com:

```text
google.com, pub-SEU_ID_REAL, DIRECT, f08c47fec0942fa0
```

- Publicar o site.
- Conferir no navegador:

```text
https://paciencia.net.br/ads.txt
```

- Pedir revisao do site no AdSense quando o status permitir.
- Nao clicar nos proprios anuncios em nenhum ambiente.

## 3. ADS - Posicionamento Inicial

Locais recomendados:

- `/play/`: banner entre a lista de jogos e os links finais.
- `/play/solitaire/`: bloco abaixo da mesa, dentro ou depois da area SEO.
- `/play/ranking/`: bloco abaixo da tabela ou em uma lateral apenas em desktop
  largo.
- `/como-jogar/` e `/regras/`: bloco no meio ou final do conteudo.
- `/sobre/` e `/contato/`: no maximo um bloco discreto no final.

Locais para evitar:

- Dentro da mesa do Solitaire.
- Entre colunas, cartas, fundacoes, estoque ou descarte.
- Perto dos botoes `Novo`, `Menu`, `Entrar`, `Criar sala` ou `Procurar sala`.
- Rodape fixo do jogo.
- Interstitial a cada partida.
- Tela de vitoria com anuncio perto do botao `Novo jogo`.

## 4. ADS - Implementacao Recomendada

Depois da aprovacao:

- Criar um snippet reutilizavel de AdSense para paginas estaticas.
- Adicionar o script do AdSense no `<head>` das paginas que exibirao anuncios.
- Criar classes CSS de container com altura minima reservada para evitar layout
  shift.
- Marcar visualmente o bloco como publicidade quando fizer sentido.
- Testar desktop e mobile antes de publicar.
- Subir poucos posicionamentos primeiro e acompanhar o Policy Center.

Possivel ordem:

1. `site/ads.txt`.
2. Blocos em `/como-jogar/`, `/regras/` e `/sobre/`.
3. Bloco em `/play/`.
4. Bloco abaixo de `/play/solitaire/`.
5. Ranking depois que a tabela estiver estavel.

## 5. Cloudflare

Checklist operacional:

- Confirmar DNS para `paciencia.net.br`.
- Decidir se `www.paciencia.net.br` sera usado.
- Se `www` existir, criar redirect canonico para o host principal.
- Manter proxy Cloudflare ligado para trafego web.
- Usar SSL/TLS `Full` no minimo.
- Migrar para `Full (strict)` com certificado valido no origin.
- Evitar cache de `/api/*`.
- Cachear arquivos estaticos com extensoes como `.js`, `.css`, `.svg`, `.png`,
  `.webp`, `.ico`, `.woff` e `.woff2`.
- Conferir se `/ads.txt`, `/robots.txt` e `/sitemap.xml` respondem sem bloqueio.

## 6. Firebase Login

Pendencias:

- Confirmar o Firebase project definitivo.
- Conferir `site/firebase-config.js`.
- Ativar provider Google no Firebase Authentication.
- Autorizar dominios:

```text
paciencia.net.br
localhost
127.0.0.1
```

- Configurar em producao:

```text
Firebase__ProjectId
```

- Configurar GitHub environment secrets:

```text
FIREBASE_WEB_API_KEY
FIREBASE_AUTH_DOMAIN
FIREBASE_PROJECT_ID
FIREBASE_APP_ID
```

- Validar `/api/auth/me` com token real.
- Confirmar que partidas deslogadas aparecem como nao ranqueadas.

## 7. Ranking e Banco

Pendencias:

- Definir PostgreSQL de producao.
- Configurar `Ranking__ConnectionString` no servidor.
- Garantir backup do banco.
- Decidir politica de moderacao/remocao de nomes no ranking.
- Documentar processo de remocao solicitado por usuario.
- Testar fallback SQLite apenas para ambiente local.

## 8. Deploy

Manual:

```powershell
.\scripts\Publish-Downloads.ps1 -Server REDACTED_SSH_TARGET
.\scripts\Publish-Api.ps1 -Server REDACTED_SSH_TARGET
```

GitHub Actions:

- Criar ambiente `production`.
- Configurar `SSH_HOST`, `SSH_USER`, `SSH_PRIVATE_KEY` e `SSH_KNOWN_HOSTS`.
- Conferir chave publica em `~/.ssh/authorized_keys` no servidor.
- Rodar workflow manual uma vez antes de depender apenas do push em `main`.
- Conferir health check publico:

```text
https://paciencia.net.br/api/health
```

## 9. Qualidade Antes de Monetizar

- Corrigir textos com caracteres quebrados nas paginas publicas.
- Testar o site em mobile real.
- Confirmar que `robots.txt` e `sitemap.xml` estao corretos.
- Rodar build do app Windows.
- Rodar build da API.
- Fazer uma partida completa de Solitaire web.
- Fazer login Firebase.
- Registrar uma vitoria ranqueada.
- Conferir ranking apos o cache de cinco minutos.

## 10. Proximas Decisoes

- Dominio canonico: apex `paciencia.net.br` ou `www`.
- ADS: manual only na primeira versao ou Auto Ads limitado.
- Analytics: manter so metricas internas da API ou adicionar ferramenta externa.
- App mobile: monetizar depois com AdMob ou manter ads somente na web.
- Ranking: somente Solitaire por enquanto ou incluir outros jogos.
