param(
    [string]$GameDir = "D:\SteamLibrary\steamapps\steamapps\common\CCFR"
)

$ErrorActionPreference = "Stop"

$repo = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repo "MusicStartup\MusicStartup.csproj"

$candidates = @(
    "C:\Users\$env:USERNAME\.dotnet\dotnet.exe",
    (Join-Path $env:ProgramFiles "dotnet\dotnet.exe")
)

$dotnet = $candidates | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $dotnet) {
    $cmd = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($cmd) { $dotnet = $cmd.Source }
}

if (-not $dotnet) {
    throw "No .NET SDK found. Install one (https://dot.net) or set up a user-local SDK."
}

& $dotnet build $project -c Release -p:GameDir="$GameDir" -v minimal
if ($LASTEXITCODE -ne 0) {
    throw "Build failed with exit code $LASTEXITCODE"
}

Write-Host "Built: $repo\MusicStartup\bin\Release\CCFR.MusicStartup.dll"
