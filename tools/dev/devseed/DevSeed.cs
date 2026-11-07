using System;
using System.IO;
using System.Linq;
using Playnite.SDK;
using Playnite.SDK.Events;
using Playnite.SDK.Models;
using Playnite.SDK.Plugins;

// Local test helper only (never shipped): fills an empty portable Playnite with a few games and covers.
public class DevSeed : GenericPlugin {
  public override Guid Id => new Guid("5d1f6c2a-6a43-4c55-9a8e-1b2f3c4d5e6f");
  public DevSeed(IPlayniteAPI api) : base(api) { }
  public override void OnApplicationStarted(OnApplicationStartedEventArgs args) {
    if (PlayniteApi.Database.Games.Count > 0) return;
    var dir = Environment.GetEnvironmentVariable("BLASTLOG_DEVSEED");
    if (string.IsNullOrEmpty(dir) || !File.Exists(Path.Combine(dir, "games.txt"))) return;
    var statuses = PlayniteApi.Database.CompletionStatuses.ToList();
    var i = 0;
    using (PlayniteApi.Database.BufferedUpdate()) {
      foreach (var line in File.ReadAllLines(Path.Combine(dir, "games.txt")).Where(l => l.Contains("|"))) {
        var parts = line.Trim().Split('|');
        var game = new Game(parts[0]) { IsInstalled = i % 3 != 0, UserScore = i % 4 == 0 ? (int?)(60 + i * 2) : null,
          CompletionStatusId = statuses.Count > 0 ? statuses[i % statuses.Count].Id : Guid.Empty, Playtime = (ulong)(i * 3600 * 3) };
        PlayniteApi.Database.Games.Add(game);
        var cover = Path.Combine(dir, "covers", parts[1] + ".jpg");
        if (File.Exists(cover)) { game.CoverImage = PlayniteApi.Database.AddFile(cover, game.Id); PlayniteApi.Database.Games.Update(game); }
        i++;
      }
    }
  }
}
