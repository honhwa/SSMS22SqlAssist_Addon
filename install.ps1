# Installs SqlAssist into SSMS 22 (per user, no admin needed). Close SSMS first.
param(
    [string]$SsmsIde = "C:\Program Files\Microsoft SQL Server Management Studio 22\Release\Common7\IDE",
    [switch]$NoBuild
)
$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
$vsix = Join-Path $root "src\SqlAssist.Vsix\bin\Release\net48\SqlAssist.vsix"

if (Get-Process -Name Ssms -ErrorAction SilentlyContinue) {
    throw "SSMS is running. Close every SSMS window, then run this again."
}

if (-not $NoBuild) {
    Write-Host "Building..." -ForegroundColor Cyan
    dotnet build (Join-Path $root "src\SqlAssist.Vsix\SqlAssist.Vsix.csproj") -c Release --nologo -v quiet
    if ($LASTEXITCODE -ne 0) { throw "Build failed." }
}
if (-not (Test-Path $vsix)) { throw "Not found: $vsix" }

# SSMS's own installer: double-clicking a .vsix would open Visual Studio's instead.
$installer = Join-Path $SsmsIde "VSIXInstaller.exe"
if (-not (Test-Path $installer)) { throw "VSIXInstaller.exe not found in $SsmsIde (pass -SsmsIde)." }

Write-Host "Installing $vsix ..." -ForegroundColor Cyan
$p = Start-Process -FilePath $installer -ArgumentList "`"$vsix`"" -Wait -PassThru
if ($p.ExitCode -ne 0) { throw "VSIXInstaller exited with code $($p.ExitCode)." }

& (Join-Path $root "clear-ssms-cache.ps1")
Write-Host "Done. Start SSMS; the Tools menu should now show 'SqlAssist: ...' items." -ForegroundColor Green
