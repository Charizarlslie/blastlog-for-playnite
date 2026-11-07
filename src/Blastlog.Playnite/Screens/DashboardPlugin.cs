using Newtonsoft.Json.Linq;

namespace Blastlog.Playnite {
public sealed partial class BlastlogPlugin {
  /// <summary>
  /// The saved copy of a mirror read, however old, without asking the server: screens draw it at once and replace it when
  /// <see cref="Mirror"/> answers. Null when there's no saved copy (never read, or cleared by a change).
  /// </summary>
  public JObject SavedRead(string path) => cache.TryGetValue(path, out var hit) ? hit.Value : null;
}
}
