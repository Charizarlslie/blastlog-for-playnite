using System;
using System.Linq;
using System.Threading.Tasks;
using Blastlog.Sync.Core;

namespace Blastlog.Playnite {
/// <summary>What a game page change came to: the run to undo it with, or the reason it didn't go.</summary>
public sealed class PanelOutcome {
  public bool Applied; public string RunId; public string Result; public string Reason; public int? CurrentVersion;
}

/// <summary>
/// The game page's writes (status, stars, review from Playnite's game details). Each is one "panel" push, the same as the
/// site's own edits go through (History, friends hearing about stars and reviews), and each can be undone as a run.
/// </summary>
public sealed partial class BlastlogPlugin {
  /// <summary>Raised on the UI thread after a game page change or undo, with the entry id, so every game page element refreshes.</summary>
  public event Action<string> EntryChanged;

  public async Task<PanelOutcome> PanelChange(ChangeItem change) {
    if (!Configured) { PlayniteApi.Dialogs.ShowMessage("Add your Blastlog sync token in Add-ons, Extension settings, Blastlog first.", "Blastlog"); return null; }
    var push = new PushRequest { Origin = "panel", Client = new ClientInfo { Version = PlayniteApi.ApplicationInfo.ApplicationVersion.ToString(), Plugin = "0.2.4" } };
    push.Changes.Add(change);
    try {
      var response = (await Task.Run(() => Client().PushAsync(push))).Value;
      var result = response?.Results.FirstOrDefault(r => r.Kind == "change");
      var outcome = new PanelOutcome { RunId = response?.RunId, Result = result?.Result, Reason = result?.Reason, CurrentVersion = result?.CurrentVersion, Applied = result?.Result == "applied" };
      Forget(change.EntryId);
      // Playnite's own copy of the status and score follows on the next sync, a few seconds from now.
      if (outcome.Applied) Debounced();
      EntryChanged?.Invoke(change.EntryId);
      return outcome;
    } catch (BlastlogException e) { PlayniteApi.Dialogs.ShowErrorMessage(Words(e), "Blastlog"); return null; }
    catch (Exception e) { PlayniteApi.Dialogs.ShowErrorMessage("Couldn't reach Blastlog: " + e.Message, "Blastlog"); return null; }
  }

  /// <summary>Undoes one game page change (its run). True when Blastlog undid it.</summary>
  public async Task<bool> UndoPanel(string runId, string entryId) {
    if (runId == null) return false;
    try {
      var r = (await Task.Run(() => Client().UndoAsync(runId))).Value;
      Forget(entryId); Debounced(); EntryChanged?.Invoke(entryId);
      if (r.Kept.Count > 0) PlayniteApi.Dialogs.ShowMessage("Kept as it is: it changed again since.", "Blastlog");
      return r.Undone > 0;
    } catch (BlastlogException e) { PlayniteApi.Dialogs.ShowErrorMessage(e.Error == "nothing_to_undo" ? "That's already undone." : Words(e), "Blastlog"); return false; }
    catch (Exception e) { PlayniteApi.Dialogs.ShowErrorMessage("Couldn't reach Blastlog: " + e.Message, "Blastlog"); return false; }
  }

  /// <summary>Drops the cached reads about one entry (its page and history), so the next read is fresh.</summary>
  public void Forget(string entryId) {
    if (entryId == null) return;
    foreach (var key in cache.Keys.Where(k => k.StartsWith("entries/" + entryId, StringComparison.Ordinal) || k.StartsWith("games/", StringComparison.Ordinal) || k == "now-playing" || k.StartsWith("next-up", StringComparison.Ordinal)).ToList()) cache.Remove(key);
    SaveCache();
  }
}
}
