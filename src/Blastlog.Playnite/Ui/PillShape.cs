using System;
using System.Windows;
using System.Windows.Controls;
using Playnite.SDK;

namespace Blastlog.Playnite.Ui {
/// <summary>
/// Fully rounded chips and buttons drawn the way the site draws them. CSS shrinks an oversized radius (999px) to half
/// the box's height on every corner, so a wide chip is a bar with round ends. WPF shrinks each side separately, so the
/// same radius on a wide box gives long elliptical corners: an oval. Any Border whose radius is 99 or more (our "fully
/// rounded" value) gets its radius set to half its shorter side whenever its size changes.
/// </summary>
public static class PillShape {
  static readonly ILogger Log = LogManager.GetLogger();
  static bool started;
  static readonly DependencyProperty IsPillProperty = DependencyProperty.RegisterAttached("IsPill", typeof(bool), typeof(PillShape), new PropertyMetadata(false));

  public static void Start() {
    if (started) return;
    started = true;
    EventManager.RegisterClassHandler(typeof(Border), FrameworkElement.SizeChangedEvent, new SizeChangedEventHandler(Resized));
  }

  static void Resized(object sender, SizeChangedEventArgs e) {
    try {
      if (!(sender is Border b)) return;
      var pill = (bool)b.GetValue(IsPillProperty);
      if (!pill) {
        var r = b.CornerRadius;
        if (r.TopLeft < 99 || r.TopRight < 99 || r.BottomLeft < 99 || r.BottomRight < 99) return;
        b.SetValue(IsPillProperty, true);
      }
      var half = Math.Max(0, Math.Min(e.NewSize.Width, e.NewSize.Height) / 2);
      if (half > 0 && Math.Abs(b.CornerRadius.TopLeft - half) > 0.1) b.CornerRadius = new CornerRadius(half);
    } catch (Exception ex) { Log.Error(ex, "Blastlog: pill shape"); }
  }
}
}
