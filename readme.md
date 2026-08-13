dotnet build -t:Run -f net9.0-android


dotnet clean -f net9.0-windows10.0.19041.0
dotnet build -f net9.0-windows10.0.19041.0
dotnet run -f net9.0-windows10.0.19041.0

## Publish web site

The static web site lives in `site/`.

To upload the site to the server:

```powershell
.\scripts\Publish-Downloads.ps1 -Server REDACTED_SSH_TARGET
```

To publish the server-authoritative Solitaire API:

```powershell
.\scripts\Publish-Api.ps1 -Server REDACTED_SSH_TARGET
```

Public URL:

```text
https://paciencia.net.br/
```

Web game:

```text
https://paciencia.net.br/play/
```

Solitaire web route:

```text
https://paciencia.net.br/play/solitaire/
```

## Automatic production deployment

The workflow `.github/workflows/deploy.yml` publishes the API and static site
whenever a commit reaches the `main` branch. It can also be started manually
from the GitHub Actions page.

Create a GitHub environment named `production` and add these environment
secrets:

- `SSH_HOST`: server hostname or IP address, for example `REDACTED_HOST`.
- `SSH_USER`: SSH user allowed to update the application and restart the
  service (currently `root`).
- `SSH_PRIVATE_KEY`: private key used only for deployment.
- `SSH_KNOWN_HOSTS`: trusted host-key line produced locally with
  `ssh-keyscan -H REDACTED_HOST` after verifying the fingerprint.

The matching public key must be present in the deployment user's
`~/.ssh/authorized_keys` on the server. Never commit a private key to this
repository.

## Firebase login

The Firebase login work is developed on `feature/firebase-auth` until the
Firebase project is configured. The public UI uses an in-page modal opened by
the `Entrar` buttons instead of a separate login route.

To enable it:

1. Create or open a Firebase project.
2. Add a Web app and copy the config into `site/firebase-config.js`.
3. Enable the Google provider in Firebase Authentication.
4. Add `paciencia.net.br` and local dev hosts to Authorized domains.
5. Set `Firebase__ProjectId` for `SolitaireNET.WebApi` in production.
6. Verify the login modal signs in and `/api/auth/me` validates the Firebase ID token.

Ranked Solitaire games use `Ranking__ConnectionString` when configured, which
is the production path for PostgreSQL. Without it, the API falls back to a local
SQLite database at `data/ranking.db` beside the API binary. Override that local
path with `Ranking__DatabasePath`.

The public `/api/ranking` response is cached in memory for five minutes. Writes
go to the configured database immediately, but the public table only refreshes
when the current snapshot expires and the endpoint is requested again.
