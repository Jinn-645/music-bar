# Captures MusicBar's real lyrics engine singing the promo's lines in three text styles,
# then encodes each PNG sequence to a transparent WebM in assets/product/.
$ErrorActionPreference = "Stop"
$root = Split-Path (Split-Path $PSScriptRoot)
$exe = Join-Path $PSScriptRoot "bin\Release\net10.0-windows10.0.19041.0\Capture.exe"
$frames = Join-Path $root ".capture"
$out = Join-Path $root "assets\product"

# name, font, text, stroke, strokeOpacity, strokeRadius
$styles = @(
    @("lyrics-default",  "bundled:Be Vietnam Pro",   "#FFFFFF", "#000000", "0.8", "6"),
    @("lyrics-dancing",  "bundled:Dancing Script",   "#FFE066", "#3D0C11", "1.0", "4"),
    @("lyrics-playfair", "bundled:Playfair Display", "#FFB3C7", "#1B2440", "0.9", "12")
)

foreach ($s in $styles) {
    $dir = Join-Path $frames $s[0]
    if (Test-Path $dir) { Remove-Item $dir -Recurse -Force }
    & $exe lyrics $dir 11.6 $s[1] $s[2] $s[3] $s[4] $s[5] 560 100 2 30
    if ($LASTEXITCODE -ne 0) { throw "capture failed: $($s[0])" }
    $webm = Join-Path $out "$($s[0]).webm"
    ffmpeg -y -loglevel error -framerate 30 -i (Join-Path $dir "f%04d.png") -c:v libvpx-vp9 -pix_fmt yuva420p `
        -b:v 0 -crf 22 -row-mt 1 -auto-alt-ref 0 $webm
    if ($LASTEXITCODE -ne 0) { throw "encode failed: $($s[0])" }
    "{0,-16} {1,8:N0} KB" -f $s[0], ((Get-Item $webm).Length / 1KB)
}
