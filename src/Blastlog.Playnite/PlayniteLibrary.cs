using System;
using System.Collections.Generic;
using System.Linq;
using Blastlog.Sync.Core;
using Playnite.SDK;
using Playnite.SDK.Models;
using Game = Playnite.SDK.Models.Game;

namespace Blastlog.Playnite {
/// <summary>
/// Playnite's database for the sync core (IPlayniteLibrary). Reads the fields the sync uses; writes only the completion
/// status and user score, inside one BufferedUpdate (docs/PLAN.md §4.5). Store ids come from the library plugin
/// (Steam's GameId is its app id).
/// </summary>
public sealed class PlayniteLibrary : IPlayniteLibrary {
  readonly IPlayniteAPI api;
  public PlayniteLibrary(IPlayniteAPI api) { this.api = api; }

  public IList<LocalGame> Games() => api.Database.Games.Select(ToLocal).ToList();

  public static LocalGame ToLocal(Game g) => new LocalGame {
    Id = g.Id.ToString(), Name = g.Name, StatusId = g.CompletionStatusId == Guid.Empty ? null : g.CompletionStatusId.ToString(),
    UserScore = g.UserScore, PlaytimeSeconds = (long)g.Playtime, LastActivity = g.LastActivity == null ? (DateTimeOffset?)null : new DateTimeOffset(g.LastActivity.Value.ToUniversalTime()),
    Hidden = g.Hidden, Source = g.Source?.Name, PluginId = g.PluginId == Guid.Empty ? null : g.PluginId.ToString(),
    StoreGameId = string.IsNullOrEmpty(g.GameId) ? null : g.GameId, ReleaseYear = g.ReleaseYear,
    Platforms = g.Platforms?.Select(p => p.Name).Where(n => n != null).ToArray() ?? new string[0] };

  public IList<KeyValuePair<string,string>> Statuses() =>
    api.Database.CompletionStatuses.Select(s => new KeyValuePair<string,string>(s.Id.ToString(), s.Name)).ToList();

  public void Apply(IList<PlayniteWrite> writes) {
    if (writes.Count == 0) return;
    using (api.Database.BufferedUpdate()) {
      foreach (var w in writes) {
        if (!Guid.TryParse(w.GameId, out var id)) continue;
        var game = api.Database.Games.Get(id); if (game == null) continue;
        var changed = false;
        if (w.StatusId != null && Guid.TryParse(w.StatusId, out var status) && game.CompletionStatusId != status) { game.CompletionStatusId = status; changed = true; }
        if (w.SetScore && game.UserScore != w.Score) { game.UserScore = w.Score; changed = true; }
        if (changed) api.Database.Games.Update(game);
      }
    }
  }
}

/// <summary>
/// Blastlog's tags on Playnite games (docs/PLAN.md §4.6), so Playnite's own filters work as the Blastlog library. Every tag
/// the plugin makes starts with "Blastlog", and it only ever adds or removes those, never the user's own tags.
/// </summary>
public static class BlastlogTags {
  public const string Prefix = "Blastlog";
  public const string Backlog = "Blastlog: Backlog", NextUp = "Blastlog: Next up", Playing = "Blastlog: Currently playing", Wishlist = "Blastlog: Wishlist", TraitPrefix = "Blastlog trait: ";

  /// <summary>Sets each paired game's Blastlog tags to exactly <paramref name="wanted"/> (game id → tag names).</summary>
  public static int Apply(IPlayniteAPI api, IDictionary<string, HashSet<string>> wanted) {
    var tagIds = new Dictionary<string, Guid>(StringComparer.Ordinal);
    foreach (var t in api.Database.Tags) if (t.Name != null && t.Name.StartsWith(Prefix) && !tagIds.ContainsKey(t.Name)) tagIds[t.Name] = t.Id;
    var ours = new HashSet<Guid>(tagIds.Values); var changedGames = 0;
    using (api.Database.BufferedUpdate()) {
      foreach (var pair in wanted) {
        if (!Guid.TryParse(pair.Key, out var gameId)) continue;
        var game = api.Database.Games.Get(gameId); if (game == null) continue;
        var want = new HashSet<Guid>(pair.Value.Select(name => tagIds.TryGetValue(name, out var id) ? id : (tagIds[name] = api.Database.Tags.Add(name).Id)));
        ours.UnionWith(want);
        var current = game.TagIds ?? new List<Guid>();
        var next = current.Where(id => !ours.Contains(id) || want.Contains(id)).Concat(want).Distinct().ToList();
        if (next.Count != current.Count || next.Except(current).Any()) { game.TagIds = next; api.Database.Games.Update(game); changedGames++; }
      }
    }
    return changedGames;
  }

  /// <summary>"Remove Blastlog tags": takes every Blastlog tag off every game and deletes the tags. The user's own stay.</summary>
  public static int RemoveAll(IPlayniteAPI api) {
    var ours = api.Database.Tags.Where(t => t.Name != null && t.Name.StartsWith(Prefix)).ToList();
    var ids = new HashSet<Guid>(ours.Select(t => t.Id)); var changed = 0;
    using (api.Database.BufferedUpdate()) {
      foreach (var game in api.Database.Games.Where(g => g.TagIds != null && g.TagIds.Any(ids.Contains)).ToList()) {
        game.TagIds = game.TagIds.Where(id => !ids.Contains(id)).ToList(); api.Database.Games.Update(game); changed++;
      }
      foreach (var t in ours) api.Database.Tags.Remove(t.Id);
    }
    return changed;
  }
}
}
