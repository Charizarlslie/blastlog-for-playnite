using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Blastlog.Playnite.Ui;
using Blastlog.Sync.Core.Design;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Playnite.SDK;
using static Blastlog.Playnite.Screens.TasteUi;

namespace Blastlog.Playnite.Screens {
/// <summary>
/// The site's Import page (apps/web LibraryImport.tsx, /library/import) natively, in its own window: Steam sync's daily
/// finds, needs-a-look reminder, Quick start and Fill in links, and "Your imports", each opening that import's page
/// (Screens/ImportBatch.cs) in the same window. Bringing games in (the store doors, Connect Steam, pasting a list) stays
/// on the site for now: "Import games" opens it. Reads GET /sync/v1/imports.
/// </summary>
static class ImportPage {
  static readonly ILogger Log = LogManager.GetLogger();

  /// <summary>The import list; <paramref name="batchId"/> opens straight into one import (Back goes to the list).</summary>
  public static void Open(BlastlogPlugin plugin, string batchId = null, Action done = null) {
    Window window = null;
    try {
      window = plugin.Api.Dialogs.CreateWindow(new WindowCreationOptions { ShowCloseButton = true, ShowMaximizeButton = true, ShowMinimizeButton = false });
      // Fits the screen it opens on (the work area is in the same units as the window's size).
      window.Title = "Import your games"; window.Width = Math.Min(1100, SystemParameters.WorkArea.Width * 0.95); window.Height = Math.Min(880, SystemParameters.WorkArea.Height * 0.92);
      window.Owner = plugin.Api.Dialogs.GetCurrentAppWindow();
      window.WindowStartupLocation = WindowStartupLocation.CenterOwner;
      var host = new Host(plugin, window);
      window.Content = Kit.Page(host);
      // Keys go to the page first (the sort cards' Y / N / S, Quick sort's numbers); Escape closes what's open, then the window.
      window.PreviewKeyDown += (s, e) => {
        try {
          if (host.Key(e)) { e.Handled = true; return; }
          if (e.Key == Key.Escape) { e.Handled = true; window.Close(); }
        } catch (Exception ex) { Log.Error(ex, "Blastlog import: keys"); }
      };
      if (batchId != null) host.ShowBatch(batchId, fromList: false); else host.ShowList();
      window.ShowDialog();
    } catch (Exception e) { Log.Error(e, "Blastlog import page"); }
    try { done?.Invoke(); } catch (Exception e) { Log.Error(e, "Blastlog import page closed"); }
  }

  /// <summary>The window's content: the list or one import, swapped in place.</summary>
  sealed class Host : ContentControl {
    readonly BlastlogPlugin plugin; readonly Window window;
    ImportBatchView batch;
    public Host(BlastlogPlugin plugin, Window window) { this.plugin = plugin; this.window = window; MaxWidth = 1100; HorizontalAlignment = HorizontalAlignment.Left; Focusable = false; }
    public void ShowList() { batch?.Stop(); batch = null; window.Title = "Import your games"; Content = new ListView(plugin, this); Top(); }
    public void ShowBatch(string id, bool fromList = true) {
      batch?.Stop();
      batch = new ImportBatchView(plugin, id, () => ShowList(), () => { try { window.Close(); } catch (Exception) { } });
      Content = batch; Top();
    }
    public bool Key(KeyEventArgs e) => batch != null && batch.Key(e);
    void Top() { try { (Parent as ScrollViewer ?? FindScroll(this))?.ScrollToTop(); } catch (Exception) { } }
    static ScrollViewer FindScroll(DependencyObject d) { for (var p = System.Windows.Media.VisualTreeHelper.GetParent(d); p != null; p = System.Windows.Media.VisualTreeHelper.GetParent(p)) if (p is ScrollViewer s) return s; return null; }
    public void Close() { try { window.Close(); } catch (Exception) { } }
  }

  /// <summary>
  /// A wrapping line of text with links in it (the site's inline links): each part is a string, or (label, action) for a
  /// link in the game's text colour. Links are keyboard-reachable like any hyperlink.
  /// </summary>
  internal static TextBlock Sentence(string style, params object[] parts) {
    var t = Kit.Text("", style); t.TextWrapping = TextWrapping.Wrap; t.MaxWidth = 900; t.HorizontalAlignment = HorizontalAlignment.Left;
    foreach (var p in parts) {
      if (p is string s) t.Inlines.Add(new System.Windows.Documents.Run(s));
      else if (p is ValueTuple<string, Action> link) {
        var h = new System.Windows.Documents.Hyperlink(new System.Windows.Documents.Run(link.Item1)) { TextDecorations = null, FontWeight = FontWeights.Bold, Cursor = Cursors.Hand };
        h.SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty, "Blastlog.GameText");
        var act = link.Item2;
        h.Click += (sender, e) => { try { act(); } catch (Exception ex) { Log.Error(ex, "Blastlog import: link " + link.Item1); } };
        t.Inlines.Add(h);
      }
    }
    return t;
  }

  /// <summary>Imports the player gives a look later on this device ("Keep sorting" picks up where they stopped).</summary>
  internal static class Handled {
    static Dictionary<string, HashSet<string>> all;
    static string PathFor(BlastlogPlugin plugin) => System.IO.Path.Combine(plugin.GetPluginUserDataPath(), "import-sorted.json");
    static Dictionary<string, HashSet<string>> Load(BlastlogPlugin plugin) {
      if (all != null) return all;
      try { all = JsonConvert.DeserializeObject<Dictionary<string, HashSet<string>>>(File.ReadAllText(PathFor(plugin))); } catch (Exception) { }
      return all = all ?? new Dictionary<string, HashSet<string>>();
    }
    public static HashSet<string> For(BlastlogPlugin plugin, string batchId) {
      var d = Load(plugin);
      if (!d.TryGetValue(batchId, out var set)) d[batchId] = set = new HashSet<string>();
      return set;
    }
    public static void Save(BlastlogPlugin plugin) { try { File.WriteAllText(PathFor(plugin), JsonConvert.SerializeObject(Load(plugin))); } catch (Exception e) { Log.Warn("Blastlog import: couldn't keep sort progress: " + e.Message); } }
  }

  sealed class ListView : StackPanel {
    readonly BlastlogPlugin plugin; readonly Host host;
    readonly StackPanel body = new StackPanel();
    bool loaded;
    public ListView(BlastlogPlugin plugin, Host host) {
      this.plugin = plugin; this.host = host;
      Children.Add(LinkPages.Head("Library", "Import your games",
        "Bring in the games you own, then sort them: finished, playing, backlog or not interested. Blastlog never sees your store passwords: Steam you can connect on Steam’s own sign-in page, and for the rest you copy your list and paste it on Blastlog."));
      Children.Add(body);
      body.Children.Add(T("Opening import…", "Blastlog.Muted"));
      Loaded += (s, e) => { if (!loaded) { loaded = true; Run(() => Load(), "import list"); } };
    }

    async Task Load() {
      if (!plugin.Configured) { Show(T("Add your Blastlog sync token in Add-ons, Extension settings, Blastlog to see your imports.", "Blastlog.Muted")); return; }
      var list = (await plugin.Mirror("imports", fresh: true)).Value;
      if (list == null) {
        var panel = Stack(null, T("Your imports couldn’t be read just now.", "Blastlog.Body", "Blastlog.Danger"));
        panel.Children.Add(Kit.Button("Try again", async () => await Load(), "Blastlog.Ghost").Also(b => { b.HorizontalAlignment = HorizontalAlignment.Left; b.Margin = new Thickness(0, 12, 0, 0); }));
        Show(panel); return;
      }
      Build(list);
    }

    void Show(UIElement e) { body.Children.Clear(); body.Children.Add(e); }

    void Build(JObject list) {
      body.Children.Clear();
      var batches = list.Objects("batches");
      // Steam sync's daily finds wait here to be sorted.
      foreach (var b in batches.Where(b => b.S("origin") == "steam_sync" && b.S("status") == "ready")) {
        var total = (int?)b["counts"]?.D("total") ?? 0;
        var line = Sentence("Blastlog.Body", $"New from Steam: {Plural(total, ImportSort.IsWishlist(b.S("format")) ? "wishlist game" : "game")} waiting to be sorted. ", ("Sort them", (Action)(() => host.ShowBatch(b.S("id")))));
        line.SetResourceReference(TextBlock.ForegroundProperty, "Blastlog.Ok"); line.FontWeight = FontWeights.SemiBold;
        line.Margin = new Thickness(0, 0, 0, 14); body.Children.Add(line);
      }
      if (list["steamLink"] is JObject link) {
        var checkedAt = link.S("lastCheckedAt");
        var when = checkedAt != null && DateTimeOffset.TryParse(checkedAt, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var at)
          ? " Last checked " + at.ToLocalTime().ToString("ddd h:mm tt", CultureInfo.CurrentCulture) : " First check coming up";
        var result = link.S("lastResult");
        body.Children.Add(LinkPages.Card("Steam sync is on",
          "Blastlog checks Steam once a day for games and wishlist games it hasn’t seen and puts them here, ready to sort. It only remembers your Steam ID." + when + (result != null ? ": " + result.ToLowerInvariant() + "." : "."),
          Wrap(Kit.Button("Check now or stop it on Blastlog ↗", () => Nav.SiteOnly(plugin, "/library/import"), "Blastlog.Ghost"))));
      }
      body.Children.Add(Sentence("Blastlog.Body", "No list handy, or just want picks fast? ", ("Rate 10 games you know", (Action)(() => { host.Close(); Nav.QuickStart(plugin); })),
        " instead. It takes a couple of minutes.").Also(w => w.Margin = new Thickness(0, 0, 0, 8)));
      body.Children.Add(Sentence("Blastlog.Body", "Already imported? ", ("Fill in your library ↗", (Action)(() => Nav.Site(plugin, "/library/import/fill-in"))),
        ": the rest of your series, and games your friends played.").Also(w => w.Margin = new Thickness(0, 0, 0, 18)));
      var needsLook = (int?)list.D("needsLook") ?? 0;
      if (!list.B("canImport")) body.Children.Add(LinkPages.Notice("Importing isn’t open yet. You can still add games one at a time from your library.", false));
      if (needsLook > 0) body.Children.Add(LinkPages.Notice($"{Plural(needsLook, "game")} from your imports still {(needsLook == 1 ? "needs" : "need")} a look. Open the import below to pick the right one.", true));

      body.Children.Add(LinkPages.Card("Where are your games?",
        "Steam, PlayStation, Xbox, Nintendo Switch, GOG, Epic, another tracker or any list. Bringing them in happens on Blastlog (Connect Steam, or copy your list and paste it); the import then shows up here to sort.",
        Wrap(Kit.Button("Import games on Blastlog ↗", () => Nav.SiteOnly(plugin, "/library/import")),
          Kit.Button("Refresh", async () => await Load(), "Blastlog.Ghost").Also(b => b.Margin = new Thickness(10, 0, 0, 0)))));

      // The demo (docs/DEMO.md): a pretend Steam import of made-up games, to see how sorting works (POST demo/import, demo only).
      if (Uri.TryCreate(plugin.ServerUrl, UriKind.Absolute, out var server) && server.Host.StartsWith("demo.", StringComparison.OrdinalIgnoreCase))
        body.Children.Add(LinkPages.Card("Try a pretend Steam import",
          "This is the demo, so nothing real comes in: about 30 made-up Steam games with made-up playtimes, to see how sorting works.",
          Wrap(Kit.Button("Import from Steam", async () => {
            var made = await plugin.Write("demo/import", new JObject());
            if (made?.S("id") is string madeId) host.ShowBatch(madeId);
          }))));

      if (batches.Count > 0) {
        var rows = new StackPanel();
        foreach (var b in batches) rows.Children.Add(BatchRow(b));
        body.Children.Add(LinkPages.Card("Your imports", null, rows));
      }
      body.Children.Add(T(plugin.CacheLine(), "Blastlog.Small").Also(t => t.Margin = new Thickness(0, 6, 0, 0)));
    }

    /// <summary>One import in "Your imports": its name, date and size, and its badge; opens its page.</summary>
    FrameworkElement BatchRow(JObject b) {
      var counts = b["counts"] as JObject;
      var total = (int?)counts?.D("total") ?? 0; var look = (int?)counts?.D("needs_look") ?? 0;
      var created = b.S("createdAt");
      var date = created != null && DateTimeOffset.TryParse(created, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var at) ? at.ToLocalTime().ToString("MMM d, yyyy", CultureInfo.CurrentCulture) : "";
      var grid = new Grid { Margin = new Thickness(0, 0, 0, 0) };
      grid.ColumnDefinitions.Add(new ColumnDefinition()); grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
      var words = Stack(null, T(ImportSort.ImportName(b.S("door"), b.S("format"), b.S("origin")), "Blastlog.Body", null, 0, FontWeights.Bold),
        T($"{date} · {Plural(total, "game")}", "Blastlog.Small"));
      words.VerticalAlignment = VerticalAlignment.Center;
      grid.Children.Add(words);
      var status = b.S("status");
      var badge = Kit.Chip(ImportSort.StatusText(status, look), neutral: status != "ready");
      badge.VerticalAlignment = VerticalAlignment.Center; Grid.SetColumn(badge, 1); grid.Children.Add(badge);
      var button = new Button { Content = grid, Style = Kit.Style("Blastlog.TextButton"), HorizontalContentAlignment = HorizontalAlignment.Stretch, MinHeight = 56,
        Padding = new Thickness(4, 8, 4, 8), BorderThickness = new Thickness(0, 0, 0, 1) };
      button.SetResourceReference(Control.BorderBrushProperty, "Blastlog.Line");
      System.Windows.Automation.AutomationProperties.SetName(button, $"{ImportSort.ImportName(b.S("door"), b.S("format"), b.S("origin"))}, {date}, {Plural(total, "game")}, {ImportSort.StatusText(status, look)}");
      button.Click += (s, e) => { try { host.ShowBatch(b.S("id")); } catch (Exception ex) { Log.Error(ex, "Blastlog import: open"); } };
      return button;
    }
  }
}
}
