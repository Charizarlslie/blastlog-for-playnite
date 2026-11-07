using System;
using System.IO;
using System.Linq;
using Blastlog.Sync.Core.Design;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Blastlog.Sync.Core.Tests {
/// <summary>
/// The game page fingerprint must order, size and describe its bars exactly as the site does. Fixtures/fingerprint.json is
/// generated from Blastlog's web code (fingerprint-bars.ts, GameFingerprint.tsx, fingerprint-api.ts) over demo games.
/// </summary>
public class FingerprintTests {
  static readonly JObject Fixture = JObject.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "fingerprint.json")));

  [Fact]
  public void BarsMatchTheSite() {
    foreach (var c in Fixture["cases"]!.OfType<JObject>()) {
      var data = FingerprintData.Parse(c["input"])!;
      var areas = FingerprintBars.Areas(data);
      var want = (JArray)c["expected"]!["areas"]!;
      Assert.Equal(want.Select(a => (string)a["name"]!), areas.Select(a => a.Name));
      for (var i = 0; i < areas.Count; i++) {
        var w = (JObject)want[i];
        var fill = (double?)w["fill"];
        if (fill == null) Assert.Null(areas[i].Fill); else Assert.Equal(fill.Value, areas[i].Fill!.Value, 9);
        Assert.Equal((string)w["sentence"], areas[i].Sentence);
        Assert.Equal(w["order"]!.Select(t => (string)t!), areas[i].Traits.Select(t => t.Label));
      }
      Assert.Equal(c["expected"]!["defining"]!.Select(t => (string)t!), FingerprintBars.Defining(data).Select(t => t.Label));
    }
  }

  [Fact]
  public void FillIsHalfAtTypicalAndFullAtTwice() {
    Assert.Equal(0.5, FingerprintBars.BarFill(0.1, 0.1));
    Assert.Equal(1, FingerprintBars.BarFill(0.5, 0.1));
    Assert.Equal(0.25, FingerprintBars.BarFill(0.05, 0.1));
    Assert.Null(FingerprintBars.BarFill(0.3, 0));
  }

  [Fact]
  public void SeriesAndStrengthBands() {
    var data = FingerprintData.Parse(JObject.Parse(@"{""active"":[
      {""characteristicId"":""a"",""label"":""Franchise: Silent Hill"",""family"":""franchise"",""role"":""taste"",""strength"":0.9},
      {""characteristicId"":""b"",""label"":""Horror"",""family"":""genre"",""role"":""taste"",""strength"":0.55,""reasons"":[{""kind"":""interpretation"",""text"":""Scary.""}]}]}"))!;
    Assert.Equal(new[] { "Silent Hill" }, FingerprintBars.Series(data));
    Assert.Equal(1, FingerprintBars.TraitCount(data));
    Assert.Equal("mid", data.Active[1].Band);
    Assert.Equal("Scary.", data.Active[1].Reasons[0].Text);
    Assert.Null(FingerprintData.Parse(null));
  }
}
}
