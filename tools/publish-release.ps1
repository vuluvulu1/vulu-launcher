<#
.SYNOPSIS
    vulu Launcher'ın yeni sürümünü derler, paketler ve (istenirse) GitHub Releases'a yükler.

.DESCRIPTION
    1. Launcher ve Updater'ı aynı klasöre self-contained (win-x64) olarak yayınlar.
    2. Her dosyanın SHA-256'sını içeren install.json'u klasöre yazar.
    3. Klasörü zip'ler ve zip'i anlatan release.json'u üretir.
    4. Kurulum programını (vulu-launcher-setup.exe, tek dosya) derler.
    5. -Publish verilirse üçünü "gh release create" ile GitHub'a yükler.
       -Local verilirse GitHub'a dokunmaz; üçünü artifacts\release\latest'e koyar. tools\local-test.ps1
       bu klasörü yerel sunucudan yayınlar ve bu bilgisayardaki kurulum/güncelleme onu kullanır.

    Kurulum programı paketi içermez; çalışınca en son release'i GitHub'dan indirir. Bu yüzden
    eski bir release'teki setup.exe de her zaman en yeni sürümü kurar.

    Kullanıcıların Updater'ı her açılışta en son release'i okur; release.json'daki sürüm
    kuruludan büyükse zip'i indirir, doğrular ve yalnızca değişen dosyaları değiştirir.

.EXAMPLE
    .\tools\publish-release.ps1 -Version 0.8.0 -Notes "Yeni ana ekran tasarımı"

.EXAMPLE
    .\tools\publish-release.ps1 -Version 0.8.1 -NotesFile notlar.txt -Publish

.EXAMPLE
    .\tools\publish-release.ps1 -Version 0.8.2 -Notes "Yerel deneme" -Local
#>
param(
    [Parameter(Mandatory = $true)][string]$Version,
    [string]$Notes = "",
    [string]$NotesFile = "",
    [switch]$Publish,
    [switch]$Local
)

if ($Publish -and $Local) { throw "-Publish ve -Local birlikte kullanılamaz." }

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.IO.Compression.FileSystem

$Repository = "vuluvulu1/vulu-launcher"

if ($Version -notmatch '^\d+\.\d+(\.\d+)?$') { throw "Sürüm 0.8 ya da 0.8.1 biçiminde olmalı: $Version" }
if ($NotesFile) { $Notes = [IO.File]::ReadAllText((Resolve-Path $NotesFile)) }

$root = Split-Path -Parent $PSScriptRoot
$outDir = Join-Path $root "artifacts\release\$Version"
$appDir = Join-Path $outDir "app"
$assetName = "vulu-launcher-$Version.zip"
$zipPath = Join-Path $outDir $assetName
$utf8 = New-Object System.Text.UTF8Encoding $false

function Write-Json($path, $value) {
    [IO.File]::WriteAllText($path, ($value | ConvertTo-Json -Depth 6), $utf8)
}

Write-Host "==> vulu Launcher v$Version hazırlanıyor" -ForegroundColor Cyan
if (Test-Path $outDir) { Remove-Item $outDir -Recurse -Force }
New-Item -ItemType Directory -Force $appDir | Out-Null

$common = @("-c", "Release", "-r", "win-x64", "--self-contained", "true", "-p:Version=$Version", "-p:DebugType=none", "-o", $appDir, "--nologo")

Write-Host "==> Launcher derleniyor"
dotnet publish (Join-Path $root "Launcher\vuluLauncher.csproj") @common
if ($LASTEXITCODE -ne 0) { throw "Launcher derlenemedi." }

Write-Host "==> Updater derleniyor"
dotnet publish (Join-Path $root "Updater\Updater.csproj") @common
if ($LASTEXITCODE -ne 0) { throw "Updater derlenemedi." }

Write-Host "==> Kurulum programı derleniyor"
dotnet publish (Join-Path $root "Installer\Installer.csproj") -c Release -r win-x64 --self-contained true "-p:Version=$Version" "-p:DebugType=none" -o (Join-Path $outDir "setup") --nologo
if ($LASTEXITCODE -ne 0) { throw "Kurulum programı derlenemedi." }
$setupPath = Join-Path $outDir "vulu-launcher-setup.exe"
Move-Item (Join-Path $outDir "setup\vulu-launcher-setup.exe") $setupPath
Remove-Item (Join-Path $outDir "setup") -Recurse -Force

# Oyun ve kullanıcı dosyaları pakete girmez (derleme klasöründen kopyalanmış olabilirler)
foreach ($skip in @("instances", "java", "minecraft_core", ".update")) {
    $path = Join-Path $appDir $skip
    if (Test-Path $path) { Remove-Item $path -Recurse -Force }
}
Get-ChildItem $appDir -Recurse -File -Filter *.pdb | Remove-Item -Force

Write-Host "==> Dosya listesi çıkarılıyor"
$files = @(Get-ChildItem $appDir -Recurse -File | Where-Object { $_.Name -ne "install.json" } | Sort-Object FullName | ForEach-Object {
    [ordered]@{
        path   = $_.FullName.Substring($appDir.Length + 1).Replace("\", "/")
        sha256 = (Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
        size   = $_.Length
    }
})
$publishedAt = (Get-Date).ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ")

# Zip'in içine de konur: elle kurulan bir kopya da hangi dosyaların kendisine ait olduğunu bilir
Write-Json (Join-Path $appDir "install.json") ([ordered]@{
    version = $Version; notes = $Notes; publishedAt = $publishedAt; files = $files
})

Write-Host "==> Zip oluşturuluyor"
[IO.Compression.ZipFile]::CreateFromDirectory($appDir, $zipPath, [IO.Compression.CompressionLevel]::Optimal, $false)
$zip = Get-Item $zipPath

$releaseJson = Join-Path $outDir "release.json"
Write-Json $releaseJson ([ordered]@{
    version     = $Version
    notes       = $Notes
    publishedAt = $publishedAt
    asset       = $assetName
    sha256      = (Get-FileHash $zipPath -Algorithm SHA256).Hash.ToLowerInvariant()
    size        = $zip.Length
    files       = $files
})

$sizeMb = [math]::Round($zip.Length / 1MB, 1)
Write-Host ""
Write-Host "Hazır: $($files.Count) dosya, $sizeMb MB" -ForegroundColor Green
Write-Host "  $zipPath"
Write-Host "  $releaseJson"
Write-Host "  $setupPath ($([math]::Round((Get-Item $setupPath).Length / 1MB, 1)) MB)"

$ghArgs = @("release", "create", "v$Version", $zipPath, $releaseJson, $setupPath, "--repo", $Repository, "--title", "vulu Launcher v$Version", "--notes", $(if ($Notes) { $Notes } else { "vulu Launcher v$Version" }))

if ($Local) {
    # Yerel "en son sürüm": GitHub'daki latest release'in yerini tutar
    $latestDir = Join-Path $root "artifacts\release\latest"
    if (Test-Path $latestDir) { Remove-Item $latestDir -Recurse -Force }
    New-Item -ItemType Directory -Force $latestDir | Out-Null
    Copy-Item $zipPath, $releaseJson, $setupPath $latestDir
    Write-Host ""
    Write-Host "Yerel en son sürüm: v$Version (artifacts\release\latest)" -ForegroundColor Green
    Write-Host "Yerel sunucu açık değilse: .\tools\local-test.ps1"
    return
}

if (-not $Publish) {
    Write-Host ""
    Write-Host "GitHub'a yüklemek için -Publish ile tekrar çalıştır ya da şunu kullan:"
    Write-Host "  gh $($ghArgs -join ' ')"
    return
}

if (-not (Get-Command gh -ErrorAction SilentlyContinue)) { throw "GitHub CLI (gh) bulunamadı: https://cli.github.com" }

$answer = Read-Host "v$Version GitHub'a yayınlansın mı? Tüm kullanıcılar bir sonraki açılışta güncellenir. (e/h)"
if ($answer -notin @("e", "E", "evet")) { Write-Host "İptal edildi."; return }

gh @ghArgs
if ($LASTEXITCODE -ne 0) { throw "Release oluşturulamadı." }
Write-Host "Yayınlandı: https://github.com/$Repository/releases/tag/v$Version" -ForegroundColor Green
