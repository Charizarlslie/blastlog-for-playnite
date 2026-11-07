using System;
using System.Collections;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Playnite.SDK;
using Playnite.SDK.Plugins;

namespace Blastlog.Playnite.Ui {
/// <summary>
/// Opens another Blastlog screen in Playnite's sidebar, as if the player had clicked it (Your taste, Play next's Sharpen
/// your picks). Playnite's SDK has no call for this, so it finds the main window's sidebar items (the desktop view
/// model's <c>SidebarItems</c>, each wrapping the <see cref="SidebarItem"/> the plugin gave it) by reflection and runs
/// the item's own command. When anything about that isn't there (another Playnite version, fullscreen mode) it returns
/// false and the caller opens the site instead, so a jump never fails silently.
/// </summary>
public static class ScreenJump {
  static readonly ILogger Log = LogManager.GetLogger();
  static DispatcherTimer seeking;

  /// <summary>
  /// Opens the screen titled <paramref name="title"/> (as its <see cref="IBlastlogScreen.Title"/>). With
  /// <paramref name="anchor"/>, scrolls to the element on it whose automation name is that, once it has loaded.
  /// </summary>
  public static bool To(string title, string anchor = null) {
    try {
      var model = Application.Current?.MainWindow?.DataContext;
      if (model == null || !(model.GetType().GetProperty("SidebarItems")?.GetValue(model) is IEnumerable items)) return false;
      foreach (var wrapper in items) {
        if (wrapper == null) continue;
        var side = wrapper.GetType().GetProperty("SideItem")?.GetValue(wrapper) as SidebarItem;
        if (side == null || side.Type != SiderbarItemType.View || Clean(side.Title) != title) continue;
        if (!(wrapper.GetType().GetProperty("Command")?.GetValue(wrapper) is ICommand command) || !command.CanExecute(null)) return false;
        command.Execute(null);
        if (anchor != null) Seek(anchor);
        return true;
      }
      return false;
    } catch (Exception e) { Log.Error(e, "Blastlog: jumping to " + title); return false; }
  }

  /// <summary>Sidebar titles carry zero-width spaces in front for their order (Kit.cs Screens.SidebarItem).</summary>
  static string Clean(string title) => (title ?? "").Replace("​", "").Trim();

  /// <summary>
  /// Screens show their cached copy first and rebuild when the fresh one comes, so the anchor is looked for over a few
  /// seconds and scrolled to again only if it was rebuilt (a new element), never after the player starts scrolling.
  /// </summary>
  static void Seek(string anchor) {
    seeking?.Stop();
    var started = DateTime.Now; FrameworkElement last = null; var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
    ScrollViewer moved = null; double movedTo = double.NaN;
    timer.Tick += (s, e) => {
      try {
        if (DateTime.Now - started > TimeSpan.FromSeconds(8)) { timer.Stop(); return; }
        // The player scrolled since we did: leave the page where they put it.
        if (moved != null && Math.Abs(moved.VerticalOffset - movedTo) > 2) { timer.Stop(); return; }
        var window = Application.Current?.MainWindow; if (window == null) { timer.Stop(); return; }
        var found = Find(window, anchor);
        if (found == null || found == last) return;
        var scroll = Ancestor<ScrollViewer>(found);
        if (scroll == null) { found.BringIntoView(); last = found; return; }
        var top = found.TransformToAncestor(scroll).Transform(new Point(0, 0)).Y + scroll.VerticalOffset - 16;
        scroll.ScrollToVerticalOffset(Math.Max(0, top));
        scroll.UpdateLayout();
        last = found; moved = scroll; movedTo = scroll.VerticalOffset;
      } catch (Exception ex) { timer.Stop(); Log.Error(ex, "Blastlog: scrolling to " + anchor); }
    };
    seeking = timer; timer.Start();
  }

  static FrameworkElement Find(DependencyObject root, string name) {
    var count = VisualTreeHelper.GetChildrenCount(root);
    for (var i = 0; i < count; i++) {
      var child = VisualTreeHelper.GetChild(root, i);
      if (child is FrameworkElement f && f.IsVisible && AutomationProperties.GetName(f) == name && f.ActualHeight > 0) return f;
      if (child is UIElement u && !u.IsVisible) continue;
      var deeper = Find(child, name);
      if (deeper != null) return deeper;
    }
    return null;
  }

  static T Ancestor<T>(DependencyObject d) where T : DependencyObject {
    for (d = d == null ? null : VisualTreeHelper.GetParent(d); d != null; d = VisualTreeHelper.GetParent(d)) if (d is T t) return t;
    return null;
  }
}
}
