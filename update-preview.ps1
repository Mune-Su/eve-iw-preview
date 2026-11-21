param(
    [switch]$AllowDeletes
)

$ErrorActionPreference = 'Stop'

function Stop-Update([string]$Message) {
    Write-Host ""
    Write-Host $Message -ForegroundColor Red
    exit 1
}

function Get-VersionKey([string]$VersionText) {
    if ($VersionText -notmatch '^(?<major>\d+)\.(?<minor>\d+)\.(?<patch>\d+)(?:-rc\.(?<rc>\d+))?$') {
        return $null
    }

    $stableRank = if ($Matches.rc) { 0 } else { 1 }
    $rcNumber = if ($Matches.rc) { [int]$Matches.rc } else { 0 }

    return '{0:D6}.{1:D6}.{2:D6}.{3:D1}.{4:D6}' -f `
        [int]$Matches.major, [int]$Matches.minor, [int]$Matches.patch, $stableRank, $rcNumber
}

function Get-RelativeFileList([string]$Root) {
    $rootPath = (Resolve-Path $Root).Path.TrimEnd('\')

    Get-ChildItem -LiteralPath $rootPath -Recurse -File | ForEach-Object {
        $_.FullName.Substring($rootPath.Length + 1).Replace('\', '/')
    }
}

$Repo = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $Repo

if (-not (Test-Path '.\.git')) {
    Stop-Update 'This script must be stored and run from the eve-iw-preview repository root.'
}

$Remote = (git remote get-url origin 2>$null).Trim()
if ($Remote -notmatch 'github\.com[/:]Mune-Su/eve-iw-preview(?:\.git)?$') {
    Stop-Update "This repository remote is '$Remote', not Mune-Su/eve-iw-preview."
}

$Branch = (git branch --show-current 2>$null).Trim()
if ($Branch -ne 'release') {
    Stop-Update "The current branch is '$Branch'. Run 'git checkout release' before updating."
}

$Pending = git status --porcelain
if ($Pending) {
    Write-Host 'Update stopped because the repository already has uncommitted changes:' -ForegroundColor Yellow
    git status --short
    Stop-Update 'Commit or discard those changes before applying another ZIP.'
}

Write-Host 'Updating the local release branch before applying the downloaded package...' -ForegroundColor Cyan
git fetch origin release --tags
if ($LASTEXITCODE -ne 0) { Stop-Update 'Could not fetch origin/release.' }

git pull --ff-only origin release
if ($LASTEXITCODE -ne 0) {
    Stop-Update 'The release branch could not be fast-forwarded. No downloaded package was applied.'
}

$Downloads = Join-Path $HOME 'Downloads'
if (-not (Test-Path $Downloads)) {
    Stop-Update "Downloads folder not found: $Downloads"
}

$Candidates = Get-ChildItem -LiteralPath $Downloads -File -Filter 'EVE-IW-Preview-v*.zip' | ForEach-Object {
    if ($_.BaseName -match '^EVE-IW-Preview-v(?<version>\d+\.\d+\.\d+(?:-rc\.\d+)?)$') {
        $key = Get-VersionKey $Matches.version
        if ($key) {
            [pscustomobject]@{
                File = $_
                Version = $Matches.version
                Key = $key
            }
        }
    }
} | Sort-Object Key, @{ Expression = { $_.File.LastWriteTimeUtc } } -Descending

$Selected = $Candidates | Select-Object -First 1
if (-not $Selected) {
    Stop-Update "No versioned source ZIP was found in $Downloads. Expected a name such as EVE-IW-Preview-v0.19.15.zip."
}

$Zip = $Selected.File.FullName
$Version = $Selected.Version
$TempRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("EVE-IW-Preview-update-" + [guid]::NewGuid().ToString('N'))

Write-Host "Selected source package: $($Selected.File.Name)" -ForegroundColor Green
Write-Host "Package version: v$Version" -ForegroundColor Green
Write-Host "The ZIP will remain in Downloads until you choose to delete it." -ForegroundColor DarkGray

try {
    New-Item -ItemType Directory -Path $TempRoot -Force | Out-Null
    Expand-Archive -LiteralPath $Zip -DestinationPath $TempRoot -Force

    $TopLevel = @(Get-ChildItem -LiteralPath $TempRoot -Force)
    if ($TopLevel.Count -ne 1 -or -not $TopLevel[0].PSIsContainer) {
        Stop-Update 'The ZIP must contain exactly one versioned project folder at its top level.'
    }

    $PackageRoot = $TopLevel[0].FullName
    if ($TopLevel[0].Name -ne "EVE-IW-Preview-v$Version") {
        Stop-Update "The ZIP root folder '$($TopLevel[0].Name)' does not match EVE-IW-Preview-v$Version."
    }

    $RequiredFiles = @(
        'AUTZ.IronWorks.Preview.sln',
        'src\AUTZ.IronWorks.Preview\AUTZ.IronWorks.Preview.csproj',
        'src\AUTZ.IronWorks.Preview\Core\MemoryTrim.cs',
        'publish-exe.ps1',
        'update-preview.ps1'
    )

    $MissingRequired = @($RequiredFiles | Where-Object {
        -not (Test-Path (Join-Path $PackageRoot $_))
    })

    if ($MissingRequired.Count -gt 0) {
        Write-Host 'The downloaded package is incomplete:' -ForegroundColor Red
        $MissingRequired | ForEach-Object { Write-Host "  Missing: $_" -ForegroundColor Red }
        Stop-Update 'No repository files were changed.'
    }

    [xml]$ProjectXml = Get-Content (Join-Path $PackageRoot 'src\AUTZ.IronWorks.Preview\AUTZ.IronWorks.Preview.csproj')
    $ProjectVersion = $ProjectXml.Project.PropertyGroup.Version | Select-Object -First 1
    if ($ProjectVersion -ne $Version) {
        Stop-Update "The project version '$ProjectVersion' does not match the ZIP version '$Version'."
    }

    $IncomingFiles = @(Get-RelativeFileList $PackageRoot)
    $IncomingSet = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
    $IncomingFiles | ForEach-Object { [void]$IncomingSet.Add($_) }

    $TrackedFiles = @(git ls-files | ForEach-Object { $_.Replace('\', '/') })
    $ProtectedPrefixes = @('.git/', '.vs/', 'bin/', 'obj/', 'publish/')
    $MissingTracked = @($TrackedFiles | Where-Object {
        $path = $_
        $protected = $false
        foreach ($prefix in $ProtectedPrefixes) {
            if ($path.StartsWith($prefix, [System.StringComparison]::OrdinalIgnoreCase)) {
                $protected = $true
                break
            }
        }
        -not $protected -and -not $IncomingSet.Contains($path)
    })

    $MissingSource = @($MissingTracked | Where-Object { $_ -like 'src/*' })
    if ($MissingSource.Count -gt 0 -and -not $AllowDeletes) {
        Write-Host ''
        Write-Host 'STOPPED: the incoming package is missing tracked source files:' -ForegroundColor Red
        $MissingSource | ForEach-Object { Write-Host "  D $_" -ForegroundColor Red }
        Write-Host ''
        Write-Host 'Nothing has been copied into the repository.' -ForegroundColor Yellow
        Write-Host 'If these deletions are intentional, inspect the ZIP and rerun:' -ForegroundColor Yellow
        Write-Host '  .\update-preview.ps1 -AllowDeletes' -ForegroundColor Cyan
        exit 1
    }

    if ($MissingTracked.Count -gt 0) {
        Write-Host ''
        Write-Host 'Tracked files absent from the incoming complete package:' -ForegroundColor Yellow
        $MissingTracked | ForEach-Object { Write-Host "  D $_" -ForegroundColor Yellow }

        if (-not $AllowDeletes) {
            Stop-Update 'Rerun with -AllowDeletes only if all listed deletions are intentional.'
        }
    }

    Write-Host ''
    Write-Host 'Applying the complete source package...' -ForegroundColor Cyan

    foreach ($RelativePath in $IncomingFiles) {
        $Source = Join-Path $PackageRoot ($RelativePath.Replace('/', '\'))
        $Destination = Join-Path $Repo ($RelativePath.Replace('/', '\'))
        $DestinationFolder = Split-Path -Parent $Destination
        if (-not (Test-Path $DestinationFolder)) {
            New-Item -ItemType Directory -Path $DestinationFolder -Force | Out-Null
        }
        Copy-Item -LiteralPath $Source -Destination $Destination -Force
    }

    if ($AllowDeletes) {
        foreach ($RelativePath in $MissingTracked) {
            $Destination = Join-Path $Repo ($RelativePath.Replace('/', '\'))
            if (Test-Path -LiteralPath $Destination) {
                Remove-Item -LiteralPath $Destination -Force
            }
        }
    }

    if (-not (Test-Path '.\src\AUTZ.IronWorks.Preview\Core\MemoryTrim.cs')) {
        Stop-Update 'Safety check failed after copying: Core\MemoryTrim.cs is missing.'
    }

    Write-Host 'Building and publishing EVE-IW-Preview.exe...' -ForegroundColor Cyan
    & '.\publish-exe.ps1'
    if ($LASTEXITCODE -ne 0) {
        Stop-Update 'Build or publish failed. Review git status before doing anything else.'
    }

    $Exe = Join-Path $Repo 'EVE-IW-Preview.exe'
    if (-not (Test-Path $Exe)) {
        Stop-Update "Publish completed but '$Exe' was not found."
    }

    $ProductVersion = (Get-Item $Exe).VersionInfo.ProductVersion
    Write-Host "Starting EVE-IW-Preview $ProductVersion..." -ForegroundColor Green
    Start-Process -FilePath $Exe -WorkingDirectory $Repo

    Write-Host ''
    Write-Host 'Changes (A = added, M = modified, D = deleted, R = renamed):' -ForegroundColor Cyan
    git status --short
    Write-Host ''
    Write-Host "If anything in src/ shows as D (deleted) that you didn't expect, stop and check. Otherwise:" -ForegroundColor Cyan
    Write-Host '  git diff'
    Write-Host '  git add --all'
    Write-Host '  git status'
    Write-Host "  git commit -m `"v$Version`""
    Write-Host '  ppush'
}
finally {
    if (Test-Path $TempRoot) {
        Remove-Item -LiteralPath $TempRoot -Recurse -Force -ErrorAction SilentlyContinue
    }
}
