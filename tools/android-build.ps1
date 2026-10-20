# Android APK'sını derler. Araçlar C: dolu olmasın diye D:\android-dev altında durur:
#   dotnet (MAUI Android workload'lu ayrı SDK), jdk-21, android-sdk, nuget önbelleği.
# Kullanım: tools\android-build.ps1 [-Configuration Release] [-Install]
#   -Install  derlenen APK'yı USB ile bağlı telefona (adb) kurar ve açar.
param(
    [string]$Configuration = "Debug",
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

& $dotnet publish $project -f net10.0-android -c $Configuration -o $out --nologo `
    "-p:AndroidSdkDirectory=$sdk" "-p:JavaSdkDirectory=$jdk"
if ($LASTEXITCODE -ne 0) { throw "Android derlemesi başarısız." }

$apk = Get-ChildItem $out -Filter "*-Signed.apk" | Select-Object -First 1
Write-Host "APK: $($apk.FullName)"

if ($Install) {
    $adb = Join-Path $sdk "platform-tools\adb.exe"
    & $adb install -r $apk.FullName
    & $adb shell monkey -p com.vuluvulu.launcher -c android.intent.category.LAUNCHER 1 | Out-Null
}
