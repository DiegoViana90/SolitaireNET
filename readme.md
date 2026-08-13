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
