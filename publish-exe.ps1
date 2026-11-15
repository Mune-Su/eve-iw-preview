# publish-exe.ps1 - builds a single-file exe and puts it in the repo root as AUTZ.IronWorks.Preview.exe
$root = $PSScriptRoot
$proj = Join-Path $root 'src\AUTZ.IronWorks.Preview\AUTZ.IronWorks.Preview.csproj'
$tmp  = Join-Path $root 'publish'
$exe  = Join-Path $root 'AUTZ.IronWorks.Preview.exe'
$old  = Join-Path $root 'AUTZ IronWorks Preview.exe'   # name used by v0.2.0

# the exe can't be replaced while it's running
Get-Process -Name 'AUTZ.IronWorks.Preview' -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 500

if (Test-Path $tmp) { Remove-Item $tmp -Recurse -Force }
dotnet publish $proj -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -p:DebugType=none -p:DebugSymbols=false -o $tmp --nologo -v q
if ($LASTEXITCODE -ne 0) {
  Write-Host "Publish FAILED - copy the errors above and send them to me." -ForegroundColor Red
  exit 1
}

$built = Join-Path $tmp 'AUTZ.IronWorks.Preview.exe'
if (-not (Test-Path $built)) {
  Write-Host "Publish finished but $built wasn't created. Contents of $tmp :" -ForegroundColor Red
  Get-ChildItem $tmp | ForEach-Object { Write-Host "  $($_.Name)" }
  exit 1
}

Copy-Item $built $exe -Force
if (Test-Path $old) { Remove-Item $old -Force }

if (Test-Path $exe) {
  $kb = [math]::Round((Get-Item $exe).Length / 1KB)
  Write-Host "Done: $exe  ($kb KB)" -ForegroundColor Green
} else {
  Write-Host "Couldn't create $exe" -ForegroundColor Red
  exit 1
}
