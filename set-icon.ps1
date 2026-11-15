# set-icon.ps1 - copies the AUTZ IronWorks website icon into the Preview app, then rebuilds the exe.
#   .\set-icon.ps1                       (uses C:\Users\sierr\Projects\AUTZ IronWorks)
#   .\set-icon.ps1 -Web "D:\other\path"  (if the website repo lives somewhere else)
# It reads the website's index.html to find the <link rel="icon"> file (falls back to favicon.ico).
# .ico files are copied as-is; .png / .jpg are converted to a multi-size .ico.
param([string]$Web = "C:\Users\sierr\Projects\AUTZ IronWorks")

$root = $PSScriptRoot
$dest = Join-Path $root 'src\AUTZ.IronWorks.Preview\Assets\app.ico'

if (-not (Test-Path $Web)) {
  Write-Host "Website folder not found: $Web" -ForegroundColor Red
  Write-Host "Run again with:  .\set-icon.ps1 -Web `"<path to your website repo>`""
  exit 1
}

# 1. work out which file the website uses as its icon
$candidates = @()
$index = Join-Path $Web 'index.html'
if (Test-Path $index) {
  $html = Get-Content $index -Raw
  foreach ($m in [regex]::Matches($html, '<link[^>]*rel\s*=\s*["''](?:shortcut )?icon["''][^>]*>', 'IgnoreCase')) {
    if ($m.Value -match 'href\s*=\s*["'']([^"'']+)["'']') {
      $href = ($Matches[1] -split '\?')[0].TrimStart('/') -replace '/', '\'
      $candidates += (Join-Path $Web $href)
    }
  }
}
$candidates += (Join-Path $Web 'favicon.ico')

$src = $candidates | Where-Object { Test-Path $_ -PathType Leaf } | Select-Object -First 1
if (-not $src) {
  Write-Host "Couldn't find the website's icon. Image files in $Web :" -ForegroundColor Red
  Get-ChildItem $Web -Recurse -File -Include *.ico, *.png, *.svg, *.jpg |
    Where-Object { $_.FullName -notmatch '\\(node_modules|\.git|\.vercel)\\' } |
    Select-Object -First 25 | ForEach-Object { Write-Host "  $($_.FullName.Substring($Web.Length))" }
  Write-Host "Copy the one you want to src\AUTZ.IronWorks.Preview\Assets\app.ico and run .\publish-exe.ps1"
  exit 1
}

Write-Host "Website icon: $src" -ForegroundColor Cyan
New-Item -ItemType Directory -Force (Split-Path $dest) | Out-Null
$ext = [IO.Path]::GetExtension($src).ToLower()

if ($ext -eq '.ico') {
  Copy-Item $src $dest -Force
}
elseif ($ext -in '.png', '.jpg', '.jpeg', '.gif', '.bmp') {
  Add-Type -AssemblyName System.Drawing
  $img = [System.Drawing.Image]::FromFile($src)
  try {
    $sizes = 16, 32, 48, 64, 128, 256
    $pngs = @()
    foreach ($s in $sizes) {
      $bmp = New-Object System.Drawing.Bitmap $s, $s, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
      $g = [System.Drawing.Graphics]::FromImage($bmp)
      $g.Clear([System.Drawing.Color]::Transparent)
      $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
      $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
      $scale = [Math]::Min($s / $img.Width, $s / $img.Height)
      $w = [Math]::Max(1, [int]($img.Width * $scale))
      $h = [Math]::Max(1, [int]($img.Height * $scale))
      $g.DrawImage($img, [int](($s - $w) / 2), [int](($s - $h) / 2), $w, $h)
      $g.Dispose()
      $ms = New-Object IO.MemoryStream
      $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
      $pngs += , ($ms.ToArray())
      $bmp.Dispose()
      $ms.Dispose()
    }

    $fs = [IO.File]::Open($dest, [IO.FileMode]::Create)
    $bw = New-Object IO.BinaryWriter $fs
    $bw.Write([uint16]0); $bw.Write([uint16]1); $bw.Write([uint16]$sizes.Count)   # ICONDIR
    $offset = 6 + 16 * $sizes.Count
    for ($i = 0; $i -lt $sizes.Count; $i++) {
      $dim = if ($sizes[$i] -ge 256) { 0 } else { $sizes[$i] }
      $bw.Write([byte]$dim); $bw.Write([byte]$dim); $bw.Write([byte]0); $bw.Write([byte]0)
      $bw.Write([uint16]1); $bw.Write([uint16]32)
      $bw.Write([uint32]$pngs[$i].Length); $bw.Write([uint32]$offset)
      $offset += $pngs[$i].Length
    }
    foreach ($p in $pngs) { $bw.Write($p) }
    $bw.Close()
    $fs.Close()
  }
  finally { $img.Dispose() }
}
else {
  Write-Host "The website icon is a $ext file, which I can't convert here." -ForegroundColor Red
  Write-Host "Export it as .ico (or .png) and put it at: $dest"
  exit 1
}

Write-Host "Saved: $dest" -ForegroundColor Green
Write-Host ""
Write-Host "Rebuilding the exe with the new icon..." -ForegroundColor Cyan
& "$root\publish-exe.ps1"
