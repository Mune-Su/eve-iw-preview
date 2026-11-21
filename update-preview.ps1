# update-preview.ps1
# Updates the local EVE-IW-Preview repository from origin/release, publishes the executable, and starts it.
# Run from the repository root: .\update-preview.ps1

$ErrorActionPreference = 'Stop'

function Stop-Update([string]$Message) {
    Write-Host $Message -ForegroundColor Red
    exit 1
}

$Repo = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $Repo

if (-not (Test-Path '.\.git')) {
    Stop-Update 'This script must be stored and run from the eve-iw-preview repository root.'
}

$Remote = (git remote get-url origin 2>$null).Trim()
if ($Remote -notmatch 'github\.com[/:]Mune-Su/eve-iw-preview(?:\.git)?$') {
    Stop-Update "This repository remote is '$Remote', not Mune-Su/eve-iw-preview."
}

$Branch = (git branch --show-current 2>$null).Trim()
if ($Branch -ne 'release') {
    Stop-Update "The current branch is '$Branch'. Switch to release before updating."
}

$Pending = git status --porcelain
if ($Pending) {
    Write-Host 'Update stopped because the repository has uncommitted changes:' -ForegroundColor Yellow
    git status --short
    Stop-Update 'Commit or discard those changes, then run update-preview.ps1 again.'
}

Write-Host 'Checking the GitHub release branch...' -ForegroundColor Cyan
git fetch origin release --tags
if ($LASTEXITCODE -ne 0) { Stop-Update 'Could not fetch origin/release.' }

$Before = (git rev-parse HEAD).Trim()
git pull --ff-only origin release
if ($LASTEXITCODE -ne 0) {
    Stop-Update 'The repository could not be fast-forwarded. No files were replaced.'
}
$After = (git rev-parse HEAD).Trim()

$Project = '.\src\AUTZ.IronWorks.Preview\AUTZ.IronWorks.Preview.csproj'
if (-not (Test-Path $Project)) { Stop-Update "Missing project file: $Project" }
if (-not (Test-Path '.\src\AUTZ.IronWorks.Preview\Core\MemoryTrim.cs')) {
    Stop-Update 'Missing Core\MemoryTrim.cs. Build stopped.'
}

[xml]$ProjectXml = Get-Content $Project
$Version = $ProjectXml.Project.PropertyGroup.Version | Select-Object -First 1
if ([string]::IsNullOrWhiteSpace($Version)) { Stop-Update 'Could not read the project version.' }

if ($Before -eq $After) {
    Write-Host "Already on the latest release-branch commit (v$Version)." -ForegroundColor Green
} else {
    Write-Host "Repository updated to v$Version." -ForegroundColor Green
}

Write-Host 'Building and publishing EVE-IW-Preview.exe...' -ForegroundColor Cyan
& '.\publish-exe.ps1'
if ($LASTEXITCODE -ne 0) { Stop-Update 'Build or publish failed. The application was not started.' }

$Exe = Join-Path $Repo 'EVE-IW-Preview.exe'
if (-not (Test-Path $Exe)) { Stop-Update "Publish completed but '$Exe' was not found." }

$ProductVersion = (Get-Item $Exe).VersionInfo.ProductVersion
Write-Host "Starting EVE-IW-Preview $ProductVersion..." -ForegroundColor Green
Start-Process -FilePath $Exe -WorkingDirectory $Repo

Write-Host ""
Write-Host "Changes (A = added, M = modified, D = deleted, R = renamed):" -ForegroundColor Cyan
git status --short
Write-Host ""
Write-Host "If anything in src/ shows as D (deleted) that you didn't expect, stop and check. Otherwise:" -ForegroundColor Cyan
Write-Host "  git commit -m `"v$Version`""
Write-Host "  ppush"
