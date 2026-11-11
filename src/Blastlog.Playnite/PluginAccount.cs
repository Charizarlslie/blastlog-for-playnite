using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using Blastlog.Sync.Core;
using Newtonsoft.Json.Linq;

namespace Blastlog.Playnite {
/// <summary>
/// The account pieces the native Settings window needs (Screens/SettingsWindow.cs): writes that answer in words on the
/// card instead of a dialog, the player's picture as bytes, and forgetting the token on this PC.
/// </summary>
public sealed partial class BlastlogPlugin {
  /// <summary>
  /// A mirror write that never shows a dialog: the answer, or null and the site's error code (or "offline"). Clears the
  /// read cache on success, as <see cref="Write(string, object, HttpMethod)"/> does.
  /// </summary>
  public async Task<(JObject value, string error)> TryWrite(string path, object body, HttpMethod method) {
    if (!Configured) return (null, "not_configured");
    try { var r = await Task.Run(() => Client().SendAsync(method, path, body)); cache.Clear(); SaveCache(); return (r.Value ?? new JObject(), null); }
    catch (BlastlogException e) { Log.Warn("Blastlog write refused (" + path + "): " + e.Error); return (null, e.Error); }
    catch (Exception e) { Log.Warn("Blastlog write failed (" + path + "): " + e.Message); return (null, "offline"); }
  }

  /// <summary>The last copy of a mirror read, however old, without asking Blastlog (null when there's none).</summary>
  public JObject Cached(string path) => cache.TryGetValue(path, out var hit) ? hit.Value : null;

  /// <summary>Words for a refusal the cards don't explain themselves.</summary>
  public static string RefusalWords(string error) => error == "offline" ? "That didn't work. Check your connection and try again."
    : error == "not_configured" ? "Add your Blastlog sync token in Add-ons, Extension settings, Blastlog first."
    : error == "sync_scope_required" ? "This token is read only. Make one that can change your library on Blastlog."
    : error == "not_found" || error == "http_error" ? "Blastlog can't change that from Playnite yet. It can on the site."
    : $"That didn't work ({error}). Try again.";

  /// <summary>
  /// A player's picture (GET /sync/v1/users/:id/picture, the site's rules), or null when there's none or it's hidden.
  /// </summary>
  public async Task<byte[]> PictureBytes(string userId) {
    if (!Configured || string.IsNullOrEmpty(userId)) return null;
    try {
      var url = new Uri(ServerUrl + "/api/sync/v1/users/" + Uri.EscapeDataString(userId) + "/picture");
      using (var request = new HttpRequestMessage(HttpMethod.Get, url)) {
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.Token);
        using (var response = await Http.SendAsync(request).ConfigureAwait(false)) {
          if (!response.IsSuccessStatusCode) return null;
          var type = response.Content.Headers.ContentType?.MediaType ?? "";
          if (!type.StartsWith("image/")) return null;
          return await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
        }
      }
    } catch (Exception e) { Log.Warn("Blastlog: picture didn't load: " + e.Message); return null; }
  }

  /// <summary>
  /// Signs Playnite out of Blastlog on this PC: forgets the saved token (it stays valid on Blastlog until revoked there)
  /// and the cached reads. Sync and the screens stop until a token is pasted again.
  /// </summary>
  public void ForgetToken() { settings.ForgetToken(); cache.Clear(); SaveCache(); }

  /// <summary>The plugin's own settings (server, token, sync choices).</summary>
  public void OpenSyncSettings() { try { OpenSettingsView(); } catch (Exception e) { Log.Error(e, "Blastlog: open sync settings"); } }
}
}
