using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Blastlog.Playnite.Ui;
using Newtonsoft.Json.Linq;
using static Blastlog.Playnite.Screens.TasteUi;

namespace Blastlog.Playnite.Screens {
/// <summary>
/// Friends (apps/web Friends.tsx): your friends (Remove, with Undo), your groups of friends (new, edit, delete with
/// Undo), and your friend link to copy and send (Make a new link). Reads GET /sync/v1/friends and /friend-groups; writes
/// through the site's own friend routes (docs/PLAYNITE_SYNC.md "Round 2"), so History and the site's checks apply.
/// </summary>
static class FriendsPage {
  public static void Open(BlastlogPlugin plugin) => LinkPages.Show(plugin, "Friends", close => new View(plugin), width: 920);

  sealed class View : StackPanel {
    readonly BlastlogPlugin plugin; readonly StackPanel body = new StackPanel(); readonly ContentControl flash = new ContentControl();
    int loading; bool busy;
    List<JObject> friends = new List<JObject>(); JObject groups; string code;
    // Which "are you sure?" is open (a friend's id, or "link"), and the group being written ("new" or its id).
    string confirming, editing; string draftName = ""; List<string> draftMembers = new List<string>();

    public View(BlastlogPlugin plugin) {
      this.plugin = plugin;
      Children.Add(LinkPages.Head("My Blastlog", "Friends", "Your friends see your library, ratings, reviews and Profile, and you see theirs. Nobody else does."));
      Children.Add(flash); Children.Add(body);
      body.Children.Add(T("Loading friends…", "Blastlog.Muted"));
      Run(() => Load(false), "friends load");
    }

    async Task Load(bool fresh) {
      var mine = ++loading;
      var data = (await plugin.Mirror("friends", fresh)).Value;
      var list = (data?["friends"] as JArray ?? new JArray()).OfType<JObject>().ToList();
      var g = list.Count > 1 ? (await plugin.Mirror("friend-groups", fresh)).Value : null;
      if (mine != loading) return;
      if (data == null) { body.Children.Clear(); body.Children.Add(T("Couldn’t load your friends. Check your connection and try again.", "Blastlog.Muted")); return; }
      friends = list; groups = g; code = S(data, "code");
      Render();
    }

    void Render() {
      body.Children.Clear();
      body.Children.Add(Friends());
      if (friends.Count > 1) body.Children.Add(Groups());
      body.Children.Add(AddFriend());
    }

    /// <summary>The result of an action, with Undo when it can be undone.</summary>
    void Say(string text, Func<Task> undo = null) {
      var buttons = new List<Button>();
      if (undo != null) buttons.Add(Kit.Button("Undo", () => Act(async () => { await undo(); Say("Undone."); }), "Blastlog.TextButton"));
      flash.Content = PnKit.Notice(text, buttons, () => flash.Content = null);
    }
    void Fail(string text) => flash.Content = LinkPages.Notice(text, false);

    /// <summary>Runs a write once at a time, then reads the page again (writes clear the read cache).</summary>
    async Task Act(Func<Task> work) {
      if (busy) return;
      busy = true;
      try { await work(); } catch (Exception e) { Global.Log.Error(e, "Blastlog friends"); Fail("That didn’t work. Try again."); }
      finally { busy = false; await Load(true); }
    }

    static string Name(JObject friend) => S(friend, "displayName") ?? "Someone";

    UIElement Friends() {
      if (friends.Count == 0) return LinkPages.Card("Your friends", null, T("No friends yet. Send someone your link below.", "Blastlog.Muted"));
      var list = new StackPanel();
      foreach (var friend in friends) {
        var name = Name(friend); var id = S(friend, "id");
        var block = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
        var row = new DockPanel { LastChildFill = true };
        if (confirming != id) {
          var remove = Kit.Button("Remove", () => { confirming = id; Render(); }, "Blastlog.Ghost");
          AutomationProperties.SetName(remove, "Remove " + name);
          DockPanel.SetDock(remove, Dock.Right); row.Children.Add(remove);
        }
        var face = LinkPages.Face(name); face.Margin = new Thickness(0, 0, 14, 0);
        DockPanel.SetDock(face, Dock.Left); row.Children.Add(face);
        row.Children.Add(Stack(null, T(name, "Blastlog.Body", null, 16, FontWeights.Bold), T(Since(friend), "Blastlog.Small")).Also(s => s.VerticalAlignment = VerticalAlignment.Center));
        block.Children.Add(row);
        if (confirming == id) block.Children.Add(Confirm($"Stop being friends with {name}? You won’t see each other’s games any more, and your current link won’t bring them back.",
          "Stop being friends", true, () => { confirming = null; Run(() => Remove(id, name), "friends remove"); }, () => { confirming = null; Render(); }));
        list.Children.Add(block);
      }
      return LinkPages.Card("Your friends", null, list);
    }

    Task Remove(string id, string name) => Act(async () => {
      var r = await plugin.Write("friends/" + id, new { }, HttpMethod.Delete);
      var friendship = S(r, "friendshipId");
      if (r == null) return;
      Say($"You and {name} aren’t friends any more.", friendship == null ? null : (Func<Task>)(async () => { await plugin.Write("friends/restore", new { friendshipId = friendship }); }));
    });

    static string Since(JObject friend) {
      if (friend["original"]?.Type == JTokenType.Boolean && (bool)friend["original"]) return "Friends from the start";
      var raw = friend["since"]; DateTime at;
      if (raw?.Type == JTokenType.Date) at = ((DateTime)raw).ToLocalTime();
      else if (DateTime.TryParse(S(friend, "since"), CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out at)) at = at.ToLocalTime();
      else return "Friends";
      return "Friends since " + at.ToString("MMM d, yyyy", CultureInfo.CurrentCulture);
    }

    // ---- Groups (friend-groups.ts): only the player sees them; nobody in a group is told. ----
    UIElement Groups() {
      const string note = "Put friends in groups (“Work”, “College”) to compare one group at a time. Only you see your groups; nobody is told.";
      var content = new StackPanel();
      var names = friends.ToDictionary(f => S(f, "id") ?? "", Name);
      if (groups == null) content.Children.Add(T("Couldn’t load your groups.", "Blastlog.Muted"));
      else foreach (var group in (groups["groups"] as JArray ?? new JArray()).OfType<JObject>()) {
        var g = group; var memberIds = Ids(g["memberIds"]);
        var members = memberIds.Select(m => names.TryGetValue(m, out var n) ? n : null).Where(n => n != null).ToList();
        var row = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
        var buttons = Row(Kit.Button("Edit", () => Edit(g), "Blastlog.Ghost").Also(b => { b.Margin = new Thickness(0, 0, 8, 0); b.IsEnabled = editing == null; AutomationProperties.SetName(b, "Edit " + S(g, "name")); }),
          Kit.Button("Delete", () => DeleteGroup(g), "Blastlog.Ghost").Also(b => AutomationProperties.SetName(b, "Delete " + S(g, "name"))));
        DockPanel.SetDock(buttons, Dock.Right); row.Children.Add(buttons);
        row.Children.Add(Stack(null, T(S(g, "name") ?? "", "Blastlog.Body", null, 16, FontWeights.Bold), T(members.Count > 0 ? string.Join(", ", members) : "Nobody yet", "Blastlog.Small"))
          .Also(s => s.VerticalAlignment = VerticalAlignment.Center));
        content.Children.Add(row);
      }
      if (editing != null) content.Children.Add(Form());
      else content.Children.Add(Link("New group", () => Edit(null)).Also(b => { b.HorizontalAlignment = HorizontalAlignment.Left; b.Margin = new Thickness(-6, 0, 0, 0); }));
      return LinkPages.Card("Groups", note, content);
    }

    static List<string> Ids(JToken t) => (t as JArray ?? new JArray()).Select(v => v.Type == JTokenType.String ? (string)v : null).Where(v => v != null).ToList();

    void Edit(JObject group) {
      editing = group == null ? "new" : S(group, "id"); draftName = S(group, "name") ?? ""; draftMembers = Ids(group?["memberIds"]);
      flash.Content = null; Render();
    }

    UIElement Form() {
      var form = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
      form.Children.Add(T("Name", "Blastlog.Small", "Blastlog.Text", 0, FontWeights.SemiBold));
      var input = new TextBox { Text = draftName, MaxLength = 40, BorderThickness = new Thickness(0), Background = System.Windows.Media.Brushes.Transparent, FontSize = 16,
        VerticalContentAlignment = VerticalAlignment.Center, MinHeight = 40 };
      input.SetResourceReference(Control.ForegroundProperty, "Blastlog.Text"); input.SetResourceReference(Control.FontFamilyProperty, "Blastlog.Font");
      input.SetResourceReference(System.Windows.Controls.Primitives.TextBoxBase.CaretBrushProperty, "Blastlog.Text");
      AutomationProperties.SetName(input, "Group name");
      var field = Box(input, "Blastlog.Surface2", 12, new Thickness(12, 2, 12, 2), "Blastlog.Edge"); field.MinHeight = 44; field.Width = 320; field.HorizontalAlignment = HorizontalAlignment.Left;
      field.Margin = new Thickness(0, 4, 0, 12);
      form.Children.Add(field);
      form.Children.Add(T("Who’s in it", "Blastlog.Small", "Blastlog.Text", 0, FontWeights.SemiBold));
      foreach (var friend in friends) {
        var id = S(friend, "id");
        form.Children.Add(LinkPages.Check(Name(friend), draftMembers.Contains(id), on => { draftMembers.Remove(id); if (on) draftMembers.Add(id); }));
      }
      var save = Kit.Button("Save group", SaveGroup); save.IsEnabled = draftName.Trim().Length > 0; save.Margin = new Thickness(0, 0, 10, 0);
      input.TextChanged += (s, e) => { draftName = input.Text; save.IsEnabled = draftName.Trim().Length > 0; };
      form.Children.Add(Row(save, Kit.Button("Cancel", () => { editing = null; Render(); }, "Blastlog.Ghost")).Also(r => r.Margin = new Thickness(0, 12, 0, 0)));
      input.Loaded += (s, e) => { try { input.Focus(); input.CaretIndex = input.Text.Length; } catch (Exception) { } };
      return Box(form, "Blastlog.Surface2", 14, new Thickness(14), "Blastlog.Line");
    }

    Task SaveGroup() => Act(async () => {
      var name = draftName.Trim(); var body = new { name, memberIds = draftMembers.ToArray() }; var made = editing == "new";
      var r = made ? await plugin.Write("friend-groups", body) : await plugin.Write("friend-groups/" + editing, body, HttpMethod.Put);
      if (r == null) return;
      editing = null;
      Say($"{(made ? "Made" : "Saved")} “{name}”.");
    });

    void DeleteGroup(JObject group) => Run(() => Act(async () => {
      var r = await plugin.Write("friend-groups/" + S(group, "id"), new { }, HttpMethod.Delete);
      if (r == null) return;
      var back = r["deleted"] as JObject ?? group;
      var name = S(back, "name") ?? S(group, "name"); var members = Ids(back["memberIds"]);
      if (editing == S(group, "id")) editing = null;
      Say($"Deleted “{name}”.", async () => { await plugin.Write("friend-groups", new { name, memberIds = members.ToArray() }); });
    }), "friends delete group");

    // ---- Your friend link ----
    UIElement AddFriend() {
      const string note = "Send this link to a friend. When they open it, you’re friends. If they’re new to Blastlog, it asks them to sign in first and finishes once they’re let in.";
      var content = new StackPanel();
      if (!string.IsNullOrEmpty(code)) {
        var url = plugin.ServerUrl + "/friends/add/" + code;
        var input = new TextBox { Text = url, IsReadOnly = true, BorderThickness = new Thickness(0), Background = System.Windows.Media.Brushes.Transparent, FontSize = 15,
          VerticalContentAlignment = VerticalAlignment.Center, MinHeight = 40 };
        input.SetResourceReference(Control.ForegroundProperty, "Blastlog.Text"); input.SetResourceReference(Control.FontFamilyProperty, "Blastlog.Font");
        AutomationProperties.SetName(input, "Your friend link");
        input.GotKeyboardFocus += (s, e) => { try { input.SelectAll(); } catch (Exception) { } };
        var field = Box(input, "Blastlog.Surface2", 12, new Thickness(12, 2, 12, 2), "Blastlog.Edge"); field.MinHeight = 44; field.Margin = new Thickness(0, 0, 10, 0);
        var copy = Kit.Button("Copy", () => {
          try { Clipboard.SetText(url); Say("Link copied."); }
          catch (Exception) { Fail("Couldn’t copy it; select the link and copy it yourself."); }
        });
        var line = new DockPanel(); DockPanel.SetDock(copy, Dock.Right); line.Children.Add(copy); line.Children.Add(field);
        content.Children.Add(line);
      }
      if (confirming == "link") content.Children.Add(Confirm("Make a new link? The old one stops working. Friends you already have stay friends.", "Make a new link", false,
        () => { confirming = null; Run(NewLink, "friends new link"); }, () => { confirming = null; Render(); }));
      else content.Children.Add(Link("Make a new link", () => { confirming = "link"; Render(); }).Also(b => { b.HorizontalAlignment = HorizontalAlignment.Left; b.Margin = new Thickness(-6, 8, 0, 0); }));
      return LinkPages.Card("Add a friend", note, content);
    }

    Task NewLink() => Act(async () => {
      if (await plugin.Write("friends/link", new { }) != null) Say("New link ready; the old one no longer works.");
    });
  }

  static class Global { public static readonly global::Playnite.SDK.ILogger Log = global::Playnite.SDK.LogManager.GetLogger(); }
}
}
