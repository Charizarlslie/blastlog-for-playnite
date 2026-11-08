using System;
using System.Linq;
using Blastlog.Sync.Core.Design;
using Xunit;

namespace Blastlog.Sync.Core.Tests {
/// <summary>The Library screen filters, sorts and matches searches exactly as the site (web library.test.ts, contracts search-text.test.ts).</summary>
public class LibraryListTests {
  /// <summary>hours: null = unknown, -1 = no end (length null, noEnd true, as the server sends it).</summary>
  static Entry E(string title, string status, double? rating, string ownership = "library", int? hours = 10, string finished = null, string collection = null) => new Entry {
    EntryId = Guid.NewGuid().ToString(), Status = status, Rating = rating, Ownership = ownership, FinishedOn = finished, CollectionId = collection,
    Length = hours == null || hours < 0 ? null : new EntryLength { Hours = hours.Value, Level = "main_plus_extra" }, NoEnd = hours == -1,
    Review = title == "Reviewed" ? "Useful review evidence" : null,
    Game = new Game { Id = Guid.NewGuid().ToString(), Title = title } };
  static readonly Entry[] Rows = { E("Backlog", "not_started", null), E("Finished", "done", 5, "library", 30), E("Wish", "not_started", 3, "wishlist"), E("Reviewed", "quit", 2) };
  static string[] Titles(LibraryFilters f, params Entry[] rows) => LibraryList.Filter(rows.Length > 0 ? rows : Rows, f).Select(e => e.Game.Title).ToArray();
  static LibraryFilters F(Action<LibraryFilters> change) => new LibraryFilters().With(change);

  [Fact]
  public void StatusAndRatingFilters() {
    Assert.Equal(new[] { "Backlog" }, Titles(F(f => f.Status = "backlog")));
    Assert.Equal(new[] { "Finished", "Reviewed" }, Titles(F(f => f.Status = "completed")));
    Assert.Equal(new[] { "Wish" }, Titles(F(f => f.Status = "wishlist")));
    Assert.Equal(new[] { "Reviewed" }, Titles(F(f => f.Rating = "2")));
    Assert.Equal(new[] { "Finished" }, Titles(F(f => f.Status = "done")));
  }

  [Fact]
  public void SortsNumbersWithoutTreatingMissingAsZero() {
    Assert.Equal(new[] { "Finished", "Backlog", "Reviewed", "Wish" }, Titles(F(f => { f.Sort = "length"; f.Direction = "desc"; })));
    var mixed = new[] { E("Ten", "not_started", null, hours: 10), E("Unknown", "not_started", null, hours: null), E("Two", "not_started", null, hours: 2) };
    Assert.Equal(new[] { "Two", "Ten", "Unknown" }, Titles(F(f => f.Sort = "length"), mixed));
    Assert.Equal(new[] { "Ten", "Two", "Unknown" }, Titles(F(f => { f.Sort = "length"; f.Direction = "desc"; }), mixed));
    Assert.Equal(new[] { "Finished", "Wish", "Reviewed", "Backlog" }, Titles(F(f => { f.Sort = "rating"; f.Direction = "desc"; })));
    Assert.Equal(new[] { "Reviewed", "Wish", "Finished", "Backlog" }, Titles(F(f => f.Sort = "rating")));
  }

  [Fact]
  public void SortsByFinishedDateThenTitle() {
    var rows = new[] { E("B", "done", 4, finished: "2025-01-02"), E("A", "done", 4, finished: "2025-01-02"), E("C", "done", 4, finished: "2026-03-01"), E("D", "playing", null) };
    Assert.Equal(new[] { "C", "A", "B", "D" }, Titles(F(f => { f.Sort = "finished"; f.Direction = "desc"; }), rows));
    Assert.Equal(new[] { "A", "B", "C", "D" }, Titles(F(f => f.Sort = "finished"), rows));
  }

  [Fact]
  public void TitlesSortCaseAndAccentFriendly() {
    var rows = new[] { E("zelda", "done", null), E("Áurea", "done", null), E("Banjo", "done", null) };
    Assert.Equal(new[] { "Áurea", "Banjo", "zelda" }, Titles(new LibraryFilters(), rows));
    Assert.Equal(new[] { "zelda", "Banjo", "Áurea" }, Titles(F(f => f.Direction = "desc"), rows));
  }

  [Fact]
  public void SearchMatchesLikeTheSite() {
    Assert.True(SearchText.Matches("Pokémon Gold", SearchText.Fold("pokemon")));
    Assert.True(SearchText.Matches("Hades II", SearchText.Fold("hades 2")));
    Assert.True(SearchText.Matches("Baldur's Gate 3", SearchText.Fold("baldurs gate")));
    Assert.True(SearchText.Matches("Marvel's Spider-Man", SearchText.Fold("spiderman")));
    Assert.True(SearchText.Matches("Ratchet & Clank", SearchText.Fold("ratchet and clank")));
    Assert.False(SearchText.Matches("Hades", SearchText.Fold("hades 2")));
    Assert.Equal(new[] { "Reviewed" }, Titles(F(f => f.Search = "review")));
    Assert.Empty(Titles(F(f => f.Search = "PC")));
    Assert.Equal(new[] { "Reviewed" }, Titles(F(f => f.Search = "evidence")));
  }

  [Fact]
  public void NoEndCountsAsLongestAndUnknownGoesLast() {
    var mixed = new[] { E("Ten", "not_started", null, hours: 10), E("Unknown", "not_started", null, hours: null), E("Endless", "not_started", null, hours: -1), E("Two", "not_started", null, hours: 2) };
    Assert.Equal(new[] { "Two", "Ten", "Endless", "Unknown" }, Titles(F(f => f.Sort = "length"), mixed));
    Assert.Equal(new[] { "Endless", "Ten", "Two", "Unknown" }, Titles(F(f => { f.Sort = "length"; f.Direction = "desc"; }), mixed));
  }

  // Collections (web library.test.ts "collections in the library list").
  static readonly CollectionRef Legendary = new CollectionRef { Id = "c1", Title = "Mass Effect Legendary Edition" };
  static readonly Entry[] All = { E("Alan Wake", "done", 4), E("Mass Effect", "done", 4, collection: "c1"), E("Mass Effect 2", "done", 5, collection: "c1"),
    E("Mass Effect 3", "playing", null, collection: "c1"), E("Zelda", "not_started", null) };
  static string[] Show(LibraryFilters f, System.Collections.Generic.List<Entry> rows = null) =>
    LibraryList.Group(rows ?? LibraryList.Filter(All, f), All, new[] { Legendary }, f)
      .Select(i => i.Entry != null ? i.Entry.Game.Title : $"[{i.Collection.Title}: {string.Join(", ", i.Members.Select(m => m.Game.Title))}]").ToArray();

  [Fact]
  public void CollectionIsOneRowWhereItsTitleFalls() =>
    Assert.Equal(new[] { "Alan Wake", "[Mass Effect Legendary Edition: Mass Effect, Mass Effect 2, Mass Effect 3]", "Zelda" }, Show(new LibraryFilters()));

  [Fact]
  public void CollectionKeepsOnlyGamesThatPass() {
    Assert.Equal(new[] { "[Mass Effect Legendary Edition: Mass Effect 3]" }, Show(F(f => f.Status = "playing")));
    Assert.Equal(new[] { "Zelda" }, Show(F(f => f.Status = "not_started")));
  }

  [Fact]
  public void CollectionDropsWhenNoneOfItsGamesHaveTheTrait() {
    var trait = F(f => f.Trait = Guid.NewGuid().ToString());
    Assert.Equal(new[] { "Zelda" }, Show(trait, All.Where(e => e.Game.Title == "Zelda").ToList()));
  }

  [Fact]
  public void SearchNamingTheCollectionShowsAllItsGames() =>
    Assert.Equal(new[] { "[Mass Effect Legendary Edition: Mass Effect, Mass Effect 2, Mass Effect 3]" }, Show(F(f => f.Search = "legendary")));

  [Fact]
  public void OtherSortsPlaceTheCollectionWhereItsFirstGameFalls() =>
    Assert.Equal(new[] { "[Mass Effect Legendary Edition: Mass Effect 2, Mass Effect, Mass Effect 3]", "Alan Wake", "Zelda" },
      Show(F(f => { f.Sort = "rating"; f.Direction = "desc"; })));

  [Fact]
  public void CountsEveryChip() {
    var counts = LibraryList.Counts(Rows);
    Assert.Equal(4, counts["all"]); Assert.Equal(1, counts["backlog"]); Assert.Equal(2, counts["completed"]); Assert.Equal(1, counts["wishlist"]);
    Assert.Equal(0, counts["playing"]); Assert.Equal(1, counts["done"]);
  }

  [Fact]
  public void RoundTripsDashboardLinks() {
    var f = LibraryFilters.Parse("status=completed&rating=4&sort=finished&direction=desc&search=fear%20the&trait=0b1f2c3d-4e5f-4a6b-8c7d-9e0f1a2b3c4d");
    Assert.Equal("completed", f.Status); Assert.Equal("4", f.Rating); Assert.Equal("finished", f.Sort); Assert.Equal("desc", f.Direction);
    Assert.Equal("fear the", f.Search); Assert.Equal("0b1f2c3d-4e5f-4a6b-8c7d-9e0f1a2b3c4d", f.Trait);
    var again = LibraryFilters.Parse(f.ToQuery());
    Assert.Equal(f.ToQuery(), again.ToQuery());
    Assert.Equal("length", LibraryFilters.Parse("sort=hltb").Sort);
    Assert.Equal("", LibraryFilters.Parse("trait=not-an-id").Trait);
    var junk = LibraryFilters.Parse("status=nope&rating=9&sort=x&direction=up");
    Assert.Equal("", junk.ToQuery());
    Assert.Equal("", new LibraryFilters().ToQuery());
  }
}
}
