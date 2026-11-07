using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Blastlog.Sync.Core.Design;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Blastlog.Sync.Core.Tests {
/// <summary>
/// The C# game colour must give exactly what the site gives. Fixtures/game-colour.json is generated from Blastlog's web
/// code (game-colour.ts, colour.ts, palette.ts); regenerate it when the site's colour rules change.
/// </summary>
public class GameColourTests {
  static readonly JObject Fixture = JObject.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "game-colour.json")));

  [Fact]
  public void GameTokensMatchTheSite() {
    var mismatches = new List<string>();
    foreach (var row in Fixture["tokens"]!.OfType<JObject>()) {
      var seed = (string)row["seed"]!; var mode = (string)row["mode"] == "dark" ? Mode.Dark : Mode.Light;
      var want = (JObject)row["tokens"]!; var got = GameColour.For(seed, mode);
      var pairs = new Dictionary<string, string> { ["game"] = got.Game, ["on-game"] = got.OnGame, ["game-text"] = got.GameText, ["game-tint"] = got.GameTint,
        ["game-tint-2"] = got.GameTint2, ["game-edge"] = got.GameEdge, ["game-bar"] = got.GameBar, ["glow"] = got.Glow };
      foreach (var p in pairs) if ((string)want[p.Key] != p.Value) mismatches.Add($"{seed} {mode} {p.Key}: site {(string)want[p.Key]}, here {p.Value}");
    }
    Assert.True(mismatches.Count == 0, string.Join("\n", mismatches));
  }

  [Fact]
  public void ContrastMatchesTheSite() {
    foreach (var row in Fixture["contrasts"]!.OfType<JObject>())
      Assert.Equal((double)row["ratio"]!, Colour.Contrast((string)row["a"]!, (string)row["b"]!), 9);
  }

  [Fact]
  public void SeedPickingMatchesTheSite() {
    foreach (var row in Fixture["picks"]!.OfType<JObject>())
      Assert.Equal((string)row["seed"], GameColour.PickSeed(row["candidates"]!.Select(c => (string)c)));
  }

  [Fact]
  public void CoverColoursMatchTheSite() {
    var mismatches = new List<string>(); var index = 0;
    foreach (var row in Fixture["covers"]!.OfType<JObject>()) {
      var hex = (string)row["pixels"]!;
      var pixels = Enumerable.Range(0, hex.Length / 6).Select(i => Colour.ParseHex(hex.Substring(i * 6, 6))!.Value).ToList();
      foreach (var limit in new[] { 1, 5 }) {
        var want = string.Join(",", row["colours" + limit]!.Select(c => (string)c));
        var got = string.Join(",", CoverColours.Of(pixels, limit));
        if (want != got) mismatches.Add($"cover {index} limit {limit}: site {want}, here {got}");
      }
      index++;
    }
    Assert.True(mismatches.Count == 0, string.Join("\n", mismatches));
  }

  [Fact]
  public void FallsBackToBlastlogGreen() {
    Assert.Equal(GameColour.For(GameColour.BrandGreen, Mode.Dark).Game, GameColour.For(null, Mode.Dark).Game);
    Assert.Equal(GameColour.For(GameColour.BrandGreen, Mode.Dark).Game, GameColour.For("not a colour", Mode.Dark).Game);
  }
}
}
