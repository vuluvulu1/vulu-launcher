# Android oyun motorunu (Amethyst çatalı) derler ve vulu'nun APK'sına girecek dosyaları artifacts\android-engine'e koyar.
# Android\vuluLauncher.Android.csproj bu klasör varsa motoru ekler; yoksa uygulama oyunsuz derlenir.
#
# Çatal D:\android-dev\src\amethyst'te (vulu dalı); araçlar (JDK 8/21, Android SDK, NDK) D:\android-dev altında.
# MAUI'nin zaten getirdiği AndroidX ve Kotlin kütüphaneleri eklenmez (aynı sınıflar iki kez girerdi); yalnızca
# androidx.preference eksik, o NuGet'ten gelir.
param(
    [string]$EngineSource = "D:\android-dev\src",
    [switch]$SkipBuild
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$export = Join-Path $EngineSource "amethyst\app_pojavlauncher\build\vulu-export"
$out = Join-Path $root "artifacts\android-engine"

if (-not $SkipBuild) {
    & (Join-Path $EngineSource "build-amethyst.ps1") -Task ":app_pojavlauncher:vuluExport"
}
if (-not (Test-Path (Join-Path $export "amethyst.aar"))) { throw "Motor derlenmemiş: $export" }

if (Test-Path $out) { Remove-Item $out -Recurse -Force }
New-Item -ItemType Directory -Force $out | Out-Null
Copy-Item (Join-Path $export "amethyst.aar") $out

$skipped = @()
foreach ($line in Get-Content (Join-Path $export "deps.txt")) {
    if (-not $line.Trim()) { continue }
    $coordinate, $file = $line -split "`t"
    if ($coordinate -match '^(androidx\.|org\.jetbrains\.kotlin|org\.jetbrains:annotations)') { $skipped += $coordinate; continue }
    # Alt projeler (androidnsbypass): yerel kütüphaneleri zaten amethyst.aar'ın içinde, Java tarafı kullanılmıyor
    if ($coordinate -like 'project *') { $skipped += $coordinate; continue }
    # Zink (Mesa'nın Vulkan üstü OpenGL'i): vulu oyunu hep MobileGlues ile açar, Zink seçilemez
    if ($coordinate -eq 'local:kopper-zink-release.aar') { $skipped += $coordinate; continue }
    Copy-Item (Join-Path $export "deps\$file") $out -Force
}

# .NET'in kaynak derleyicisi (aapt2), sırası belirtilmemiş birden fazla %s/%d içeren metinleri hata sayar; Gradle saymaz.
# Bu metinlere formatted="false" eklenir: yalnızca derleme denetimini kapatır, String.format çalışırken aynı kalır.
Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem
$stringTag = [regex]::new('<string\b(?<attrs>[^>]*)>(?<text>.*?)</string>', 'Singleline')
$formatArg = [regex]'%(?!%)(?!\d+\$)[-#+ 0,(]*\d*(?:\.\d+)?[a-zA-Z]'
$fixer = [System.Text.RegularExpressions.MatchEvaluator]{
    param($m)
    if ($m.Groups['attrs'].Value -match 'formatted=' -or $formatArg.Matches($m.Groups['text'].Value).Count -lt 2) { return $m.Value }
    "<string$($m.Groups['attrs'].Value) formatted=`"false`">$($m.Groups['text'].Value)</string>"
}
$utf8 = New-Object System.Text.UTF8Encoding $false

# APK yalnızca arm64 telefonlar için; oyun hep MobileGlues (OpenGL ES) ile açılır:
# - LWJGL'in diğer işlemci mimarileri için yerel kütüphaneleri
# - LWJGL 3.3.3'ün Vulkan parçaları (vulkan, vma, shaderc): onu kullanan sürümler (1.21.x'e kadar) Vulkan'a hiç dokunmuyor.
#   3.4.1'inkiler kalır, yeni Minecraft sürümleri Vulkan'ı kullanabilir
# - Amethyst'in kendi logosu
# - Turnip (Adreno için Vulkan sürücüsü) ve Zink'in yardımcıları: yalnızca Zink çizicisinde kullanılıyorlar
$unusedAsset = '^assets/(components/lwjgl-[^/]+-natives/(armeabi-v7a|x86|x86_64)/' +
    '|components/lwjgl3/3\.3\.3/lwjgl-(vulkan|vma)\.jar$' +
    '|components/lwjgl-3\.3\.3-natives/arm64-v8a/(libshaderc|liblwjgl_vma)\.so$' +
    '|amethyst\.png$)'
$unusedLib = '^jni/[^/]+/(libvulkan_freedreno|libVkLayer_khronos_timeline_semaphore|libglxshim)\.so$'
foreach ($aar in Get-ChildItem $out -Filter *.aar) {
    $zip = [System.IO.Compression.ZipFile]::Open($aar.FullName, 'Update')
    try {
        # .NET, AAR'ların assets/ klasöründeki .jar dosyalarını (motorun LWJGL ve caciocavallo parçaları) APK'ya koymuyor,
        # arm64-v8a yolundaki .so'ları da lib/'e bir kez daha kopyalıyor; bütün assets/ bu yüzden AAR'lardan çıkarılıp ayrı
        # klasöre konur, csproj onları düz asset olarak ekler. Kullanılmayanlar ($unusedAsset) hiç konmaz.
        foreach ($entry in @($zip.Entries | Where-Object { $_.FullName -like 'assets/*' })) {
            if ($entry.Name -and $entry.FullName -notmatch $unusedAsset) {
                $target = Join-Path $out ($entry.FullName -replace '/', '\')
                New-Item -ItemType Directory -Force (Split-Path $target) | Out-Null
                [System.IO.Compression.ZipFileExtensions]::ExtractToFile($entry, $target, $true)
            }
            $entry.Delete()
        }
        foreach ($entry in @($zip.Entries | Where-Object { $_.FullName -match $unusedLib })) { $entry.Delete() }
        # MAUI Gson'u zaten getiriyor; AAR'ın kendi kopyası kalırsa aynı sınıflar iki kez girer (D8 durur)
        foreach ($entry in @($zip.Entries | Where-Object { $_.FullName -match '^libs/gson-[^/]+\.jar$' })) { $entry.Delete() }
        foreach ($entry in @($zip.Entries | Where-Object { $_.FullName -match '^res/values[^/]*/.+\.xml$' })) {
            $reader = New-Object System.IO.StreamReader($entry.Open(), $utf8)
            $text = $reader.ReadToEnd(); $reader.Dispose()
            $fixed = $stringTag.Replace($text, $fixer)
            if ($fixed -eq $text) { continue }
            $name = $entry.FullName; $entry.Delete()
            $writer = New-Object System.IO.StreamWriter($zip.CreateEntry($name).Open(), $utf8)
            $writer.Write($fixed); $writer.Dispose()
        }
    } finally { $zip.Dispose() }
}

$files = Get-ChildItem $out -File -Recurse
Write-Host ("Motor hazır: {0} dosya, {1:N0} MB ({2} MAUI'de olan atlandı) → {3}" -f $files.Count, (($files | Measure-Object Length -Sum).Sum / 1MB), $skipped.Count, $out) -ForegroundColor Green
