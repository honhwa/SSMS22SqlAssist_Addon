# Clears SSMS 22's MEF component cache so it rediscovers extensions on next start.
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
}
