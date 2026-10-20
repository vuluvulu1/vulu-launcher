<#
.SYNOPSIS
    vulu Launcher'ın Android APK'sını derler ve vulu sunucusunda yayınlar.

.DESCRIPTION
    1. tools\android-build.ps1 ile imzalı Release APK'sını derler (%APPDATA%\vulu-release\android.keystore).
    2. android.json yazar: sürüm, notlar, yayın tarihi, boyut, SHA-256. Uygulama açılışta bunu okuyup yeni sürümü önerir.
    3. -Publish verilirse ikisini sunucudaki /srv/vulu-releases/android klasörüne koyar:
       https://vuluvulu.com.tr/releases/android/vulu-launcher.apk ve .../android.json.
       Dosyalar önce geçici klasöre açılır, sonra tek hamlede yer değiştirir; kimse yarım APK indirmez.
       Windows sürümleri (/srv/vulu-releases/current) bundan etkilenmez.

.EXAMPLE
    .\tools\publish-android.ps1 -Version 0.1.0 -NotesFile notlar.md -Publish
#>
param(
    [Parameter(Mandatory = $true)][string]$Version,
    [string]$Notes = "",
    [string]$NotesFile = "",
    [switch]$Publish
)

$ErrorActionPreference = "Stop"
$Server = "root@185.157.46.169"
$root = Split-Path -Parent $PSScriptRoot
$utf8 = New-Object System.Text.UTF8Encoding $false

if ($NotesFile) { $Notes = [IO.File]::ReadAllText((Resolve-Path $NotesFile)) }

& (Join-Path $PSScriptRoot "android-build.ps1") -Configuration Release -Version $Version
$signed = Join-Path $root "artifacts\android\Release\com.vuluvulu.launcher-Signed.apk"
if (-not (Test-Path $signed)) { throw "İmzalı APK bulunamadı: $signed" }

$outDir = Join-Path $root "artifacts\android\publish\$Version"
if (Test-Path $outDir) { Remove-Item $outDir -Recurse -Force }
New-Item -ItemType Directory -Force $outDir | Out-Null
$apk = Join-Path $outDir "vulu-launcher.apk"
Copy-Item $signed $apk

[IO.File]::WriteAllText((Join-Path $outDir "android.json"), ([ordered]@{
    version     = $Version
    notes       = $Notes
    publishedAt = (Get-Date).ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ")
    size        = (Get-Item $apk).Length
    sha256      = (Get-FileHash $apk -Algorithm SHA256).Hash.ToLowerInvariant()
} | ConvertTo-Json), $utf8)

Write-Host ""
Write-Host "Hazır: Android v$Version ($([math]::Round((Get-Item $apk).Length / 1MB, 1)) MB)" -ForegroundColor Green
Write-Host "  $outDir"

if (-not $Publish) {
    Write-Host "Sunucuya yüklemek için -Publish ile tekrar çalıştır."
    return
}

Write-Host "==> Sunucuya yükleniyor ($Server)"
$remote = 'set -e; base=/srv/vulu-releases; mkdir -p "$base"; tmp=$(mktemp -d "$base/.android-XXXXXX"); ' +
    'tar xzf - -C "$tmp" --no-same-owner --no-same-permissions; ' +
    'test -s "$tmp/vulu-launcher.apk" && test -s "$tmp/android.json"; chmod 755 "$tmp"; chmod 644 "$tmp"/*; ' +
    'rm -rf "$base/android.old"; if [ -d "$base/android" ]; then mv "$base/android" "$base/android.old"; fi; ' +
    'mv "$tmp" "$base/android"; rm -rf "$base/android.old"; echo yayinda'
# Windows PowerShell iki program arasındaki boruda ikili veriyi bozar; boruyu cmd kurar
cmd /c "tar -czf - -C `"$outDir`" vulu-launcher.apk android.json | ssh $Server `"$remote`""
if ($LASTEXITCODE -ne 0) { throw "Sunucuya yüklenemedi." }
Write-Host "Yayınlandı: https://vuluvulu.com.tr/releases/android/vulu-launcher.apk" -ForegroundColor Green
