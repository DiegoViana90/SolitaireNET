dotnet build -t:Run -f net9.0-android


dotnet clean -f net9.0-windows10.0.19041.0
dotnet build -f net9.0-windows10.0.19041.0
dotnet run -f net9.0-windows10.0.19041.0

## Publish downloads

The static download page lives in `site/`.

To upload the page, Android APK, and Windows ZIP to the server:

```powershell
.\scripts\Publish-Downloads.ps1 -Server REDACTED_SSH_TARGET
```

To publish the server-authoritative Solitaire API:

```powershell
.\scripts\Publish-Api.ps1 -Server REDACTED_SSH_TARGET
```

Public URL:

```text
http://REDACTED_HOST/solitairenet/
```

Web game:

```text
http://REDACTED_HOST/solitairenet/play/
```

Solitaire web route:

```text
http://REDACTED_HOST/solitairenet/play/solitaire/
```
