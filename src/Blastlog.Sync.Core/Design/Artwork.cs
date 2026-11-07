using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Blastlog.Sync.Core.Design {
/// <summary>Where a picture is shown, which decides its size (the site's ArtworkContext).</summary>
public enum ArtworkContext { Thumbnail, Cover, Backdrop, Hero }

/// <summary>
/// Game art URLs, ported from apps/web/src/artwork.ts so Playnite shows the same pictures the site does, at the same
/// sizes. Covers come from IGDB's image server.
/// </summary>
public static class Artwork {
  public static string IgdbImageUrl(string imageId, string size) => $"https://images.igdb.com/igdb/image/upload/t_{size}/{imageId}.jpg";
  /// <summary>An IGDB image URL at another size; other hosts' URLs are returned as they are.</summary>
  public static string NormalizedLegacyUrl(string url, string size) {
    if (string.IsNullOrEmpty(url)) return null;
    var absolute = url.StartsWith("//") ? "https:" + url : url;
    return absolute.Contains("images.igdb.com/") ? Regex.Replace(absolute, "/t_[^/]+/", $"/t_{size}/") : absolute;
  }
  static string CoverSize(ArtworkContext c) => c == ArtworkContext.Thumbnail ? "cover_big" : c == ArtworkContext.Cover ? "cover_big_2x" : c == ArtworkContext.Backdrop ? "original" : "720p";

  public static string Url(Game game, ArtworkContext context) {
    if (game == null) return null;
    var art = game.Artwork;
    if (context == ArtworkContext.Hero) {
      var candidates = art?.HeroCandidates ?? new List<HeroCandidate>();
      var artwork = SelectHero(candidates.Where(c => c.Source == "artwork"));
      var screenshot = artwork == null ? SelectHero(candidates.Where(c => c.Source == "screenshot")) : null;
      var hero = candidates.Count > 0 ? (artwork ?? screenshot)?.ImageId : art?.HeroImageId;
      if (!string.IsNullOrEmpty(hero)) return IgdbImageUrl(hero, "1080p");
    }
    if (!string.IsNullOrEmpty(art?.CoverImageId)) return IgdbImageUrl(art.CoverImageId, CoverSize(context));
    return NormalizedLegacyUrl(game.CoverUrl, CoverSize(context));
  }

  static readonly Regex Unsuitable = new Regex(@"(?:^|[\s_-])(logo|icon|cover|box[\s_-]?art)(?:$|[\s_-])", RegexOptions.IgnoreCase);
  /// <summary>A stable, wide image for heroes: no portrait, tiny, animated, logo or cover assets (the site's selectHeroImage).</summary>
  public static HeroCandidate SelectHero(IEnumerable<HeroCandidate> candidates) {
    var acceptable = candidates.Where(c => {
      if (string.IsNullOrEmpty(c.ImageId) || c.Animated == true) return false;
      var typeName = (string)c.ImageType?["name"];
      if (!string.IsNullOrEmpty(typeName) && Unsuitable.IsMatch(typeName.Trim())) return false;
      if (c.Width != null && c.Height != null) {
        var ratio = (double)c.Width / c.Height.Value;
        if (c.Width < 640 || c.Height < 360 || ratio < 1.25 || ratio > 2.5) return false;
      }
      return true;
    }).ToList();
    int Resolution(HeroCandidate i) => i.Width == null || i.Height == null ? 0 : i.Width >= 1280 && i.Height >= 720 ? 3 : i.Width >= 960 && i.Height >= 540 ? 2 : 1;
    int Aspect(HeroCandidate i) { if (i.Width == null || i.Height == null) return 0; var r = (double)i.Width / i.Height.Value; return r >= 1.45 && r <= 2.15 ? 1 : 0; }
    return acceptable.OrderByDescending(Aspect).ThenBy(i => i.AlphaChannel == true ? 1 : 0).ThenByDescending(Resolution)
      .ThenByDescending(i => (long)(i.Width ?? 0) * (i.Height ?? 0)).ThenBy(i => i.ImageId, StringComparer.Ordinal).FirstOrDefault();
  }
}
}
