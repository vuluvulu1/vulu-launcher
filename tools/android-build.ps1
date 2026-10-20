# Android APK'sını derler. Araçlar C: dolu olmasın diye D:\android-dev altında durur:
#   dotnet (MAUI Android workload'lu ayrı SDK), jdk-21, android-sdk, nuget önbelleği.
# Kullanım: tools\android-build.ps1 [-Configuration Release] [-Version 0.1.0] [-Install]
#   Release  %APPDATA%\vulu-release\android.keystore ile imzalanır (yayınlanan APK'ların hepsi bu anahtarla;
#            anahtar değişirse kullanıcılar güncelleyemez, silip yeniden kurmak zorunda kalır).
#   -Version Android sürümü; ApplicationVersion (sürüm kodu) bundan türetilir: 0.1.2 → 102.
#   -Install derlenen APK'yı USB ile bağlı telefona (adb) kurar ve açar.
param(
    [string]$Configuration = "Debug",
    [string]$Version = "",
    [string]$ToolsRoot = "D:\android-dev",
    [switch]$Install
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot

$env:DOTNET_ROOT = Join-Path $ToolsRoot "dotnet"
$env:DOTNET_CLI_HOME = Join-Path $ToolsRoot "cli-home"
$env:NUGET_PACKAGES = Join-Path $ToolsRoot "nuget"
$env:TEMP = Join-Path $ToolsRoot "tmp"
$env:TMP = $env:TEMP
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = "1"
New-Item -ItemType Directory -Force $env:TEMP | Out-Null

$dotnet = Join-Path $env:DOTNET_ROOT "dotnet.exe"
$sdk = Join-Path $ToolsRoot "android-sdk"
$jdk = Join-Path $ToolsRoot "jdk-21"
$project = Join-Path $root "Android\vuluLauncher.Android.csproj"
$out = Join-Path $root "artifacts\android\$Configuration"

$props = @("-p:AndroidSdkDirectory=$sdk", "-p:JavaSdkDirectory=$jdk")

if ($Version) {
    if ($Version -notmatch '^(\d+)\.(\d+)\.(\d+)$') { throw "Sürüm 0.1.0 biçiminde olmalı: $Version" }
    $code = [int]$Matches[1] * 10000 + [int]$Matches[2] * 100 + [int]$Matches[3]
    $props += "-p:Version=$Version", "-p:ApplicationVersion=$code"
}

if ($Configuration -eq "Release") {
    $keyDir = Join-Path $env:APPDATA "vulu-release"
    $keystore = Join-Path $keyDir "android.keystore"
    $passFile = Join-Path $keyDir "android-keystore.pass"
    if (-not (Test-Path $keystore) -or -not (Test-Path $passFile)) {
        throw "İmza anahtarı yok: $keystore. Yeniden üretme: yayınlanmış APK'lar güncellenemez hale gelir."
    }
    # Şifre ortam değişkeniyle verilir: apksigner aynı dosyayı iki şifre için satır satır okur, ikincisinde tükenir
    $env:VULU_ANDROID_KS_PASS = [IO.File]::ReadAllText($passFile).Trim()
    $props += "-p:AndroidKeyStore=true", "-p:AndroidSigningKeyStore=$keystore", "-p:AndroidSigningKeyAlias=vulu",
        "-p:AndroidSigningStorePass=env:VULU_ANDROID_KS_PASS", "-p:AndroidSigningKeyPass=env:VULU_ANDROID_KS_PASS"
}

if (Test-Path $out) { Remove-Item $out -Recurse -Force }
& $dotnet publish $project -f net10.0-android -c $Configuration -o $out --nologo @props
if ($LASTEXITCODE -ne 0) { throw "Android derlemesi başarısız." }

$apk = Get-ChildItem $out -Filter "*-Signed.apk" | Select-Object -First 1
Write-Host "APK: $($apk.FullName)"

if ($Install) {
    $adb = Join-Path $sdk "platform-tools\adb.exe"
    & $adb install -r $apk.FullName
    & $adb shell monkey -p com.vuluvulu.launcher -c android.intent.category.LAUNCHER 1 | Out-Null
}
