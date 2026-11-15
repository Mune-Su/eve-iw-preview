# publish-exe.ps1 - builds a single-file exe and puts it in the repo root as "AUTZ IronWorks Preview.exe"
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$proj = Join-Path $root 'src\AUTZ.IronWorks.Preview\AUTZ.IronWorks.Preview.csproj'
$tmp  = Join-Path $root 'publish'
$exe  = Join-Path $root 'AUTZ IronWorks Preview.exe'

# the exe can't be replaced while it's running
Get-Process -Name 'AUTZ.IronWorks.Preview', 'AUTZ IronWorks Preview' -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 500

dotnet publish $proj -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -p:DebugType=none -p:DebugSymbols=false -o $tmp --nologo -v q
if ($LASTEXITCODE -ne 0) { Write-Host "Publish failed - see the errors above." -ForegroundColor Red; exit 1 }

Copy-Item (Join-Path $tmp 'AUTZ.IronWorks.Preview.exe') $exe -Force
Write-Host "Done: $exe" -ForegroundColor Green
