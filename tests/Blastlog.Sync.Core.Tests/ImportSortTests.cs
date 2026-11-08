using System;
using System.Linq;
using Blastlog.Sync.Core.Design;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Blastlog.Sync.Core.Tests {
/// <summary>The import page's decisions must match the site's (apps/web LibraryImport.tsx).</summary>
public class ImportSortTests {
  static ImportRow Row(string json) => ImportRow.Parse(JObject.Parse(json));

  [Fact]
  public void PileSelectValuesBecomeTheSitesChanges() {
    var started = ImportSort.ChangeFor("started");
    Assert.Equal(("backlog", "started"), (started.Pile, started.ChosenStatus));
    var dnf = ImportSort.ChangeFor("dnf");
    Assert.Equal(("finished", "dnf"), (dnf.Pile, dnf.ChosenStatus));
    var skip = ImportSort.ChangeFor("skip");
    Assert.Equal(("skip", (string)null), (skip.Pile, skip.ChosenStatus));
    Assert.Equal("Didn’t finish", ImportSort.PileLabel("finished", "dnf"));
    Assert.Equal("Don’t import", ImportSort.PileLabel("skip", null));
  }

  [Fact]
  public void PilesStandForLibraryStatusesAfterItWentIn() {
    Assert.Equal("done", ImportSort.StatusOf("finished", null));
    Assert.Equal("started", ImportSort.StatusOf("backlog", "started"));
    Assert.Equal("not_started", ImportSort.StatusOf("backlog", null));
    Assert.Equal("ignored", ImportSort.StatusOf("not_interested", null));
    Assert.Null(ImportSort.StatusOf("skip", null));
    var removed = ImportSort.PileAfter(null, true);
    Assert.Equal("skip", removed.Pile);
    var quit = ImportSort.PileAfter("quit", false);
    Assert.Equal(("finished", "quit"), (quit.Pile, quit.ChosenStatus));
    Assert.Null(ImportSort.PileAfter(null, false)); // a rating alone
  }

  [Fact]
  public void SortCardsAskAboutPlayedGamesMostPlayedFirst() {
    var rows = new[] {
      Row("{\"id\":\"a\",\"position\":1,\"rawTitle\":\"A\",\"outcome\":\"committed\",\"play\":{\"group\":\"some\",\"hours\":3}}"),
      Row("{\"id\":\"b\",\"position\":2,\"rawTitle\":\"B\",\"outcome\":\"committed\",\"play\":{\"group\":\"never\",\"hours\":0}}"),
      Row("{\"id\":\"c\",\"position\":3,\"rawTitle\":\"C\",\"outcome\":\"committed\",\"play\":{\"group\":\"finished\",\"hours\":40}}"),
      Row("{\"id\":\"d\",\"position\":4,\"rawTitle\":\"D\",\"outcome\":\"linked\"}"),
      Row("{\"id\":\"e\",\"position\":5,\"rawTitle\":\"E\",\"outcome\":\"committed\",\"declared\":true}"),
      Row("{\"id\":\"f\",\"position\":0,\"rawTitle\":\"F\",\"outcome\":\"committed\"}"),
      Row("{\"id\":\"g\",\"position\":6,\"rawTitle\":\"G\",\"outcome\":\"committed\"}") };
    Assert.Equal(new[] { "c", "a", "f", "g" }, ImportSort.CardRows(rows).Select(r => r.Id));
  }

  [Fact]
  public void PlayLineReadsAsTheSites() {
    Assert.Equal("Never played", ImportSort.PlayLine(new ImportPlay { Hours = 0 }));
    Assert.Equal("30 min played · beats in ~12 h", ImportSort.PlayLine(new ImportPlay { Hours = 0.5, BeatHours = 11.6 }));
    Assert.Equal("4.5 h played", ImportSort.PlayLine(new ImportPlay { Hours = 4.5 }));
    Assert.Equal("13 h played", ImportSort.PlayLine(new ImportPlay { Hours = 12.6 }));
    Assert.Equal(0.5, ImportSort.Share(new ImportPlay { Hours = 5, BeatHours = 10 }));
    Assert.Equal(1, ImportSort.Share(new ImportPlay { Hours = 50, BeatHours = 10 }));
    Assert.Null(ImportSort.Share(new ImportPlay { Hours = 5 }));
  }

  [Fact]
  public void ImportNamesAndBadges() {
    Assert.Equal("Steam wishlist", ImportSort.ImportName("steam", "steam_wishlist", null));
    Assert.Equal("Grouvee", ImportSort.ImportName("list", "grouvee_csv", null));
    Assert.Equal("Steam (daily check)", ImportSort.ImportName("steam", "steam_page", "steam_sync"));
    Assert.Equal("List", ImportSort.ImportName("playnite", "playnite", null));
    Assert.Equal("Added · 3 to look at", ImportSort.StatusText("committed", 3));
    Assert.Equal("Ready to sort", ImportSort.StatusText("ready", 0));
  }

  [Fact]
  public void LookReasonsSayYearsApartPlainly() {
    var sorcery = Row("{\"id\":\"s\",\"rawTitle\":\"Sorcery!\",\"state\":\"needs_look\",\"reason\":\"maybe_in_library\",\"options\":[" +
      "{\"title\":\"Sorcery\",\"year\":1984,\"note\":\"in_library\",\"gameId\":\"g1\"},{\"title\":\"Sorcery!\",\"year\":2013,\"igdbId\":\"9\"}]}");
    Assert.Equal("Your library’s “Sorcery” is from 1984; this one is from 2013, so they’re probably different games. Pick yours only if it’s really the same.", ImportSort.LookReason(sorcery));
    Assert.Equal("{\"gameId\":\"g1\"}", sorcery.Options[0].Choice().ToString(Newtonsoft.Json.Formatting.None));
    Assert.Equal("{\"igdbId\":\"9\"}", sorcery.Options[1].Choice().ToString(Newtonsoft.Json.Formatting.None));
    Assert.Equal("1984 · In your library", sorcery.Options[0].Line());
    Assert.Equal("This is a bundle. Pick the game you mean, or skip it.", ImportSort.LookReason(Row("{\"state\":\"needs_look\",\"reason\":\"bundle\"}")));
    Assert.Equal("You said this isn’t a game.", ImportSort.LookReason(Row("{\"state\":\"not_a_game\",\"reason\":\"player\"}")));
    Assert.Equal("123", ImportSort.SteamIdOnly("Steam app 123"));
    Assert.Null(ImportSort.SteamIdOnly("Portal 2"));
  }
}
}
