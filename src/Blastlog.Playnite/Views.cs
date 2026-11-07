using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Blastlog.Sync.Core;
using Newtonsoft.Json.Linq;
using Playnite.SDK;
using Playnite.SDK.Controls;
using Playnite.SDK.Models;
using Game = Playnite.SDK.Models.Game;

namespace Blastlog.Playnite {
/// <summary>The preview of a sync: what goes to Blastlog, what changes in Playnite, what's held back or needs you.</summary>
public sealed class PreviewWindow {
  public static bool? Show(BlastlogPlugin plugin, SyncOutcome preview, Func<string,string> nameOf) {
    var window = plugin.Api.Dialogs.CreateWindow(new WindowCreationOptions { ShowCloseButton = true, ShowMaximizeButton = true, ShowMinimizeButton = false });
    window.Title = "Blastlog sync preview"; window.Width = 720; window.Height = 600; window.Owner = plugin.Api.Dialogs.GetCurrentAppWindow(); window.WindowStartupLocation = WindowStartupLocation.CenterOwner;
    var root = new DockPanel { Margin = new Thickness(20) }; var list = new StackPanel(); var plan = preview.Plan;
    var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right }; DockPanel.SetDock(buttons, Dock.Bottom);
    buttons.Children.Add(UiKit.Button(plan.IsEmpty ? "Close" : "Sync these", () => { window.DialogResult = !plan.IsEmpty; window.Close(); }));
    if (!plan.IsEmpty) buttons.Children.Add(UiKit.Button("Cancel", () => { window.DialogResult = false; window.Close(); }));
    root.Children.Add(buttons);
    if (plan.IsEmpty) list.Children.Add(UiKit.Text("Everything is already in step."));
    var rejected = preview.Response?.Results.Where(r => r.Result == "rejected" || r.Result == "conflict").ToDictionary(r => r.Kind + ":" + r.Ref) ?? new Dictionary<string, PushResult>();
    if (plan.Push.Changes.Count > 0) { UiKit.Heading(list, "To Blastlog", 16); foreach (var c in plan.Push.Changes) list.Children.Add(UiKit.Text(nameOf(c.EntryId) + ": " + string.Join(", ",
      new[] { c.Status != null ? "status " + c.Status.Replace('_', ' ') : null, c.SetRating ? (c.RatingValue == null ? "no rating" : c.RatingValue + "★") : null }.Where(x => x != null))
      + (rejected.TryGetValue("change:" + c.EntryId, out var r) ? $"  (won't go: {r.Reason ?? r.Result})" : ""))); }
    if (plan.Push.Playtime.Count > 0) list.Children.Add(UiKit.Text($"Playtime for {plan.Push.Playtime.Count} game(s)", muted: true));
    if (plan.PlayniteWrites.Count > 0) { UiKit.Heading(list, "In Playnite", 16); foreach (var w in plan.PlayniteWrites) list.Children.Add(UiKit.Text(plugin.GameName(w.GameId) + ": "
      + string.Join(", ", new[] { w.StatusId != null ? "status " + plugin.StatusName(w.StatusId) : null, w.SetScore ? "score " + (w.Score?.ToString() ?? "none") : null }.Where(x => x != null)))); }
    if (plan.RatingsHeldBack.Count > 0) { UiKit.Heading(list, "Waiting", 16); list.Children.Add(UiKit.Text("Ratings go to Blastlog once the game is finished: " + string.Join(", ", plan.RatingsHeldBack.Select(plugin.GameName)), muted: true)); }
    if (plan.Conflicts.Count > 0) { UiKit.Heading(list, "Changed on both sides (left as they are)", 16); foreach (var c in plan.Conflicts) list.Children.Add(UiKit.Text($"{c.Name}: {c.Field} is {c.Playnite ?? "none"} in Playnite, {c.Blastlog ?? "none"} on Blastlog")); }
    root.Children.Add(new ScrollViewer { Content = list, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
    window.Content = root;
    return window.ShowDialog();
  }
}

}
