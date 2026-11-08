using System;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Blastlog.Playnite.Ui;
using Playnite.SDK;

namespace Blastlog.Playnite.Screens {
/// <summary>The site's collection form pieces (LibraryCollections.tsx: .coll-small, .coll-rename/.coll-picker inputs, .rating-control).</summary>
static class CollectionUi {
  static readonly ILogger Log = LogManager.GetLogger();

  /// <summary>The site's quiet .coll-small control: muted words, 44px tall.</summary>
  public static Button Small(string label, Action onClick, string name = null) {
    var b = Fingerprint.LinkButton(label, onClick);
    b.FontSize = 14; b.VerticalAlignment = VerticalAlignment.Center;
    if (name != null) AutomationProperties.SetName(b, name);
    return b;
  }

  /// <summary>A one-line text field in the site's input box (panel background, line border, 44px), with a placeholder.</summary>
  public static Border Field(TextBox box, string placeholder, string name, double width = 300) {
    box.MaxLength = 120;
    AutomationProperties.SetName(box, name);
    var hint = Kit.Text(placeholder ?? "", "Blastlog.Muted"); hint.IsHitTestVisible = false; hint.VerticalAlignment = VerticalAlignment.Center;
    void Hint() => hint.Visibility = string.IsNullOrEmpty(box.Text) ? Visibility.Visible : Visibility.Collapsed;
    box.TextChanged += (s, e) => { try { Hint(); } catch (Exception ex) { Log.Error(ex, "Blastlog collection field"); } };
    Hint();
    var grid = new Grid(); grid.Children.Add(hint); grid.Children.Add(box);
    var frame = new Border { Child = grid, CornerRadius = new CornerRadius(10), BorderThickness = new Thickness(1), Padding = new Thickness(12, 0, 12, 0), MinHeight = 44, Width = width, VerticalAlignment = VerticalAlignment.Center };
    frame.SetResourceReference(Border.BackgroundProperty, "Blastlog.Surface2"); frame.SetResourceReference(Border.BorderBrushProperty, "Blastlog.Line");
    return frame;
  }

  /// <summary>A many-line text box (the site's .coll-take textarea) with its placeholder.</summary>
  public static FrameworkElement Area(TextBox box, string placeholder, string name) {
    box.AcceptsReturn = true; box.TextWrapping = TextWrapping.Wrap; box.MinHeight = 150; box.MaxLength = 10000; box.FontSize = 15;
    box.Padding = new Thickness(12, 10, 12, 10); box.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
    box.SetResourceReference(Control.FontFamilyProperty, "Blastlog.Font"); box.SetResourceReference(Control.BackgroundProperty, "Blastlog.Surface2");
    box.SetResourceReference(Control.ForegroundProperty, "Blastlog.Text"); box.SetResourceReference(Control.BorderBrushProperty, "Blastlog.Line");
    box.SetResourceReference(TextBoxBase.CaretBrushProperty, "Blastlog.Text");
    AutomationProperties.SetName(box, name);
    var hint = Kit.Text(placeholder, "Blastlog.Muted"); hint.IsHitTestVisible = false; hint.Margin = new Thickness(15, 11, 15, 0); hint.VerticalAlignment = VerticalAlignment.Top; hint.TextWrapping = TextWrapping.Wrap;
    void Hint() => hint.Visibility = string.IsNullOrEmpty(box.Text) ? Visibility.Visible : Visibility.Collapsed;
    box.TextChanged += (s, e) => { try { Hint(); } catch (Exception ex) { Log.Error(ex, "Blastlog collection review"); } };
    Hint();
    var grid = new Grid(); grid.Children.Add(box); grid.Children.Add(hint);
    return grid;
  }

  /// <summary>The site's .rating-control: five star buttons; pressing the current one clears it.</summary>
  public static FrameworkElement Stars(int? rating, bool enabled, Action<int?> pick) {
    var row = new StackPanel { Orientation = Orientation.Horizontal };
    AutomationProperties.SetName(row, "Overall stars");
    for (var i = 1; i <= 5; i++) {
      var value = i; var on = (rating ?? 0) >= value;
      var b = new Button { Content = "★", Style = Kit.Style("Blastlog.Primary"), FontSize = 25, Width = 49, Height = 45, Padding = new Thickness(0), Margin = new Thickness(0, 0, 8, 0), IsEnabled = enabled };
      b.SetResourceReference(Control.BackgroundProperty, on ? "Blastlog.Game" : "Blastlog.Surface2");
      b.SetResourceReference(Control.ForegroundProperty, on ? "Blastlog.OnGame" : "Blastlog.Edge");
      AutomationProperties.SetName(b, $"{value} star{(value == 1 ? "" : "s")}");
      if (rating == value) AutomationProperties.SetItemStatus(b, "Selected");
      b.Click += (s, e) => { try { pick(rating == value ? (int?)null : value); } catch (Exception ex) { Log.Error(ex, "Blastlog collection stars"); } };
      row.Children.Add(b);
    }
    return row;
  }

  /// <summary>
  /// The player's usual colour, for pages that wear no game of their own (the trait and collection pages): the site's
  /// accent there is Next up's game (App.tsx setAccent from next-up), Blastlog green until it's known or with no Next up.
  /// </summary>
  public static async void WearUsual(FrameworkElement element, BlastlogPlugin plugin) {
    Kit.GameScope(element, Blastlog.Sync.Core.Design.GameColour.BrandGreen);
    try {
      var game = (await plugin.Mirror("next-up?mode=recommended", false)).Value?["entry"]?["game"] as Newtonsoft.Json.Linq.JObject;
      if (game != null && plugin.Theme != null) Kit.GameScope(element, await plugin.Theme.SeedFor(game.ToObject<Blastlog.Sync.Core.Game>()));
    } catch (Exception e) { Log.Warn("Blastlog: usual colour: " + e.Message); }
  }

  /// <summary>An error line under a form (the site's .form-error).</summary>
  public static TextBlock Error(string words) =>
    Kit.Text(words ?? "", "Blastlog.Body").Also(t => { t.SetResourceReference(TextBlock.ForegroundProperty, "Blastlog.Danger"); t.FontSize = 14; t.TextWrapping = TextWrapping.Wrap; t.Margin = new Thickness(0, 6, 0, 0); });
}
}
