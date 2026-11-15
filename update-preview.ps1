# update-preview.ps1 - replace the AUTZ IronWorks Preview repo with the newest AUTZ-IronWorks-Preview-vX.Y.Z version.
#  1. Looks for the newest AUTZ-IronWorks-Preview-v*.zip in Downloads and moves it into "AUTZ IronWorks Preview versions".
#  2. Picks the newest version (by version number) in that folder, zip or folder, and extracts it if needed.
#  3. Replaces everything in the repo EXCEPT Git history, .gitignore, build output (bin/obj/.vs) and this script.
#  4. Runs a Release build so you know straight away if it compiles, then git add and git status.
#     It does NOT commit or push - you check the list first.

$versions  = "C:\Users\sierr\OneDrive\Documents\AUTZ IronWorks Preview versions"
$downloads = "$HOME\Downloads"
$repo      = "C:\Users\sierr\Projects\AUTZ-IronWorks-Preview"
$keep      = '.git', '.gitignore', 'bin', 'obj', '.vs', 'publish', 'update-preview.ps1'

function VersionOf($item) {
  if ($item.Name -match 'v(\d+\.\d+\.\d+)') { [version]$Matches[1] } else { [version]'0.0.0' }
}
function Stop-Update($msg) { Write-Host $msg -ForegroundColor Red; exit 1 }

if (-not (Test-Path $versions)) { New-Item -ItemType Directory -Path $versions | Out-Null }

# 1. a new zip in Downloads goes into the versions folder
$dl = Get-ChildItem $downloads -Filter 'AUTZ-IronWorks-Preview-v*.zip' -ErrorAction SilentlyContinue |
  Sort-Object { VersionOf $_ } -Descending | Select-Object -First 1
if ($dl) {
  $dest = Join-Path $versions ("AUTZ-IronWorks-Preview-v{0}.zip" -f (VersionOf $dl))
  Move-Item $dl.FullName $dest -Force
  Write-Host "Moved $($dl.Name) from Downloads into AUTZ IronWorks Preview versions"
}

# 2. newest version in the versions folder
$latest = Get-ChildItem $versions -Filter 'AUTZ-IronWorks-Preview-v*' |
  Where-Object { $_.PSIsContainer -or $_.Extension -eq '.zip' } |
  Sort-Object { VersionOf $_ }, { $_.PSIsContainer } -Descending | Select-Object -First 1
if (-not $latest) { Stop-Update "No AUTZ-IronWorks-Preview-v* zip or folder found in $versions" }
$ver = VersionOf $latest

$src = $latest.FullName
if (-not $latest.PSIsContainer) {
  $src = Join-Path $versions ("AUTZ-IronWorks-Preview-v{0}" -f $ver)
  Expand-Archive $latest.FullName -DestinationPath $src -Force
}
# the folder with the .sln in it (handles a folder inside a folder of the same name)
$sln = Get-ChildItem $src -Recurse -Filter 'AUTZ.IronWorks.Preview.sln' | Sort-Object { $_.FullName.Length } | Select-Object -First 1
if (-not $sln) { Stop-Update "No AUTZ.IronWorks.Preview.sln inside $src - is it the right folder? Nothing was changed." }
$src = $sln.DirectoryName
if (-not (Test-Path "$src\src\AUTZ.IronWorks.Preview\AUTZ.IronWorks.Preview.csproj")) {
  Stop-Update "$src doesn't look like AUTZ IronWorks Preview (no src\AUTZ.IronWorks.Preview\*.csproj). Nothing was changed."
}

Write-Host ""
Write-Host "Updating to v$ver from:" -ForegroundColor Cyan
Write-Host "  $src"

# 3. don't wipe work that hasn't been committed
if (-not (Test-Path $repo)) { Stop-Update "Repo folder not found: $repo" }
Set-Location $repo
$dirty = git status --porcelain
if ($dirty) {
  Write-Host ""
  Write-Host "The repo has changes that aren't committed yet:" -ForegroundColor Yellow
  $dirty | Select-Object -First 15 | ForEach-Object { Write-Host "  $_" }
  $ans = Read-Host "Replace them anyway? (y/n)"
  if ($ans -ne 'y') { Stop-Update "Stopped - nothing was changed." }
}

Get-ChildItem -Force | Where-Object { $keep -notcontains $_.Name } | Remove-Item -Recurse -Force
Copy-Item "$src\*" . -Recurse -Force

# 4. build check, then stage and show what changed
Write-Host ""
Write-Host "Building (Release)..." -ForegroundColor Cyan
dotnet build -c Release --nologo -v q
if ($LASTEXITCODE -ne 0) {
  Write-Host ""
  Write-Host "BUILD FAILED - copy the errors above and send them to me before you commit." -ForegroundColor Red
} else {
  Write-Host "Build succeeded." -ForegroundColor Green
}

git add --all 2>$null
Write-Host ""
Write-Host "Changes (A = added, M = modified, D = deleted, R = renamed):" -ForegroundColor Cyan
git status --short
Write-Host ""
Write-Host "If anything in src/ shows as D (deleted) that you didn't expect, stop and check. Otherwise:" -ForegroundColor Cyan
Write-Host "  git commit -m `"v$ver`""
Write-Host "  git push"
