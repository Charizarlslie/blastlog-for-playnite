using System;
using System.Collections.Generic;
using System.Linq;

namespace Blastlog.Sync.Core.Design {
/// <summary>One item in the game page's media viewer: a trailer (a YouTube id) or a screenshot (a picture URL).</summary>
public sealed class MediaItem {
  public string YoutubeId; public string Name; public string Url;
  public bool IsVideo => YoutubeId != null;
}

/// <summary>
/// The site's media viewer sequence (web media-viewer.ts), ported exactly: trailers first, then every screenshot (not
/// only the six the page shows); the counter's labels; the keys that step (arrows, Home, End), stopping at the ends.
/// </summary>
public static class MediaSequence {
  public static List<MediaItem> Items(IEnumerable<(string youtubeId, string name)> videos, IEnumerable<string> screenshots) =>
    (videos ?? Enumerable.Empty<(string, string)>()).Select(v => new MediaItem { YoutubeId = v.youtubeId, Name = v.name })
      .Concat((screenshots ?? Enumerable.Empty<string>()).Select(url => new MediaItem { Url = url })).ToList();

  /// <summary>What the item is, for the counter and screen readers: "Trailer", "Launch trailer", "Screenshot 2".</summary>
  public static string Label(IList<MediaItem> items, int index) {
    if (items == null || index < 0 || index >= items.Count) return "";
    var item = items[index];
    if (item.IsVideo) return string.IsNullOrWhiteSpace(item.Name) ? "Trailer" : item.Name.Trim();
    return "Screenshot " + items.Take(index + 1).Count(i => !i.IsVideo);
  }

  /// <summary>The viewer's keys (web key names): arrows step, Home and End jump to the ends. Anything else is null.</summary>
  public static int? KeyTarget(string key, int index, int count) {
    if (count <= 0) return null;
    switch (key) {
      case "ArrowRight": return Math.Min(count - 1, index + 1);
      case "ArrowLeft": return Math.Max(0, index - 1);
      case "Home": return 0;
      case "End": return count - 1;
      default: return null;
    }
  }
}
}
