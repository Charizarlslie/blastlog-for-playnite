using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Blastlog.Playnite.Ui;
using Newtonsoft.Json.Linq;

namespace Blastlog.Playnite.Screens {
/// <summary>
/// The player's game subscriptions (GET /sync/v1/subscriptions; the site's Subscriptions.tsx): Play next's "On my
/// subscriptions" filter and the "On Game Pass" chip on Play next and Discover cards.
/// </summary>
static class PlayNextSubscriptions {
  static readonly Dictionary<string, string> Short = new Dictionary<string, string> {
    ["game_pass_ultimate"] = "Game Pass", ["pc_game_pass"] = "PC Game Pass", ["ps_plus_extra"] = "PS Plus", ["ps_plus_premium"] = "PS Plus" };
  const string Ticket = "<path d=\"M2 9a3 3 0 0 1 0 6v2a2 2 0 0 0 2 2h16a2 2 0 0 0 2-2v-2a3 3 0 0 1 0-6V7a2 2 0 0 0-2-2H4a2 2 0 0 0-2 2Z\" /><path d=\"M13 5v2\" /><path d=\"M13 17v2\" /><path d=\"M13 11v2\" />";
  const string Clock = "<circle cx=\"12\" cy=\"12\" r=\"10\" /><polyline points=\"12 6 12 12 16 14\" />";

  static JToken Game(JObject subs, string gameId) => gameId == null ? null : subs?["games"]?[gameId];
  /// <summary>Whether a game is on one of the player's services (the site's onMyServices).</summary>
  public static bool OnMine(JObject subs, string gameId) => Game(subs, gameId).Strings("services").Count > 0;
  static List<string> Names(IEnumerable<string> keys) => keys.Select(k => Short.TryGetValue(k, out var n) ? n : k).Distinct().ToList();

  /// <summary>"On Game Pass · PS Plus", or "Leaving Game Pass soon" when every service of theirs is dropping it; null when none.</summary>
  public static FrameworkElement Chip(JObject subs, string gameId) {
    var game = Game(subs, gameId);
    var services = game.Strings("services");
    if (services.Count == 0) return null;
    // Leaving one service doesn't matter while another of theirs keeps it.
    var leaving = Names(game.Strings("leavingSoon"));
    var staying = Names(services).Where(n => !leaving.Contains(n)).ToList();
    var gone = staying.Count == 0;
    var brush = gone ? "Blastlog.Warn" : "Blastlog.GameText";
    var row = new StackPanel { Orientation = Orientation.Horizontal };
    var icon = Icons.Make(gone ? Clock : Ticket, 14, new SolidColorBrush()); icon.Margin = new Thickness(0, 0, 6, 0); icon.VerticalAlignment = VerticalAlignment.Center;
    foreach (var shape in ((Canvas)((Viewbox)icon).Child).Children.OfType<Shape>()) shape.SetResourceReference(Shape.StrokeProperty, brush);
    row.Children.Add(icon);
    var words = PnKit.Text(gone ? $"Leaving {string.Join(" and ", leaving)} soon" : "On " + string.Join(" · ", staying), "Blastlog.Small", gone ? "Blastlog.Warn" : "Blastlog.Text", FontWeights.Bold);
    row.Children.Add(words);
    var chip = new Border { Child = row, CornerRadius = new CornerRadius(999), Padding = new Thickness(10, 3, 10, 4), Margin = new Thickness(0, 0, 0, 6),
      HorizontalAlignment = HorizontalAlignment.Left, BorderThickness = new Thickness(1),
      ToolTip = gone ? $"{string.Join(" and ", leaving)} lists it as leaving soon" : $"Included with your {string.Join(" and ", staying)}" };
    chip.Res(Border.BackgroundProperty, "Blastlog.Surface2").Res(Border.BorderBrushProperty, gone ? "Blastlog.Warn" : "Blastlog.Line");
    return chip;
  }
}
}
