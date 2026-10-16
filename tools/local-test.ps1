<#
.SYNOPSIS
    GitHub'a sürüm yüklemeden kurulumu ve güncellemeyi bu bilgisayarda dener.

.DESCRIPTION
    Test modunu açar ve artifacts\release'i http://localhost:5318 üzerinden yayınlar. Test modu açıkken
    bu bilgisayardaki kurulum programı, Updater ve launcher'ın güncelleme denetimi GitHub yerine
    artifacts\release\latest'teki sürümü (publish-release.ps1 -Local) kullanır.

    Sunucu Ctrl+C ile kapanır; test modu açık kalır (sunucu kapalıyken launcher mevcut sürümle açılır).
    Test modunu kapatmak için: .\tools\local-test.ps1 -Off

.EXAMPLE
    .\tools\publish-release.ps1 -Version 0.8.1 -Local
    .\tools\local-test.ps1
    # artifacts\release\latest\vulu-launcher-setup.exe ile kur

    # Güncellemeyi denemek için başka bir pencerede:
    .\tools\publish-release.ps1 -Version 0.8.2 -Local
    # launcher'ı kısayoldan aç → 0.8.2'ye güncellenir
#>
param(
    [switch]$Off,
    [int]$Port = 5318
)

$ErrorActionPreference = "Stop"
$feed = "http://localhost:$Port/latest/release.json"

if ($Off) {
    [Environment]::SetEnvironmentVariable("VULU_UPDATE_FEED", $null, "User")
    Write-Host "Test modu kapatıldı. Kurulum ve güncellemeler yeniden GitHub'dan yapılır." -ForegroundColor Green
    Write-Host "Not: Açık pencereler eski ayarı görür; yeni açılan programlar GitHub'ı kullanır."
    return
}

$root = Join-Path (Split-Path -Parent $PSScriptRoot) "artifacts\release"
if (-not (Test-Path (Join-Path $root "latest\release.json"))) {
    throw "Yerel sürüm yok. Önce: .\tools\publish-release.ps1 -Version <sürüm> -Local"
}

# Kullanıcı ortam değişkeni: Gezgin'den, kısayoldan açılan programlar da görür
if ([Environment]::GetEnvironmentVariable("VULU_UPDATE_FEED", "User") -ne $feed) {
    [Environment]::SetEnvironmentVariable("VULU_UPDATE_FEED", $feed, "User")
}
$env:VULU_UPDATE_FEED = $feed

$latest = Get-Content (Join-Path $root "latest\release.json") -Raw -Encoding UTF8 | ConvertFrom-Json
Write-Host "Test modu açık. Yerel en son sürüm: v$($latest.version)" -ForegroundColor Green
Write-Host "  Kurulum: $(Join-Path $root 'latest\vulu-launcher-setup.exe')"
Write-Host "  Sunucu:  http://localhost:$Port/  (kapatmak için Ctrl+C)"
Write-Host "  Kapatmak için: .\tools\local-test.ps1 -Off"
Write-Host ""

$types = @{ ".json" = "application/json; charset=utf-8"; ".zip" = "application/zip"; ".exe" = "application/octet-stream" }
$rootFull = [IO.Path]::GetFullPath($root).TrimEnd('\') + '\'

$listener = New-Object System.Net.HttpListener
$listener.Prefixes.Add("http://localhost:$Port/")
try { $listener.Start() }
catch { throw "Port $Port kullanımda. Başka bir yerel sunucu açık olabilir; onu kapatıp tekrar dene." }

try {
    while ($listener.IsListening) {
        # Ctrl+C'nin çalışması için bekleme kısa aralıklarla yapılır
        $pending = $listener.GetContextAsync()
        while (-not $pending.Wait(300)) { }
        $context = $pending.Result
        $response = $context.Response
        try {
            $relative = [Uri]::UnescapeDataString($context.Request.Url.AbsolutePath).TrimStart('/').Replace('/', '\')
            $path = [IO.Path]::GetFullPath((Join-Path $root $relative))
            if (-not $path.StartsWith($rootFull, [StringComparison]::OrdinalIgnoreCase) -or -not (Test-Path $path -PathType Leaf)) {
                $response.StatusCode = 404
                Write-Host "404 $($context.Request.Url.AbsolutePath)" -ForegroundColor DarkGray
                continue
            }

            $file = [IO.File]::OpenRead($path)
            try {
                $type = $types[[IO.Path]::GetExtension($path).ToLowerInvariant()]
                $response.ContentType = $(if ($type) { $type } else { "application/octet-stream" })
                $response.ContentLength64 = $file.Length
                $response.Headers.Add("Cache-Control", "no-store")
                if ($context.Request.HttpMethod -ne "HEAD") { $file.CopyTo($response.OutputStream) }
            }
            finally { $file.Dispose() }
            Write-Host "200 $($context.Request.Url.AbsolutePath)"
        }
        catch { Write-Host "Hata: $($_.Exception.Message)" -ForegroundColor Yellow }
        finally { try { $response.Close() } catch { } }
    }
}
finally {
    $listener.Stop()
    $listener.Close()
    Write-Host "Sunucu kapandı. Test modu hâlâ açık; kapatmak için: .\tools\local-test.ps1 -Off"
}
