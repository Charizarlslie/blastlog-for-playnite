using System;
using System.Net.Http;
using System.Threading.Tasks;
using Blastlog.Sync.Core;
using Newtonsoft.Json.Linq;

namespace Blastlog.Playnite {
/// <summary>What a Rate &amp; review write came to: the answer, or the error code and the words for it.</summary>
public sealed class ReviewWrite {
  public JObject Value; public string Error; public string Words;
  public bool Ok => Error == null;
}

/// <summary>
/// The Rate &amp; review dialog's writes (Screens/ReviewDialog.cs). Unlike <see cref="Write(string, object, HttpMethod)"/>
/// they show no message box: the dialog says what went wrong in place and handles a version conflict itself.
/// </summary>
public sealed partial class BlastlogPlugin {
  public async Task<ReviewWrite> ReviewSend(HttpMethod method, string path, object body) {
    if (!Configured) return new ReviewWrite { Error = "not_configured", Words = "Add your Blastlog sync token in Add-ons, Extension settings, Blastlog first." };
    try { var r = await Task.Run(() => Client().SendAsync(method, path, body)); return new ReviewWrite { Value = r.Value ?? new JObject() }; }
    catch (BlastlogException e) { return new ReviewWrite { Error = e.Error, Words = Words(e) }; }
    catch (Exception e) { return new ReviewWrite { Error = "unreachable", Words = "Couldn't reach Blastlog: " + e.Message }; }
  }

  /// <summary>
  /// After a review saved: every cached read goes (stars feed picks, the dashboard and taste), Playnite's status and score
  /// follow on the next sync, and every game page showing the entry refreshes.
  /// </summary>
  public void ReviewSaved(string entryId) {
    cache.Clear(); SaveCache();
    Debounced();
    EntryChanged?.Invoke(entryId);
  }
}
}
