using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Playnite.SDK;

namespace Blastlog.Playnite.Ui {
/// <summary>
/// The rest of the site's sidebar around Playnite's menu (apps/web/src/App.tsx: Brand, FriendsNav, SidebarCompletion,
/// FeedbackButton, AccountMenu, SiteCredit). The Blastlog theme's Views/Sidebar.xaml places empty hosts by name
/// (Blastlog_SideName, Blastlog_SideFriends, Blastlog_SideCompletion, Blastlog_SideFooter); this finds them in the main
/// window and fills them. Under another theme there are no hosts and nothing changes.
///
/// It also gathers Playnite's own menu items (Library, Statistics, other add-ons' views) under a divider in one
/// collapsible "Playnite" group (Charlie, 2026-11-07: make it really look like native Blastlog), collapsed by default and
/// remembered in sidebar.json. A selected Playnite item stays in view while the group is collapsed (the Blastlog game
/// pages switch to Playnite's Library). The plugin setting "Hide Playnite's menu items" hides the group's buttons
/// entirely; the items themselves stay in Playnite's list, so its Library view keeps working (Nav.Entry switches to
/// it), and the footer then offers "Playnite library" for installing and launching.
/// </summary>
public static class SidebarChrome {
  static readonly ILogger Log = LogManager.GetLogger();
  static BlastlogPlugin plugin;
  static DispatcherTimer watch, refresh;
  static ContentControl nameHost, friendsHost, completionHost, footerHost;
  static Panel panel;
  static Border groupHeader;
  static readonly HashSet<FrameworkElement> seen = new HashSet<FrameworkElement>();
  static readonly HashSet<INotifyPropertyChanged> watched = new HashSet<INotifyPropertyChanged>();
  static List<JObject> friends = new List<JObject>();
  static string openFriend, displayName;
  static bool demo, loading, logged;
  static double? rate;
  static State state = new State();

  sealed class State { public bool PlayniteOpen { get; set; } }
  static string StatePath => Path.Combine(plugin.GetPluginUserDataPath(), "sidebar.json");

  public static void Start(BlastlogPlugin owner) {
    plugin = owner;
    try { if (File.Exists(StatePath)) state = JsonConvert.DeserializeObject<State>(File.ReadAllText(StatePath)) ?? new State(); } catch (Exception) { state = new State(); }
    // The sidebar is drawn while the main window opens, and drawn again when Playnite changes its items: check
    // for the hosts and the group every second (cheap: nothing happens while they're in place).
    watch = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
    watch.Tick += (s, e) => Guard(Attach, "attach");
    watch.Start();
    // Name, friends and completion: a cached read (the mirror keeps them 10 minutes), again whenever Playnite comes back
    // to the front.
    refresh = new DispatcherTimer { Interval = TimeSpan.FromMinutes(5) };
    refresh.Tick += (s, e) => Load(false);
    refresh.Start();
    if (Application.Current?.MainWindow != null) Application.Current.MainWindow.Activated += (s, e) => Load(false);
  }

  /// <summary>Called after the plugin's settings are saved (token, "Hide Playnite's menu items").</summary>
  public static void SettingsChanged() => Guard(() => { Group(); Footer(); Load(true); }, "settings");

  static bool HidePlaynite => plugin?.HidePlayniteItems == true;

  static void Guard(Action act, string what) { try { act(); } catch (Exception e) { Log.Error(e, "Blastlog sidebar " + what); } }

  // ---- Finding the hosts ----
  static void Attach() {
    var window = Application.Current?.MainWindow; if (window == null) return;
    var footer = Find(window, "Blastlog_SideFooter") as ContentControl;
    if (footer == null) return; // another theme
    if (footer != footerHost) {
      footerHost = footer;
      nameHost = Find(window, "Blastlog_SideName") as ContentControl;
      friendsHost = Find(window, "Blastlog_SideFriends") as ContentControl;
      completionHost = Find(window, "Blastlog_SideCompletion") as ContentControl;
      foreach (var host in new[] { nameHost, friendsHost, completionHost, footerHost }) if (host != null && !host.Resources.MergedDictionaries.Contains(Kit.Styles)) host.Resources.MergedDictionaries.Add(Kit.Styles);
      Name(); Friends(); Completion(); Footer();
      Load(false);
    }
    var items = Find(window, "PART_PanelSideBarItems") as Panel;
    if (items != panel) seen.Clear();
    if (items != panel || (groupHeader != null && groupHeader.Parent != panel) || Unplaced()) { panel = items; Group(); }
  }

  /// <summary>True when Playnite drew a button the group hasn't sorted yet (it redraws them all on any change).</summary>
  static bool Unplaced() => panel != null && panel.Children.OfType<FrameworkElement>().Any(c => c != groupHeader && !seen.Contains(c));

  // ---- Data ----
  static async void Load(bool fresh) {
    if (plugin == null || footerHost == null || loading) return;
    loading = true;
    try {
      var me = (await plugin.Mirror("me", fresh)).Value;
      displayName = (string)me?["user"]?["displayName"];
      demo = me?["server"]?["demo"]?.Type == JTokenType.Boolean && (bool)me["server"]["demo"];
      var f = (await plugin.Mirror("friends", fresh)).Value;
      friends = (f?["friends"] as JArray ?? new JArray()).OfType<JObject>().ToList();
      var dash = (await plugin.Mirror("dashboard", fresh)).Value;
      var stats = dash?["stats"] as JObject;
      if (stats != null) {
        double completed = (double?)stats["completed"] ?? 0, backlog = (double?)stats["backlog"] ?? 0;
        rate = completed + backlog > 0 ? Math.Round(completed / (completed + backlog) * 100, MidpointRounding.AwayFromZero) : 0;
      } else rate = null;
      Name(); Friends(); Completion(); Footer();
    } catch (Exception e) { Log.Error(e, "Blastlog sidebar load"); }
    finally { loading = false; }
  }

  // ---- Brand: the player's name under "Blastlog" ----
  static void Name() {
    if (nameHost == null) return;
    nameHost.Content = string.IsNullOrEmpty(displayName) ? null : Words(displayName, 14, FontWeights.Normal, "Blastlog.TextMuted").Also(t => { t.Margin = new Thickness(0, 2, 0, 0); t.TextTrimming = TextTrimming.CharacterEllipsis; });
  }

  // ---- Friends (FriendsNav) ----
  static void Friends() {
    if (friendsHost == null) return;
    if (plugin?.Configured != true) { friendsHost.Content = null; return; }
    var nav = new StackPanel();
    var box = new Border { Child = nav, BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(0, 22, 0, 0), Margin = new Thickness(0, 21, 0, 0) };
    box.SetResourceReference(Border.BorderBrushProperty, "Blastlog.Line");
    nav.Children.Add(Label("Friends"));
    foreach (var friend in friends) {
      var id = (string)friend["id"]; var name = (string)friend["displayName"] ?? "Someone";
      if (id == null) continue;
      var open = openFriend == id;
      var item = new Border { CornerRadius = new CornerRadius(12), Margin = new Thickness(0, 0, 0, 2) };
      if (open) item.SetResourceReference(Border.BackgroundProperty, "Blastlog.Surface");
      var column = new StackPanel(); item.Child = column;
      var face = Face(name, (string)friend["avatarUrl"]);
      var caret = Icons.Make("<path d=\"m6 9 6 6 6-6\" />", 15); caret.Opacity = 0.55;
      if (open) caret.RenderTransform = new RotateTransform(180, 7.5, 7.5);
      var row = new DockPanel { LastChildFill = true };
      DockPanel.SetDock(caret, Dock.Right); caret.VerticalAlignment = VerticalAlignment.Center;
      DockPanel.SetDock(face, Dock.Left);
      row.Children.Add(caret); row.Children.Add(face);
      row.Children.Add(Words(name, 14, FontWeights.SemiBold).Also(t => { t.Margin = new Thickness(10, 0, 8, 0); t.TextTrimming = TextTrimming.CharacterEllipsis; t.VerticalAlignment = VerticalAlignment.Center; }));
      var toggle = Row(row, () => { openFriend = openFriend == id ? null : id; Friends(); }, name, 12, new Thickness(10, 8, 10, 8));
      AutomationProperties.SetName(toggle, name + (open ? ", open" : ""));
      column.Children.Add(toggle);
      if (open) {
        var links = new StackPanel { Margin = new Thickness(22, 2, 6, 8) };
        foreach (var page in new[] { new[] { "dashboard", "Dashboard", Icons.Dashboard }, new[] { "library", "Library", Icons.Library }, new[] { "profile", "Taste", ProfileIcon } }) {
          var p = page[0];
          var link = Row(IconWords(page[2], page[1], 15, 11, FontWeights.SemiBold), () => Nav.FriendPage(plugin, id, p), page[1] + " of " + name, 0, new Thickness(12, 8, 12, 8));
          link.BorderThickness = new Thickness(1, 0, 0, 0); link.SetResourceReference(Control.BorderBrushProperty, "Blastlog.Line");
          link.Template = RowTemplate("0,10,10,0");
          links.Children.Add(link);
        }
        column.Children.Add(links);
      }
      nav.Children.Add(item);
    }
    if (friends.Count == 0) nav.Children.Add(Words("No friends here yet.", 14, FontWeights.Normal, "Blastlog.TextMuted").Also(t => t.Margin = new Thickness(10, 4, 10, 4)));
    var manage = Row(IconWords(PeopleIcon, friends.Count > 0 ? "Add or manage friends" : "Add a friend", 15, 10, FontWeights.SemiBold), () => Nav.Friends(plugin),
      friends.Count > 0 ? "Add or manage friends" : "Add a friend", 12, new Thickness(10, 9, 10, 9));
    manage.Margin = new Thickness(0, 6, 0, 0);
    nav.Children.Add(manage);
    friendsHost.Content = box;
  }

  // ---- Completion rate (SidebarCompletion) ----
  static void Completion() {
    if (completionHost == null) return;
    if (rate == null || plugin?.Configured != true) { completionHost.Content = null; return; }
    var box = new StackPanel { Margin = new Thickness(8, 16, 8, 12) };
    var meta = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
    var value = Words(rate.Value.ToString("0") + "%", 14, FontWeights.ExtraBold, "Blastlog.TextMuted"); DockPanel.SetDock(value, Dock.Right);
    meta.Children.Add(value); meta.Children.Add(Words("Completion rate", 14, FontWeights.ExtraBold, "Blastlog.TextMuted"));
    box.Children.Add(meta);
    var track = new Grid { Height = 8, ClipToBounds = true };
    track.Children.Add(new Border { CornerRadius = new CornerRadius(4) }.Also(b => b.SetResourceReference(Border.BackgroundProperty, "Blastlog.Surface2")));
    var fill = new Border { CornerRadius = new CornerRadius(4), HorizontalAlignment = HorizontalAlignment.Left };
    fill.SetResourceReference(Border.BackgroundProperty, "Blastlog.Game");
    var r = rate.Value / 100.0;
    track.SizeChanged += (s, e) => fill.Width = Math.Max(0, track.ActualWidth * r);
    track.Children.Add(fill);
    AutomationProperties.SetName(track, $"Completion rate {rate.Value:0}%");
    box.Children.Add(track);
    completionHost.Content = box;
  }

  // ---- Footer: Send feedback, Settings, Playnite library (when hidden), the IGDB credit ----
  static void Footer() {
    if (footerHost == null) return;
    var stack = new StackPanel();
    var box = new Border { Child = stack, BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(4, 12, 4, 4), Margin = new Thickness(0, 8, 0, 0) };
    box.SetResourceReference(Border.BorderBrushProperty, "Blastlog.Line");
    // The site leaves feedback out of the demo, whose visitors' notes would reach no one.
    if (plugin?.Configured == true && !demo)
      stack.Children.Add(FooterRow(FeedbackIcon, "Send feedback", null, () => Nav.Feedback(plugin, CurrentPage())));
    stack.Children.Add(FooterRow(SettingsIcon, "Settings", "▸", () => Nav.Settings(plugin)));
    if (HidePlaynite) stack.Children.Add(FooterRow(Icons.Gamepad, "Playnite library", null, () => Nav.PlayniteLibrary(plugin)));
    stack.Children.Add(Credit());
    footerHost.Content = box;
  }

  static Button FooterRow(string icon, string label, string after, Action click) {
    var content = new DockPanel { LastChildFill = true };
    if (after != null) { var a = Words(after, 14, FontWeights.Normal, "Blastlog.TextMuted"); DockPanel.SetDock(a, Dock.Right); content.Children.Add(a); }
    content.Children.Add(IconWords(icon, label, 18, 10, FontWeights.Bold));
    var b = Row(content, click, label, 10, new Thickness(10, 0, 10, 0));
    b.MinHeight = 44; b.SetResourceReference(Control.ForegroundProperty, "Blastlog.Text");
    return b;
  }

  static TextBlock Credit() {
    var t = Words("", 14, FontWeights.Normal, "Blastlog.TextMuted");
    t.TextWrapping = TextWrapping.Wrap; t.LineHeight = 21; t.Margin = new Thickness(10, 4, 10, 0);
    t.Inlines.Clear();
    t.Inlines.Add(new Run("Game data and images from "));
    t.Inlines.Add(Link("IGDB.com", "https://www.igdb.com"));
    foreach (var page in new[] { new[] { "About", "/about" }, new[] { "Privacy", "/privacy" }, new[] { "Terms", "/terms" } }) {
      t.Inlines.Add(new Run(" · "));
      var path = page[1];
      t.Inlines.Add(Link(page[0], null, () => Nav.Site(plugin, path)));
    }
    return t;
  }

  static Hyperlink Link(string text, string url, Action click = null) {
    var link = new Hyperlink(new Run(text)) { TextDecorations = TextDecorations.Underline, Cursor = Cursors.Hand };
    link.SetResourceReference(TextElement.ForegroundProperty, "Blastlog.TextMuted");
    link.MouseEnter += (s, e) => link.SetResourceReference(TextElement.ForegroundProperty, "Blastlog.Text");
    link.MouseLeave += (s, e) => link.SetResourceReference(TextElement.ForegroundProperty, "Blastlog.TextMuted");
    link.Click += (s, e) => Guard(() => { if (click != null) click(); else UiKit.Open(url); }, "link " + text);
    return link;
  }

  /// <summary>The open view for a feedback note ("dashboard"); Blastlog stores it as /playnite/dashboard.</summary>
  static string CurrentPage() {
    try {
      foreach (var w in SidebarWrappers()) {
        if (!(w.GetType().GetProperty("Selected")?.GetValue(w) is bool on) || !on) continue;
        var title = SidebarOrder.Clean(SidebarOrder.SideOf(w)?.Title);
        if (!string.IsNullOrEmpty(title)) return string.Join("-", title.ToLowerInvariant().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries));
      }
    } catch (Exception) { }
    return "library";
  }

  // ---- The "Playnite" group ----
  static IEnumerable<object> SidebarWrappers() {
    var model = Application.Current?.MainWindow?.DataContext;
    return model?.GetType().GetProperty("SidebarItems")?.GetValue(model) is IEnumerable list ? list.Cast<object>().Where(w => w != null) : Enumerable.Empty<object>();
  }

  static void Group() {
    if (panel == null || footerHost == null) return;
    if (groupHeader?.Parent is Panel old) old.Children.Remove(groupHeader);
    var buttons = panel.Children.OfType<FrameworkElement>().ToList();
    var ours = buttons.Where(b => SidebarOrder.IsBlastlog(b.DataContext)).ToList();
    var theirs = buttons.Where(b => !SidebarOrder.IsBlastlog(b.DataContext)).ToList();
    seen.Clear(); foreach (var b in buttons) seen.Add(b);
    if (theirs.Count == 0) return;
    if (groupHeader == null) groupHeader = Header();
    Retitle();
    groupHeader.Visibility = HidePlaynite ? Visibility.Collapsed : Visibility.Visible;
    // Playnite's buttons sit after Blastlog's (Ui/SidebarOrder.cs); the header goes between them.
    var at = ours.Count == 0 ? 0 : panel.Children.IndexOf(ours.Last()) + 1;
    panel.Children.Insert(Math.Min(at, panel.Children.Count), groupHeader);
    foreach (var b in theirs) {
      if (b.DataContext is INotifyPropertyChanged n && watched.Add(n))
        n.PropertyChanged += (s, e) => { if (e.PropertyName == "Selected") Guard(Show, "selection"); };
    }
    Show();
  }

  /// <summary>Shows or hides Playnite's buttons for the group's state; a selected one shows while collapsed.</summary>
  static void Show() {
    if (panel == null) return;
    foreach (var b in panel.Children.OfType<FrameworkElement>()) {
      if (b == groupHeader || SidebarOrder.IsBlastlog(b.DataContext)) continue;
      var selected = b.DataContext?.GetType().GetProperty("Selected")?.GetValue(b.DataContext) as bool? == true;
      b.Visibility = HidePlaynite ? Visibility.Collapsed : state.PlayniteOpen || selected ? Visibility.Visible : Visibility.Collapsed;
      if (!logged && b.DataContext != null) Log.Info("Blastlog sidebar item " + SidebarOrder.Clean(SidebarOrder.SideOf(b.DataContext)?.Title) + ": " + string.Join(", ", b.DataContext.GetType().GetProperties().Select(p => { try { return p.Name + "=" + p.GetValue(b.DataContext); } catch { return p.Name; } })));
    }
    logged = true;
  }

  static Border Header() {
    var label = Words("Playnite", 14, FontWeights.ExtraBold, "Blastlog.TextMuted");
    label.VerticalAlignment = VerticalAlignment.Center;
    var caret = Icons.Make("<path d=\"m6 9 6 6 6-6\" />", 15); caret.Opacity = 0.55; caret.VerticalAlignment = VerticalAlignment.Center; caret.Name = "Caret";
    DockPanel.SetDock(caret, Dock.Right);
    var row = new DockPanel { LastChildFill = true }; row.Children.Add(caret); row.Children.Add(label);
    var b = Row(row, () => {
      state.PlayniteOpen = !state.PlayniteOpen;
      try { File.WriteAllText(StatePath, JsonConvert.SerializeObject(state)); } catch (Exception e) { Log.Warn("Blastlog: couldn't save the sidebar state: " + e.Message); }
      Retitle(); Show();
    }, "Playnite", 12, new Thickness(10, 0, 10, 0));
    b.MinHeight = 44;
    // The divider over the group (the site's friends-nav border-top).
    var wrap = new Border { BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(0, 10, 0, 0), Margin = new Thickness(0, 16, 0, 4) };
    wrap.SetResourceReference(Border.BorderBrushProperty, "Blastlog.Line");
    wrap.Child = b;
    wrap.Resources.MergedDictionaries.Add(Kit.Styles);
    return wrap;
  }

  static void Retitle() {
    if (!(groupHeader?.Child is Button b)) return;
    AutomationProperties.SetName(b, "Playnite's menu, " + (state.PlayniteOpen ? "open" : "closed"));
    b.ToolTip = state.PlayniteOpen ? "Hide Playnite's own menu items" : "Show Playnite's own menu items (Library, Statistics and other add-ons)";
    if (b.Content is DockPanel row && row.Children.OfType<FrameworkElement>().FirstOrDefault(c => c.Name == "Caret") is FrameworkElement caret)
      caret.RenderTransform = state.PlayniteOpen ? new RotateTransform(180, 7.5, 7.5) : null;
  }

  // ---- Small builders ----
  public const string ProfileIcon = "<circle cx=\"12\" cy=\"8\" r=\"4\" /><path d=\"M4 21a8 8 0 0 1 16 0\" />";
  public const string PeopleIcon = "<circle cx=\"9\" cy=\"8\" r=\"3.5\" /><path d=\"M2.5 20a6.5 6.5 0 0 1 13 0\" /><path d=\"M16 4.6a3.5 3.5 0 0 1 0 6.8\" /><path d=\"M18 14.2a6.5 6.5 0 0 1 3.5 5.8\" />";
  const string FeedbackIcon = "<path d=\"M21 15a2 2 0 0 1-2 2H7l-4 4V5a2 2 0 0 1 2-2h14a2 2 0 0 1 2 2z\" />";
  const string SettingsIcon = "<circle cx=\"12\" cy=\"12\" r=\"3\" /><path d=\"M19.4 15a1.65 1.65 0 0 0 .33 1.82l.06.06a2 2 0 1 1-2.83 2.83l-.06-.06a1.65 1.65 0 0 0-1.82-.33 1.65 1.65 0 0 0-1 1.51V21a2 2 0 0 1-4 0v-.09A1.65 1.65 0 0 0 9 19.4a1.65 1.65 0 0 0-1.82.33l-.06.06a2 2 0 1 1-2.83-2.83l.06-.06A1.65 1.65 0 0 0 4.68 15a1.65 1.65 0 0 0-1.51-1H3a2 2 0 0 1 0-4h.09A1.65 1.65 0 0 0 4.6 9a1.65 1.65 0 0 0-.33-1.82l-.06-.06a2 2 0 1 1 2.83-2.83l.06.06A1.65 1.65 0 0 0 9 4.68a1.65 1.65 0 0 0 1-1.51V3a2 2 0 0 1 4 0v.09a1.65 1.65 0 0 0 1 1.51 1.65 1.65 0 0 0 1.82-.33l.06-.06a2 2 0 1 1 2.83 2.83l-.06.06A1.65 1.65 0 0 0 19.4 9a1.65 1.65 0 0 0 1.51 1H21a2 2 0 0 1 0 4h-.09a1.65 1.65 0 0 0-1.51 1z\" />";

  /// <summary>Text in the sidebar's font; the colour follows the button it's in unless <paramref name="brush"/> is given.</summary>
  static TextBlock Words(string text, double size, FontWeight weight, string brush = null) {
    var t = new TextBlock { Text = text, FontSize = size, FontWeight = weight };
    t.SetResourceReference(TextBlock.FontFamilyProperty, "Blastlog.Font");
    if (brush != null) t.SetResourceReference(TextBlock.ForegroundProperty, brush);
    return t;
  }

  /// <summary>A section label ("Friends"), like the theme's "My Blastlog".</summary>
  static TextBlock Label(string text) => Words(text, 14, FontWeights.ExtraBold, "Blastlog.TextMuted").Also(t => t.Margin = new Thickness(10, 0, 10, 10));

  static StackPanel IconWords(string svg, string text, double iconSize, double gap, FontWeight weight) {
    var row = new StackPanel { Orientation = Orientation.Horizontal };
    var icon = Icons.Make(svg, iconSize); icon.VerticalAlignment = VerticalAlignment.Center;
    row.Children.Add(icon);
    row.Children.Add(Words(text, 14, weight).Also(t => { t.Margin = new Thickness(gap, 0, 0, 0); t.VerticalAlignment = VerticalAlignment.Center; t.TextTrimming = TextTrimming.CharacterEllipsis; }));
    return row;
  }

  /// <summary>A menu row: muted text that turns to body text on a Surface 2 fill when hovered; Blastlog's focus ring.</summary>
  static Button Row(object content, Action click, string name, double radius, Thickness padding) {
    var b = new Button { Content = content, Template = RowTemplate(radius.ToString(System.Globalization.CultureInfo.InvariantCulture)), Padding = padding, Cursor = Cursors.Hand,
      HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch, Background = Brushes.Transparent,
      FocusVisualStyle = Kit.Style("Blastlog.FocusRing") };
    b.SetResourceReference(Control.ForegroundProperty, "Blastlog.TextMuted");
    b.BorderThickness = new Thickness(0); // the theme's Button style would draw an outline
    AutomationProperties.SetName(b, name);
    b.Click += (s, e) => Guard(click, name);
    return b;
  }

  static readonly Dictionary<string, ControlTemplate> templates = new Dictionary<string, ControlTemplate>();
  static ControlTemplate RowTemplate(string radius) {
    if (templates.TryGetValue(radius, out var known)) return known;
    var xaml = "<ControlTemplate xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" TargetType=\"Button\">"
      + "<Border x:Name=\"B\" Background=\"{TemplateBinding Background}\" BorderBrush=\"{TemplateBinding BorderBrush}\" BorderThickness=\"{TemplateBinding BorderThickness}\" CornerRadius=\"" + radius + "\" Padding=\"{TemplateBinding Padding}\" MinHeight=\"{TemplateBinding MinHeight}\">"
      + "<ContentPresenter VerticalAlignment=\"Center\" HorizontalAlignment=\"{TemplateBinding HorizontalContentAlignment}\" /></Border>"
      + "<ControlTemplate.Triggers><Trigger Property=\"IsMouseOver\" Value=\"True\"><Setter TargetName=\"B\" Property=\"Background\" Value=\"{DynamicResource Blastlog.Surface2}\" />"
      + "<Setter Property=\"Foreground\" Value=\"{DynamicResource Blastlog.Text}\" /></Trigger></ControlTemplate.Triggers></ControlTemplate>";
    return templates[radius] = (ControlTemplate)XamlReader.Parse(xaml);
  }

  /// <summary>The site's friend-face: 26px, radius 8, the initial on Surface 2, or their picture when there is one.</summary>
  static FrameworkElement Face(string name, string url) {
    var letter = Words(string.IsNullOrEmpty(name) ? "?" : name.Substring(0, 1).ToUpperInvariant(), 14, FontWeights.ExtraBold, "Blastlog.Text");
    letter.HorizontalAlignment = HorizontalAlignment.Center; letter.VerticalAlignment = VerticalAlignment.Center;
    var face = new Border { Width = 26, Height = 26, CornerRadius = new CornerRadius(8), Child = letter, VerticalAlignment = VerticalAlignment.Center };
    face.SetResourceReference(Border.BackgroundProperty, "Blastlog.Surface2");
    if (!string.IsNullOrEmpty(url)) Picture(face, url);
    return face;
  }
  static async void Picture(Border face, string url) {
    try {
      var file = await global::Blastlog.Playnite.Screens.PnKit.Picture(plugin, url);
      if (file == null) return;
      var image = new BitmapImage(); image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad; image.UriSource = new Uri(file); image.DecodePixelWidth = 52; image.EndInit(); image.Freeze();
      face.Background = new ImageBrush(image) { Stretch = Stretch.UniformToFill }; face.Child = null;
    } catch (Exception e) { Log.Warn("Blastlog: friend picture didn't load: " + e.Message); }
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
