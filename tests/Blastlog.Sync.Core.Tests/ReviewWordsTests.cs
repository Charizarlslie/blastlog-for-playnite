using System.Collections.Generic;
using Blastlog.Sync.Core.Design;
using Xunit;

namespace Blastlog.Sync.Core.Tests {
/// <summary>The Rate &amp; review starter line and taps must read exactly as the site's (web review-picks-api.test.ts).</summary>
public class ReviewWordsTests {
  static readonly Dictionary<string, TraitName> Names = new Dictionary<string, TraitName> {
    ["h"] = new TraitName("Hacking", "gameplay"), ["o"] = new TraitName("Open world", "structure"), ["s"] = new TraitName("Stealth & Evasion", "gameplay"),
    ["l"] = new TraitName("LGBTQ+ Representation", "narrative"), ["g"] = new TraitName("Grief and Loss", "narrative"), ["d"] = new TraitName("Story-driven", "narrative"),
    ["j"] = new TraitName("JRPG", "genre"), ["p"] = new TraitName("First-person perspective", "presentation"),
  };
  static TraitName NameOf(Pick p) => p.CharacteristicId != null && Names.TryGetValue(p.CharacteristicId, out var n) ? n : null;
  static Pick T(string side, string id) => new Pick(side, id, null);
  static Pick C(string side, string chip) => new Pick(side, null, chip);
  static string Line(int rating, params Pick[] picks) => ReviewWords.StarterLine(picks, NameOf, rating);

  [Fact]
  public void SaysWhatWorkedAndWhatHeldItBack() {
    Assert.Equal("Loved the hacking and the open world. Held back by bugs and a slow start.", Line(4, T("worked", "h"), T("worked", "o"), C("held_back", "bugs"), C("held_back", "slow_start")));
    Assert.Equal("Loved the story, the themes of grief and loss, and the look and sound. Held back by bugs.", Line(5, T("worked", "g"), C("worked", "looks_sound"), T("worked", "d"), C("held_back", "bugs")));
  }

  [Fact]
  public void OxfordCommaAndItemsWithTheirOwnAnd() {
    Assert.Equal("Loved the hacking, the open world, and the JRPG elements.", Line(5, T("worked", "h"), T("worked", "o"), T("worked", "j")));
    Assert.Equal("Loved the hacking and the look and sound.", Line(5, C("worked", "looks_sound"), T("worked", "h")));
    Assert.Equal("Loved the themes of grief and loss, plus the look and sound.", Line(5, T("worked", "g"), C("worked", "looks_sound")));
  }

  [Fact]
  public void LengthGetsItsOwnSentence() {
    Assert.Equal("Loved the first-person perspective. It was the right length. Held back by the grind. It ran too long.",
      Line(4, T("worked", "p"), C("worked", "right_length"), C("held_back", "grind"), C("held_back", "too_long")));
    Assert.Equal("It was over too soon.", Line(4, C("held_back", "too_short")));
  }

  [Fact]
  public void MilderForMiddlingRatingAndNothingLovedIt() {
    Assert.Equal("Liked the LGBTQ+ representation. Held back by the stealth and evasion.", Line(3, T("worked", "l"), T("held_back", "s")));
    Assert.Equal("Nothing held it back.", Line(5, C("held_back", "nothing")));
    Assert.Equal("", Line(5));
  }

  [Theory]
  [InlineData("Story-driven", "narrative", "the story")]
  [InlineData("Physics-driven", "gameplay", "the physics-driven design")]
  [InlineData("Medieval", "setting", "the medieval setting")]
  [InlineData("Visual novel", "genre", "the visual novel elements")]
  [InlineData("Hack-and-slash combat", "genre", "the hack-and-slash combat")]
  [InlineData("Platformer", "genre", "the platforming")]
  [InlineData("Third-person perspective", "presentation", "the third-person perspective")]
  [InlineData("2D Visuals", "presentation", "the 2D visuals")]
  [InlineData("Low-Gravity & Zero-G Movement", "gameplay", "the low-gravity and zero-G movement")]
  [InlineData("Japanese Setting", "setting", "the Japanese setting")]
  [InlineData("Cold War Setting", "setting", "the Cold War setting")]
  [InlineData("RPG Class & Job System", "gameplay", "the RPG class and job system")]
  [InlineData("Open world", null, "the open world")]
  public void TraitWordsReadNaturally(string label, string family, string words) => Assert.Equal(words, ReviewWords.TraitWords(new TraitName(label, family)));

  [Fact]
  public void TogglesFollowTheSite() {
    var picks = ReviewWords.ToggleTrait(new List<Pick>(), "worked", "h");
    picks = ReviewWords.ToggleTrait(picks, "held_back", "h");
    Assert.Equal(new[] { "held_back:h" }, picks.ConvertAll(p => p.Key));
    picks = ReviewWords.ToggleChip(picks, "held_back", "nothing");
    Assert.Equal(new[] { "held_back:nothing" }, picks.ConvertAll(p => p.Key));
    picks = ReviewWords.ToggleChip(picks, "held_back", "bugs");
    Assert.Equal(new[] { "held_back:bugs" }, picks.ConvertAll(p => p.Key));
    picks = ReviewWords.ToggleChip(picks, "held_back", "bugs");
    Assert.Empty(picks);
    Assert.True(ReviewWords.SamePicks(new[] { T("worked", "a"), C("held_back", "bugs") }, new[] { C("held_back", "bugs"), T("worked", "a") }));
  }

  [Fact]
  public void CompletionDateFillsOnFinish() {
    Assert.Equal("2026-11-07", ReviewWords.CompletionDate("done", "", "2026-11-07"));
    Assert.Equal("2026-01-02", ReviewWords.CompletionDate("dnf", "2026-01-02", "2026-11-07"));
    Assert.Equal("", ReviewWords.CompletionDate("playing", "", "2026-11-07"));
    Assert.True(ReviewWords.CanRateOrReview("unplayable"));
    Assert.False(ReviewWords.CanRateOrReview("playing"));
  }
}
}
