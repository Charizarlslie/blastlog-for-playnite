using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Blastlog.Playnite.Ui;
using Newtonsoft.Json.Linq;
using Playnite.SDK;
using BlastlogGame = Blastlog.Sync.Core.Game;

namespace Blastlog.Playnite.Screens {
/// <summary>
/// Pieces the dashboard's cards share: the site's text rules (lengths, platforms, stores, dates) ported from Blastlog's
/// web and domain code, game links, Play / Install, and small controls the kit doesn't have yet.
/// </summary>
static class Dash {
  static readonly ILogger Log = LogManager.GetLogger();

  public const string Clock = "<circle cx=\"12\" cy=\"12\" r=\"10\" /><polyline points=\"12 6 12 12 16 14\" />";
  public const string Eye = "<path d=\"M2.062 12.348a1 1 0 0 1 0-.696 10.75 10.75 0 0 1 19.876 0 1 1 0 0 1 0 .696 10.75 10.75 0 0 1-19.876 0\" /><circle cx=\"12\" cy=\"12\" r=\"3\" />";
  public const string Pencil = "<path d=\"M21.174 6.812a1 1 0 0 0-3.986-3.987L3.842 16.174a2 2 0 0 0-.5.83l-1.321 4.352a.5.5 0 0 0 .623.622l4.353-1.32a2 2 0 0 0 .83-.497z\" /><path d=\"m15 5 4 4\" />";
  public const string Archive = "<rect x=\"3\" y=\"4\" width=\"18\" height=\"5\" rx=\"1\" /><path d=\"M5 9v10a1 1 0 0 0 1 1h12a1 1 0 0 0 1-1V9\" /><path d=\"M10 13h4\" />";
  public const string Search = "<circle cx=\"11\" cy=\"11\" r=\"8\" /><line x1=\"21\" y1=\"21\" x2=\"16.65\" y2=\"16.65\" />";
  public const string Play = "<polygon points=\"6 3 20 12 6 21 6 3\" />";
  public const string Download = "<path d=\"M12 15V3\" /><path d=\"M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4\" /><path d=\"m7 10 5 5 5-5\" />";

  // ---- Data ----
  /// <summary>A site game DTO (or an entry's game) as the sync core's Game, for covers and game colour.</summary>
  public static BlastlogGame Game(JToken game) {
    if (game == null || game.Type != JTokenType.Object) return null;
    try { return game.ToObject<BlastlogGame>(); } catch (Exception) { return new BlastlogGame { Id = (string)game["id"], Title = (string)game["title"], CoverUrl = (string)game["coverUrl"] }; }
  }
  /// <summary>A game known only by its cover URL (search results, friends' activity).</summary>
  public static BlastlogGame GameFromCover(string id, string title, string coverUrl) => new BlastlogGame { Id = id, Title = title, CoverUrl = coverUrl };
  public static string Str(JToken t, string key) => t?.Type == JTokenType.Object ? (string)t[key] : null;
  public static JArray Arr(JToken t) => t as JArray ?? new JArray();

  static readonly Dictionary<string, string> LevelLabel = new Dictionary<string, string> { ["main_story"] = "main story", ["main_plus_extra"] = "main + extras", ["completionist"] = "completionist" };
  public static string Level(string level) => level != null && LevelLabel.TryGetValue(level, out var l) ? l : "main + extras";
  /// <summary>"~9h", "~9.5h", "No end", "&lt;0.5h", with the level when it isn't the player's (domain game-length.ts lengthText).</summary>
  public static string LengthText(JToken length) {
    if (length == null || length.Type != JTokenType.Object) return null;
    if ((bool?)length["noEnd"] == true) return "No end";
    var raw = (double?)length["hours"]; if (raw == null) return null;
    var hours = raw.Value < 10 ? Math.Round(raw.Value * 2, MidpointRounding.AwayFromZero) / 2 : Math.Round(raw.Value, MidpointRounding.AwayFromZero);
    var text = hours == 0 ? "<0.5h" : "~" + hours.ToString("0.#", CultureInfo.InvariantCulture) + "h";
    var from = (string)length["fromLevel"]; var level = (string)length["level"];
    if (from != null && level != null && from != level) text += " " + Level(from);
    if ((bool?)length["estimate"] == true) text += " (estimate)";
    return text;
  }
  public static double? Hours(JToken length) => length?.Type == JTokenType.Object && (bool?)length["noEnd"] != true ? (double?)length["hours"] : null;

  // Platforms (contracts platforms.ts DEFAULT_PLATFORM_RULES): label, family, show. Unlisted ones show as a fallback.
  /// <summary>A platform rule: show 0 = always, 1 = fallback, 2 = never; Rank is its place in the display order.</summary>
  sealed class PlatformRule { public string Name, Label, Family; public int Show, Rank; }
  static readonly PlatformRule[] PlatformRules = BuildRules();
  static PlatformRule[] BuildRules() {
    var list = new List<PlatformRule>();
    void Add(int show, params string[][] rows) { foreach (var r in rows) list.Add(new PlatformRule { Name = r[0], Label = r[1], Family = r.Length > 2 ? r[2] : r[1], Show = show, Rank = list.Count }); }
    Add(0, new[] { "PC (Microsoft Windows)", "PC" }, new[] { "PlayStation 5", "PS5", "PlayStation" }, new[] { "PlayStation 4", "PS4", "PlayStation" },
      new[] { "Xbox Series X|S", "Xbox Series", "Xbox" }, new[] { "Xbox One", "Xbox One", "Xbox" }, new[] { "Nintendo Switch 2", "Switch 2", "Switch" },
      new[] { "Nintendo Switch", "Switch", "Switch" }, new[] { "DOS", "DOS", "PC" }, new[] { "Nintendo Entertainment System", "NES" },
      new[] { "Family Computer", "NES", "NES" }, new[] { "Family Computer Disk System", "NES", "NES" }, new[] { "Sega Master System/Mark III", "Master System" },
      new[] { "Game Boy", "Game Boy" }, new[] { "Game Boy Color", "Game Boy Color", "Game Boy" }, new[] { "Sega Mega Drive/Genesis", "Genesis" },
      new[] { "Super Nintendo Entertainment System", "SNES" }, new[] { "Super Famicom", "SNES", "SNES" }, new[] { "PlayStation", "PS1", "PlayStation" },
      new[] { "Sega Saturn", "Saturn" }, new[] { "Nintendo 64", "N64" }, new[] { "Game Boy Advance", "GBA" }, new[] { "Dreamcast", "Dreamcast" },
      new[] { "PlayStation 2", "PS2", "PlayStation" }, new[] { "Nintendo GameCube", "GameCube" }, new[] { "Xbox", "Original Xbox", "Xbox" },
      new[] { "Nintendo DS", "DS" }, new[] { "Nintendo DSi", "DS", "DS" }, new[] { "PlayStation Portable", "PSP", "PlayStation" },
      new[] { "PlayStation 3", "PS3", "PlayStation" }, new[] { "Xbox 360", "Xbox 360", "Xbox" }, new[] { "Wii", "Wii" }, new[] { "Nintendo 3DS", "3DS" },
      new[] { "New Nintendo 3DS", "3DS", "3DS" }, new[] { "PlayStation Vita", "PS Vita", "PlayStation" }, new[] { "Wii U", "Wii U" });
    Add(1, new[] { "iOS", "iOS" }, new[] { "Android", "Android" }, new[] { "Mac", "Mac" }, new[] { "Meta Quest 3", "Quest 3", "Quest" },
      new[] { "Meta Quest 2", "Quest 2", "Quest" }, new[] { "Oculus Quest", "Quest", "Quest" }, new[] { "PlayStation VR2", "PS VR2", "PS VR" },
      new[] { "PlayStation VR", "PS VR", "PS VR" }, new[] { "SteamVR", "SteamVR" }, new[] { "Web browser", "Browser" }, new[] { "Arcade", "Arcade" });
    Add(2, new[] { "Linux", "Linux" }, new[] { "Google Stadia", "Stadia" }, new[] { "64DD", "64DD" }, new[] { "Satellaview", "Satellaview" },
      new[] { "Ouya", "Ouya" }, new[] { "OnLive Game System", "OnLive" }, new[] { "Legacy Mobile Device", "Old phones" }, new[] { "Windows Phone", "Windows Phone" });
    return list.ToArray();
  }
  static PlatformRule Rule(string name) =>
    PlatformRules.FirstOrDefault(r => string.Equals(r.Name, name.Trim(), StringComparison.OrdinalIgnoreCase))
    ?? new PlatformRule { Name = name, Label = name, Family = name, Show = 1, Rank = PlatformRules.Length };
  /// <summary>"PC · PS5 · Xbox Series", at most four chips plus "+N" (contracts platforms.ts platformLine).</summary>
  public static string PlatformLine(IEnumerable<string> platforms, int limit = 4) {
    var found = platforms.Where(p => !string.IsNullOrWhiteSpace(p)).Distinct().Select(Rule).OrderBy(r => r.Rank).ThenBy(r => r.Name, StringComparer.Ordinal).ToList();
    if (found.Count == 0) return "";
    var always = found.Where(r => r.Show == 0).ToList(); var fallback = found.Where(r => r.Show == 1).ToList();
    var pool = always.Count > 0 ? always : fallback.Count > 0 ? fallback : found.Take(1).ToList();
    var families = new List<KeyValuePair<string, HashSet<string>>>();
    foreach (var r in pool) {
      var key = r.Family.ToLowerInvariant(); var f = families.FirstOrDefault(x => x.Key == key);
      if (f.Key == null) { f = new KeyValuePair<string, HashSet<string>>(key, new HashSet<string>()); families.Add(f); }
      f.Value.Add(r.Label);
    }
    var chips = families.Select(f => f.Value.Count == 1 ? f.Value.First() : pool.First(r => r.Family.ToLowerInvariant() == f.Key).Family).ToList();
    return string.Join(" · ", chips.Take(limit)) + (chips.Count > limit ? $" +{chips.Count - limit}" : "");
  }
  public static IEnumerable<string> OrderedPlatforms(IEnumerable<string> platforms) => platforms.Distinct().Select(Rule).OrderBy(r => r.Rank).ThenBy(r => r.Name).Select(r => r.Name);

  /// <summary>Where you own it: the store's name, a plain line, and its page when it has one (domain owned-stores.ts storePlace).</summary>
  public sealed class Place { public string Name, Where, Url; public Place(string name, string where, string url) { Name = name; Where = where; Url = url; } }
  public static Place StorePlace(string store, string storeId) {
    switch (store) {
      case "steam": return storeId != null && System.Text.RegularExpressions.Regex.IsMatch(storeId, "^\\d{1,12}$")
        ? new Place("Steam", "Play or install it from its Steam page.", $"https://store.steampowered.com/app/{storeId}/")
        : new Place("Steam", "It's in your Steam library.", "https://steamcommunity.com/my/games/?tab=all");
      case "gog": return new Place("GOG", "Download it from your GOG library, or install it in GOG Galaxy.", "https://www.gog.com/en/account");
      case "epic": return new Place("Epic Games", "Install it from Library in the Epic Games Launcher.", null);
      case "playstation": return new Place("PlayStation", "Download it from the Game Library on your PlayStation.", null);
      case "xbox": return new Place("Xbox", "Install it from My games & apps on your Xbox, or the Xbox app on a PC.", null);
      case "nintendo": return new Place("Nintendo", "Redownload it from the Nintendo eShop on your Switch.", null);
      case "physical": return new Place("Disc or cartridge", "You have it on a disc or cartridge.", null);
      case "playnite": return new Place("Playnite", "It's in your Playnite library.", null);
      default: return new Place("Somewhere else", "You own it somewhere else.", null);
    }
  }

  static readonly Dictionary<string, string> Statuses = new Dictionary<string, string> { ["done"] = "Done", ["playing"] = "Playing", ["started"] = "Started",
    ["not_started"] = "Not started", ["set_aside"] = "Set aside", ["quit"] = "Quit", ["dnf"] = "Didn't finish", ["unplayable"] = "Unplayable" };
  public static string StatusWord(string status) => status != null && Statuses.TryGetValue(status, out var w) ? w : (status ?? "").Replace('_', ' ');

  // ---- Time: always Pacific ----
  static readonly TimeZoneInfo PacificZone = FindPacific();
  static TimeZoneInfo FindPacific() { try { return TimeZoneInfo.FindSystemTimeZoneById("Pacific Standard Time"); } catch (Exception) { return TimeZoneInfo.Local; } }
  public static DateTime Pacific(DateTimeOffset at) => TimeZoneInfo.ConvertTime(at, PacificZone).DateTime;
  public static DateTime PacificNow => Pacific(DateTimeOffset.Now);
  /// <summary>"1:53 PM" today, else "Nov 3, 1:53 PM" (Pacific).</summary>
  public static string Clock12(DateTimeOffset at) { var p = Pacific(at); return (p.Date == PacificNow.Date ? "" : p.ToString("MMM d, ", CultureInfo.InvariantCulture)) + p.ToString("h:mm tt", CultureInfo.InvariantCulture); }
  /// <summary>A calendar day ("2026-10-21") as "Oct 21", with the year when it isn't this year.</summary>
  public static string ShortDate(string day) {
    if (!DateTime.TryParseExact(day ?? "", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)) return day ?? "";
    return d.ToString(d.Year == PacificNow.Year ? "MMM d" : "MMM d, yyyy", CultureInfo.InvariantCulture);
  }
  /// <summary>Today's date in Pacific, for "finished on".</summary>
  public static string Today => PacificNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
  static string Plural(int n, string word) => $"{n} {word}{(n == 1 ? "" : "s")}";
  /// <summary>"3 days", "2 weeks", "4 months", "over a year" (NowPlaying.tsx span).</summary>
  public static string Span(int days) {
    if (days < 14) return Plural(Math.Max(1, days), "day");
    if (days < 60) return Plural((int)Math.Round(days / 7.0, MidpointRounding.AwayFromZero), "week");
    var months = (int)Math.Round(days / 30.4, MidpointRounding.AwayFromZero);
    return months < 12 ? Plural(months, "month") : "over a year";
  }
  /// <summary>"Just now", "3h ago", "Yesterday", "Mon", then "Nov 3" (FriendActivity.tsx activityWhen), in Pacific.</summary>
  public static string When(DateTimeOffset at) {
    var minutes = (int)Math.Floor((DateTimeOffset.Now - at).TotalMinutes);
    if (minutes < 60) return minutes < 2 ? "Just now" : $"{minutes}m ago";
    if (minutes < 12 * 60) return $"{minutes / 60}h ago";
    var when = Pacific(at); var days = (int)Math.Round((PacificNow.Date - when.Date).TotalDays);
    if (days <= 0) return $"{minutes / 60}h ago";
    if (days == 1) return "Yesterday";
    if (days < 7) return when.ToString("ddd", CultureInfo.InvariantCulture);
    return when.ToString(when.Year == PacificNow.Year ? "MMM d" : "MMM d, yyyy", CultureInfo.InvariantCulture);
  }

  // ---- Going places ----
  public static void Site(BlastlogPlugin plugin, string path) => Nav.Site(plugin, path);
  /// <summary>A game's page: Playnite's own (selected in the library) when it's paired, else Blastlog's.</summary>
  public static void OpenEntry(BlastlogPlugin plugin, string entryId) => Nav.Entry(plugin, entryId);
  /// <summary>Rate and review (its questions are what teach Blastlog).</summary>
  public static void Review(BlastlogPlugin plugin, string entryId) => Nav.Review(plugin, entryId);
  public static void DiscoverGame(BlastlogPlugin plugin, string gameId) => Nav.DiscoverGame(plugin, gameId);
  /// <summary>The Library screen with the site's filters (Nav.Library).</summary>
  public static void Library(BlastlogPlugin plugin, string query) => Nav.Library(plugin, query);

  /// <summary>
  /// Playnite adds (PLAYNITE_DESIGN 4.1): Play when the paired game is installed, Install when it isn't; null when the
  /// entry isn't in Playnite.
  /// </summary>
  public static Button PlayButton(BlastlogPlugin plugin, string entryId, string style = "Blastlog.Primary") {
    var game = entryId != null ? plugin.GameFor(entryId) : null;
    if (game == null) return null;
    var installed = game.IsInstalled;
    var b = Kit.Button(installed ? "Play" : "Install", () => { if (installed) plugin.Api.StartGame(game.Id); else plugin.Api.InstallGame(game.Id); }, style);
    b.Content = IconLabel(installed ? Play : Download, installed ? "Play" : "Install");
    AutomationName(b, (installed ? "Play " : "Install ") + game.Name);
    return b;
  }
  public static void AutomationName(DependencyObject d, string name) => System.Windows.Automation.AutomationProperties.SetName(d, name);

  // ---- Controls ----
  public static StackPanel Row(double gap = 0, params UIElement[] children) {
    var p = new StackPanel { Orientation = Orientation.Horizontal };
    foreach (var c in children.Where(c => c != null)) { if (gap > 0 && c is FrameworkElement f && p.Children.Count > 0) f.Margin = new Thickness(gap, f.Margin.Top, f.Margin.Right, f.Margin.Bottom); p.Children.Add(c); }
    return p;
  }
  public static WrapPanel Wrap(params UIElement[] children) { var w = new WrapPanel(); foreach (var c in children.Where(c => c != null)) w.Children.Add(c); return w; }
  public static T Margin<T>(this T e, double l, double t, double r, double b) where T : FrameworkElement { e.Margin = new Thickness(l, t, r, b); return e; }
  public static TextBlock Text(string text, string style = "Blastlog.Body", string brush = null, FontWeight? weight = null, double? size = null) {
    var t = Kit.Text(text, style);
    if (brush != null) t.SetResourceReference(TextBlock.ForegroundProperty, brush);
    if (weight != null) t.FontWeight = weight.Value;
    if (size != null) t.FontSize = size.Value;
    return t;
  }
  /// <summary>An icon and a word, for button content (stroke follows the button's foreground).</summary>
  public static StackPanel IconLabel(string icon, string label, double size = 16) {
    var i = Icons.Make(icon, size); i.Margin = new Thickness(0, 0, 8, 0); i.VerticalAlignment = VerticalAlignment.Center;
    var t = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center };
    return new StackPanel { Orientation = Orientation.Horizontal, Children = { i, t } };
  }
  /// <summary>A chip with an icon (the hero's length, platforms and stores chips).</summary>
  public static Border IconChip(string icon, UIElement content, string tooltip = null) {
    var i = Icons.Make(icon, 15, null); i.Margin = new Thickness(0, 0, 7, 0); i.VerticalAlignment = VerticalAlignment.Center;
    ((Shape)((Canvas)((Viewbox)i).Child).Children[0]).SetResourceReference(Shape.StrokeProperty, "Blastlog.Text");
    var chip = new Border { Child = new StackPanel { Orientation = Orientation.Horizontal, Children = { i, content } }, CornerRadius = new CornerRadius(999), Padding = new Thickness(12, 6, 14, 6),
      Margin = new Thickness(0, 0, 8, 8), BorderThickness = new Thickness(1) };
    chip.SetResourceReference(Border.BackgroundProperty, "Blastlog.Surface2"); chip.SetResourceReference(Border.BorderBrushProperty, "Blastlog.Line");
    if (tooltip != null) chip.ToolTip = tooltip;
    return chip;
  }
  /// <summary>A count in a small pill (Currently playing's and the shelf's counts).</summary>
  public static Border Badge(int count) {
    var t = Text(count.ToString(CultureInfo.InvariantCulture), "Blastlog.Small", "Blastlog.OnGame", FontWeights.Bold);
    var b = new Border { Child = t, CornerRadius = new CornerRadius(999), Padding = new Thickness(9, 1, 9, 2), Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
    b.SetResourceReference(Border.BackgroundProperty, "Blastlog.Game");
    return b;
  }
  /// <summary>A 44px link pill in the game's colour that opens a web page (Guides ↗, Reddit ↗).</summary>
  public static Button LinkPill(string label, string url, string accessibleName) {
    var b = Kit.Button(label + "  ↗", () => UiKit.Open(url), "Blastlog.Ghost");
    b.SetResourceReference(Control.ForegroundProperty, "Blastlog.GameText"); b.SetResourceReference(Control.BorderBrushProperty, "Blastlog.GameEdge");
    b.SetResourceReference(Control.BackgroundProperty, "Blastlog.GameTint2");
    b.MinHeight = 44; b.Padding = new Thickness(16, 0, 16, 0); b.FontSize = 14;
    AutomationName(b, accessibleName);
    return b;
  }
  /// <summary>A text link ("Open library →"), in the page's game colour.</summary>
  public static Button Link(string label, Action onClick) {
    var b = Kit.Button(label, onClick, "Blastlog.TextButton");
    b.SetResourceReference(Control.ForegroundProperty, "Blastlog.GameText");
    b.Margin = new Thickness(0); b.Padding = new Thickness(6, 0, 6, 0);
    return b;
  }
  public static Border Card(UIElement child, double padding = 22) { var c = Kit.Card(child); c.Padding = new Thickness(padding); return c; }
  /// <summary>Lets a click anywhere on an element act, except on buttons inside it (the whole-card click).</summary>
  public static void OnClick(UIElement element, Action act) {
    element.MouseLeftButtonUp += (s, e) => {
      try {
        for (var d = e.OriginalSource as DependencyObject; d != null && d != element; d = d is Visual || d is System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d))
          if (d is ButtonBase || d is TextBoxBase) return;
        act();
      } catch (Exception ex) { Log.Error(ex, "Blastlog dashboard click"); }
    };
    if (element is FrameworkElement f) f.Cursor = Cursors.Hand;
  }

  /// <summary>A borderless text box for the kit's field look (Playnite's theme templates TextBox its own way).</summary>
  public static TextBox Field() {
    var box = new TextBox { Template = (ControlTemplate)XamlReader.Parse("<ControlTemplate xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" TargetType=\"TextBox\"><ScrollViewer x:Name=\"PART_ContentHost\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" Focusable=\"False\" HorizontalScrollBarVisibility=\"Hidden\" VerticalScrollBarVisibility=\"Hidden\" /></ControlTemplate>"),
      Background = Brushes.Transparent, BorderThickness = new Thickness(0), FontSize = 16, VerticalContentAlignment = VerticalAlignment.Center };
    box.SetResourceReference(Control.FontFamilyProperty, "Blastlog.Font"); box.SetResourceReference(Control.ForegroundProperty, "Blastlog.Text");
    box.SetResourceReference(TextBoxBase.CaretBrushProperty, "Blastlog.Text");
    return box;
  }
  /// <summary>A search-style field: icon, text box and placeholder in a 46px rounded box with the edge colour.</summary>
  public static Border FieldBox(TextBox box, string placeholder, string icon = Search) {
    var hint = Text(placeholder, "Blastlog.Muted"); hint.IsHitTestVisible = false; hint.VerticalAlignment = VerticalAlignment.Center; hint.TextWrapping = TextWrapping.NoWrap;
    box.TextChanged += (s, e) => hint.Visibility = string.IsNullOrEmpty(box.Text) ? Visibility.Visible : Visibility.Collapsed;
    var grid = new Grid();
    grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); grid.ColumnDefinitions.Add(new ColumnDefinition());
    var i = Icons.Make(icon, 18, null); i.Margin = new Thickness(0, 0, 10, 0); i.VerticalAlignment = VerticalAlignment.Center;
    ((Shape)((Canvas)((Viewbox)i).Child).Children[0]).SetResourceReference(Shape.StrokeProperty, "Blastlog.TextMuted");
    grid.Children.Add(i);
    Grid.SetColumn(hint, 1); Grid.SetColumn(box, 1); grid.Children.Add(hint); grid.Children.Add(box);
    var border = new Border { Child = grid, CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(1), Padding = new Thickness(14, 0, 14, 0), Height = 46 };
    border.SetResourceReference(Border.BackgroundProperty, "Blastlog.Surface"); border.SetResourceReference(Border.BorderBrushProperty, "Blastlog.Edge");
    border.MouseLeftButtonDown += (s, e) => box.Focus();
    return border;
  }

  /// <summary>A Blastlog image, cached on disk, as a shape's fill (UniformToFill) when it arrives.</summary>
  public static async void LoadFill(BlastlogPlugin plugin, string url, Shape target, int decodeWidth) {
    if (url == null) return;
    try {
      var file = await plugin.Theme.CachedFile(url);
      if (file == null) return;
      var image = new BitmapImage(); image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad; image.UriSource = new Uri(file);
      image.DecodePixelWidth = decodeWidth; image.EndInit(); image.Freeze();
      target.Fill = new ImageBrush(image) { Stretch = Stretch.UniformToFill };
    } catch (Exception e) { Log.Warn(e, "Blastlog: couldn't show " + url); }
  }

  /// <summary>The site's star split shades: brightest on the most-given star, fading by distance (DashboardShelf.tsx starShades).</summary>
  public static double[] StarShades(int[] counts, out int peak) {
    var fade = new[] { 1, .68, .48, .34, .25 };
    var max = counts.Max(); peak = Array.IndexOf(counts, max) + 1; var p = peak;
    return counts.Select((_, i) => fade[Math.Abs(i + 1 - p)]).ToArray();
  }

  /// <summary>Runs work from a UI event without letting an exception escape (a plugin exception takes Playnite down).</summary>
  public static async void Safe(Func<Task> work, string what) { try { await work(); } catch (Exception e) { Log.Error(e, "Blastlog dashboard: " + what); } }
}
}
