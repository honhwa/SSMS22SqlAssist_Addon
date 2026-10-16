# Removes SqlAssist from SSMS 22. Close SSMS first.
param(
    [string]$SsmsIde = "C:\Program Files\Microsoft SQL Server Management Studio 22\Release\Common7\IDE"
)
$ErrorActionPreference = "Stop"
$vsixId = "SqlAssist.9cc4aed8-f2ad-4120-ba6f-222b08aa04a0"

if (Get-Process -Name Ssms -ErrorAction SilentlyContinue) {
    throw "SSMS is running. Close every SSMS window, then run this again."
}

$installer = Join-Path $SsmsIde "VSIXInstaller.exe"
$p = Start-Process -FilePath $installer -ArgumentList "/uninstall:$vsixId" -Wait -PassThru
if ($p.ExitCode -ne 0) { throw "VSIXInstaller exited with code $($p.ExitCode)." }

& (Join-Path $PSScriptRoot "clear-ssms-cache.ps1")
Write-Host "SqlAssist removed. Your snippets file (%APPDATA%\SqlAssist\snippets.xml) was left in place." -ForegroundColor Green
