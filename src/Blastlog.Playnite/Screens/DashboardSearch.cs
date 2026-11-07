using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;
using Blastlog.Playnite.Ui;
using Blastlog.Sync.Core.Design;
using Newtonsoft.Json.Linq;
using Playnite.SDK;

namespace Blastlog.Playnite.Screens {
/// <summary>
/// The dashboard's search (design.md 8 Dashboard; web DashboardSearch.tsx): as you type, your games, games in friends'
/// libraries (whose, and their status or stars) and any other game Blastlog has read ("See how it fits you"). Your own
/// open their game page (Playnite's when paired); the rest open on Blastlog. Enter searches your library on Blastlog.
/// </summary>
sealed class DashboardSearch : Grid {
  static readonly ILogger Log = LogManager.GetLogger();
  readonly BlastlogPlugin plugin;
  readonly TextBox box = Dash.Field();
  readonly Popup popup;
  readonly StackPanel results = new StackPanel();
  readonly DispatcherTimer debounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
  string asked = ""; int askId;

  public DashboardSearch(BlastlogPlugin plugin) {
    this.plugin = plugin;
    Dash.AutomationName(box, "Search games: yours, your friends' and any other");
    var field = Dash.FieldBox(box, "Search any game");
    Children.Add(field);
    var panel = new Border { Child = new ScrollViewer { Content = results, MaxHeight = 520, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }, Padding = new Thickness(8),
      CornerRadius = new CornerRadius(16), BorderThickness = new Thickness(1), MinWidth = 420 };
    panel.SetResourceReference(Border.BackgroundProperty, "Blastlog.Surface"); panel.SetResourceReference(Border.BorderBrushProperty, "Blastlog.Line");
    panel.Resources.MergedDictionaries.Add(Kit.Styles);
    popup = new Popup { Child = panel, PlacementTarget = field, Placement = PlacementMode.Bottom, StaysOpen = false, AllowsTransparency = true, VerticalOffset = 6 };
    Children.Add(popup);
    box.TextChanged += (s, e) => { try { debounce.Stop(); debounce.Start(); } catch (Exception) { } };
    debounce.Tick += (s, e) => { debounce.Stop(); Dash.Safe(Ask, "search"); };
    box.GotKeyboardFocus += (s, e) => { try { if (asked.Length >= 2) popup.IsOpen = true; } catch (Exception) { } };
    box.PreviewKeyDown += (s, e) => {
      try {
        if (e.Key == Key.Escape) { popup.IsOpen = false; e.Handled = true; }
        else if (e.Key == Key.Enter) { popup.IsOpen = false; Dash.Library(plugin, "search=" + Uri.EscapeDataString(box.Text.Trim())); e.Handled = true; }
        else if (e.Key == Key.Down && popup.IsOpen) { results.Children.OfType<FrameworkElement>().SelectMany(Buttons).FirstOrDefault()?.Focus(); e.Handled = true; }
      } catch (Exception ex) { Log.Error(ex, "Blastlog search keys"); }
    };
  }
  static System.Collections.Generic.IEnumerable<Button> Buttons(FrameworkElement e) => e is Button b ? new[] { b } : e is Panel p ? p.Children.OfType<FrameworkElement>().SelectMany(Buttons) : Enumerable.Empty<Button>();

  async System.Threading.Tasks.Task Ask() {
    var words = box.Text.Trim(); asked = words; var id = ++askId;
    if (words.Length < 2) { popup.IsOpen = false; return; }
    results.Children.Clear(); results.Children.Add(Dash.Text("Searching…", "Blastlog.Muted").Margin(10, 8, 10, 8));
    popup.IsOpen = true;
    var data = (await plugin.Mirror("search?q=" + Uri.EscapeDataString(words), fresh: false)).Value;
    if (id != askId) return;
    results.Children.Clear();
    if (data == null) { results.Children.Add(Dash.Text("Search isn't working right now. Press Enter to search your library.", "Blastlog.Muted").Margin(10, 8, 10, 8)); return; }
    var mine = Dash.Arr(data["mine"]).OfType<JObject>().ToList(); var friends = Dash.Arr(data["friends"]).OfType<JObject>().ToList(); var others = Dash.Arr(data["others"]).OfType<JObject>().ToList();
    if (mine.Count + friends.Count + others.Count == 0) { results.Children.Add(Dash.Text($"Nothing matches “{words}”. Press Enter to search your library's reviews too.", "Blastlog.Muted").Margin(10, 8, 10, 8)); return; }
    if (mine.Count > 0) Section("In your library", mine.Select(m => Result(m, string.Join(" · ", new[] { (string)m["year"], Where(m) }.Where(x => !string.IsNullOrEmpty(x))),
      () => Dash.OpenEntry(plugin, (string)m["entryId"]))));
    if (friends.Count > 0) Section("In friends' libraries", friends.Select(f => {
      var people = Dash.Arr(f["people"]).OfType<JObject>().ToList();
      var line = string.Join(" · ", people.Take(3).Select(p => $"{(string)p["name"]} {Where(p)}")) + (people.Count > 3 ? $" · +{people.Count - 3} more" : "");
      return Result(f, line, () => Dash.DiscoverGame(plugin, (string)f["gameId"]));
    }));
    if (others.Count > 0) Section("More games", others.Select(o => Result(o, string.Join(" · ", new[] { (string)o["year"], "See how it fits you" }.Where(x => !string.IsNullOrEmpty(x))),
      () => Dash.DiscoverGame(plugin, (string)o["gameId"]))));
  }
  static string Where(JObject p) {
    var rating = (double?)p["rating"];
    return rating != null && rating > 0 ? rating.Value.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + "★" : (string)p["ownership"] == "wishlist" ? "Wishlist" : Dash.StatusWord((string)p["status"]);
  }
  void Section(string title, System.Collections.Generic.IEnumerable<FrameworkElement> rows) {
    results.Children.Add(Dash.Text(title, "Blastlog.Small", "Blastlog.GameText", FontWeights.Bold).Margin(10, 8, 10, 4));
    foreach (var r in rows) results.Children.Add(r);
  }
  FrameworkElement Result(JObject item, string line, Action open) {
    var game = Dash.GameFromCover((string)item["gameId"], (string)item["title"], (string)item["coverUrl"]);
    var cover = Kit.Cover(plugin, game, 36, ArtworkContext.Thumbnail, 6); cover.Margin = new Thickness(0, 0, 12, 0);
    var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Children = { Dash.Text((string)item["title"], "Blastlog.Body", weight: FontWeights.SemiBold), Dash.Text(line, "Blastlog.Small") } };
    var content = new DockPanel { Children = { cover, text } }; DockPanel.SetDock(cover, Dock.Left);
    var b = new Button { Content = content, Style = Kit.Style("Blastlog.TextButton"), HorizontalContentAlignment = HorizontalAlignment.Stretch, Padding = new Thickness(10, 6, 10, 6), Margin = new Thickness(0), MinHeight = 56 };
    b.SetResourceReference(Control.ForegroundProperty, "Blastlog.Text");
    // The kit's button template centres its content; results read left to right.
    content.Width = 380;
    b.Click += (s, e) => { try { popup.IsOpen = false; open(); } catch (Exception ex) { Log.Error(ex, "Blastlog search result"); } };
    Dash.AutomationName(b, $"{(string)item["title"]}, {line}");
    return b;
  }
}
}
