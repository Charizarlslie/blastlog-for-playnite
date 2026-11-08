using System;
using System.Net.Http;
using System.Threading.Tasks;

namespace Blastlog.Playnite {
/// <summary>
/// Collection writes (Screens/CollectionPage.cs, Screens/CollectionPicker.cs): POST/PATCH/DELETE /sync/v1/collections and
/// PUT /sync/v1/entries/:id/collection. Like Rate &amp; review's writes they show no message box: the page says what went
/// wrong in place and handles a version conflict itself.
/// </summary>
public sealed partial class BlastlogPlugin {
  /// <summary>
  /// Sends a collection write. On success every cached read goes (the library, the collections and the entries all show
  /// the grouping) and game pages showing <paramref name="entryIds"/> refresh.
  /// </summary>
  public async Task<ReviewWrite> CollectionSend(HttpMethod method, string path, object body, params string[] entryIds) {
    var result = await ReviewSend(method, path, body);
    if (!result.Ok) { result.Words = CollectionWords(result); return result; }
    cache.Clear(); SaveCache();
    foreach (var id in entryIds ?? new string[0]) if (id != null) EntryChanged?.Invoke(id);
    return result;
  }

  static string CollectionWords(ReviewWrite failed) {
    switch (failed.Error) {
      case "title_required": return "Give it a name.";
      case "version_conflict": return "This collection changed on Blastlog while this was open.";
      case "collection_not_found": return "That collection isn’t there any more. It may have been removed.";
      case "entry_not_found": return "That game isn’t in your library any more.";
      case "not_library_owner": return "Only the collection’s owner can change it.";
      default: return failed.Words;
    }
  }
}
}
