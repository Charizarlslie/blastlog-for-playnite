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
/// <summary>Pairing (docs/PLAN.md §4.1): every Playnite game with what matching found; exact ones ticked; Apply is one push.</summary>
public sealed class PairingWindow {
  public static bool? Show(BlastlogPlugin plugin, List<PairingChoice> choices) {
    var window = plugin.Api.Dialogs.CreateWindow(new WindowCreationOptions { ShowCloseButton = true, ShowMaximizeButton = true, ShowMinimizeButton = false });
    window.Title = "Pair games with Blastlog"; window.Width = 820; window.Height = 640; window.Owner = plugin.Api.Dialogs.GetCurrentAppWindow(); window.WindowStartupLocation = WindowStartupLocation.CenterOwner;
    var root = new DockPanel { Margin = new Thickness(20) };
    var top = new StackPanel(); DockPanel.SetDock(top, Dock.Top);
    top.Children.Add(UiKit.Text("Ticked rows are paired when you press Apply. Exact matches start ticked; check the rest. Nothing changes until Apply.", muted: true));
    top.Children.Add(UiKit.Text($"{choices.Count(c => c.Match.Result == "entry")} in your Blastlog library · {choices.Count(c => c.Match.Result == "catalogue")} to add · {choices.Count(c => c.Match.Result == "unknown")} not found · {choices.Count(c => c.Match.Result == "linked")} already paired{(choices.Any(c => c.Match.Result == "skipped") ? $" · {choices.Count(c => c.Match.Result == "skipped")} couldn't be sent" : "")}", muted: true));
    root.Children.Add(top);
    var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right }; DockPanel.SetDock(buttons, Dock.Bottom);
    buttons.Children.Add(UiKit.Button("Apply", () => { window.DialogResult = true; window.Close(); }));
    buttons.Children.Add(UiKit.Button("Cancel", () => { window.DialogResult = false; window.Close(); }));
    root.Children.Add(buttons);
    var list = new StackPanel();
    foreach (var group in choices.Where(c => c.Match.Result != "linked").GroupBy(c => c.Action)) {
      UiKit.Heading(list, group.Key, 16);
      foreach (var choice in group.OrderBy(c => c.Game.Name)) {
        var label = choice.Game.Name + (choice.Match.Result == "catalogue" && !string.Equals(choice.Match.Title, choice.Game.Name, StringComparison.OrdinalIgnoreCase) ? $" → {choice.Match.Title}" : "")
          + (choice.Match.Confidence == "likely" ? "  (likely: check it)" : "");
        var c = choice;
        if (choice.Match.Result == "skipped") list.Children.Add(UiKit.Text(choice.Game.Name + ": " + (choice.Problem ?? "Blastlog couldn't read it") + ". Fix it in Playnite and pair again.", muted: true));
        else list.Children.Add(UiKit.Check(label, choice.Ticked, v => c.Ticked = v));
      }
    }
    root.Children.Add(new ScrollViewer { Content = list, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
    window.Content = root;
    return window.ShowDialog();
  }
}

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
