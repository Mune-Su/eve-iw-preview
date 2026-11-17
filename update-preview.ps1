# update-preview.ps1 - replace the EVE-IW-Preview repo with the newest EVE-IW-Preview-vX.Y.Z version.
#  0. Finds your repo and your "versions" folder by itself (see the lookup below), so renaming or moving them doesn't
#     break it. Keep this script in the repo root and run it from there: .\update-preview.ps1
#  1. Looks for the newest EVE-IW-Preview-v*.zip (or AUTZ-IronWorks-Preview-v*.zip, the name used before the rename)
#     in Downloads and moves it into the versions folder.
#  2. Picks the newest version (by version number) in that folder, zip or folder, and extracts it if needed.
#  3. Replaces everything in the repo EXCEPT Git history, LICENSE,
#     build output (bin/obj/.vs/publish), this script and the Assets folder (icon + logo).
#  4. Builds and publishes EVE-IW-Preview.exe into the repo root, then git add and git status.
#     A running app is NOT closed: the update applies the next time you open it.
#     It does NOT commit or push - you check the list first.

# ---- where things are: found automatically. Only fill these in if you keep them somewhere unusual. ----
$versionsOverride = ''
$repoOverride     = ''

$downloads = Join-Path $HOME 'Downloads'

# the versions folder: the first of these that exists. Documents\IronWorks is checked first, then Documents itself.
# (OneDrive Documents is "My Documents" when OneDrive backs it up.)
$versions = $versionsOverride
if (-not $versions) {
  $myDocs       = [Environment]::GetFolderPath('MyDocuments')
  $docsFolders  = @($myDocs, (Join-Path $HOME 'OneDrive\Documents'), (Join-Path $HOME 'Documents')) | Where-Object { $_ } | Select-Object -Unique
  $searchIn     = @($docsFolders | ForEach-Object { Join-Path $_ 'IronWorks' }) + $docsFolders
  $versionNames = 'EVE IronWorks Preview versions', 'EVE-IW-Preview versions', 'AUTZ IronWorks Preview versions'
  foreach ($d in $searchIn) {
    foreach ($n in $versionNames) {
      if (-not $versions -and (Test-Path (Join-Path $d $n))) { $versions = Join-Path $d $n }
    }
  }
  if (-not $versions) { $versions = Join-Path $myDocs 'IronWorks\EVE IronWorks Preview versions' }
}

# the repo: the folder this script sits in if that is a git repo, otherwise the first of these under Projects
$repo = $repoOverride
if (-not $repo -and $PSScriptRoot -and (Test-Path (Join-Path $PSScriptRoot '.git'))) { $repo = $PSScriptRoot }
if (-not $repo) {
  foreach ($n in 'EVE-IronWorks-Preview', 'EVE-IW-Preview', 'AUTZ-IronWorks-Preview') {
    $candidate = Join-Path (Join-Path $HOME 'Projects') $n
    if (-not $repo -and (Test-Path (Join-Path $candidate '.git'))) { $repo = $candidate }
  }
}

$keep      = '.git', 'LICENSE', 'bin', 'obj', '.vs', 'publish', 'update-preview.ps1', 'EVE-IW-Preview.exe', 'AUTZ.IronWorks.Preview.exe'
$zipName   = '^(EVE-IW-Preview|AUTZ-IronWorks-Preview)-v\d+\.\d+\.\d+'   # new and earlier release names (a browser's " (1)" suffix is fine)
$assetsRel = 'src\AUTZ.IronWorks.Preview\Assets'   # app.ico + logo.png live here and are kept across updates

function VersionOf($item) {
  if ($item.Name -match 'v(\d+\.\d+\.\d+)') { [version]$Matches[1] } else { [version]'0.0.0' }
}
function Stop-Update($msg) { Write-Host $msg -ForegroundColor Red; exit 1 }

if (-not (Test-Path $versions)) { New-Item -ItemType Directory -Force -Path $versions | Out-Null; Write-Host "Created $versions" }
Write-Host "Versions folder: $versions"
Write-Host "Repo:            $(if ($repo) { $repo } else { '(not found)' })"

# 1. a new zip in Downloads goes into the versions folder
$dl = Get-ChildItem $downloads -File -Filter '*.zip' -ErrorAction SilentlyContinue |
  Where-Object { $_.Name -match $zipName } | Sort-Object { VersionOf $_ } -Descending | Select-Object -First 1
if ($dl) {
  $dest = Join-Path $versions ("EVE-IW-Preview-v{0}.zip" -f (VersionOf $dl))
  Move-Item $dl.FullName $dest -Force
  Write-Host "Moved $($dl.Name) from Downloads into the versions folder"
}

# 2. newest version in the versions folder
$latest = Get-ChildItem $versions |
  Where-Object { $_.Name -match $zipName -and ($_.PSIsContainer -or $_.Extension -eq '.zip') } |
  Sort-Object { VersionOf $_ }, { $_.PSIsContainer } -Descending | Select-Object -First 1
if (-not $latest) { Stop-Update "No EVE-IW-Preview-v* (or AUTZ-IronWorks-Preview-v*) zip or folder found in $versions" }
$ver = VersionOf $latest

$src = $latest.FullName
if (-not $latest.PSIsContainer) {
  $src = Join-Path $versions ("EVE-IW-Preview-v{0}" -f $ver)
  Expand-Archive $latest.FullName -DestinationPath $src -Force
}
# the folder with the .sln in it (handles a folder inside a folder of the same name)
$sln = Get-ChildItem $src -Recurse -Filter '*.sln' | Sort-Object { $_.FullName.Length } | Select-Object -First 1
if (-not $sln) { Stop-Update "No .sln file inside $src - is it the right folder? Nothing was changed." }
$src = $sln.DirectoryName
if (-not (Test-Path "$src\src\AUTZ.IronWorks.Preview\AUTZ.IronWorks.Preview.csproj")) {
  Stop-Update "$src doesn't look like EVE-IW-Preview (no src\AUTZ.IronWorks.Preview\*.csproj). Nothing was changed."
}

Write-Host ""
Write-Host "Updating to v$ver from:" -ForegroundColor Cyan
Write-Host "  $src"

# 3. don't wipe work that hasn't been committed
if (-not $repo -or -not (Test-Path $repo)) { Stop-Update "Couldn't find your repo. I looked for a git repo named EVE-IronWorks-Preview, EVE-IW-Preview or AUTZ-IronWorks-Preview in $HOME\Projects. Put its full path in `$repoOverride at the top of this script." }
Set-Location $repo
$dirty = git status --porcelain
if ($dirty) {
  Write-Host ""
  Write-Host "The repo has changes that aren't committed yet:" -ForegroundColor Yellow
  $dirty | Select-Object -First 15 | ForEach-Object { Write-Host "  $_" }
  $ans = Read-Host "Replace them anyway? (y/n)"
  if ($ans -ne 'y') { Stop-Update "Stopped - nothing was changed." }
}

# remember the Assets folder (icon + logo) so the wipe doesn't lose it
$assetsBackup = Join-Path $env:TEMP 'eve-iw-preview-assets'
if (Test-Path $assetsBackup) { Remove-Item $assetsBackup -Recurse -Force }
if (Test-Path "$repo\$assetsRel") {
  New-Item -ItemType Directory -Force $assetsBackup | Out-Null
  Copy-Item "$repo\$assetsRel\*" $assetsBackup -Recurse -Force
}

Get-ChildItem -Force | Where-Object { $keep -notcontains $_.Name -and $_.Name -notlike '*.exe.old*' } | Remove-Item -Recurse -Force
Copy-Item "$src\*" . -Recurse -Force

# put back any asset the new version didn't ship (the update itself never replaces your icon or logo)
if (Test-Path $assetsBackup) {
  New-Item -ItemType Directory -Force "$repo\$assetsRel" | Out-Null
  Get-ChildItem $assetsBackup -File | ForEach-Object {
    if (-not (Test-Path "$repo\$assetsRel\$($_.Name)")) { Copy-Item $_.FullName "$repo\$assetsRel\$($_.Name)" -Force }
  }
}

# 4. build + publish the exe (a running copy is left alone), then stage and show what changed
Get-ChildItem $repo -Filter *.ps1 | Unblock-File

Write-Host ""
Write-Host "Building and publishing the exe..." -ForegroundColor Cyan
& "$repo\publish-exe.ps1"
if ($LASTEXITCODE -ne 0) {
  Write-Host ""
  Write-Host "BUILD/PUBLISH FAILED - send me the first error above before you commit." -ForegroundColor Red
}

git add --all 2>$null
Write-Host ""
Write-Host "Changes (A = added, M = modified, D = deleted, R = renamed):" -ForegroundColor Cyan
git status --short
Write-Host ""
Write-Host "If anything in src/ shows as D (deleted) that you didn't expect, stop and check. Otherwise:" -ForegroundColor Cyan
Write-Host "  git commit -m `"v$ver`""
Write-Host "  ppush"




