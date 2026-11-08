using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Blastlog.Playnite.Ui;
using Blastlog.Sync.Core.Design;
using Newtonsoft.Json.Linq;
using static Blastlog.Playnite.Screens.TasteUi;

namespace Blastlog.Playnite.Screens {
/// <summary>
/// Every game with one trait (apps/web Similar.tsx GamesWithTrait, /traits/:traitId), natively, in its own window: Back,
/// "Games with" and the trait's name and definition, then In your library (not played yet first), Your friends' games and
/// From Discover, each best match first, each tile wearing its own game. A game in the player's library opens its own page
/// (Playnite's when paired), any other its Discover page. Reads GET /sync/v1/traits/:traitId (the site's own answer).
/// The page wears Blastlog green, as the site's does (it sets no page colour).
/// </summary>
static class TraitPage {
  /// <summary>Opens the page. <paramref name="from"/>: what it was opened from ("Hades"), for the Back button.</summary>
  public static void Open(BlastlogPlugin plugin, string traitId, string from = null) =>
    LinkPages.Show(plugin, "Games with a trait", close => new View(plugin, traitId, from, close), width: 1180, height: 900);

  /// <summary>
  /// The site shows every tile; a common trait can have a few hundred in the library, so each group shows this many at
  /// first and "Show N more" adds the rest (covers load four at a time, top first).
  /// </summary>
  const int FirstShown = 24;

  sealed class View : StackPanel {
    readonly BlastlogPlugin plugin; readonly string traitId, from; readonly Action close;
    readonly StackPanel body = new StackPanel();
    JObject page; bool loaded;

    public View(BlastlogPlugin plugin, string traitId, string from, Action close) {
      this.plugin = plugin; this.traitId = traitId; this.from = from; this.close = close;
      MaxWidth = 1180; HorizontalAlignment = HorizontalAlignment.Left;
      Kit.GameScope(this, GameColour.BrandGreen);
      Children.Add(Fingerprint.LinkButton(from != null ? "← Back to " + from : "← Back", close, game: true)
        .Also(b => { b.Margin = new Thickness(-10, 0, 0, 10); b.HorizontalAlignment = HorizontalAlignment.Left; }));
      Children.Add(body);
      body.Children.Add(T("Finding games with it…", "Blastlog.Muted"));
      Loaded += (s, e) => { if (!loaded) { loaded = true; Run(() => Load(false), "trait page"); } };
    }

    async Task Load(bool fresh) {
      if (!plugin.Configured) { Show(T("Add your Blastlog sync token in Add-ons, Extension settings, Blastlog to see this trait.", "Blastlog.Muted")); return; }
      page = (await plugin.Mirror("traits/" + Uri.EscapeDataString(traitId), fresh)).Value;
      if (!(page?["trait"] is JObject)) { Unavailable(); return; }
      Build();
    }

    void Show(UIElement content) { body.Children.Clear(); body.Children.Add(content); }

    void Unavailable() {
      var panel = Stack(null, T("That trait couldn’t be opened.", "Blastlog.Body", "Blastlog.Danger"));
      panel.Children.Add(Kit.Button("Try again", async () => await Load(true), "Blastlog.Ghost").Also(b => { b.HorizontalAlignment = HorizontalAlignment.Left; b.Margin = new Thickness(0, 12, 0, 0); }));
      Show(panel);
    }

    void Build() {
      body.Children.Clear();
      var trait = (JObject)page["trait"];
      var label = trait.S("label") ?? "this trait";
      var window = Window.GetWindow(this); if (window != null) window.Title = "Games with " + label;
      var head = Stack(new Thickness(0, 0, 0, 22),
        T("Games with", "Blastlog.Small", "Blastlog.GameText", 0, FontWeights.Bold),
        T(label, "Blastlog.PageTitle").Also(t => t.Margin = new Thickness(0, 2, 0, 0)));
      var definition = trait.S("definition");
      if (!string.IsNullOrWhiteSpace(definition)) head.Children.Add(T(definition, "Blastlog.Muted").Also(t => { t.Margin = new Thickness(0, 8, 0, 0); t.MaxWidth = 720; t.HorizontalAlignment = HorizontalAlignment.Left; }));
      body.Children.Add(head);

      var library = page.Objects("library"); var friends = page.Objects("friends"); var discover = page.Objects("discover");
      if (library.Count == 0 && friends.Count == 0 && discover.Count == 0)
        body.Children.Add(T("No other games here have it yet.", "Blastlog.Muted"));
      Group("library", "In your library", library, "Not played yet first, best match first.");
      Group("friends", "Your friends’ games", friends, "Games your friends have that you don’t.");
      Group("discover", "From Discover", discover, "Games nobody here has yet, after Blastlog’s careful check.");
      body.Children.Add(T(plugin.CacheLine(), "Blastlog.Small").Also(t => t.Margin = new Thickness(0, 8, 0, 0)));
    }

    /// <summary>One group (the site's .similar section): heading with its count, the line under it, the tiles.</summary>
    void Group(string key, string title, List<JObject> items, string note) {
      if (items.Count == 0) return;
      var section = new StackPanel { Margin = new Thickness(0, 6, 0, 18) };
      var heading = T(title, "Blastlog.Section");
      heading.Inlines.Add(new System.Windows.Documents.Run($" ({items.Count})") { FontSize = 14, FontWeight = FontWeights.Normal }
        .Also(r => r.SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty, "Blastlog.TextMuted")));
      section.Children.Add(heading);
      section.Children.Add(T(note, "Blastlog.Muted").Also(t => { t.FontSize = 14; t.Margin = new Thickness(0, 4, 0, 12); }));
      var grid = new CardGrid { MinColumn = 170, Gap = 16 };
      foreach (var item in items.Take(FirstShown)) grid.Children.Add(Tile(item));
      section.Children.Add(grid);
      if (items.Count > FirstShown) {
        Button more = null;
        more = Kit.Button($"Show {items.Count - FirstShown} more", () => {
          foreach (var item in items.Skip(FirstShown)) grid.Children.Add(Tile(item));
          section.Children.Remove(more);
        }, "Blastlog.Ghost");
        more.HorizontalAlignment = HorizontalAlignment.Left; more.Margin = new Thickness(0, 14, 0, 0);
        section.Children.Add(more);
      }
      body.Children.Add(section);
    }

    static readonly Dictionary<string, string> SourceTag = new Dictionary<string, string> { ["library"] = "Your library", ["friends"] = "Friends", ["discover"] = "Discover" };
    // The site's .discover-tag colours: library the text colour, friends the game's text colour, Discover the critics' warn.
    static string TagBrush(string source) => source == "friends" ? "Blastlog.GameText" : source == "discover" ? "Blastlog.Warn" : "Blastlog.Text";

    /// <summary>A game tile (the site's Tile): cover with where it's from, title, year · status · predicted, Shares, friends' stars, why, Watch out.</summary>
    FrameworkElement Tile(JObject item) {
      var gameId = item.S("gameId"); var title = item.S("title") ?? ""; var source = item.S("source") ?? "discover"; var entryId = item.S("entryId");
      void Open() {
        if (entryId != null && plugin.GameFor(entryId) != null) {
          // Playnite's own game page is behind this window: shut it first.
          close(); Application.Current?.Dispatcher.BeginInvoke(new Action(() => Nav.Entry(plugin, entryId)));
        } else if (entryId != null) Nav.Entry(plugin, entryId);
        else Nav.DiscoverGame(plugin, gameId);
      }
      var artGrid = new Grid();
      artGrid.Children.Add(PnKit.Art(plugin, gameId, item.S("coverUrl"), new CornerRadius(17, 17, 0, 0)));
      artGrid.Children.Add(PnKit.OverPill(SourceTag.TryGetValue(source, out var tag) ? tag : "Discover", TagBrush(source), HorizontalAlignment.Left, VerticalAlignment.Top));
      var art = PnKit.Bare(artGrid, Open, "Open " + title);

      var text = new StackPanel { Margin = new Thickness(14, 12, 14, 14) };
      text.Children.Add(PnKit.Bare(PnKit.Text(title, "Blastlog.Body", null, FontWeights.Bold).Also(t => { t.FontSize = 15; t.TextWrapping = TextWrapping.Wrap; }), Open, title));
      var predicted = item.D("predictedRating"); var status = item.S("status");
      var facts = string.Join(" · ", new[] { item.S("year"), status != null ? ReviewWords.StatusLabel(status) : null,
        predicted != null ? "predicted " + predicted.Value.ToString("0.0", CultureInfo.InvariantCulture) + "★" : null }.Where(x => !string.IsNullOrEmpty(x)));
      if (facts.Length > 0) text.Children.Add(Para(PnKit.Text(facts, "Blastlog.Small")));
      var shared = item.Strings("shared");
      if (shared.Count > 0) text.Children.Add(Para(PnKit.Text("Shares: " + string.Join(", ", shared), "Blastlog.Small")));
      var friendLine = string.Join(" · ", item.Objects("friends").Where(f => f.D("rating") != null).Select(f => (f.S("name") ?? "A friend") + " " + Stars(f.D("rating").Value)));
      if (friendLine.Length > 0) text.Children.Add(Para(PnKit.Text(friendLine, "Blastlog.Small")));
      var reasons = item.Strings("reasons");
      if (reasons.Count > 0) text.Children.Add(Para(PnKit.Line("Blastlog.Body", PnKit.Run("Because you like ", "Blastlog.TextMuted"), PnKit.Run(string.Join(", ", reasons)))));
      var watch = item.S("watchOut");
      if (watch != null) text.Children.Add(Para(PnKit.Text("Watch out: " + watch, "Blastlog.Body", "Blastlog.Warn")));

      var dock = new DockPanel { LastChildFill = false };
      DockPanel.SetDock(art, Dock.Top); dock.Children.Add(art);
      DockPanel.SetDock(text, Dock.Top); dock.Children.Add(text);
      return PnKit.GameCard(plugin, gameId, item.S("coverUrl"), dock);
    }
    static FrameworkElement Para(TextBlock t) { t.FontSize = 14; t.TextWrapping = TextWrapping.Wrap; t.Margin = new Thickness(0, 4, 0, 0); return t; }
  }
}
}
