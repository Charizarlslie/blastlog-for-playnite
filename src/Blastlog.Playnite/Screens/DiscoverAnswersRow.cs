using System;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Playnite.SDK;

namespace Blastlog.Playnite.Screens {
/// <summary>
/// The Discover game page's answers in one row, as the site lays them out there (`.discover-game-buttons`: Want to play,
/// I own it, Played it, Not for me side by side). <see cref="DiscoverAnswers"/> stacks Want to play above the rest, which
/// is right on a card; this lays its first view out flat each time it's shown, moving the same buttons (their clicks and
/// busy states stay theirs). The steps inside it (how far, stars, where you own it) are left as they are.
/// </summary>
static class DiscoverAnswersRow {
  static readonly ILogger Log = LogManager.GetLogger();

  public static DiscoverAnswers Inline(DiscoverAnswers answers) {
    var content = DependencyPropertyDescriptor.FromProperty(ContentControl.ContentProperty, typeof(ContentControl));
    void Flatten() {
      try {
        // The first view: a StackPanel holding Want to play and a WrapPanel with the other answers.
        if (!(answers.Content is StackPanel stack) || stack.Children.Count != 2 || !(stack.Children[0] is Button want) || !(stack.Children[1] is WrapPanel more)) return;
        var rest = more.Children.OfType<UIElement>().ToList();
        stack.Children.Clear(); more.Children.Clear();
        var row = new WrapPanel();
        want.HorizontalAlignment = HorizontalAlignment.Left; want.Margin = new Thickness(0, 0, 8, 8);
        row.Children.Add(want);
        foreach (var b in rest) { if (b is FrameworkElement f) f.Margin = new Thickness(0, 0, 8, 8); row.Children.Add(b); }
        answers.Content = row;
      } catch (Exception e) { Log.Error(e, "Blastlog Discover answers row"); }
    }
    EventHandler changed = (s, e) => Flatten();
    content.AddValueChanged(answers, changed);
    // The descriptor holds on to what it watches: let go when the page drops these answers.
    answers.Unloaded += (s, e) => { try { content.RemoveValueChanged(answers, changed); } catch (Exception ex) { Log.Error(ex, "Blastlog Discover answers row"); } };
    Flatten();
    return answers;
  }
}
}
