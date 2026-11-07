using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Blastlog.Playnite.Ui;
using Playnite.SDK;
using static Blastlog.Playnite.Screens.TasteUi;

namespace Blastlog.Playnite.Screens {
/// <summary>
/// Shared pieces for the small pages the dashboard, Compare and Your taste link to (Quick start, Find a co-op game,
/// Friends): each is a Playnite window holding a Kit page, since none of them is in Blastlog's menu either.
/// </summary>
static class LinkPages {
  /// <summary>
  /// Opens a page in its own window over Playnite. <paramref name="build"/> gets a close action, so a button can leave
  /// the window before it jumps elsewhere. Esc closes it; <paramref name="closed"/> runs afterwards.
  /// </summary>
  public static void Show(BlastlogPlugin plugin, string title, Func<Action, FrameworkElement> build, Action closed = null, double width = 1040, double height = 760) {
    Window window = null;
    try {
      window = plugin.Api.Dialogs.CreateWindow(new WindowCreationOptions { ShowCloseButton = true, ShowMaximizeButton = true, ShowMinimizeButton = false });
      window.Title = title; window.Width = width; window.Height = height;
      window.Owner = plugin.Api.Dialogs.GetCurrentAppWindow();
      window.WindowStartupLocation = WindowStartupLocation.CenterOwner;
      var w = window;
      window.Content = Kit.Page(build(() => { try { w.Close(); } catch (Exception) { } }));
      window.PreviewKeyDown += (s, e) => { if (e.Key == Key.Escape) { e.Handled = true; w.Close(); } };
      window.ShowDialog();
    } catch (Exception e) { LogManager.GetLogger().Error(e, "Blastlog page " + title); }
    try { closed?.Invoke(); } catch (Exception e) { LogManager.GetLogger().Error(e, "Blastlog page closed " + title); }
  }

  /// <summary>The site's page head: eyebrow in the game's text colour, the title, a line under it.</summary>
  public static StackPanel Head(string eyebrow, string title, string note) {
    var head = Stack(new Thickness(0, 0, 0, 22),
      T(eyebrow, "Blastlog.Small", "Blastlog.GameText", 0, FontWeights.Bold),
      T(title, "Blastlog.PageTitle").Also(t => t.Margin = new Thickness(0, 2, 0, 0)));
    if (note != null) head.Children.Add(T(note, "Blastlog.Muted").Also(t => { t.Margin = new Thickness(0, 8, 0, 0); t.MaxWidth = 720; t.HorizontalAlignment = HorizontalAlignment.Left; }));
    return head;
  }

  /// <summary>A card with a section heading and an optional line under it (the site's cr-card).</summary>
  public static Border Card(string title, string note, UIElement content) {
    var body = Stack(null, T(title, "Blastlog.Section"));
    if (note != null) body.Children.Add(T(note, "Blastlog.Muted").Also(t => { t.Margin = new Thickness(0, 4, 0, 0); t.MaxWidth = 720; t.HorizontalAlignment = HorizontalAlignment.Left; }));
    if (content is FrameworkElement f && f.Margin == default(Thickness)) f.Margin = new Thickness(0, 14, 0, 0);
    if (content != null) body.Children.Add(content);
    var card = Kit.Card(body); card.Margin = new Thickness(0, 0, 0, 18);
    return card;
  }

  /// <summary>A person's initial in a circle (the site's PlayerFace without a picture).</summary>
  public static Border Face(string name, double size = 44) {
    var letter = T(string.IsNullOrEmpty(name) ? "?" : name.Substring(0, 1).ToUpperInvariant(), "Blastlog.Body", "Blastlog.OnGame", Math.Max(16, size * 0.42), FontWeights.ExtraBold);
    letter.HorizontalAlignment = HorizontalAlignment.Center; letter.VerticalAlignment = VerticalAlignment.Center;
    var face = new Border { Width = size, Height = size, CornerRadius = new CornerRadius(size / 2), Child = letter, VerticalAlignment = VerticalAlignment.Center };
    face.SetResourceReference(Border.BackgroundProperty, "Blastlog.Game");
    return face;
  }

  /// <summary>A notice line (the site's fp-notice): green when it worked, the danger colour when it didn't.</summary>
  public static TextBlock Notice(string text, bool ok) =>
    T(text, "Blastlog.Body", ok ? "Blastlog.Ok" : "Blastlog.Danger", 15, FontWeights.SemiBold).Also(t => t.Margin = new Thickness(0, 0, 0, 14));

  /// <summary>A checkbox with Kit text, 44px tall (the site's qs-dnf label).</summary>
  public static CheckBox Check(string label, bool value, Action<bool> changed) {
    var c = new CheckBox { Content = T(label, "Blastlog.Body"), IsChecked = value, MinHeight = 44, VerticalContentAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left };
    c.Checked += (s, e) => { try { changed(true); } catch (Exception ex) { LogManager.GetLogger().Error(ex, "Blastlog check " + label); } };
    c.Unchecked += (s, e) => { try { changed(false); } catch (Exception ex) { LogManager.GetLogger().Error(ex, "Blastlog check " + label); } };
    return c;
  }

  /// <summary>"Alek", "Alek and Velvet", "Alek, Charlie and Velvet" (the site's and()).</summary>
  public static string And(System.Collections.Generic.IList<string> names) =>
    names.Count < 2 ? string.Join("", names) : string.Join(", ", System.Linq.Enumerable.Take(names, names.Count - 1)) + " and " + names[names.Count - 1];
}
}
