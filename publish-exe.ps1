# publish-exe.ps1 - builds a single-file exe and puts it in the repo root as EVE-IW-Preview.exe
#
# A running copy is NEVER closed or crashed. Windows won't let you overwrite a running exe, but it does
# let you rename it. So if the app is open, the running exe is renamed to "...exe.old-<time>" (it keeps
# running from that file) and the new exe takes its place. The new version starts the next time you
# open the app. Old copies are cleaned up automatically once nothing is running them.
#
# Earlier versions were called AUTZ.IronWorks.Preview.exe. That file is retired here the same way.
$root = $PSScriptRoot
$proj = Join-Path $root 'src\AUTZ.IronWorks.Preview\AUTZ.IronWorks.Preview.csproj'
$tmp  = Join-Path $root 'publish'
$name = 'EVE-IW-Preview'
$exe  = Join-Path $root "$name.exe"
$new  = "$exe.new"
$earlierNames = 'AUTZ.IronWorks.Preview', 'AUTZ IronWorks Preview'

if (Test-Path $tmp) { Remove-Item $tmp -Recurse -Force -ErrorAction SilentlyContinue }
dotnet publish $proj -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -p:DebugType=none -p:DebugSymbols=false -o $tmp --nologo -v q
if ($LASTEXITCODE -ne 0) {
  Write-Host "Publish FAILED - copy the first error above and send it to me. Your running app (if any) was not touched." -ForegroundColor Red
  exit 1
}

$built = Join-Path $tmp "$name.exe"
if (-not (Test-Path $built)) {
  Write-Host "Publish finished but $built wasn't created. Contents of $tmp :" -ForegroundColor Red
  Get-ChildItem $tmp | ForEach-Object { Write-Host "  $($_.Name)" }
  exit 1
}

# clean up leftovers from earlier updates (silently skips any that are still running)
foreach ($n in @($name) + $earlierNames) {
  Get-ChildItem $root -Filter "$n.exe.old*" -ErrorAction SilentlyContinue | Remove-Item -Force -ErrorAction SilentlyContinue
}

# retire the exe from before the rename (steps aside if it's running, otherwise deleted)
foreach ($n in $earlierNames) {
  $oldExe = Join-Path $root "$n.exe"
  if (Test-Path $oldExe) {
    try {
      if (Get-Process -Name $n -ErrorAction SilentlyContinue) {
        Rename-Item $oldExe ("$n.exe.old-{0}" -f (Get-Date -Format 'yyyyMMddHHmmss')) -ErrorAction Stop
      } else {
        Remove-Item $oldExe -Force -ErrorAction Stop
      }
    } catch {
      Write-Host "Couldn't remove the old $n.exe: $($_.Exception.Message)" -ForegroundColor Yellow
    }
  }
}

Copy-Item $built $new -Force

$running = [bool](Get-Process -Name $name -ErrorAction SilentlyContinue)
$applied = $false
try {
  if (Test-Path $exe) {
    if ($running) {
      # step the running exe aside; it keeps running from the renamed file
      Rename-Item $exe ("$name.exe.old-{0}" -f (Get-Date -Format 'yyyyMMddHHmmss')) -ErrorAction Stop
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
  Write-Host "Close the app, delete $name.exe (if present), then rename that file to $name.exe."
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
