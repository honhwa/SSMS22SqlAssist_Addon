# Builds dist\SqlAssistSetup-<version>.exe (tests, extension, then the installer that embeds it).
# The version comes from Directory.Build.props — bump it there before building a release.
$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
$version = ([xml](Get-Content (Join-Path $root "Directory.Build.props"))).Project.PropertyGroup.Version

& (Join-Path $root "make-icons.ps1")

dotnet test (Join-Path $root "tests\SqlAssist.Core.Tests") --nologo -v quiet
if ($LASTEXITCODE -ne 0) { throw "Tests failed." }

dotnet build (Join-Path $root "src\SqlAssist.Setup\SqlAssist.Setup.csproj") -c Release --nologo -v quiet
if ($LASTEXITCODE -ne 0) { throw "Build failed." }

$dist = Join-Path $root "dist"
New-Item -ItemType Directory -Force $dist | Out-Null
$target = Join-Path $dist "SqlAssistSetup-$version.exe"
Copy-Item (Join-Path $root "src\SqlAssist.Setup\bin\Release\net48\SqlAssistSetup.exe") $target -Force
Write-Host "Built $target" -ForegroundColor Green
