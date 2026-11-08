using System.Linq;
using Blastlog.Sync.Core.Design;
using Xunit;

namespace Blastlog.Sync.Core.Tests {
/// <summary>The media viewer's order, labels and keys must match the site's (web media-viewer.test.ts).</summary>
public class MediaSequenceTests {
  static readonly System.Collections.Generic.List<MediaItem> Items = MediaSequence.Items(new[] { ("a", "Launch trailer"), ("b", (string)null) }, new[] { "s1", "s2", "s3" });

  [Fact]
  public void TrailersFirstThenEveryScreenshot() =>
    Assert.Equal(new[] { "a", "b", "s1", "s2", "s3" }, Items.Select(i => i.IsVideo ? i.YoutubeId : i.Url).ToArray());

  [Fact]
  public void LabelsEachItem() {
    Assert.Equal(new[] { "Launch trailer", "Trailer", "Screenshot 1", "Screenshot 3" }, new[] { 0, 1, 2, 4 }.Select(i => MediaSequence.Label(Items, i)).ToArray());
    Assert.Equal("", MediaSequence.Label(Items, 9));
  }

  [Fact]
  public void ArrowsHomeAndEndStopAtTheEnds() {
    Assert.Equal(1, MediaSequence.KeyTarget("ArrowRight", 0, 5));
    Assert.Equal(4, MediaSequence.KeyTarget("ArrowRight", 4, 5));
    Assert.Equal(0, MediaSequence.KeyTarget("ArrowLeft", 0, 5));
    Assert.Equal(0, MediaSequence.KeyTarget("Home", 3, 5));
    Assert.Equal(4, MediaSequence.KeyTarget("End", 0, 5));
    Assert.Null(MediaSequence.KeyTarget("Enter", 0, 5));
    Assert.Null(MediaSequence.KeyTarget("ArrowRight", 0, 0));
  }
}
}
