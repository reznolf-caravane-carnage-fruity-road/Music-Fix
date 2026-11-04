param(
    [string]$GameDir = "D:\SteamLibrary\steamapps\steamapps\common\CCFR"
)

$ErrorActionPreference = "Stop"

$repo = Split-Path -Parent $PSScriptRoot
$dll = Join-Path $repo "MusicStartup\bin\Release\CCFR.MusicStartup.dll"
$plugins = Join-Path $GameDir "BepInEx\plugins"

if (-not (Test-Path $dll)) {
    Write-Host "DLL not found, building first..."
    & (Join-Path $PSScriptRoot "build.ps1") -GameDir $GameDir
}

if (-not (Test-Path (Join-Path $GameDir "BepInEx\core\BepInEx.dll"))) {
    throw "BepInEx is not installed in '$GameDir'."
}

New-Item -ItemType Directory -Force -Path $plugins | Out-Null
Copy-Item $dll $plugins -Force

Write-Host "Deployed to: $plugins\CCFR.MusicStartup.dll"
