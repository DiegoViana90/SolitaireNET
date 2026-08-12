[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $Server,

    [string] $RemotePath = "/var/www/solitairenet"
)

$ErrorActionPreference = "Stop"

$Root = Split-Path -Parent $PSScriptRoot
$SiteDir = Join-Path $Root "site"

ssh $Server "mkdir -p '$RemotePath'"
scp -r (Join-Path $SiteDir "*") "${Server}:$RemotePath/"
ssh $Server "rm -f '$RemotePath/SolitaireNET.apk' '$RemotePath/SolitaireNET-Windows.zip'"

$HostName = $Server -replace '^.*@', ''
Write-Host "Published files to ${RemotePath} on ${HostName}."
Write-Host "Open http://$HostName/solitairenet/ when Nginx is configured with that path."
