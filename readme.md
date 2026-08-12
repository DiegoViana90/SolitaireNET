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
