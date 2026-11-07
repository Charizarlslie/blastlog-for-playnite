# Local Playnite for checking screens (Windows only; never shipped). See docs/BUILDING_SCREENS.md.
#   playnite.ps1 setup  -Dir <folder>  [-From <existing portable folder>]   one-time: portable Playnite 10.63 + 15 test games
#   playnite.ps1 deploy -Dir <folder>  [-Who <name>]                         build plugin + theme, install them, (re)start Playnite
#   playnite.ps1 stop   [-Who <name>]                                        close Playnite and release the lock
# Playnite allows one running copy per Windows user, so deploy takes a lock ($env:TEMP\blastlog-playnite.lock) that other
# checkouts wait on; stop releases it (and a lock older than 15 minutes counts as abandoned). Take screenshots with
# shot.ps1 and click with click.ps1 while you hold it, then stop.
param(
  [Parameter(Mandatory = $true, Position = 0)][ValidateSet("setup", "deploy", "stop")][string]$Command,
  [string]$Dir, [string]$From, [string]$Who
)
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
if (-not $Who) { $Who = Split-Path -Leaf $root }  # one lock owner per checkout
$lock = Join-Path $env:TEMP "blastlog-playnite.lock"

function Dotnet {
  # A .NET 8 SDK folder named by BLASTLOG_DOTNET, else dotnet on PATH.
  $local = if ($env:BLASTLOG_DOTNET) { Join-Path $env:BLASTLOG_DOTNET "dotnet.exe" } else { "" }
  if ($local -and (Test-Path $local)) { $env:DOTNET_ROOT = Split-Path -Parent $local; $env:DOTNET_CLI_TELEMETRY_OPTOUT = "1"; $env:DOTNET_NOLOGO = "1"; return $local }
  return "dotnet"
}
function Stop-Playnite {
  Get-Process Playnite.DesktopApp, Playnite.FullscreenApp -ErrorAction SilentlyContinue | ForEach-Object { $_.CloseMainWindow() | Out-Null }
  Start-Sleep 4
  Get-Process Playnite.DesktopApp, Playnite.FullscreenApp -ErrorAction SilentlyContinue | Stop-Process -Force
}
function Take-Lock {
  for ($i = 0; $i -lt 120; $i++) {
    if (Test-Path $lock) {
      $owner = Get-Content $lock -ErrorAction SilentlyContinue
      $age = (Get-Date) - (Get-Item $lock).LastWriteTime
      if ($owner -eq $Who -or $age.TotalMinutes -gt 15) { break }
      if ($i -eq 0) { Write-Host "Waiting for $owner to finish with Playnite..." }
      Start-Sleep 10; continue
    }
    break
  }
  Set-Content $lock $Who
}

switch ($Command) {
  "setup" {
    if (-not $Dir) { throw "-Dir is required" }
    New-Item -ItemType Directory -Force $Dir | Out-Null
    # Copying a set-up portable keeps its library (a brand-new library makes Playnite show its first-run wizard).
    if ($From) { Copy-Item -Recurse -Force (Join-Path $From "*") $Dir; Remove-Item -Force (Join-Path $Dir "*.log") -ErrorAction SilentlyContinue }
    elseif (-not (Test-Path (Join-Path $Dir "Playnite.DesktopApp.exe"))) {
      $archive = Join-Path $env:TEMP "playnite-10.63.7z"
      if (-not (Test-Path $archive)) { Invoke-WebRequest https://github.com/JosefNemec/Playnite/releases/download/10.63/10.63.7z -OutFile $archive }
      tar -xf $archive -C $Dir
    }
    '{ "FirstTimeWizardComplete": true, "Theme": "Blastlog_Desktop_Theme", "StartInFullscreen": false, "UpdateLibStartup": false, "CheckForProgramUpdates": false }' |
      Set-Content -Encoding UTF8 (Join-Path $Dir "config.json")
    # Test games: names and IGDB cover ids in devseed/games.txt; covers fetched once.
    $seed = Join-Path $Dir "devseed"; New-Item -ItemType Directory -Force (Join-Path $seed "covers") | Out-Null
    Copy-Item (Join-Path $PSScriptRoot "devseed/games.txt") $seed -Force
    Get-Content (Join-Path $seed "games.txt") | Where-Object { $_ -match "\|" } | ForEach-Object {
      $id = $_.Trim().Split("|")[1]; $file = Join-Path $seed "covers/$id.jpg"
      if (-not (Test-Path $file)) { Invoke-WebRequest "https://images.igdb.com/igdb/image/upload/t_cover_big_2x/$id.jpg" -OutFile $file }
    }
    $dotnet = Dotnet
    & $dotnet build (Join-Path $PSScriptRoot "devseed/DevSeed.csproj") -c Release -v q -p:EnableSourceControlManagerQueries=false -o (Join-Path $Dir "Extensions/DevSeed") | Out-Null
    "Id: Blastlog_DevSeed`nName: Blastlog dev seed (local only)`nAuthor: Blastlog`nVersion: 0.0.1`nModule: DevSeed.dll`nType: GenericPlugin" |
      Set-Content -Encoding UTF8 (Join-Path $Dir "Extensions/DevSeed/extension.yaml")
    "Set up $Dir. Next: playnite.ps1 deploy -Dir $Dir"
  }
  "deploy" {
    if (-not $Dir) { throw "-Dir is required" }
    $dotnet = Dotnet
    Push-Location $root
    try {
      & $dotnet build src/Blastlog.Playnite -c Release -v q -p:EnableSourceControlManagerQueries=false | Select-String " error " | ForEach-Object { throw "Build failed: $_" }
      & powershell -NoProfile -ExecutionPolicy Bypass -File tools/pack-plugin.ps1 | Out-Null
      # git writes progress to stderr, which Windows PowerShell would treat as an error here.
      $ErrorActionPreference = "Continue"; & powershell -NoProfile -ExecutionPolicy Bypass -File tools/build-theme.ps1 2>&1 | Out-Null; $ErrorActionPreference = "Stop"
      if ($LASTEXITCODE) { throw "The theme didn't build: run tools/build-theme.ps1 to see why." }
    } finally { Pop-Location }
    Take-Lock
    Stop-Playnite
    $ext = Join-Path $Dir "Extensions/BlastlogForPlaynite"; $theme = Join-Path $Dir "Themes/Desktop/Blastlog"
    Remove-Item -Recurse -Force $ext, $theme -ErrorAction SilentlyContinue
    foreach ($pair in @(@("*.pext", $ext), @("*.pthm", $theme))) {
      $file = Get-ChildItem (Join-Path $root "artifacts") -Filter $pair[0] | Sort-Object LastWriteTime | Select-Object -Last 1
      $zip = Join-Path $env:TEMP ("bl-" + [guid]::NewGuid() + ".zip"); Copy-Item $file.FullName $zip; Expand-Archive $zip $pair[1] -Force; Remove-Item $zip
    }
    $env:BLASTLOG_DEVSEED = Join-Path $Dir "devseed"
    Start-Process (Join-Path $Dir "Playnite.DesktopApp.exe")
    # After a forced close Playnite offers safe mode; answer No.
    $shell = New-Object -ComObject WScript.Shell
    for ($i = 0; $i -lt 25; $i++) { Start-Sleep 1; if ($shell.AppActivate("Startup Error")) { Start-Sleep -Milliseconds 300; $shell.SendKeys("n"); break } }
    for ($i = 0; $i -lt 40; $i++) { $p = Get-Process Playnite.DesktopApp -ErrorAction SilentlyContinue | Where-Object { $_.MainWindowTitle -eq "Playnite" }; if ($p) { break }; Start-Sleep 1 }
    Start-Sleep 3
    Set-Content $lock $Who
    "Playnite is running ($Dir). Screenshot: tools/dev/shot.ps1 -Out <png>. Errors: $Dir\playnite.log. Then: playnite.ps1 stop"
  }
  "stop" {
    Stop-Playnite
    if ((Test-Path $lock) -and ((Get-Content $lock) -eq $Who)) { Remove-Item $lock }
    "Stopped."
  }
}
