using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Blastlog.Playnite.Ui;
using Blastlog.Sync.Core.Design;
using Newtonsoft.Json.Linq;
using static Blastlog.Playnite.Screens.TasteUi;
using BlastlogGame = Blastlog.Sync.Core.Game;

namespace Blastlog.Playnite.Screens {
/// <summary>
/// One of the player's collections (apps/web LibraryCollections.tsx CollectionPage, /library/collections/:id), natively,
/// in its own window: Back, the hero (cover, "Collection", its name, what a collection is), the overall take (stars and
/// review for the whole collection, never a game rating) and the games in it, each opening its own page.
/// Reads GET /sync/v1/collections (the player's collections with their games; the game page only links the player's own).
/// The owner's edits (Rename, the overall take, Take out, Remove collection) open the site's page: the token API has no
/// collection writes yet. The page wears Blastlog green, as the site's does (it sets no page colour).
/// </summary>
static class CollectionPage {
  /// <summary>Opens the page; <paramref name="done"/> runs after the window closes.</summary>
  public static void Open(BlastlogPlugin plugin, string collectionId, Action done) =>
    LinkPages.Show(plugin, "Collection", close => new View(plugin, collectionId, close), done, width: 1100, height: 860);

  /// <summary>The site's stars for the overall take: "★★★★☆", or "No stars".</summary>
  internal static string StarsText(int? n) => n > 0 ? new string('★', Math.Min(5, n.Value)) + new string('☆', 5 - Math.Min(5, n.Value)) : "No stars";

  sealed class View : StackPanel {
    readonly BlastlogPlugin plugin; readonly string collectionId; readonly Action close;
    readonly StackPanel body = new StackPanel();
    bool loaded;

    public View(BlastlogPlugin plugin, string collectionId, Action close) {
      this.plugin = plugin; this.collectionId = collectionId; this.close = close;
      MaxWidth = 1100; HorizontalAlignment = HorizontalAlignment.Left;
      Kit.GameScope(this, GameColour.BrandGreen);
      Children.Add(Fingerprint.LinkButton("← Back", close, game: true).Also(b => { b.Margin = new Thickness(-10, 0, 0, 10); b.HorizontalAlignment = HorizontalAlignment.Left; }));
      Children.Add(body);
      body.Children.Add(T("Opening collection…", "Blastlog.Muted"));
      Loaded += (s, e) => { if (!loaded) { loaded = true; Run(() => Load(false), "collection page"); } };
    }

    string SitePage => "/library/collections/" + collectionId;

    async Task Load(bool fresh) {
      if (!plugin.Configured) { Show(T("Add your Blastlog sync token in Add-ons, Extension settings, Blastlog to see this collection.", "Blastlog.Muted")); return; }
      var c = Find((await plugin.Mirror("collections", fresh)).Value);
      // A collection made since the cached copy: ask again before giving up.
      if (c == null && !fresh) c = Find((await plugin.Mirror("collections", true)).Value);
      if (c == null) { Unavailable(); return; }
      Build(c);
    }
    JObject Find(JObject list) => list.Objects("items").FirstOrDefault(i => i.S("id") == collectionId);

    void Show(UIElement content) { body.Children.Clear(); body.Children.Add(content); }

    void Unavailable() {
      var panel = Stack(null, T("That collection couldn’t be opened. It may have been removed.", "Blastlog.Body", "Blastlog.Danger"));
      var row = Wrap(
        Kit.Button("Try again", async () => await Load(true), "Blastlog.Ghost").Also(b => b.Margin = new Thickness(0, 0, 10, 0)),
        Fingerprint.LinkButton("Open on Blastlog ↗", () => Nav.Site(plugin, SitePage), game: true));
      row.Margin = new Thickness(0, 12, 0, 0);
      panel.Children.Add(row);
      Show(panel);
    }

    void Build(JObject c) {
      body.Children.Clear();
      var title = c.S("title") ?? "Collection";
      var window = Window.GetWindow(this); if (window != null) window.Title = title;
      var games = c.Objects("games");
      body.Children.Add(Hero(c, title, games.Count));
      var panels = new CardGrid { MinColumn = 380, Gap = 18 };
      panels.Children.Add(Take(c));
      panels.Children.Add(Games(games));
      body.Children.Add(panels);
      body.Children.Add(T(plugin.CacheLine(), "Blastlog.Small").Also(t => t.Margin = new Thickness(0, 18, 0, 0)));
    }

    /// <summary>The site's detail-hero: cover, "Collection", the name with Rename, and what a collection's take is.</summary>
    FrameworkElement Hero(JObject c, string title, int count) {
      var grid = new Grid { Margin = new Thickness(0, 0, 0, 24) };
      grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(220) });
      grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(26) });
      grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
      var image = c.S("coverImageId");
      var cover = Kit.Cover(plugin, new BlastlogGame { Id = c.S("gameId") ?? collectionId, Title = title, CoverUrl = image != null ? Artwork.IgdbImageUrl(image, "cover_big_2x") : null }, 220, ArtworkContext.Cover, 18);
      cover.VerticalAlignment = VerticalAlignment.Top;
      Grid.SetColumn(cover, 0); grid.Children.Add(cover);

      var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
      text.Children.Add(T("Collection", "Blastlog.Small", "Blastlog.GameText", 0, FontWeights.Bold));
      var name = new WrapPanel { Margin = new Thickness(0, 2, 0, 0) };
      name.Children.Add(T(title, "Blastlog.HeroTitle").Also(t => { t.TextWrapping = TextWrapping.Wrap; t.VerticalAlignment = VerticalAlignment.Center; t.Margin = new Thickness(0, 0, 12, 0); }));
      name.Children.Add(Fingerprint.LinkButton("Rename on Blastlog ↗", () => Nav.Site(plugin, SitePage)).Also(b => b.VerticalAlignment = VerticalAlignment.Center));
      text.Children.Add(name);
      var bundle = c.S("bundleTitle");
      if (bundle != null) text.Children.Add(T("Bundle: " + bundle, "Blastlog.Small").Also(t => t.Margin = new Thickness(0, 4, 0, 0)));
      text.Children.Add(T($"{Plural(count, "game")}, each its own entry: their stars and reviews are what Compare and picks use. This is the take on the whole collection.", "Blastlog.Body")
        .Also(t => { t.TextWrapping = TextWrapping.Wrap; t.MaxWidth = 640; t.HorizontalAlignment = HorizontalAlignment.Left; t.Margin = new Thickness(0, 12, 0, 0); }));
      Grid.SetColumn(text, 2); grid.Children.Add(text);
      return grid;
    }

    /// <summary>Overall take: the collection's own stars and review (they don't count as a rating for its games).</summary>
    FrameworkElement Take(JObject c) {
      var stack = new StackPanel();
      stack.Children.Add(T("Overall take", "Blastlog.Small", "Blastlog.GameText", 0, FontWeights.Bold).Also(t => t.Margin = new Thickness(0, 0, 0, 8)));
      var rating = (int?)c.D("rating");
      stack.Children.Add(T(StarsText(rating), "Blastlog.Body", rating > 0 ? "Blastlog.Star" : "Blastlog.TextMuted", rating > 0 ? 22 : 0).Also(t => t.Margin = new Thickness(0, 0, 0, 8)));
      var review = c.S("review");
      if (!string.IsNullOrWhiteSpace(review))
        stack.Children.Add(new Border { BorderThickness = new Thickness(2, 0, 0, 0), Padding = new Thickness(12, 2, 0, 2),
          Child = T("“" + review.Trim() + "”", "Blastlog.Body").Also(t => { t.FontStyle = FontStyles.Italic; t.TextWrapping = TextWrapping.Wrap; }) }
          .Res(Border.BorderBrushProperty, "Blastlog.GameEdge"));
      else stack.Children.Add(T("No overall review.", "Blastlog.Muted"));
      stack.Children.Add(T("These stars are for the collection only; they don’t count as a rating for the games in it.", "Blastlog.Small").Also(t => { t.TextWrapping = TextWrapping.Wrap; t.Margin = new Thickness(0, 12, 0, 0); }));
      stack.Children.Add(Fingerprint.LinkButton(rating > 0 || !string.IsNullOrWhiteSpace(review) ? "Change it on Blastlog ↗" : "Add stars or a review on Blastlog ↗", () => Nav.Site(plugin, SitePage), game: true)
        .Also(b => { b.Margin = new Thickness(-10, 6, 0, 0); b.HorizontalAlignment = HorizontalAlignment.Left; }));
      return Kit.Card(stack);
    }

    /// <summary>Games in it: each title opens its own page (Playnite's when paired), with its status, stars and whether it's reviewed.</summary>
    FrameworkElement Games(System.Collections.Generic.List<JObject> games) {
      var stack = new StackPanel();
      stack.Children.Add(T("Games in it", "Blastlog.Section").Also(t => t.Margin = new Thickness(0, 0, 0, 8)));
      for (var i = 0; i < games.Count; i++) {
        var game = games[i]; var entryId = game.S("entryId"); var gameTitle = game.S("title") ?? "A game";
        var row = new Border { BorderThickness = new Thickness(0, 0, 0, i == games.Count - 1 ? 0 : 1), Padding = new Thickness(0, 6, 0, 6) };
        row.SetResourceReference(Border.BorderBrushProperty, "Blastlog.Line");
        var line = new WrapPanel();
        line.Children.Add(Fingerprint.LinkButton(gameTitle, () => OpenEntry(entryId), game: true).Also(b => { b.Margin = new Thickness(-10, 0, 10, 0); b.FontWeight = FontWeights.Bold; }));
        var rating = game.D("rating");
        var facts = string.Join(" · ", new[] { ReviewWords.StatusLabel(game.S("status") ?? "unknown"), rating > 0 ? Stars(rating.Value) : null, game.B("hasReview") ? "reviewed" : null }.Where(x => x != null));
        line.Children.Add(T(facts, "Blastlog.Muted").Also(t => { t.FontSize = 14; t.VerticalAlignment = VerticalAlignment.Center; }));
        row.Child = line;
        stack.Children.Add(row);
      }
      if (games.Count == 0) stack.Children.Add(T("No games in it. Add one from its game page.", "Blastlog.Muted"));
      else stack.Children.Add(T("Add a game from its own page.", "Blastlog.Small").Also(t => t.Margin = new Thickness(0, 10, 0, 0)));
      stack.Children.Add(Fingerprint.LinkButton("Take games out or remove the collection on Blastlog ↗", () => Nav.Site(plugin, SitePage))
        .Also(b => { b.Margin = new Thickness(-10, 6, 0, 0); b.HorizontalAlignment = HorizontalAlignment.Left; }));
      return Kit.Card(stack);
    }

    /// <summary>A game in Playnite shuts this window first (its page is behind it); any other opens over it.</summary>
    void OpenEntry(string entryId) {
      if (entryId == null) return;
      if (plugin.GameFor(entryId) != null) { close(); Application.Current?.Dispatcher.BeginInvoke(new Action(() => Nav.Entry(plugin, entryId))); }
      else Nav.Entry(plugin, entryId);
    }
  }
}
}
