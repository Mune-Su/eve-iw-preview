param([switch]$AllowDeletes)
$ErrorActionPreference = 'Stop'
function Stop-Update([string]$Message) { Write-Host ''; Write-Host $Message -ForegroundColor Red; exit 1 }
function Version-Key([string]$v) {
    if ($v -notmatch '^(\d+)\.(\d+)\.(\d+)(?:-rc\.(\d+))?$') { return $null }
    $stable = if ($Matches[4]) { 0 } else { 1 }; $rc = if ($Matches[4]) { [int]$Matches[4] } else { 0 }
    '{0:D6}.{1:D6}.{2:D6}.{3}.{4:D6}' -f [int]$Matches[1],[int]$Matches[2],[int]$Matches[3],$stable,$rc
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
    if ($_.BaseName -match '^EVE-IW-Preview-v(.+)$') { $k=Version-Key $Matches[1]; if($k){[pscustomobject]@{File=$_;Version=$Matches[1];Key=$k}} }
} | Sort-Object Key -Descending
$Selected=$Packages|Select-Object -First 1
if(-not $Selected){Stop-Update 'No EVE-IW-Preview-vX.Y.Z.zip found in Downloads.'}
$Version=$Selected.Version; $Temp=Join-Path $env:TEMP ('EVE-IW-update-'+[guid]::NewGuid().ToString('N'))
try {
    Expand-Archive $Selected.File.FullName $Temp -Force
    $roots=@(Get-ChildItem $Temp -Directory)
    if($roots.Count -ne 1 -or $roots[0].Name -ne "EVE-IW-Preview-v$Version"){Stop-Update 'ZIP root folder does not match its versioned filename.'}
    $Package=$roots[0].FullName; $ManifestPath=Join-Path $Package '.update-overlay.json'
    if(Test-Path $ManifestPath){
        $Manifest=Get-Content $ManifestPath -Raw|ConvertFrom-Json
        if($Manifest.mode -ne 'overlay' -or $Manifest.version -ne $Version){Stop-Update 'Invalid overlay manifest.'}
        Write-Host "Applying targeted overlay v$Version ($($Manifest.files.Count) changed files)..." -ForegroundColor Cyan
        foreach($rel in $Manifest.files){
            if($rel -match '(^|/)\.\.?(/|$)'){Stop-Update "Unsafe manifest path: $rel"}
            $src=Join-Path $Package ($rel -replace '/','\'); if(-not(Test-Path $src)){Stop-Update "Overlay file missing: $rel"}
            $dst=Join-Path $Repo ($rel -replace '/','\'); New-Item -ItemType Directory -Force (Split-Path $dst) | Out-Null; Copy-Item $src $dst -Force
        }
        if($Manifest.deletes.Count -gt 0 -and -not $AllowDeletes){$Manifest.deletes|ForEach-Object{Write-Host "  D $_" -ForegroundColor Yellow};Stop-Update 'Rerun with -AllowDeletes only if these deletions are intentional.'}
        if($AllowDeletes){foreach($rel in $Manifest.deletes){$dst=Join-Path $Repo ($rel -replace '/','\');if(Test-Path $dst){Remove-Item $dst -Force}}}
    } else {
        Stop-Update 'This updater now requires a targeted overlay package with .update-overlay.json. No files were changed.'
    }
    if(-not(Test-Path '.\src\AUTZ.IronWorks.Preview\Core\MemoryTrim.cs')){Stop-Update 'MemoryTrim.cs is missing. Build stopped.'}
    Write-Host 'Building and publishing...' -ForegroundColor Cyan
    & '.\publish-exe.ps1'; if($LASTEXITCODE){Stop-Update 'Build failed.'}
    $Exe=Join-Path $Repo 'EVE-IW-Preview.exe'; if(Test-Path $Exe){Start-Process $Exe -WorkingDirectory $Repo}
    Write-Host ''; Write-Host 'Changes (A = added, M = modified, D = deleted, R = renamed):' -ForegroundColor Cyan
    git status --short
    Write-Host ''; Write-Host "If anything in src/ shows as D (deleted) that you didn't expect, stop and check. Otherwise:" -ForegroundColor Cyan
    Write-Host '  git diff'; Write-Host '  git add --all'; Write-Host '  git status'; Write-Host "  git commit -m `"v$Version`""; Write-Host '  ppush'
} finally { if(Test-Path $Temp){Remove-Item $Temp -Recurse -Force -ErrorAction SilentlyContinue} }
