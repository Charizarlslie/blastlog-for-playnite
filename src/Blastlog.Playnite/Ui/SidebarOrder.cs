using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Playnite.SDK;
using Playnite.SDK.Plugins;

namespace Blastlog.Playnite.Ui {
/// <summary>
/// Blastlog's screens at the top of Playnite's sidebar, in the site's menu order, and Playnite's own items (Library,
/// Statistics, other add-ons' views) under them (Charlie, 2026-11-07: "blastlog up top, playnite at the bottom").
/// Playnite puts its own items first and has no setting or SDK call for the order, so this moves the main window's
/// sidebar items (the desktop view model's <c>SidebarItems</c>, as Ui/ScreenJump.cs finds them) by reflection, and the
/// sidebar's buttons too if Playnite drew them already. Anything it doesn't find (another Playnite version, fullscreen
/// mode) leaves the sidebar as Playnite made it. It runs again whenever Playnite changes the list.
/// </summary>
public static class SidebarOrder {
  static readonly ILogger Log = LogManager.GetLogger();
  static bool hooked, busy, reported;

  public static void Start() {
    // Playnite fills the sidebar while the main window opens; try then, and a few more times while it settles.
    var tries = 0; var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
    timer.Tick += (s, e) => { if (++tries >= 10 || Apply()) timer.Stop(); };
    timer.Start();
  }

  static string Clean(string title) => (title ?? "").Replace("​", "").Trim();
  static Dictionary<string, int> OurOrder() => Screens.All().ToDictionary(s => s.Title, s => s.Order);
  static SidebarItem SideOf(object wrapper) => wrapper?.GetType().GetProperty("SideItem")?.GetValue(wrapper) as SidebarItem;

  /// <summary>Puts Blastlog's items first; true once the sidebar was found (moved or already in order).</summary>
  static bool Apply() {
    if (busy) return false;
    busy = true;
    try {
      var model = Application.Current?.MainWindow?.DataContext;
      if (!(model?.GetType().GetProperty("SidebarItems")?.GetValue(model) is IList list) || list.Count == 0) return false;
      var ours = OurOrder();
      int Rank(object w) { var side = SideOf(w); return side != null && side.Type == SiderbarItemType.View && ours.TryGetValue(Clean(side.Title), out var o) ? o : int.MaxValue; }
      var items = list.Cast<object>().ToList();
      var wanted = items.Select((w, i) => (w, i)).OrderBy(x => Rank(x.w) == int.MaxValue ? 1 : 0).ThenBy(x => Rank(x.w)).ThenBy(x => x.i).Select(x => x.w).ToList();
      if (!reported) { reported = true; Log.Info("Blastlog sidebar: " + list.GetType().Name + ", " + string.Join(" | ", items.Select(w => Clean(SideOf(w)?.Title) + (Rank(w) < int.MaxValue ? "*" : "")))); }
      if (!wanted.SequenceEqual(items)) {
        var move = list.GetType().GetMethod("Move", new[] { typeof(int), typeof(int) });
        for (var i = 0; i < wanted.Count; i++) {
          var at = list.IndexOf(wanted[i]);
          if (at == i) continue;
          if (move != null) move.Invoke(list, new object[] { at, i }); else { list.RemoveAt(at); list.Insert(i, wanted[i]); }
        }
      }
      if (!hooked && list is INotifyCollectionChanged changes) {
        hooked = true;
        changes.CollectionChanged += (s, e) => {
          if (busy) return;
          Application.Current?.Dispatcher.BeginInvoke(new Action(() => { try { Apply(); } catch (Exception ex) { Log.Error(ex, "Blastlog sidebar order"); } }), DispatcherPriority.Background);
        };
      }
      OrderButtons(wanted);
      return true;
    } catch (Exception e) { Log.Error(e, "Blastlog: ordering the sidebar"); return true; }
    finally { busy = false; }
  }

  /// <summary>If the sidebar drew its buttons from the list once and didn't follow the move, move the buttons too.</summary>
  static void OrderButtons(List<object> wanted) {
    var window = Application.Current?.MainWindow; if (window == null) return;
    if (!(Find(window, "PART_PanelSideBarItems") is Panel panel) || panel.Children.Count == 0) return;
    var children = panel.Children.Cast<UIElement>().ToList();
    int Place(UIElement c) { var i = wanted.IndexOf((c as FrameworkElement)?.DataContext); return i < 0 ? int.MaxValue : i; }
    if (children.All(c => Place(c) == int.MaxValue)) return; // not bound to the items: nothing to go by
    var sorted = children.Select((c, i) => (c, i)).OrderBy(x => Place(x.c)).ThenBy(x => x.i).Select(x => x.c).ToList();
    if (sorted.SequenceEqual(children)) return;
    panel.Children.Clear();
    foreach (var c in sorted) panel.Children.Add(c);
  }

  static FrameworkElement Find(DependencyObject root, string name) {
    for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) {
      var child = VisualTreeHelper.GetChild(root, i);
      if (child is FrameworkElement f && f.Name == name) return f;
      var deeper = Find(child, name);
      if (deeper != null) return deeper;
    }
    return null;
  }
}
}
