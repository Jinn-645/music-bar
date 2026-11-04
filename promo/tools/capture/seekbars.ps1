# Renders the seekbar clips the promo uses with MusicBar's real SeekRenderer, then encodes each
# PNG sequence to a transparent WebM (VP9 + alpha) in assets/product/. Pass clip names to redo only those.
param([string[]]$Only)
$ErrorActionPreference = "Stop"
$root = Split-Path (Split-Path $PSScriptRoot)
$exe = Join-Path $PSScriptRoot "bin\Release\net10.0-windows10.0.19041.0\Capture.exe"
$frames = Join-Path $root ".capture"
$out = Join-Path $root "assets\product"

# name, style, seconds, wDip, hDip, scale, progressFrom, progressTo, colorA, colorB
$clips = @(
    @("seek-aurora",   "Aurora",   2.2, 860, 100, 2,    0.55, 0.62, "#1FD1C1", "#2B6CFF"),
    @("seek-silk",     "Silk",     2.2, 860, 100, 2,    0.62, 0.68, "#FF3D7F", "#FFA62B"),
    @("seek-holo",     "Holo",     2.2, 860, 100, 2,    0.66, 0.72, "#FF6B6B", "#FFD166"),
    @("seek-ink",      "Ink",      2.2, 860, 100, 2,    0.70, 0.76, "#4CC9F0", "#9B5DE5"),
    @("seek-bokeh",    "Bokeh",    2.2, 860, 100, 2,    0.74, 0.80, "#FFA62B", "#FF3D7F"),
    @("seek-spectrum", "Spectrum", 2.2, 860, 100, 2,    0.78, 0.84, "#1ED760", "#1FD1C1"),
    @("seek-silk-long","Silk",     6.0, 860, 100, 2,    0.80, 0.90, "#FF3D7F", "#FFA62B"),
    @("seek-desktop",  "Aurora",  12.0, 1536, 57.6, 1.25, 0.38, 0.52, "#1FD1C1", "#2B6CFF")
)

foreach ($c in $clips) {
    if ($Only -and $c[0] -notin $Only) { continue }
    $dir = Join-Path $frames $c[0]
    if (Test-Path $dir) { Remove-Item $dir -Recurse -Force }
    & $exe seek $c[1] $dir $c[2] $c[3] $c[4] $c[5] $c[6] $c[7] $c[8] $c[9]
    if ($LASTEXITCODE -ne 0) { throw "capture failed: $($c[0])" }
    $webm = Join-Path $out "$($c[0]).webm"
    ffmpeg -y -loglevel error -framerate 30 -i (Join-Path $dir "f%04d.png") -c:v libvpx-vp9 -pix_fmt yuva420p `
        -b:v 0 -crf 24 -row-mt 1 -auto-alt-ref 0 $webm
    if ($LASTEXITCODE -ne 0) { throw "encode failed: $($c[0])" }
    "{0,-16} {1,8:N0} KB" -f $c[0], ((Get-Item $webm).Length / 1KB)
}
