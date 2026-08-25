# SolitaireNET / paciencia.net.br

> Plataforma de jogos casuais multiplataforma, com aplicativo .NET MAUI, site
> estático e API ASP.NET Core para partidas online e ranking.

Este repositório é um bom exemplo de produto full-stack em .NET: a mesma visão
de jogo atende Android, Windows e navegador, enquanto a API mantém as partidas
online de forma autoritativa.

SolitaireNET is a .NET 9 project with a MAUI app, a static public web site, and
a small ASP.NET Core API for online game state, ranking, presence, and Firebase
login validation.

Production site:

```text
https://paciencia.net.br/
```

Main web routes:

```text
https://paciencia.net.br/play/
https://paciencia.net.br/play/solitaire/
https://paciencia.net.br/play/ranking/
```

## Project Shape

- `clients/maui/SolitaireNET.csproj`: .NET MAUI app targeting Android and Windows.
- `clients/maui/Games/`: native game engines and MAUI pages for Solitaire, Blackjack,
  Domino, Poker, and related UI.
- `clients/web/`: static web frontend served publicly at `paciencia.net.br`.
- `clients/web/play/solitaire/`: browser Solitaire game.
- `clients/web/play/checkers/`: browser checkers game.
- `clients/web/play/chess/`: browser chess game.
- `clients/web/play/ranking/`: public ranking UI.
- `server/api/SolitaireNET.WebApi/`: ASP.NET Core API for web game sessions,
  multiplayer rooms, ranking, presence, usage metrics, and Firebase auth.
- `scripts/`: manual publish scripts for the static site and API.
- `.github/workflows/deploy.yml`: production deployment workflow.
- `docs/`: project notes and planning documents.

A explicação completa das camadas, decisões e fluxos está disponível em:

- [`ARCHITECTURE_PT-BR.md`](ARCHITECTURE_PT-BR.md)
- [`ARCHITECTURE_EN.md`](ARCHITECTURE_EN.md)

### Princípios de manutenção

- regras de negócio ficam em `Domain`/`Engine`;
- para Solitaire online, a API é a única autoridade das regras e do estado;
- páginas MAUI cuidam de navegação, eventos e renderização;
- comunicação externa fica em clientes/adaptadores;
- site e API têm publicação independente;
- mudanças em partidas devem incluir testes de regra e contrato JSON.

## Requirements

- .NET SDK 9.x.
- MAUI workloads for Android and Windows app development.
- PowerShell for local publish scripts.
- SSH access to the production server for manual deployment.
- Firebase project only when login/ranked identity is enabled.
- PostgreSQL connection string for production ranking storage.

## Local App Commands

Android:

```powershell
dotnet build .\clients\maui\SolitaireNET.csproj -t:Run -f net9.0-android
```

Windows:

```powershell
dotnet clean .\clients\maui\SolitaireNET.csproj -f net9.0-windows10.0.19041.0
dotnet build .\clients\maui\SolitaireNET.csproj -f net9.0-windows10.0.19041.0
dotnet run --project .\clients\maui\SolitaireNET.csproj -f net9.0-windows10.0.19041.0
```

## Static Web Site

The public static site lives in `clients/web/`. It includes SEO pages, game routes,
privacy/contact pages, sitemap, robots.txt, Firebase browser config, and the web
game frontends.

Important public pages:

- `/`: public home page.
- `/play/`: game selector.
- `/play/solitaire/`: main Solitaire web route.
- `/play/checkers/`: checkers route.
- `/play/chess/`: chess route.
- `/play/ranking/`: ranking route.
- `/como-jogar/`, `/regras/`, `/sobre/`, `/privacidade/`, `/contato/`: support
  and SEO pages.

To serve the static site locally:

```powershell
node .\scripts\local-site-server.mjs
```

## Web API

The API project is:

```text
server/api/SolitaireNET.WebApi/SolitaireNET.WebApi.csproj
```

Build it locally:

```powershell
dotnet build .\server\api\SolitaireNET.WebApi\SolitaireNET.WebApi.csproj
```

Run it locally:

```powershell
dotnet run --project .\server\api\SolitaireNET.WebApi\SolitaireNET.WebApi.csproj --urls http://127.0.0.1:5010
```

Useful endpoints:

- `GET /api/health`: health, usage, Firebase status, and ranking summary.
- `GET /api/usage`: current usage snapshot.
- `GET /api/ranking`: public ranking snapshot.
- `GET /api/auth/me`: validates Firebase ID token when Firebase is configured.
- `/api/games/*`: server-authoritative Solitaire sessions.
- `/api/checkers/*`, `/api/chess/*`: online room APIs.

## Ranking Storage

Ranked Solitaire games use `Ranking__ConnectionString` when configured. This is
the production path for PostgreSQL.

Without `Ranking__ConnectionString`, the API falls back to a local SQLite
database at `data/ranking.db` beside the API binary. Override that local path
with:

```text
Ranking__DatabasePath
```

The public `/api/ranking` response is cached in memory for five minutes. Writes
go to the configured database immediately, but the public table refreshes only
when the current snapshot expires and the endpoint is requested again.

## Firebase Login

The public UI uses an in-page login modal opened by the `Entrar` buttons instead
of a separate login route.

To enable Firebase login:

1. Create or open a Firebase project.
2. Add a Web app and copy the config into `clients/web/firebase-config.js`.
3. Enable the Google provider in Firebase Authentication.
4. Add `paciencia.net.br` and local dev hosts to Authorized domains.
5. Set `Firebase__ProjectId` for `SolitaireNET.WebApi` in production.
6. Verify the login modal signs in and `/api/auth/me` validates the Firebase ID
   token.

The GitHub Actions deployment can also inject the public Firebase web config
into `clients/web/firebase-config.js` from environment secrets.

## Manual Publish

Publish the static site to the server:

```powershell
.\scripts\Publish-Downloads.ps1 -Server <USER>@<HOST>
```

Publish the server-authoritative API:

```powershell
.\scripts\Publish-Api.ps1 -Server <USER>@<HOST>
```

Optional Firebase project during manual API publish:

```powershell
.\scripts\Publish-Api.ps1 -Server <USER>@<HOST> -FirebaseProjectId <PROJECT_ID>
```

## Automatic Production Deployment

The workflow `.github/workflows/deploy.yml` publishes the API and static site
whenever a commit reaches the `main` branch. It can also be started manually
from the GitHub Actions page.

Create a GitHub environment named `production` and add these environment
secrets:

- `SSH_HOST`: hostname or IP address do servidor de produção.
- `SSH_USER`: usuário SSH autorizado a atualizar a aplicação e reiniciar o
  serviço.
- `SSH_PRIVATE_KEY`: private key used only for deployment.
- `SSH_KNOWN_HOSTS`: linha de host key gerada com `ssh-keyscan -H <HOST>` após
  conferir a impressão digital do servidor.
- `FIREBASE_WEB_API_KEY`: Firebase Web app API key.
- `FIREBASE_AUTH_DOMAIN`: Firebase auth domain.
- `FIREBASE_PROJECT_ID`: Firebase project ID. Also becomes
  `Firebase__ProjectId` for the API service.
- `FIREBASE_STORAGE_BUCKET`: Firebase Storage bucket.
- `FIREBASE_MESSAGING_SENDER_ID`: Firebase Cloud Messaging sender ID.
- `FIREBASE_APP_ID`: Firebase Web app ID.

The matching public key must be present in the deployment user's
`~/.ssh/authorized_keys` on the server. Never commit a private key to this
repository.

## Cloudflare

The production domain is expected to run behind Cloudflare. Keep these settings
checked when changing DNS or the server:

- DNS records for `paciencia.net.br` and, if used, `www`.
- Orange-cloud proxy enabled for public web traffic.
- SSL/TLS mode set to `Full` at minimum.
- `Full (strict)` after the origin has a valid certificate.
- Redirect rule for the canonical host if both apex and `www` are active.
- Cache rules that are aggressive for static assets but conservative for API
  routes.

## Ads Plan

The first monetization target is AdSense for the web site, not the MAUI app.
The conservative approach is:

- Approve the domain with the current content pages first.
- Add `clients/web/ads.txt` only after the real AdSense publisher ID is known.
- Keep ads away from the Solitaire board, card drag areas, and `Novo` buttons.
- Start with manual placements on content/support pages and below game content.
- Avoid aggressive Auto Ads, anchors, and vignettes until the account has clean
  traffic history.
- Keep `/privacidade/` updated for ads, cookies, consent, and third-party
  advertising partners.

Expected `ads.txt` format:

```text
google.com, pub-SEU_ID_AQUI, DIRECT, f08c47fec0942fa0
```

After deploy, it must be visible at:

```text
https://paciencia.net.br/ads.txt
```

Detailed operational checklist:

```text
docs/proximos-passos.md
```

## References

- AdSense Program policies:
  https://support.google.com/adsense/answer/48182
- AdSense ad placement policies:
  https://support.google.com/adsense/answer/1346295
- AdSense ads.txt guide:
  https://support.google.com/adsense/answer/12171612
- AdSense Auto ads:
  https://support.google.com/adsense/answer/9261805
- AdSense Privacy & messaging:
  https://support.google.com/adsense/answer/10924669
