# publish-exe.ps1 - builds a single-file exe and puts it in the repo root as AUTZ.IronWorks.Preview.exe
#
# A running copy is NEVER closed or crashed. Windows won't let you overwrite a running exe, but it does
# let you rename it. So if the app is open, the running exe is renamed to "...exe.old-<time>" (it keeps
# running from that file) and the new exe takes its place. The new version starts the next time you
# open the app. Old copies are cleaned up automatically once nothing is running them.
$root = $PSScriptRoot
$proj = Join-Path $root 'src\AUTZ.IronWorks.Preview\AUTZ.IronWorks.Preview.csproj'
$tmp  = Join-Path $root 'publish'
$exe  = Join-Path $root 'AUTZ.IronWorks.Preview.exe'
$new  = "$exe.new"
$old  = Join-Path $root 'AUTZ IronWorks Preview.exe'   # name used by v0.2.0

if (Test-Path $tmp) { Remove-Item $tmp -Recurse -Force -ErrorAction SilentlyContinue }
dotnet publish $proj -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -p:DebugType=none -p:DebugSymbols=false -o $tmp --nologo -v q
if ($LASTEXITCODE -ne 0) {
  Write-Host "Publish FAILED - copy the first error above and send it to me. Your running app (if any) was not touched." -ForegroundColor Red
  exit 1
}

$built = Join-Path $tmp 'AUTZ.IronWorks.Preview.exe'
if (-not (Test-Path $built)) {
  Write-Host "Publish finished but $built wasn't created. Contents of $tmp :" -ForegroundColor Red
  Get-ChildItem $tmp | ForEach-Object { Write-Host "  $($_.Name)" }
  exit 1
}

# clean up leftovers from earlier updates (silently skips any that are still running)
Get-ChildItem $root -Filter 'AUTZ.IronWorks.Preview.exe.old*' -ErrorAction SilentlyContinue |
  Remove-Item -Force -ErrorAction SilentlyContinue
if (Test-Path $old) { Remove-Item $old -Force -ErrorAction SilentlyContinue }

Copy-Item $built $new -Force

$running = [bool](Get-Process -Name 'AUTZ.IronWorks.Preview' -ErrorAction SilentlyContinue)
$applied = $false
try {
  if (Test-Path $exe) {
    if ($running) {
      # step the running exe aside; it keeps running from the renamed file
      Rename-Item $exe ("AUTZ.IronWorks.Preview.exe.old-{0}" -f (Get-Date -Format 'yyyyMMddHHmmss')) -ErrorAction Stop
    } else {
      Remove-Item $exe -Force -ErrorAction Stop
    }
  }
  Move-Item $new $exe -Force -ErrorAction Stop
  $applied = $true
}
catch {
  Write-Host "Couldn't swap the exe: $($_.Exception.Message)" -ForegroundColor Red
  Write-Host "The new build is saved as: $new" -ForegroundColor Yellow
  Write-Host "Close the app, delete AUTZ.IronWorks.Preview.exe (if present), then rename that file to AUTZ.IronWorks.Preview.exe."
  exit 1
}

if ($applied) {
  $kb = [math]::Round((Get-Item $exe).Length / 1KB)
  Write-Host "Done: $exe  ($kb KB)" -ForegroundColor Green
  if ($running) {
    Write-Host "The app is open and was left running. The update applies next time you open it." -ForegroundColor Cyan
  }
}
exit 0
