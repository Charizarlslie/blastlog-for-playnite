using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using Blastlog.Playnite.Ui;
using Blastlog.Sync.Core;
using Newtonsoft.Json.Linq;
using Playnite.SDK;

namespace Blastlog.Playnite {
public sealed partial class BlastlogPlugin {
  /// <summary>
  /// A write that hands Blastlog's answer back as it is (a <see cref="BlastlogException"/> with its error code), for a
  /// window that says what went wrong in its own words instead of a Playnite dialog. Clears the read cache when it works.
  /// </summary>
  public async Task<JObject> WriteOrThrow(string path, object body) {
    var r = await Task.Run(() => Client().SendAsync(System.Net.Http.HttpMethod.Post, path, body));
    cache.Clear(); SaveCache();
    return r.Value ?? new JObject();
  }
}
}

namespace Blastlog.Playnite.Screens {
/// <summary>
/// Send feedback (apps/web/src/Feedback.tsx FeedbackDialog): an idea or something that's broken, up to 2,000 characters,
/// sent with the screen it came from ("dashboard", stored as /playnite/dashboard). Posts through the sync API's feedback route
/// (POST /sync/v1/feedback, { kind, body, page }); the admin reads it in Control Room → Feedback, as from the site.
/// </summary>
public static class FeedbackDialog {
  static readonly ILogger Log = LogManager.GetLogger();
  const int Max = 2000;
  static readonly Dictionary<string, string> Errors = new Dictionary<string, string> {
    ["feedback_empty"] = "Write something first.", ["feedback_too_long"] = $"Keep it under {Max} characters.",
    ["feedback_limit"] = "That’s today’s limit. Try again tomorrow.", ["feedback_demo"] = "The demo can’t send feedback.",
  };

  public static void Open(BlastlogPlugin plugin, string page) {
    try { Show(plugin, page); } catch (Exception e) { Log.Error(e, "Blastlog feedback"); Nav.Site(plugin, "/dashboard"); }
  }

  static void Show(BlastlogPlugin plugin, string page) {
    if (!plugin.Configured) { plugin.Api.Dialogs.ShowMessage("Add your Blastlog sync token first: Add-ons, Extension settings, Blastlog.", "Blastlog"); return; }
    var window = plugin.Api.Dialogs.CreateWindow(new WindowCreationOptions { ShowCloseButton = true, ShowMaximizeButton = false, ShowMinimizeButton = false });
    window.Title = "Send feedback"; window.Width = 560; window.Height = 520; window.MinHeight = 360;
    window.Owner = plugin.Api.Dialogs.GetCurrentAppWindow(); window.WindowStartupLocation = WindowStartupLocation.CenterOwner;
    var root = new StackPanel { Margin = new Thickness(28, 24, 28, 24) };
    var surface = new Border { Child = new ScrollViewer { Content = root, VerticalScrollBarVisibility = ScrollBarVisibility.Auto } };
    surface.Resources.MergedDictionaries.Add(Kit.Styles); surface.SetResourceReference(Border.BackgroundProperty, "Blastlog.Surface");
    window.Content = surface;

    var kind = "idea"; var busy = false;
    var title = Kit.Text("Send feedback", "Blastlog.Section");
    var kinds = new WrapPanel { Margin = new Thickness(0, 16, 0, 16) };
    var label = Kit.Text("What’s your idea?", "Blastlog.Body").Also(t => { t.FontWeight = FontWeights.SemiBold; t.Margin = new Thickness(0, 0, 0, 6); });
    var text = new TextBox { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MaxLength = Max, MinHeight = 130, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, FontSize = 15, Padding = new Thickness(10) };
    text.SetResourceReference(Control.FontFamilyProperty, "Blastlog.Font");
    AutomationProperties.SetName(text, "Your feedback");
    var count = Kit.Text("", "Blastlog.Small").Also(t => t.Margin = new Thickness(0, 6, 0, 0));
    var error = Kit.Text("", "Blastlog.Body").Also(t => { t.SetResourceReference(TextBlock.ForegroundProperty, "Blastlog.Danger"); t.Margin = new Thickness(0, 10, 0, 0); t.Visibility = Visibility.Collapsed; t.TextWrapping = TextWrapping.Wrap; });
    var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 18, 0, 0) };
    Button send = null;

    void Count() { count.Text = $"Sent with the screen you’re on. {Max - text.Text.Length} characters left."; if (send != null && !busy) send.IsEnabled = text.Text.Trim().Length > 0; }
    void Kinds() {
      kinds.Children.Clear();
      foreach (var k in new[] { new[] { "idea", "An idea" }, new[] { "bug", "Something’s broken" } }) {
        var value = k[0];
        var b = Kit.Button(k[1], () => { if (busy) return; kind = value; Kinds(); }, kind == value ? "Blastlog.Primary" : "Blastlog.Ghost");
        b.Margin = new Thickness(0, 0, 8, 8);
        AutomationProperties.SetName(b, k[1] + (kind == value ? ", picked" : ""));
        kinds.Children.Add(b);
      }
      label.Text = kind == "bug" ? "What went wrong?" : "What’s your idea?";
    }
    void Fail(string words) { error.Text = words; error.Visibility = Visibility.Visible; }

    async Task Submit() {
      if (busy) return;
      if (text.Text.Trim().Length == 0) { Fail(Errors["feedback_empty"]); return; }
      busy = true; error.Visibility = Visibility.Collapsed; send.Content = "Sending…"; send.IsEnabled = false; text.IsEnabled = false;
      try {
        await plugin.WriteOrThrow("feedback", new { kind, body = text.Text, page = page ?? "library" });
        Thanks();
      } catch (BlastlogException e) {
        Fail(Errors.TryGetValue(e.Error ?? "", out var words) ? words : e.StatusCode == 404 ? "Blastlog can’t take feedback from Playnite yet. Send it from the site for now." : "That didn’t send. Try again.");
      } catch (Exception e) { Log.Warn("Blastlog feedback failed: " + e.Message); Fail("That didn’t send. Check your connection and try again."); }
      finally { busy = false; if (window.IsVisible && send != null) { send.Content = "Send"; text.IsEnabled = true; Count(); } }
    }

    void Thanks() {
      root.Children.Clear();
      root.Children.Add(Kit.Text("Thanks!", "Blastlog.Section"));
      root.Children.Add(Kit.Text("Got it. Charlie reads every one.", "Blastlog.Muted").Also(t => t.Margin = new Thickness(0, 10, 0, 18)));
      var done = Kit.Button("Done", () => window.Close());
      done.HorizontalAlignment = HorizontalAlignment.Left;
      root.Children.Add(done);
      window.Height = 260;
      done.Focus();
    }

    send = Kit.Button("Send", Submit);
    send.IsEnabled = false;
    var cancel = Kit.Button("Cancel", () => { if (!busy) window.Close(); }, "Blastlog.TextButton");
    cancel.Margin = new Thickness(8, 0, 0, 0);
    buttons.Children.Add(send); buttons.Children.Add(cancel);
    text.TextChanged += (s, e) => Count();
    text.PreviewKeyDown += (s, e) => { if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.Control) { e.Handled = true; _ = Submit(); } };
    window.PreviewKeyDown += (s, e) => { if (e.Key == Key.Escape && !busy) window.Close(); };

    Kinds(); Count();
    root.Children.Add(title); root.Children.Add(kinds); root.Children.Add(label); root.Children.Add(text); root.Children.Add(count); root.Children.Add(error); root.Children.Add(buttons);
    window.Loaded += (s, e) => text.Focus();
    window.ShowDialog();
  }
}
}
