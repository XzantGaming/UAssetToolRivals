<#
.SYNOPSIS
    Publish a new UAssetTool release via GitHub Actions.
    Tags the current commit and pushes to trigger the release workflow.

.PARAMETER Version
    Version tag (e.g. '1.0.0'). The 'v' prefix is added automatically.

.PARAMETER Message
    Optional tag message. Defaults to "Release vX.Y.Z".

.EXAMPLE
    .\build.ps1 1.0.0
    .\build.ps1 1.2.0 -Message "Added StaticMesh support"
#>
param(
    [Parameter(Mandatory = $true, Position = 0)]
    [string]$Version,

    [string]$Message
)

$ErrorActionPreference = 'Stop'
$tag = "v$Version"

if (-not $Message) {
    $Message = "Release $tag"
}

# Verify we're in a git repo
if (-not (Test-Path .git)) {
    Write-Host "ERROR: Not in a git repository root." -ForegroundColor Red
    exit 1
}

# Check for uncommitted changes
$status = git status --porcelain
if ($status) {
    Write-Host "Uncommitted changes detected:" -ForegroundColor Yellow
    git status --short
    $confirm = Read-Host "Commit all changes before tagging? (y/n)"
    if ($confirm -eq 'y') {
        git add -A
        git commit -m $Message
    } else {
        Write-Host "Aborted. Commit or stash your changes first." -ForegroundColor Red
        exit 1
    }
}

# Check if tag already exists
$existingTag = git tag -l $tag
if ($existingTag) {
    Write-Host "ERROR: Tag '$tag' already exists." -ForegroundColor Red
    exit 1
}

# Push commits first
Write-Host "`nPushing commits..." -ForegroundColor Cyan
git push
if ($LASTEXITCODE -ne 0) {
    Write-Host "ERROR: Failed to push commits." -ForegroundColor Red
    exit 1
}

# Create and push tag
Write-Host "Creating tag: $tag" -ForegroundColor Cyan
git tag -a $tag -m $Message
git push origin $tag
if ($LASTEXITCODE -ne 0) {
    Write-Host "ERROR: Failed to push tag." -ForegroundColor Red
    exit 1
}

Write-Host "`nDone! Tag '$tag' pushed." -ForegroundColor Green
Write-Host "GitHub Actions will now build and create the release." -ForegroundColor Green
Write-Host "Track progress at: https://github.com/XzantGaming/UassetToolRivals/actions" -ForegroundColor Cyan
