# publish-exe.ps1 - builds the app and the installer, then opens the installer.
# (The file name is kept so update-preview.ps1, including older copies of it, keeps working.)
#
# EVE-IW-Preview is installed with EVE-IW-Preview-Setup.exe. There is no portable exe in the repo any more:
# a leftover EVE-IW-Preview.exe in this folder is removed once the build has succeeded (or stepped aside
# if it is still running, which Windows allows).
param([switch]$NoLaunch)

$root    = $PSScriptRoot
$builder = Join-Path $root 'installer\build-installer.ps1'
$setup   = Join-Path $root 'installer-output\EVE-IW-Preview-Setup.exe'

& $builder -InstallInnoIfMissing
$code = $LASTEXITCODE
if ($code -ne 0 -and $code -ne 3) {
    Write-Host 'Build FAILED - copy the first error above and send it to me.' -ForegroundColor Red
    exit 1
}

# retire the old portable exe and leftovers from earlier updates
$oldExe = Join-Path $root 'EVE-IW-Preview.exe'
Get-ChildItem $root -Filter 'EVE-IW-Preview.exe.*' -ErrorAction SilentlyContinue | Remove-Item -Force -ErrorAction SilentlyContinue
if (Test-Path $oldExe) {
    try {
        Remove-Item $oldExe -Force -ErrorAction Stop
        Write-Host 'Removed the old portable EVE-IW-Preview.exe from the repo folder.' -ForegroundColor Cyan
    }
    catch {
        try {
            Rename-Item $oldExe ('EVE-IW-Preview.exe.old-{0}' -f (Get-Date -Format 'yyyyMMddHHmmss')) -ErrorAction Stop
            Write-Host 'The old portable EVE-IW-Preview.exe is still running, so it was stepped aside. Close it once Setup asks.' -ForegroundColor Cyan
        }
        catch {
            Write-Host "Couldn't remove the old portable EVE-IW-Preview.exe: $($_.Exception.Message)" -ForegroundColor Yellow
        }
    }
}

if ($code -eq 3) { exit 0 }

if (-not $NoLaunch -and (Test-Path $setup)) {
    Write-Host 'Opening the installer...' -ForegroundColor Cyan
    Start-Process $setup
}
exit 0
