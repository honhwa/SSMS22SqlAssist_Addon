# Clears SSMS 22's MEF component cache and image cache so it rediscovers extensions and their icons on next start.
# Run with SSMS closed. Safe: SSMS rebuilds the cache on startup.
$ErrorActionPreference = "Stop"
if (Get-Process -Name Ssms -ErrorAction SilentlyContinue) {
    throw "SSMS is running. Close it first."
}
$dirs = Get-ChildItem (Join-Path $env:LOCALAPPDATA "Microsoft\SSMS") -Directory -Filter "22.0_*" -ErrorAction SilentlyContinue
foreach ($d in $dirs) {
    $cache = Join-Path $d.FullName "ComponentModelCache"
    if (Test-Path $cache) {
        Remove-Item (Join-Path $cache "*") -Recurse -Force
        Write-Host "Cleared $cache"
    }
    # Cached image manifests: without this a changed toolbar icon keeps the old image.
    $images = Join-Path $d.FullName "ImageLibrary\ImageLibrary.cache"
    if (Test-Path $images) {
        Remove-Item $images -Force
        Write-Host "Cleared $images"
    }
}
