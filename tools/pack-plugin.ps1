# Packs the plugin as a .pext: a zip of the build output with extension.yaml at its root. Playnite's own SDK isn't
# shipped, and the sync core is compiled into Blastlog.Playnite.dll (one binary of ours to sign).
param([string]$Out = "artifacts")
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$bin = Join-Path $root "src/Blastlog.Playnite/bin/Release/net462"
if (-not (Test-Path (Join-Path $bin "Blastlog.Playnite.dll"))) { throw "Build the plugin first (dotnet build src/Blastlog.Playnite -c Release)." }
foreach ($forbidden in "Playnite.SDK.dll", "Blastlog.Sync.Core.dll") { if (Test-Path (Join-Path $bin $forbidden)) { throw "$forbidden must not ship with the plugin." } }
$version = ((Get-Content (Join-Path $root "src/Blastlog.Playnite/extension.yaml")) -match "^Version:" | Select-Object -First 1) -replace "^Version:\s*", ""
$outDir = Join-Path $root $Out; New-Item -ItemType Directory -Force $outDir | Out-Null
$stage = Join-Path $root "obj/pext"; if (Test-Path $stage) { Remove-Item -Recurse -Force $stage }; New-Item -ItemType Directory $stage | Out-Null
Get-ChildItem $bin -File | Where-Object { $_.Extension -in ".dll", ".yaml", ".png" } | Copy-Item -Destination $stage
$pext = Join-Path $outDir "Blastlog_for_Playnite_$version.pext"
Compress-Archive -Path (Join-Path $stage "*") -DestinationPath "$pext.zip" -Force; Move-Item "$pext.zip" $pext -Force
Get-Item $pext | ForEach-Object { "{0}  {1:N0} bytes" -f $_.Name, $_.Length }
