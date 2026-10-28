using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using Blastlog.Sync.Core;
using Playnite.SDK;

namespace Blastlog.Playnite {
/// <summary>
/// The sync token, encrypted with Windows' per-user protection (DPAPI, CurrentUser; security review 2026-10-23 rule 6).
/// Only the encrypted bytes are saved in the plugin's settings; the token itself is never written to disk or logged,
/// and is sent only in the Authorization header.
/// </summary>
public static class TokenVault {
  static readonly byte[] Entropy = Encoding.UTF8.GetBytes("Blastlog for Playnite · sync token");
  public static string Protect(string token) => string.IsNullOrEmpty(token) ? null
    : Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(token), Entropy, DataProtectionScope.CurrentUser));
  public static string Unprotect(string blob) {
    if (string.IsNullOrEmpty(blob)) return null;
    try { return Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(blob), Entropy, DataProtectionScope.CurrentUser)); }
    catch (Exception) { return null; } // Another Windows user or machine: the token has to be pasted again.
  }
  /// <summary>What logs and screens may show of a token: its first 8 characters.</summary>
  public static string Masked(string token) => string.IsNullOrEmpty(token) ? "(none)" : token.Substring(0, Math.Min(8, token.Length)) + "…";
}

/// <summary>What the plugin saves (Playnite's plugin settings). The token is only ever here encrypted.</summary>
public sealed class BlastlogSettingsData {
  public string ServerUrl {get;set;} = "https://blastlog.games";
  public string ProtectedToken {get;set;}
  public bool AutoSync {get;set;} = true;
  public ConflictRule ConflictRule {get;set;} = ConflictRule.BlastlogWins;
  public bool IncludeHidden {get;set;}
  public bool BlastlogTags {get;set;} = true;
  public bool TraitTags {get;set;} = true;
}

public sealed class BlastlogSettings : ISettings {
  readonly BlastlogPlugin plugin; BlastlogSettingsData editing;
  public BlastlogSettingsData Data {get; private set;}
  /// <summary>A token typed in the settings view, not saved until OK.</summary>
  public string PendingToken {get;set;}
  public BlastlogSettings(BlastlogPlugin plugin) { this.plugin = plugin; Data = plugin.LoadPluginSettings<BlastlogSettingsData>() ?? new BlastlogSettingsData(); }
  public string Token => TokenVault.Unprotect(Data.ProtectedToken);
  public void BeginEdit() { editing = Clone(Data); PendingToken = null; }
  public void CancelEdit() { if (editing != null) Data = editing; PendingToken = null; }
  public void EndEdit() {
    if (!string.IsNullOrWhiteSpace(PendingToken)) Data.ProtectedToken = TokenVault.Protect(PendingToken.Trim());
    PendingToken = null; plugin.SavePluginSettings(Data); plugin.SettingsChanged();
  }
  public bool VerifySettings(out List<string> errors) {
    errors = new List<string>();
    if (!Uri.TryCreate(Data.ServerUrl, UriKind.Absolute, out var uri) || (uri.Scheme != "https" && !(uri.Scheme == "http" && uri.IsLoopback))) errors.Add("The server address must start with https://.");
    var token = string.IsNullOrWhiteSpace(PendingToken) ? null : PendingToken.Trim();
    if (token != null && !(token.StartsWith("blt_") && token.Length == 47)) errors.Add("That doesn't look like a Blastlog sync token (it starts with blt_).");
    return errors.Count == 0;
  }
  /// <summary>The kit's settings pack: sync choices only (never the server or the token). Returns what it set, in words.</summary>
  public string ApplyPack(string json) {
    var pack = Newtonsoft.Json.Linq.JObject.Parse(json);
    if (pack["autoSync"] != null) Data.AutoSync = (bool)pack["autoSync"];
    if (pack["includeHidden"] != null) Data.IncludeHidden = (bool)pack["includeHidden"];
    if (pack["blastlogTags"] != null) Data.BlastlogTags = (bool)pack["blastlogTags"];
    if (pack["traitTags"] != null) Data.TraitTags = (bool)pack["traitTags"];
    if (pack["conflictRule"] != null && Enum.TryParse((string)pack["conflictRule"], out ConflictRule rule)) Data.ConflictRule = rule;
    return "Settings pack loaded. Press Save to keep it.";
  }
  public void ForgetToken() { Data.ProtectedToken = null; plugin.SavePluginSettings(Data); plugin.SettingsChanged(); }
  static BlastlogSettingsData Clone(BlastlogSettingsData d) => new BlastlogSettingsData { ServerUrl = d.ServerUrl, ProtectedToken = d.ProtectedToken, AutoSync = d.AutoSync,
    ConflictRule = d.ConflictRule, IncludeHidden = d.IncludeHidden, BlastlogTags = d.BlastlogTags, TraitTags = d.TraitTags };
}

/// <summary>Settings view, built in code: server, token (paste once), test connection, the sync choices.</summary>
public sealed class BlastlogSettingsView : UserControl {
  public BlastlogSettingsView(BlastlogSettings settings, BlastlogPlugin plugin) {
    var d = settings.Data;
    var stack = new StackPanel { Margin = new Thickness(20) };
    UiKit.Heading(stack, "Blastlog server");
    var server = new TextBox { Text = d.ServerUrl, MinWidth = 360, Margin = new Thickness(0, 4, 0, 12) };
    server.TextChanged += (s, e) => d.ServerUrl = server.Text.Trim();
    stack.Children.Add(server);
    UiKit.Heading(stack, "Sync token");
    stack.Children.Add(UiKit.Text(settings.Token == null ? "Make one on Blastlog: Your taste → Your account → Playnite sync. Paste it here once." : $"Saved ({TokenVault.Masked(settings.Token)}). Paste a new one to replace it.", muted: true));
    var token = new PasswordBox { MinWidth = 360, Margin = new Thickness(0, 4, 0, 6) };
    token.PasswordChanged += (s, e) => settings.PendingToken = token.Password;
    stack.Children.Add(token);
    var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 14) };
    var result = UiKit.Text("", muted: true); result.Margin = new Thickness(12, 0, 0, 0); result.VerticalAlignment = VerticalAlignment.Center;
    var test = UiKit.Button("Test connection", async () => {
      result.Text = "Checking…";
      result.Text = await plugin.TestConnection(d.ServerUrl, string.IsNullOrWhiteSpace(settings.PendingToken) ? settings.Token : settings.PendingToken.Trim());
    });
    row.Children.Add(test); row.Children.Add(result);
    if (settings.Token != null) { var forget = UiKit.Button("Forget the token", () => { settings.ForgetToken(); result.Text = "Forgotten. Paste a new one to sync again."; }); forget.Margin = new Thickness(10, 0, 0, 0); row.Children.Insert(1, forget); }
    stack.Children.Add(row);
    UiKit.Heading(stack, "Syncing");
    stack.Children.Add(UiKit.Check("Sync on its own: at start-up, after a library update, after you play, and every 30 minutes", d.AutoSync, v => d.AutoSync = v));
    stack.Children.Add(UiKit.Check("Include hidden games", d.IncludeHidden, v => d.IncludeHidden = v));
    stack.Children.Add(UiKit.Check("Add Blastlog tags (Backlog, Next up, Currently playing, Wishlist)", d.BlastlogTags, v => d.BlastlogTags = v));
    stack.Children.Add(UiKit.Check("Add a tag per Blastlog trait (\"Blastlog trait: …\")", d.TraitTags, v => d.TraitTags = v));
    stack.Children.Add(UiKit.Text("When both changed the same game differently:", muted: false));
    var rule = new ComboBox { MinWidth = 260, Margin = new Thickness(0, 4, 0, 12) };
    // No tuples: .NET Framework 4.6.2 has no ValueTuple without an extra package.
    foreach (var choice in new[] { new KeyValuePair<ConflictRule,string>(ConflictRule.BlastlogWins, "Blastlog wins"), new KeyValuePair<ConflictRule,string>(ConflictRule.PlayniteWins, "Playnite wins"),
      new KeyValuePair<ConflictRule,string>(ConflictRule.AskMe, "Ask me (leave both, list it)") })
      rule.Items.Add(new ComboBoxItem { Content = choice.Value, Tag = choice.Key, IsSelected = d.ConflictRule == choice.Key });
    rule.SelectionChanged += (s, e) => { if (rule.SelectedItem is ComboBoxItem i) d.ConflictRule = (ConflictRule)i.Tag; };
    stack.Children.Add(rule);
    stack.Children.Add(UiKit.Text("Nothing is ever deleted by a sync, and every sync can be undone on Blastlog (Your account → Playnite sync).", muted: true));
    var packNote = UiKit.Text("", muted: true);
    stack.Children.Add(UiKit.Button("Load a settings pack…", () => {
      var file = plugin.Api.Dialogs.SelectFile("Settings pack|*.json"); if (string.IsNullOrEmpty(file)) return;
      // Rebuilt so the boxes show the pack; the fresh view gives up its content first (WPF allows one parent).
      try { var note = settings.ApplyPack(System.IO.File.ReadAllText(file)); var fresh = new BlastlogSettingsView(settings, plugin); var c = fresh.Content; fresh.Content = null; Content = c; plugin.Api.Dialogs.ShowMessage(note); }
      catch (Exception e) { packNote.Text = "That file isn't a Blastlog settings pack (" + e.Message + ")."; }
    }));
    stack.Children.Add(packNote);
    Content = new ScrollViewer { Content = stack, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
  }
}
}
