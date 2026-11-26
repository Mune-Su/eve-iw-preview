# make-admin-key.ps1 - unlocks the Admin menu in EVE-IW-Preview on THIS PC only.
#
# First run: makes your private key (kept in %USERPROFILE%\.eve-iw-admin, never in the repo) and writes the public
# half into src\AUTZ.IronWorks.Preview\Services\AdminAccess.cs. Commit that change and build a new version.
# Every run: writes a signed admin.key for this PC into the app's settings folder.
# To use it on another PC, copy the .eve-iw-admin folder there and run this script on that PC.
$ErrorActionPreference = 'Stop'

$repo      = Split-Path -Parent $PSScriptRoot
$source    = Join-Path $repo 'src\AUTZ.IronWorks.Preview\Services\AdminAccess.cs'
$keyFolder = Join-Path $HOME '.eve-iw-admin'
$private   = Join-Path $keyFolder 'private-key.xml'
$appFolder = Join-Path $env:LOCALAPPDATA 'EVE-IronWorks-Preview'

$rsa = New-Object System.Security.Cryptography.RSACryptoServiceProvider 2048
$rsa.PersistKeyInCsp = $false
if (Test-Path $private) {
    $rsa.FromXmlString((Get-Content $private -Raw))
    Write-Host 'Using your existing private key.' -ForegroundColor Cyan
} else {
    New-Item -ItemType Directory -Force $keyFolder | Out-Null
    Set-Content -Path $private -Value $rsa.ToXmlString($true) -Encoding ASCII
    Write-Host "Made a new private key: $private (keep it safe, never commit it)" -ForegroundColor Cyan
}

# put the public half in the app source (only changes the file the first time)
$public = $rsa.ToXmlString($false)
$code = Get-Content $source -Raw
$line = '    private const string PublicKeyXml = "' + $public + '";'
$updated = [regex]::Replace($code, '(?m)^    private const string PublicKeyXml = ".*";\r?$', $line)
if ($updated -ne $code) {
    Set-Content -Path $source -Value $updated -NoNewline -Encoding UTF8
    Write-Host 'Public key written to AdminAccess.cs. Commit it and build a new version.' -ForegroundColor Yellow
}

# sign this PC's id
$machine = ((Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Cryptography').MachineGuid).Trim().ToLowerInvariant()
$bytes = [System.Text.Encoding]::UTF8.GetBytes("EVE-IW-ADMIN|$machine")
$signature = $rsa.SignData($bytes, [System.Security.Cryptography.HashAlgorithmName]::SHA256, [System.Security.Cryptography.RSASignaturePadding]::Pkcs1)
New-Item -ItemType Directory -Force $appFolder | Out-Null
Set-Content -Path (Join-Path $appFolder 'admin.key') -Value ([Convert]::ToBase64String($signature)) -Encoding ASCII
Write-Host 'admin.key written for this PC. Restart EVE-IW-Preview to see the Admin menu.' -ForegroundColor Green
