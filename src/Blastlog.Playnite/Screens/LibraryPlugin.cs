using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Blastlog.Sync.Core;

namespace Blastlog.Playnite {
/// <summary>
/// The whole Blastlog library for the Library screen (Screens/LibraryScreen.cs): the synced copy every sync keeps
/// (GET /sync/v1/library from its cursor), games that aren't in Playnite included.
/// </summary>
public sealed partial class BlastlogPlugin {
  /// <summary>Every entry as of the last read, at once (a background sync may be adding to it: copied, tried again once).</summary>
  public List<Entry> LibraryEntries() {
    for (var i = 0; ; i++) {
      try { return store.Entries.Values.ToList(); }
      catch (InvalidOperationException) when (i < 2) { }
    }
  }

  /// <summary>
  /// Reads what changed on Blastlog since the last read (the sync's own library read, from its cursor). False when it
  /// couldn't (offline, no token); when a sync is already running it waits for it, since the sync reads the library too.
  /// </summary>
  public async Task<bool> RefreshLibrary() {
    if (!Configured) return false;
    await running.WaitAsync();
    try { await Task.Run(() => Engine().RefreshAsync()); SaveStore(); return true; }
    catch (Exception e) { Log.Warn("Blastlog library read failed: " + (e is BlastlogException b ? b.Error : e.Message)); return false; }
    finally { running.Release(); }
  }
}
}
