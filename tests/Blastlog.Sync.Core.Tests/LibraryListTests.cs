using System;
using System.Linq;
using Blastlog.Sync.Core.Design;
using Xunit;

namespace Blastlog.Sync.Core.Tests {
/// <summary>The Library screen filters, sorts and matches searches exactly as the site (web library.test.ts, contracts search-text.test.ts).</summary>
public class LibraryListTests {
  static Entry E(string title, string status, double? rating, string ownership = "library", int? hours = 10, string finished = null) => new Entry {
    EntryId = Guid.NewGuid().ToString(), Status = status, Rating = rating, Ownership = ownership, FinishedOn = finished,
    Length = hours == null ? null : new EntryLength { Hours = hours.Value, Level = "main_plus_extra" },
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
    // The site searches review text too; the screen passes it when it has it.
    Assert.Equal(new[] { "Finished" }, LibraryList.Filter(Rows, F(f => f.Search = "evidence"), e => e.Game.Title == "Finished" ? "Useful review evidence" : null).Select(e => e.Game.Title));
  }

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
