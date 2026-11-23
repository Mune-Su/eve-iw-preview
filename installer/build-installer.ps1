# build-installer.ps1 - publishes the app and builds installer-output\EVE-IW-Preview-Setup.exe with Inno Setup.
#
#   -Version               version to build; read from the project file when left out
#   -InstallInnoIfMissing  installs Inno Setup (choco or winget) when it isn't on this PC
#
# Exit codes: 0 = installer built, 1 = build failed, 3 = the app built fine but Inno Setup isn't available.
param(
    [string]$Version,
    [switch]$InstallInnoIfMissing
)

$ErrorActionPreference = 'Stop'

function Find-Iscc {
    $cmd = Get-Command 'ISCC.exe' -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }
    $bases = @($env:ProgramFiles, ${env:ProgramFiles(x86)}, (Join-Path $env:LOCALAPPDATA 'Programs')) | Where-Object { $_ }
    foreach ($base in $bases) {
        $folders = Get-ChildItem -Path $base -Filter 'Inno Setup*' -Directory -ErrorAction SilentlyContinue | Sort-Object Name -Descending
        foreach ($folder in $folders) {
            $candidate = Join-Path $folder.FullName 'ISCC.exe'
            if (Test-Path $candidate) { return $candidate }
        }
    }
    return $null
}

try {
    $root       = Split-Path -Parent $PSScriptRoot
    $project    = Join-Path $root 'src\AUTZ.IronWorks.Preview\AUTZ.IronWorks.Preview.csproj'
    $publishDir = Join-Path $root 'publish-installer'
    $outputDir  = Join-Path $root 'installer-output'
    $issFile    = Join-Path $PSScriptRoot 'EVE-IW-Preview.iss'

    if (-not $Version) {
        [xml]$xml = Get-Content $project -Raw
        $Version = @($xml.Project.PropertyGroup | ForEach-Object { $_.Version } | Where-Object { $_ }) | Select-Object -First 1
    }
    if ($Version -match '^(\d+)\.(\d+)\.(\d+)') {
        $numericVersion = '{0}.{1}.{2}.0' -f $Matches[1], $Matches[2], $Matches[3]
    } else {
        throw "Version '$Version' is not in the form X.Y.Z."
    }

    Write-Host "Building EVE-IW-Preview v$Version..." -ForegroundColor Cyan
    if (Test-Path $publishDir) { Remove-Item $publishDir -Recurse -Force }
    dotnet publish $project -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -p:DebugType=none -p:DebugSymbols=false -o $publishDir --nologo -v q
    if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed.' }

    $exe = Join-Path $publishDir 'EVE-IW-Preview.exe'
    if (-not (Test-Path $exe)) { throw "Publish finished but $exe was not created." }
    $exeVersion = (Get-Item $exe).VersionInfo.ProductVersion
    if (-not $exeVersion.StartsWith($Version)) { throw "Executable version '$exeVersion' does not match '$Version'." }

    $iscc = Find-Iscc
    if (-not $iscc -and $InstallInnoIfMissing) {
        Write-Host 'Inno Setup is not installed. Installing it...' -ForegroundColor Cyan
        if (Get-Command choco -ErrorAction SilentlyContinue) {
            choco install innosetup -y --no-progress | Out-Host
            $iscc = Find-Iscc
        }
        if (-not $iscc -and (Get-Command winget -ErrorAction SilentlyContinue)) {
            winget install --id JRSoftware.InnoSetup -e --silent --accept-package-agreements --accept-source-agreements | Out-Host
            $iscc = Find-Iscc
        }
    }
    if (-not $iscc) {
        Write-Host 'The app built fine, but Inno Setup is not available, so no installer was made.' -ForegroundColor Yellow
        Write-Host 'Install it with:  winget install --id JRSoftware.InnoSetup -e   then run this again.' -ForegroundColor Yellow
        exit 3
    }

    if (Test-Path $outputDir) { Remove-Item $outputDir -Recurse -Force }
    New-Item -ItemType Directory -Path $outputDir | Out-Null
    & $iscc '/Q' "/DAppVersion=$Version" "/DNumericVersion=$numericVersion" "/DSourceExe=$exe" "/DOutputDir=$outputDir" $issFile
    if ($LASTEXITCODE -ne 0) { throw 'Inno Setup failed to build the installer.' }

    $setup = Join-Path $outputDir 'EVE-IW-Preview-Setup.exe'
    if (-not (Test-Path $setup)) { throw "Inno Setup finished but $setup was not created." }
    $mb = [math]::Round((Get-Item $setup).Length / 1MB, 1)
    Write-Host "Installer ready: $setup ($mb MB)" -ForegroundColor Green
    exit 0
}
catch {
    Write-Host ''
    Write-Host "Build FAILED: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}
