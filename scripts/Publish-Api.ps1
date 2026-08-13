[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $Server,

    [string] $RemotePath = "/opt/solitairenet-api",

    [string] $ServiceName = "solitairenet-api",

    [string] $Url = "http://127.0.0.1:5010",

    [string] $FirebaseProjectId = ""
)

$ErrorActionPreference = "Stop"

$Root = Split-Path -Parent $PSScriptRoot
$Project = Join-Path $Root "server\SolitaireNET.WebApi\SolitaireNET.WebApi.csproj"
$PublishDir = Join-Path $Root "artifacts\solitairenet-api-linux-x64"

dotnet publish $Project `
    -c Release `
    -r linux-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -o $PublishDir

ssh $Server "mkdir -p '$RemotePath'"
ssh $Server "systemctl stop '$ServiceName' 2>/dev/null || true"
scp (Join-Path $PublishDir "SolitaireNET.WebApi") "${Server}:$RemotePath/"
scp (Join-Path $PublishDir "SolitaireNET.WebApi.staticwebassets.endpoints.json") "${Server}:$RemotePath/"

$service = @"
[Unit]
Description=SolitaireNET Web API
After=network.target

[Service]
WorkingDirectory=$RemotePath
ExecStart=$RemotePath/SolitaireNET.WebApi --urls $Url
Restart=always
RestartSec=3
Environment=ASPNETCORE_ENVIRONMENT=Production
$(if ($FirebaseProjectId) { "Environment=Firebase__ProjectId=$FirebaseProjectId" } else { "" })

[Install]
WantedBy=multi-user.target
"@

$tmpService = New-TemporaryFile
try {
    Set-Content -Path $tmpService -Value $service -Encoding ascii
    scp $tmpService "${Server}:/tmp/$ServiceName.service"
}
finally {
    Remove-Item -Force $tmpService
}

ssh $Server "chmod +x '$RemotePath/SolitaireNET.WebApi' && mv '/tmp/$ServiceName.service' '/etc/systemd/system/$ServiceName.service' && systemctl daemon-reload && systemctl enable '$ServiceName' && systemctl restart '$ServiceName' && systemctl --no-pager --full status '$ServiceName'"
