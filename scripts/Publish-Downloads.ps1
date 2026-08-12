[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $Server,

    [string] $RemotePath = "/var/www/solitairenet",

    [string] $Configuration = "Debug"
)

$ErrorActionPreference = "Stop"

$Root = Split-Path -Parent $PSScriptRoot
$SiteDir = Join-Path $Root "site"
$ArtifactsDir = Join-Path $Root "artifacts"
$AndroidApk = Join-Path $Root "bin\$Configuration\net9.0-android\com.diegoviana.solitairenet-Signed.apk"
$WindowsDir = Join-Path $Root "bin\$Configuration\net9.0-windows10.0.19041.0\win10-x64"
$WindowsZip = Join-Path $ArtifactsDir "SolitaireNET-Windows.zip"

New-Item -ItemType Directory -Force -Path $ArtifactsDir | Out-Null

ssh $Server "mkdir -p '$RemotePath'"
scp -r (Join-Path $SiteDir "*") "${Server}:$RemotePath/"

if (Test-Path $AndroidApk) {
    scp $AndroidApk "${Server}:$RemotePath/SolitaireNET.apk"
} else {
    Write-Warning "APK not found: $AndroidApk"
}

if (Test-Path $WindowsDir) {
    if (Test-Path $WindowsZip) {
        Remove-Item -Force $WindowsZip
    }

    Compress-Archive -Path (Join-Path $WindowsDir "*") -DestinationPath $WindowsZip -Force
    scp $WindowsZip "${Server}:$RemotePath/"
} else {
    Write-Warning "Windows build folder not found: $WindowsDir"
}

$HostName = $Server -replace '^.*@', ''
Write-Host "Published files to ${RemotePath} on ${HostName}."
Write-Host "Open http://$HostName/solitairenet/ when Nginx is configured with that path."
