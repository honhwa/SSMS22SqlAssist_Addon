# Publishes dist\SqlAssistSetup-<version>.exe as GitHub release v<version>; the extension's update check reads it.
# Run .\build-setup.ps1 first, commit and push. Needs a token with "Contents: read and write" on the repository
# in $env:GITHUB_TOKEN (fine-grained PAT, or `gh auth token`).
param([switch]$Draft)
$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
$repo = "kriangdet/SSMS22SqlAssist_Addon"   # keep in sync with ReleaseInfo.Repository
$version = ([xml](Get-Content (Join-Path $root "Directory.Build.props"))).Project.PropertyGroup.Version
$tag = "v$version"
$exe = Join-Path $root "dist\SqlAssistSetup-$version.exe"

if (-not $env:GITHUB_TOKEN) { throw "Set `$env:GITHUB_TOKEN first." }
if (-not (Test-Path $exe)) { throw "$exe not found - run .\build-setup.ps1 first." }
if (git -C $root status --porcelain) { throw "Working tree has uncommitted changes." }
$head = (git -C $root rev-parse HEAD).Trim()
git -C $root fetch origin --quiet
if (-not (git -C $root branch -r --contains $head)) { throw "HEAD $head is not pushed to origin." }

$headers = @{
    Authorization          = "Bearer $env:GITHUB_TOKEN"
    Accept                 = "application/vnd.github+json"
    "X-GitHub-Api-Version" = "2022-11-28"
}
$api = "https://api.github.com/repos/$repo"

$existing = $null
try { $existing = Invoke-RestMethod "$api/releases/tags/$tag" -Headers $headers } catch { }
if ($existing) { throw "Release $tag already exists: $($existing.html_url)" }

# GitHub creates the tag on HEAD if it doesn't exist yet; release notes are generated from the commits since the last release.
$body = @{
    tag_name               = $tag
    target_commitish       = $head
    name                   = "SqlAssist $version"
    draft                  = [bool]$Draft
    generate_release_notes = $true
} | ConvertTo-Json
$release = Invoke-RestMethod "$api/releases" -Method Post -Headers $headers -Body $body -ContentType "application/json"

$name = Split-Path $exe -Leaf
$upload = $release.upload_url -replace "\{.*\}$", "?name=$name"
$asset = Invoke-RestMethod $upload -Method Post -Headers $headers -InFile $exe -ContentType "application/octet-stream"

$hash = (Get-FileHash $exe -Algorithm SHA256).Hash.ToLowerInvariant()
if ($asset.digest -and $asset.digest -ne "sha256:$hash") { throw "Uploaded asset digest $($asset.digest) does not match $hash." }
Write-Host "Published $($release.html_url)" -ForegroundColor Green
Write-Host "  $name  sha256:$hash"
