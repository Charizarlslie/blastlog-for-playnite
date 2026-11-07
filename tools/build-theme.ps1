# Builds the Blastlog desktop theme (.pthm) from Playnite's own default desktop theme (MIT, credited in CREDITS.md):
# fetches that theme at a pinned Playnite release, applies Blastlog's colours and type (docs/design.md), places the
# plugin's custom elements in the game details, and zips it. Runs on the Windows CI job; needs only git and PowerShell.
# Playnite's files are never committed here: they are fetched at build time from the public JosefNemec/Playnite repo.
param(
  [string]$PlayniteRef = "10.63",
  [string]$Out = "artifacts"
)
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
# The theme ships with the plugin and carries the plugin's version.
$Version = ((Get-Content (Join-Path $root "src/Blastlog.Playnite/extension.yaml")) -match "^Version:" | Select-Object -First 1) -replace "^Version:\s*", ""
$work = Join-Path $root "obj/theme-build"
if (Test-Path $work) { Remove-Item -Recurse -Force $work }
New-Item -ItemType Directory -Force $work, (Join-Path $root $Out) | Out-Null

# 1. Playnite's default desktop theme at the pinned release (sparse: only that folder).
$repo = Join-Path $work "playnite"
git clone --quiet --depth 1 --filter=blob:none --sparse --branch $PlayniteRef https://github.com/JosefNemec/Playnite.git $repo
# Cone mode takes folders; top-level files such as LICENSE.md come with it anyway.
git -C $repo sparse-checkout set "source/Playnite.DesktopApp/Themes/Desktop/Default"
$base = Join-Path $repo "source/Playnite.DesktopApp/Themes/Desktop/Default"
if (-not (Test-Path (Join-Path $base "Constants.xaml"))) { throw "Playnite's default theme wasn't found at $PlayniteRef." }
$theme = Join-Path $work "Blastlog"
Copy-Item -Recurse $base $theme

# 2. Blastlog's colours (dark tokens from docs/design.md) and type. Each key must exist, so a changed base fails loudly.
$constants = Join-Path $theme "Constants.xaml"
$xaml = Get-Content -Raw -Encoding UTF8 $constants
$colors = [ordered]@{
  TextColor = "#F4F4F6"; TextColorDarker = "#A6A6B1"; MainColor = "#1E1E24"; MainColorDark = "#0B0B0D"; HoverColor = "#00B386"
  GlyphColor = "#00E4A8"; HighlightGlyphColor = "#8800E4A8"; PopupBackgroundColor = "#151519"; PopupBorderColor = "#6A6A76"
  BackgroundToneColor = "#1E1E24"; GridItemBackgroundColor = "#151519"; PanelSeparatorColor = "#2A2A31"; WindowPanelSeparatorColor = "#3300E4A8"
}
foreach ($key in $colors.Keys) {
  $pattern = "(<Color\s+x:Key=""$key""\s*>)[^<]*(</Color>)"
  if ($xaml -notmatch $pattern) { throw "Constants.xaml has no Color $key" }
  $xaml = [regex]::Replace($xaml, $pattern, "`${1}$($colors[$key])`${2}")
}
$brushes = [ordered]@{ NormalBorderBrush = "#6A6A76"; ButtonBackgroundBrush = "#151519"; PopupBackgroundBrush = "#151519"; PositiveRatingBrush = "#6EE7B0"; MixedRatingBrush = "#FFC400" }
foreach ($key in $brushes.Keys) {
  $pattern = "(<SolidColorBrush\s+x:Key=""$key""\s+Color="")[^""]*("")"
  if ($xaml -notmatch $pattern) { Write-Warning "Constants.xaml: no literal colour on $key; left as Playnite's"; continue }
  $xaml = [regex]::Replace($xaml, $pattern, "`${1}$($brushes[$key])`${2}")
}
# Outfit when it's installed (the setup guide links it), Segoe UI otherwise. Nothing under 14px (docs/design.md §4).
$xaml = [regex]::Replace($xaml, "(<FontFamily\s+x:Key=""FontFamily""\s*>)[^<]*(</FontFamily>)", "`${1}Outfit, Segoe UI`${2}")
$sizes = @{ FontSizeSmall = "14"; FontSize = "15"; FontSizeLarge = "16"; FontSizeLarger = "20"; FontSizeLargest = "29" }
foreach ($key in $sizes.Keys) { $xaml = [regex]::Replace($xaml, "(<sys:Double\s+x:Key=""$key""\s*>)[^<]*(</sys:Double>)", "`${1}$($sizes[$key])`${2}") }
$xaml = [regex]::Replace($xaml, "(<CornerRadius\s+x:Key=""ControlCornerRadius""\s*>)[^<]*(</CornerRadius>)", "`${1}10`${2}")
[IO.File]::WriteAllText($constants, $xaml)

# 3. The plugin's elements in the game details, above the description: status line, then the Blastlog panel.
$elements = '<StackPanel Margin="0,0,0,15"><ContentControl x:Name="Blastlog_Status" /><ContentControl x:Name="Blastlog_GamePanel" /></StackPanel>'
foreach ($view in "DetailsViewGameOverview.xaml", "GridViewGameOverview.xaml") {
  $path = Join-Path $theme "Views/$view"
  if (-not (Test-Path $path)) { Write-Warning "$view not in the base theme"; continue }
  $text = Get-Content -Raw -Encoding UTF8 $path
  if ($text -notmatch "<StackPanel\s+Name=""PART_ElemDescription""") { throw "$view has no PART_ElemDescription to place the Blastlog panel by" }
  $text = [regex]::Replace($text, "(<StackPanel\s+Name=""PART_ElemDescription"")", "$elements`r`n`${1}", 1)
  [IO.File]::WriteAllText($path, $text)
}

# 4. Its own manifest, the credit and Playnite's licence.
$baseYaml = Get-Content -Raw (Join-Path $base "theme.yaml")
$api = ([regex]::Match($baseYaml, "ThemeApiVersion:\s*(\S+)")).Groups[1].Value
if (-not $api) { throw "No ThemeApiVersion in Playnite's theme.yaml" }
@"
Id: Blastlog_Desktop_Theme
Name: Blastlog
Author: Blastlog
Version: $Version
Mode: Desktop
ThemeApiVersion: $api
Website: https://blastlog.games
"@ | Set-Content -Encoding UTF8 (Join-Path $theme "theme.yaml")
Copy-Item (Join-Path $root "theme/CREDITS.md") (Join-Path $theme "CREDITS.md")
Copy-Item (Join-Path $repo "LICENSE.md") (Join-Path $theme "PLAYNITE-LICENSE.md")

# 5. A .pthm is a zip with theme.yaml at its root.
$pthm = Join-Path $root "$Out/Blastlog_Desktop_Theme_$Version.pthm"
if (Test-Path $pthm) { Remove-Item $pthm }
$zip = "$pthm.zip"; Compress-Archive -Path (Join-Path $theme "*") -DestinationPath $zip -Force; Move-Item $zip $pthm -Force
Write-Host "Theme: $pthm (Playnite $PlayniteRef, ThemeApiVersion $api)"
