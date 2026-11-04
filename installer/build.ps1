# Builds the two release files into artifacts\:
#   MusicBar-<version>-Setup.exe     per-user installer (Inno Setup 6)
#   MusicBar-<version>-portable.zip  the same self-contained app, unzip and run
# Needs the .NET 10 SDK, MSVC (for the Windows 11 taskbar module) and Inno Setup 6.
param([string]$Version = "1.0.0")
$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot
$artifacts = Join-Path $root "artifacts"
$publish = Join-Path $artifacts "publish"

if (Test-Path $artifacts) { Remove-Item $artifacts -Recurse -Force }

# Self-contained single file: nothing to install first, not even .NET.
dotnet publish (Join-Path $root "src\MusicBar\MusicBar.csproj") -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true `
    -p:DebugType=none -p:Version=$Version -o $publish
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }

# Portable: the app, its font licenses and the license, in one folder.
$stage = Join-Path $artifacts "MusicBar"
New-Item -ItemType Directory -Force $stage | Out-Null
Copy-Item (Join-Path $publish "*") $stage -Recurse
Copy-Item (Join-Path $root "LICENSE") (Join-Path $stage "LICENSE.txt")
Compress-Archive -Path $stage -DestinationPath (Join-Path $artifacts "MusicBar-$Version-portable.zip") -CompressionLevel Optimal
Remove-Item $stage -Recurse -Force

# Installer.
$iscc = @(
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) { throw "Inno Setup 6 not found (winget install JRSoftware.InnoSetup)" }
& $iscc /Q "/DAppVersion=$Version" "/DPublishDir=$publish" (Join-Path $PSScriptRoot "MusicBar.iss")
if ($LASTEXITCODE -ne 0) { throw "ISCC failed" }

Get-ChildItem $artifacts -File | ForEach-Object { "{0,-34} {1,7:N1} MB" -f $_.Name, ($_.Length / 1MB) }
