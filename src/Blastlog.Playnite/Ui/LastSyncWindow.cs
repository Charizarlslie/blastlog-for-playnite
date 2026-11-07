using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Blastlog.Playnite.Ui;
using Blastlog.Sync.Core;
using Playnite.SDK;

namespace Blastlog.Playnite {
/// <summary>
/// "What the last sync changed": every game it changed in Playnite (status and score, before → after), every change it
/// sent to Blastlog, and how many playtime readings it sent. Read from the saved store, so it works after a restart.
/// </summary>
public static class LastSyncWindow {
  public static void Show(BlastlogPlugin plugin, LastRun run) {
    var window = plugin.Api.Dialogs.CreateWindow(new WindowCreationOptions { ShowCloseButton = true, ShowMaximizeButton = true, ShowMinimizeButton = false });
    window.Title = "What the last sync changed"; window.Width = 860; window.Height = 720;
    window.Owner = plugin.Api.Dialogs.GetCurrentAppWindow(); window.WindowStartupLocation = WindowStartupLocation.CenterOwner;
    var body = new StackPanel { Margin = new Thickness(28, 24, 28, 16) };
    if (run == null) body.Children.Add(Kit.Text("No sync yet.", "Blastlog.Muted"));
    else {
      body.Children.Add(Kit.Text($"Synced {run.At.LocalDateTime:g}.", "Blastlog.Muted").Also(t => t.Margin = new Thickness(0, 0, 0, 14)));
      var inPlaynite = run.Changes.Where(c => c.Side == "playnite").OrderBy(c => c.Name).ToList();
      var onBlastlog = run.Changes.Where(c => c.Side == "blastlog").OrderBy(c => c.Name).ToList();
      body.Children.Add(Group($"In Playnite ({inPlaynite.Count})", "Status and score set to match Blastlog. Undo on Blastlog doesn't change these back.",
        inPlaynite.Select(c => Line(c.Name ?? plugin.GameName(c.PlayniteGameId), c.StatusBefore != null || c.StatusAfter != null ? $"{Status(plugin, c.StatusBefore)} → {Status(plugin, c.StatusAfter)}" : null,
          c.ScoreChanged ? $"score {Score(c.ScoreBefore)} → {Score(c.ScoreAfter)}" : null))));
      body.Children.Add(Group($"On Blastlog ({onBlastlog.Count})", "Status and stars sent to your Blastlog library. Undo the last sync puts these back.",
        onBlastlog.Select(c => Line(c.Name ?? "a game", c.StatusAfter != null ? $"{Words(c.StatusBefore)} → {Words(c.StatusAfter)}" : null,
          c.ScoreChanged ? $"stars {Stars(c.ScoreBefore)} → {Stars(c.ScoreAfter)}" : null))));
      if (run.PlaytimeSent > 0) body.Children.Add(Kit.Card(Kit.Text($"Playtime: {run.PlaytimeSent} reading{(run.PlaytimeSent == 1 ? "" : "s")} sent to Blastlog as background information. They don't change any status or stars, and Undo withdraws them.", "Blastlog.Muted")));
      if (run.Changes.Count == 0 && run.PlaytimeSent == 0) body.Children.Add(Kit.Text("Nothing changed: both sides already agreed.", "Blastlog.Muted"));
    }
    var root = new DockPanel();
    root.Resources.MergedDictionaries.Add(Kit.Styles);
    root.SetResourceReference(Panel.BackgroundProperty, "Blastlog.Bg");
    var close = Kit.Button("Close", () => window.Close(), "Blastlog.Ghost"); close.HorizontalAlignment = HorizontalAlignment.Right; close.Margin = new Thickness(28, 8, 28, 20);
    DockPanel.SetDock(close, Dock.Bottom); root.Children.Add(close);
    root.Children.Add(new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
    window.Content = root;
    window.ShowDialog();
  }

  static FrameworkElement Group(string title, string meaning, System.Collections.Generic.IEnumerable<FrameworkElement> lines) {
    var panel = new StackPanel();
    panel.Children.Add(Kit.Text(title, "Blastlog.CardTitle"));
    panel.Children.Add(Kit.Text(meaning, "Blastlog.Small").Also(t => t.Margin = new Thickness(0, 2, 0, 10)));
    var any = false; foreach (var l in lines) { panel.Children.Add(l); any = true; }
    if (!any) panel.Children.Add(Kit.Text("None.", "Blastlog.Small"));
    return Kit.Card(panel);
  }
  static FrameworkElement Line(string name, string status, string score) {
    var t = new TextBlock { TextWrapping = TextWrapping.Wrap, Style = Kit.Style("Blastlog.Body"), Margin = new Thickness(0, 3, 0, 3) };
    t.Inlines.Add(new System.Windows.Documents.Run(name) { FontWeight = FontWeights.SemiBold });
    var detail = string.Join(" · ", new[] { status, score }.Where(x => x != null));
    if (detail != "") { var r = new System.Windows.Documents.Run("   " + detail); r.SetResourceReference(TextBlock.ForegroundProperty, "Blastlog.TextMuted"); t.Inlines.Add(r); }
    return t;
  }
  static string Status(BlastlogPlugin plugin, string statusId) => statusId == null ? "no status" : plugin.StatusName(statusId);
  static string Score(int? s) => s == null ? "none" : s.ToString();
  static string Stars(int? s) => s == null ? "none" : s + "★";
  static string Words(string status) => status == null ? "no status" : status.Replace('_', ' ');
}
}
