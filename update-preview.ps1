param([switch]$AllowDeletes)
$ErrorActionPreference = 'Stop'

function Stop-Update([string]$Message) { Write-Host ''; Write-Host $Message -ForegroundColor Red; exit 1 }

function Version-Key([string]$v) {
    if ($v -notmatch '^(\d+)\.(\d+)\.(\d+)(?:-rc\.(\d+))?$') { return $null }
    $stable = if ($Matches[4]) { 0 } else { 1 }; $rc = if ($Matches[4]) { [int]$Matches[4] } else { 0 }
    '{0:D6}.{1:D6}.{2:D6}.{3}.{4:D6}' -f [int]$Matches[1],[int]$Matches[2],[int]$Matches[3],$stable,$rc
}

# Where applied ZIPs are kept: the existing versions folder, otherwise a new one in Documents.
function Get-VersionsFolder {
    $docs = [Environment]::GetFolderPath('MyDocuments')
    $candidates = @(
        (Join-Path $docs 'EVE IronWorks Preview versions'),
        (Join-Path $docs 'IronWorks\EVE IronWorks Preview versions')
    )
    foreach ($c in $candidates) { if (Test-Path $c) { return $c } }
    New-Item -ItemType Directory -Force $candidates[0] | Out-Null
    return $candidates[0]
}

$Repo = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $Repo
if (-not (Test-Path '.\.git')) { Stop-Update 'Run this script from the eve-iw-preview repository root.' }
if ((git branch --show-current).Trim() -ne 'release') { Stop-Update "Run 'git checkout release' first." }
if (git status --porcelain) { git status --short; Stop-Update 'Commit or discard existing changes before applying another ZIP.' }

Write-Host 'Updating local release branch...' -ForegroundColor Cyan
git fetch origin release --tags; if ($LASTEXITCODE) { Stop-Update 'Fetch failed.' }
git pull --ff-only origin release; if ($LASTEXITCODE) { Stop-Update 'Fast-forward pull failed.' }

$Downloads = Join-Path $HOME 'Downloads'
$Packages = Get-ChildItem $Downloads -File -Filter 'EVE-IW-Preview-v*.zip' | ForEach-Object {
    if ($_.BaseName -match '^EVE-IW-Preview-v(.+)$') { $k = Version-Key $Matches[1]; if ($k) { [pscustomobject]@{ File = $_; Version = $Matches[1]; Key = $k } } }
} | Sort-Object Key -Descending
$Selected = $Packages | Select-Object -First 1
if (-not $Selected) { Stop-Update 'No EVE-IW-Preview-vX.Y.Z.zip found in Downloads.' }
$Version = $Selected.Version
Write-Host "Using $($Selected.File.Name)" -ForegroundColor Cyan

$ProjectRel = 'src\AUTZ.IronWorks.Preview\AUTZ.IronWorks.Preview.csproj'
$Temp = Join-Path $env:TEMP ('EVE-IW-update-' + [guid]::NewGuid().ToString('N'))
$Built = $false
try {
    Expand-Archive -LiteralPath $Selected.File.FullName -DestinationPath $Temp -Force
    $roots = @(Get-ChildItem $Temp -Directory)
    if ($roots.Count -ne 1 -or $roots[0].Name -ne "EVE-IW-Preview-v$Version") { Stop-Update 'ZIP root folder does not match its versioned filename.' }
    $Package = $roots[0].FullName
    $ManifestPath = Join-Path $Package '.update-overlay.json'

    if (Test-Path $ManifestPath) {
        # targeted overlay: only the files listed in the manifest are copied
        $Manifest = Get-Content $ManifestPath -Raw | ConvertFrom-Json
        if ($Manifest.mode -ne 'overlay' -or $Manifest.version -ne $Version) { Stop-Update 'Invalid overlay manifest.' }
        $files = @($Manifest.files)
        $deletes = @($Manifest.deletes | Where-Object { $_ })
        Write-Host "Applying overlay v$Version ($($files.Count) changed files)..." -ForegroundColor Cyan
        foreach ($rel in $files) {
            if ($rel -match '(^|/)\.\.?(/|$)') { Stop-Update "Unsafe manifest path: $rel" }
            $src = Join-Path $Package ($rel -replace '/', '\'); if (-not (Test-Path $src)) { Stop-Update "Overlay file missing: $rel" }
            $dst = Join-Path $Repo ($rel -replace '/', '\'); New-Item -ItemType Directory -Force (Split-Path $dst) | Out-Null; Copy-Item $src $dst -Force
        }
        if ($deletes.Count -gt 0 -and -not $AllowDeletes) { $deletes | ForEach-Object { Write-Host "  D $_" -ForegroundColor Yellow }; Stop-Update 'Rerun with -AllowDeletes only if these deletions are intentional.' }
        if ($AllowDeletes) { foreach ($rel in $deletes) { $dst = Join-Path $Repo ($rel -replace '/', '\'); if (Test-Path $dst) { Remove-Item $dst -Force } } }
    } else {
        # complete source package: every file in it is copied over the repo; nothing is deleted
        if (-not (Test-Path (Join-Path $Package $ProjectRel))) { Stop-Update 'This ZIP is neither an overlay package nor a complete source package. No files were changed.' }
        $files = @(Get-ChildItem $Package -Recurse -File -Force | Where-Object { $_.FullName -notmatch '\\\.git\\' })
        Write-Host "Applying complete package v$Version ($($files.Count) files, nothing deleted)..." -ForegroundColor Cyan
        foreach ($f in $files) {
            $rel = $f.FullName.Substring($Package.Length).TrimStart('\')
            $dst = Join-Path $Repo $rel
            New-Item -ItemType Directory -Force (Split-Path $dst) | Out-Null
            Copy-Item -LiteralPath $f.FullName -Destination $dst -Force
        }
    }

    if (-not (Test-Path '.\src\AUTZ.IronWorks.Preview\Core\MemoryTrim.cs')) { Stop-Update 'MemoryTrim.cs is missing. Build stopped.' }
    [xml]$proj = Get-Content (Join-Path $Repo $ProjectRel) -Raw
    $projVersion = @($proj.Project.PropertyGroup | ForEach-Object { $_.Version } | Where-Object { $_ }) | Select-Object -First 1
    if ($projVersion -ne $Version) { Stop-Update "Project version '$projVersion' doesn't match the ZIP version '$Version'. Build stopped." }

    # compile check only. Your own PC updates from the GitHub Latest release, like everyone else's.
    Write-Host 'Checking the code builds...' -ForegroundColor Cyan
    dotnet build '.\AUTZ.IronWorks.Preview.sln' --configuration Release --nologo -v q
    if ($LASTEXITCODE -ne 0) { Stop-Update 'Build failed. Copy the first error above and send it to me.' }
    $Built = $true
} finally { if (Test-Path $Temp) { Remove-Item $Temp -Recurse -Force -ErrorAction SilentlyContinue } }

if ($Built) {
    $Versions = Get-VersionsFolder
    try {
        Move-Item -LiteralPath $Selected.File.FullName -Destination (Join-Path $Versions $Selected.File.Name) -Force
        Write-Host "Moved $($Selected.File.Name) to $Versions" -ForegroundColor Cyan
    } catch { Write-Host "Couldn't move the ZIP out of Downloads: $($_.Exception.Message)" -ForegroundColor Yellow }

    Write-Host ''; Write-Host 'Changes (A = added, M = modified, D = deleted, R = renamed):' -ForegroundColor Cyan
    git status --short
    Write-Host ''; Write-Host "If anything in src/ shows as D (deleted) that you didn't expect, stop and check. Otherwise:" -ForegroundColor Cyan
    Write-Host '  git add --all'; Write-Host "  git commit -m `"v$Version`""; Write-Host '  ppush'
}
