<#
.SYNOPSIS
    vulu Sosyal'ın test APK'sı GitHub'da vulu'nun Android yayın anahtarıyla imzalansın diye iki GitHub sırrını hazırlar.

.DESCRIPTION
    Anahtar (%APPDATA%\vulu-release\android.keystore) ve şifresi panoya sırayla kopyalanır; her birini GitHub'daki
    sır sayfasına yapıştırırsın. Hiçbir şey dosyaya yazılmaz, ekrana basılmaz, internete gönderilmez.
    Sır sayfası: https://github.com/vuluvulu1/vulu-launcher/settings/secrets/actions/new

.EXAMPLE
    .\tools\social-signing-secrets.ps1
#>
$ErrorActionPreference = "Stop"
$keyDir = Join-Path $env:APPDATA "vulu-release"
$keystore = Join-Path $keyDir "android.keystore"
$passFile = Join-Path $keyDir "android-keystore.pass"
if (-not (Test-Path $keystore) -or -not (Test-Path $passFile)) {
    throw "Yayın anahtarı bulunamadı: $keystore. Yeni anahtar ÜRETME; Android launcher bu anahtarla imzalı."
}

$page = "https://github.com/vuluvulu1/vulu-launcher/settings/secrets/actions/new"
Start-Process $page

[Convert]::ToBase64String([IO.File]::ReadAllBytes($keystore)) | Set-Clipboard
Write-Host ""
Write-Host "1/2  Anahtar panoya kopyalandı." -ForegroundColor Green
Write-Host "     Tarayıcıda açılan sayfada  Name: ANDROID_KEYSTORE_BASE64   Secret: Ctrl+V   -> Add secret"
Read-Host "     Bitince Enter'a bas"

[IO.File]::ReadAllText($passFile).Trim() | Set-Clipboard
Start-Process $page
Write-Host ""
Write-Host "2/2  Şifre panoya kopyalandı." -ForegroundColor Green
Write-Host "     Yeni sayfada  Name: ANDROID_KEYSTORE_PASS   Secret: Ctrl+V   -> Add secret"
Read-Host "     Bitince Enter'a bas"

Set-Clipboard -Value " "
Write-Host ""
Write-Host "Pano temizlendi. Tamam: bir sonraki test APK'sı yayın anahtarıyla imzalanacak." -ForegroundColor Green
