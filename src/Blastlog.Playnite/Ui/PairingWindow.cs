using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Blastlog.Playnite.Ui;
using Blastlog.Sync.Core;
using Playnite.SDK;

namespace Blastlog.Playnite {
/// <summary>
/// Pairing (docs/PLAN.md §4.1), in three plainly labelled sections so it's clear what Apply does: games already in the
/// player's Blastlog library (pairing only links them; exact matches start ticked), games Blastlog knows but the player
/// doesn't have (ticking adds them to their library; off by default, folded away), and names Blastlog doesn't recognise
/// (ticking sends them to Blastlog's import; off by default, folded away). A live line says what Apply will do.
/// </summary>
public sealed class PairingWindow {
  static readonly ILogger Log = LogManager.GetLogger();

  public static bool? Show(BlastlogPlugin plugin, List<PairingChoice> choices) {
    var window = plugin.Api.Dialogs.CreateWindow(new WindowCreationOptions { ShowCloseButton = true, ShowMaximizeButton = true, ShowMinimizeButton = false });
    window.Title = "Pair games with Blastlog"; window.Width = 900; window.Height = 760;
    window.Owner = plugin.Api.Dialogs.GetCurrentAppWindow(); window.WindowStartupLocation = WindowStartupLocation.CenterOwner;

    var inLibrary = choices.Where(c => c.Match.Result == "entry").OrderBy(c => c.Match.Confidence == "exact" ? 1 : 0).ThenBy(c => c.Game.Name).ToList();
    var toAdd = choices.Where(c => c.Match.Result == "catalogue").OrderBy(c => c.Game.Name).ToList();
    var unknown = choices.Where(c => c.Match.Result == "unknown").OrderBy(c => c.Game.Name).ToList();
    var linked = choices.Count(c => c.Match.Result == "linked");
    var skipped = choices.Where(c => c.Match.Result == "skipped").ToList();

    var summary = Kit.Text("", "Blastlog.Body"); summary.FontWeight = FontWeights.SemiBold; summary.VerticalAlignment = VerticalAlignment.Center;
    void Update() {
      try {
        int p = inLibrary.Count(c => c.Ticked), a = toAdd.Count(c => c.Ticked), u = unknown.Count(c => c.Ticked);
        summary.Text = $"Apply will pair {p} game{(p == 1 ? "" : "s")}" + (a > 0 ? $", add {a} to your Blastlog library" : ", add nothing to your library")
          + (u > 0 ? $", and send {u} to Blastlog's import." : ".");
      } catch (Exception e) { Log.Error(e, "Blastlog pairing summary"); }
    }

    var body = new StackPanel { Margin = new Thickness(28, 24, 28, 12) };
    body.Children.Add(Kit.Text("Pairing links a game in Playnite to the same game in your Blastlog library, so its status and stars sync and Blastlog's screens can show Play and Install. Nothing is added to your Blastlog library.", "Blastlog.Muted").Also(t => t.Margin = new Thickness(0, 0, 0, 18)));

    // Pairing is the job (Charlie, 2026-11-07): it's the one open section. Adding games or sending them to the import
    // are rare extras, tucked behind one small "Other games" link.
    body.Children.Add(Section("Already in your Blastlog library", inLibrary,
      "Pairing just links them. Exact matches are ticked. A “check it” row is a close match: tick it only if it's the same game.",
      expanded: true, Update));
    var others = toAdd.Count + unknown.Count;
    if (others > 0) {
      var extra = new StackPanel { Visibility = Visibility.Collapsed, Margin = new Thickness(0, 6, 0, 0) };
      extra.Children.Add(Section("Not in your Blastlog library", toAdd,
        "Blastlog knows these games, but you don't have them on Blastlog. Ticking one adds it to your Blastlog library as a game you own. Leave Game Pass and other games you don't own unticked.",
        expanded: true, Update));
      extra.Children.Add(Section("Blastlog doesn't recognise these", unknown,
        "Ticking one sends it to Blastlog's import page (Library, Import games) for you to sort out there. Nothing is added until you finish that import.",
        expanded: true, Update));
      var more = Kit.Button($"▸  Other games ({others}): add to Blastlog or send to the import", () => { }, "Blastlog.TextButton");
      more.HorizontalAlignment = HorizontalAlignment.Left;
      more.Click += (s, e) => {
        try {
          var open = extra.Visibility != Visibility.Visible;
          extra.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
          more.Content = $"{(open ? "▾" : "▸")}  Other games ({others}): add to Blastlog or send to the import";
        } catch (Exception ex) { Log.Error(ex, "Blastlog pairing other games"); }
      };
      body.Children.Add(more); body.Children.Add(extra);
    }
    var notes = new List<string>();
    if (linked > 0) notes.Add($"{linked} game{(linked == 1 ? " is" : "s are")} already paired.");
    if (skipped.Count > 0) notes.Add($"{skipped.Count} couldn't be checked: " + string.Join(", ", skipped.Take(8).Select(s => s.Game.Name + (s.Problem != null ? $" ({s.Problem})" : ""))) + (skipped.Count > 8 ? "…" : "") + ". Fix them in Playnite and pair again.");
    foreach (var n in notes) body.Children.Add(Kit.Text(n, "Blastlog.Small").Also(t => t.Margin = new Thickness(0, 4, 0, 0)));

    var footer = new DockPanel { Margin = new Thickness(28, 12, 28, 20), LastChildFill = true };
    var buttons = new StackPanel { Orientation = Orientation.Horizontal }; DockPanel.SetDock(buttons, Dock.Right);
    buttons.Children.Add(Kit.Button("Apply", () => { window.DialogResult = true; window.Close(); }));
    buttons.Children.Add(Kit.Button("Cancel", () => { window.DialogResult = false; window.Close(); }, "Blastlog.Ghost").Also(b => b.Margin = new Thickness(0)));
    footer.Children.Add(buttons); footer.Children.Add(summary);
    var line = new Border { BorderThickness = new Thickness(0, 1, 0, 0), Child = footer }; line.SetResourceReference(Border.BorderBrushProperty, "Blastlog.Line");

    var root = new DockPanel();
    root.Resources.MergedDictionaries.Add(Kit.Styles);
    root.SetResourceReference(Panel.BackgroundProperty, "Blastlog.Bg");
    DockPanel.SetDock(line, Dock.Bottom); root.Children.Add(line);
    root.Children.Add(new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
    window.Content = root;
    Update();
    return window.ShowDialog();
  }

  /// <summary>A card with a heading and count, what ticking means, Tick all / Untick all, and its rows (folded when not expanded).</summary>
  static FrameworkElement Section(string title, List<PairingChoice> rows, string meaning, bool expanded, Action changed) {
    var card = new StackPanel();
    var head = new DockPanel();
    var toggle = new Button { Style = Kit.Style("Blastlog.TextButton"), Padding = new Thickness(0), HorizontalAlignment = HorizontalAlignment.Left, MinHeight = 44 };
    var heading = Kit.Text("", "Blastlog.CardTitle");
    toggle.Content = heading;
    head.Children.Add(toggle);
    card.Children.Add(head);
    card.Children.Add(Kit.Text(meaning, "Blastlog.Muted").Also(t => t.Margin = new Thickness(0, 2, 0, 10)));
    var list = new StackPanel { Visibility = expanded ? Visibility.Visible : Visibility.Collapsed };
    var boxes = new List<CheckBox>();
    if (rows.Count > 0) {
      var bulk = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
      bulk.Children.Add(Kit.Button("Tick all", () => { foreach (var b in boxes) b.IsChecked = true; }, "Blastlog.TextButton"));
      bulk.Children.Add(Kit.Button("Untick all", () => { foreach (var b in boxes) b.IsChecked = false; }, "Blastlog.TextButton"));
      list.Children.Add(bulk);
    }
    foreach (var choice in rows) {
      var c = choice;
      var renamed = !string.Equals(c.Match.Title, c.Game.Name, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(c.Match.Title);
      var label = new TextBlock { TextWrapping = TextWrapping.Wrap, Style = Kit.Style("Blastlog.Body") };
      label.Inlines.Add(new System.Windows.Documents.Run(c.Game.Name));
      if (renamed) { var r = new System.Windows.Documents.Run($"  →  {c.Match.Title} on Blastlog"); r.SetResourceReference(TextBlock.ForegroundProperty, "Blastlog.TextMuted"); label.Inlines.Add(r); }
      if (c.Match.Confidence == "likely") { var r = new System.Windows.Documents.Run("  ·  check it"); r.SetResourceReference(TextBlock.ForegroundProperty, "Blastlog.Warn"); label.Inlines.Add(r); }
      var box = new CheckBox { Content = label, IsChecked = c.Ticked, Margin = new Thickness(0, 3, 0, 3), MinHeight = 36, VerticalContentAlignment = VerticalAlignment.Center };
      box.Checked += (s, e) => { c.Ticked = true; changed(); Count(); };
      box.Unchecked += (s, e) => { c.Ticked = false; changed(); Count(); };
      boxes.Add(box); list.Children.Add(box);
    }
    if (rows.Count == 0) list.Children.Add(Kit.Text("None.", "Blastlog.Small"));
    card.Children.Add(list);
    void Count() { heading.Text = $"{(list.Visibility == Visibility.Visible ? "▾" : "▸")}  {title} ({rows.Count}{(rows.Count > 0 ? $", {rows.Count(r => r.Ticked)} ticked" : "")})"; }
    toggle.Click += (s, e) => { try { list.Visibility = list.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible; Count(); } catch (Exception ex) { Log.Error(ex, "Blastlog pairing section"); } };
    System.Windows.Automation.AutomationProperties.SetName(toggle, title);
    Count();
    return Kit.Card(card);
  }
}
}
