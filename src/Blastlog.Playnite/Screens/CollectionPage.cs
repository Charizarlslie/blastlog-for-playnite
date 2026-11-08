using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
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
/// A collection's page (apps/web LibraryCollections.tsx CollectionPage, /library/collections/:id), natively, in its own
/// window: Back, the hero (cover, "Collection", its name with Rename, what a collection's take is), the overall take
/// (stars and review for the whole collection, never a game rating) and the games in it, each opening its own page.
/// The owner renames it, gives the overall take, takes games out and removes it, as on the site (no Undo there either:
/// the site asks before Remove). Reads GET /sync/v1/collections/:id; writes PATCH/DELETE /sync/v1/collections/:id and
/// PUT /sync/v1/entries/:id/collection (Screens/CollectionPlugin.cs). The page wears the player's usual colour (Next up's game), as the site's does.
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
    bool loaded, own, busy;
    JObject current;
    // The overall take being edited (the site's form), kept through a version conflict.
    int? formRating; string formReview; int formVersion = -1;
    string naming; // null: not renaming
    string takeError, renameError, listError, takeNotice;

    public View(BlastlogPlugin plugin, string collectionId, Action close) {
      this.plugin = plugin; this.collectionId = collectionId; this.close = close;
      MaxWidth = 1100; HorizontalAlignment = HorizontalAlignment.Left;
      CollectionUi.WearUsual(this, plugin);
      Children.Add(Fingerprint.LinkButton("← Back", close).Also(b => { b.Margin = new Thickness(-10, 0, 0, 10); b.HorizontalAlignment = HorizontalAlignment.Left; }));
      Children.Add(body);
      body.Children.Add(T("Opening collection…", "Blastlog.Muted"));
      Loaded += (s, e) => { if (!loaded) { loaded = true; Run(() => Load(false), "collection page"); } };
    }

    string Path => "collections/" + Uri.EscapeDataString(collectionId);

    async Task Load(bool fresh) {
      if (!plugin.Configured) { Show(T("Add your Blastlog sync token in Add-ons, Extension settings, Blastlog to see this collection.", "Blastlog.Muted")); return; }
      var read = plugin.Mirror(Path, fresh); var mine = plugin.Mirror("collections", fresh);
      await Task.WhenAll(read, mine);
      var c = read.Result.Value;
      if (c == null || c.S("id") == null) { Unavailable(); return; }
      // Only the owner edits; their own collections are the ones in GET /collections.
      own = mine.Result.Value.Objects("items").Any(i => i.S("id") == collectionId);
      Build(c);
    }

    void Show(UIElement content) { body.Children.Clear(); body.Children.Add(content); }

    void Unavailable() {
      var panel = Stack(null, T("That collection couldn’t be opened. It may have been removed.", "Blastlog.Body", "Blastlog.Danger"));
      panel.Children.Add(Kit.Button("Try again", async () => await Load(true), "Blastlog.Ghost").Also(b => { b.HorizontalAlignment = HorizontalAlignment.Left; b.Margin = new Thickness(0, 12, 0, 0); }));
      Show(panel);
    }

    void Build(JObject c, bool keepForm = false) {
      current = c;
      var version = (int?)c.D("version") ?? 0;
      // The form follows the collection when it changes (the site's useEffect on id and version), unless a conflict kept the edits.
      if (!keepForm && version != formVersion) { formRating = (int?)c.D("rating"); formReview = c.S("review") ?? ""; }
      formVersion = version;
      Render();
    }

    void Render() {
      var c = current; if (c == null) return;
      body.Children.Clear();
      var title = c.S("title") ?? "Collection";
      var window = Window.GetWindow(this); if (window != null) window.Title = title;
      var games = c.Objects("games");
      body.Children.Add(Hero(c, title, games.Count));
      var panels = new CardGrid { MinColumn = 380, Gap = 18 };
      panels.Children.Add(Take(c));
      panels.Children.Add(Games(c, title, games));
      body.Children.Add(panels);
      body.Children.Add(T(plugin.CacheLine(), "Blastlog.Small").Also(t => t.Margin = new Thickness(0, 18, 0, 0)));
    }

    /// <summary>The site's detail-hero: cover, "Collection", the name with Rename (or the rename form), and what a collection's take is.</summary>
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
      if (naming == null) {
        var name = new WrapPanel { Margin = new Thickness(0, 2, 0, 0) };
        name.Children.Add(T(title, "Blastlog.HeroTitle").Also(t => { t.TextWrapping = TextWrapping.Wrap; t.VerticalAlignment = VerticalAlignment.Center; t.Margin = new Thickness(0, 0, 4, 0); }));
        if (own) name.Children.Add(CollectionUi.Small("Rename", () => { naming = title; renameError = null; Render(); }, "Rename the collection"));
        text.Children.Add(name);
      } else text.Children.Add(RenameForm(c, title));
      text.Children.Add(T($"{Plural(count, "game")}, each its own entry: their stars and reviews are what Compare and picks use. This is the take on the whole collection.", "Blastlog.Body")
        .Also(t => { t.TextWrapping = TextWrapping.Wrap; t.MaxWidth = 640; t.HorizontalAlignment = HorizontalAlignment.Left; t.Margin = new Thickness(0, 12, 0, 0); }));
      Grid.SetColumn(text, 2); grid.Children.Add(text);
      return grid;
    }

    /// <summary>The site's .coll-rename: Name, the field, Save, Cancel; a bundle's name can be cleared back to the bundle's.</summary>
    FrameworkElement RenameForm(JObject c, string title) {
      var bundle = c.S("gameId") != null;
      var box = Dash.Field(); box.Text = naming;
      var save = Kit.Button("Save", async () => await Rename(box.Text), "Blastlog.Primary");
      void Enable() => save.IsEnabled = !busy && (bundle || !string.IsNullOrWhiteSpace(box.Text));
      box.TextChanged += (s, e) => { naming = box.Text; Enable(); };
      box.PreviewKeyDown += (s, e) => { if (e.Key == System.Windows.Input.Key.Enter && save.IsEnabled) { e.Handled = true; Run(() => Rename(box.Text), "collection rename"); } };
      Enable();
      var form = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
      form.Children.Add(T("Name", "Blastlog.Muted").Also(t => { t.FontSize = 14; t.VerticalAlignment = VerticalAlignment.Center; t.Margin = new Thickness(0, 0, 8, 0); }));
      form.Children.Add(CollectionUi.Field(box, bundle ? c.S("bundleTitle") ?? title : "Tomb Raider games", "Name", 320).Also(f => f.Margin = new Thickness(0, 0, 8, 6)));
      form.Children.Add(save.Also(b => b.Margin = new Thickness(0, 0, 8, 6)));
      form.Children.Add(Kit.Button("Cancel", () => { naming = null; renameError = null; Render(); }, "Blastlog.Ghost").Also(b => b.Margin = new Thickness(0, 0, 0, 6)));
      var stack = new StackPanel(); stack.Children.Add(form);
      if (bundle) stack.Children.Add(T($"Leave it empty to use {c.S("bundleTitle") ?? "the bundle's name"}.", "Blastlog.Small"));
      if (renameError != null) stack.Children.Add(CollectionUi.Error(renameError));
      Dispatcher.BeginInvoke(new Action(() => { try { box.Focus(); box.SelectAll(); } catch (Exception) { } }), System.Windows.Threading.DispatcherPriority.Input);
      return stack;
    }

    async Task Rename(string text) {
      if (busy || current == null) return;
      busy = true;
      try {
        var trimmed = (text ?? "").Trim();
        var r = await plugin.CollectionSend(new HttpMethod("PATCH"), Path, new JObject { ["expectedVersion"] = formVersion, ["title"] = trimmed.Length > 0 ? (JToken)trimmed : JValue.CreateNull() }, EntryIds());
        if (r.Ok) { naming = null; renameError = null; takeNotice = null; current = r.Value; formVersion = (int?)r.Value.D("version") ?? formVersion; }
        else if (r.Error == "version_conflict") { await Latest(); renameError = r.Words + " The latest is loaded with your new name kept; save again."; }
        else renameError = r.Words;
      } finally { busy = false; Render(); }
    }

    /// <summary>Overall take: the owner's stars and review for the whole collection (the site's .coll-take form); anyone else reads it.</summary>
    FrameworkElement Take(JObject c) {
      var stack = new StackPanel();
      stack.Children.Add(T("Overall take", "Blastlog.Small", "Blastlog.GameText", 0, FontWeights.Bold).Also(t => t.Margin = new Thickness(0, 0, 0, 10)));
      if (!own) {
        var rating = (int?)c.D("rating");
        stack.Children.Add(T(StarsText(rating), "Blastlog.Body", rating > 0 ? "Blastlog.Star" : "Blastlog.TextMuted", rating > 0 ? 22 : 0).Also(t => t.Margin = new Thickness(0, 0, 0, 8)));
        var review = c.S("review");
        if (!string.IsNullOrWhiteSpace(review))
          stack.Children.Add(new Border { BorderThickness = new Thickness(2, 0, 0, 0), Padding = new Thickness(12, 2, 0, 2),
            Child = T("“" + review.Trim() + "”", "Blastlog.Body").Also(t => { t.FontStyle = FontStyles.Italic; t.TextWrapping = TextWrapping.Wrap; }) }
            .Res(Border.BorderBrushProperty, "Blastlog.GameEdge"));
        else stack.Children.Add(T("No overall review.", "Blastlog.Muted"));
        return Kit.Card(stack);
      }
      var starHost = new ContentControl();
      void Stars() => starHost.Content = CollectionUi.Stars(formRating, !busy, v => { formRating = v; takeNotice = null; Stars(); });
      Stars();
      stack.Children.Add(starHost.Also(h => h.Margin = new Thickness(0, 0, 0, 10)));
      var box = new TextBox { Text = formReview ?? "" };
      box.TextChanged += (s, e) => { formReview = box.Text; takeNotice = null; };
      stack.Children.Add(CollectionUi.Area(box, "The collection as a whole: the package, the remastering, how the games sit together…", "Overall review"));
      if (takeError != null) stack.Children.Add(CollectionUi.Error(takeError));
      if (takeNotice != null) stack.Children.Add(T(takeNotice, "Blastlog.Body", "Blastlog.Ok").Also(t => { t.FontSize = 14; t.Margin = new Thickness(0, 6, 0, 0); }));
      stack.Children.Add(Kit.Button(busy ? "Saving…" : "Save", SaveTake, "Blastlog.Primary").Also(b => { b.HorizontalAlignment = HorizontalAlignment.Left; b.Margin = new Thickness(0, 10, 0, 0); b.IsEnabled = !busy; }));
      stack.Children.Add(T("These stars are for the collection only; they don’t count as a rating for the games in it.", "Blastlog.Small").Also(t => { t.TextWrapping = TextWrapping.Wrap; t.Margin = new Thickness(0, 10, 0, 0); }));
      return Kit.Card(stack);
    }

    async Task SaveTake() {
      if (busy || current == null) return;
      busy = true;
      try {
        var review = (formReview ?? "").Trim();
        var r = await plugin.CollectionSend(new HttpMethod("PATCH"), Path, new JObject { ["expectedVersion"] = formVersion,
          ["rating"] = formRating.HasValue ? (JToken)formRating.Value : JValue.CreateNull(), ["review"] = review.Length > 0 ? (JToken)review : JValue.CreateNull() }, EntryIds());
        if (r.Ok) { takeError = null; takeNotice = "Saved."; current = r.Value; formVersion = (int?)r.Value.D("version") ?? formVersion; formRating = (int?)r.Value.D("rating"); formReview = r.Value.S("review") ?? ""; return; }
        // Someone changed it elsewhere: load the latest and keep what the player wrote here (Rate & review does the same).
        if (r.Error == "version_conflict") { await Latest(); takeError = r.Words + " The latest is loaded with your edits kept; check it and save again."; }
        else takeError = r.Words;
      } finally { busy = false; Render(); }
    }

    /// <summary>The collection as it is now on Blastlog, keeping the edits in progress.</summary>
    async Task Latest() {
      var fresh = (await plugin.Mirror(Path, true)).Value;
      if (fresh?.S("id") != null) { current = fresh; formVersion = (int?)fresh.D("version") ?? formVersion; }
    }

    string[] EntryIds() => current.Objects("games").Select(g => g.S("entryId")).Where(id => id != null).ToArray();

    /// <summary>Games in it: each opens its own page; the owner can take one out, and remove the collection (its games stay).</summary>
    FrameworkElement Games(JObject c, string title, List<JObject> games) {
      var stack = new StackPanel();
      stack.Children.Add(T("Games in it", "Blastlog.Section").Also(t => t.Margin = new Thickness(0, 0, 0, 8)));
      for (var i = 0; i < games.Count; i++) {
        var game = games[i]; var entryId = game.S("entryId"); var gameTitle = game.S("title") ?? "A game";
        var row = new Border { BorderThickness = new Thickness(0, 0, 0, i == games.Count - 1 ? 0 : 1), Padding = new Thickness(0, 4, 0, 4) };
        row.SetResourceReference(Border.BorderBrushProperty, "Blastlog.Line");
        var dock = new DockPanel();
        if (own) {
          var takeOut = CollectionUi.Small("Take out", () => Run(() => TakeOut(entryId), "collection take out"), $"Take {gameTitle} out");
          takeOut.IsEnabled = !busy; DockPanel.SetDock(takeOut, Dock.Right); dock.Children.Add(takeOut);
        }
        var line = new WrapPanel { VerticalAlignment = VerticalAlignment.Center };
        line.Children.Add(Fingerprint.LinkButton(gameTitle, () => OpenEntry(entryId), game: true).Also(b => { b.Margin = new Thickness(-10, 0, 10, 0); b.FontWeight = FontWeights.Bold; }));
        var rating = game.D("rating");
        var facts = string.Join(" · ", new[] { ReviewWords.StatusLabel(game.S("status") ?? "unknown"), rating > 0 ? Stars(rating.Value) : null, game.B("hasReview") ? "reviewed" : null }.Where(x => x != null));
        line.Children.Add(T(facts, "Blastlog.Muted").Also(t => { t.FontSize = 14; t.VerticalAlignment = VerticalAlignment.Center; }));
        dock.Children.Add(line);
        row.Child = dock;
        stack.Children.Add(row);
      }
      if (games.Count == 0) stack.Children.Add(T("No games in it. Add one from its game page.", "Blastlog.Muted"));
      else if (own) stack.Children.Add(T("Add a game from its own page.", "Blastlog.Small").Also(t => t.Margin = new Thickness(0, 10, 0, 0)));
      if (listError != null) stack.Children.Add(CollectionUi.Error(listError));
      if (own) stack.Children.Add(CollectionUi.Small("Remove collection", () => Run(() => Remove(title), "collection remove")).Also(b => { b.Margin = new Thickness(-10, 8, 0, 0); b.HorizontalAlignment = HorizontalAlignment.Left; b.IsEnabled = !busy; }));
      return Kit.Card(stack);
    }

    async Task TakeOut(string entryId) {
      if (busy || entryId == null) return;
      busy = true;
      try {
        var r = await plugin.CollectionSend(HttpMethod.Put, $"entries/{Uri.EscapeDataString(entryId)}/collection", new JObject { ["collectionId"] = JValue.CreateNull() }, entryId);
        listError = r.Ok ? null : r.Words;
      } finally { busy = false; }
      await Load(true);
    }

    async Task Remove(string title) {
      if (busy) return;
      var answer = plugin.Api.Dialogs.ShowMessage($"Remove “{title}”? Its games stay in your library.", "Remove collection", MessageBoxButton.OKCancel, MessageBoxImage.Question);
      if (answer != MessageBoxResult.OK) return;
      busy = true;
      try {
        var r = await plugin.CollectionSend(HttpMethod.Delete, Path, new JObject(), EntryIds());
        if (r.Ok) { close(); return; }
        listError = r.Words;
      } finally { busy = false; }
      Render();
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
