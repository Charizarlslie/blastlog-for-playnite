using System;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Blastlog.Playnite.Ui;
using Newtonsoft.Json.Linq;
using Playnite.SDK;
using static Blastlog.Playnite.Screens.TasteUi;

namespace Blastlog.Playnite.Screens {
/// <summary>
/// The game page's "Part of" line and its quiet collection control (apps/web LibraryCollections.tsx PartOfCollection and
/// CollectionPicker; Charlie, 2026-11-05: low-key): "Part of X" opens the collection, "Change collection" / "Add to a
/// collection" opens the picker: None, the player's collections, or New collection… with a name of their own. Grouping
/// only: the game's own stars and review don't change. Writes PUT /sync/v1/entries/:id/collection and POST
/// /sync/v1/collections. The game page keeps one per entry (<see cref="Attach"/>) so an open picker survives its redraws.
/// </summary>
sealed class CollectionPicker : ContentControl {
  static readonly ILogger Log = LogManager.GetLogger();
  const string New = "__new__";
  readonly BlastlogPlugin plugin; readonly Action changed;
  public string EntryId { get; }
  JObject collection; bool open, saving; string choice = "", name = "", error;
  JObject mine; bool loadingMine;

  CollectionPicker(BlastlogPlugin plugin, string entryId, Action changed) { this.plugin = plugin; EntryId = entryId; this.changed = changed; }

  /// <summary>
  /// The picker for this entry in <paramref name="slot"/> (made when the entry changed), showing <paramref name="current"/>
  /// (the entry's <c>collection</c>: <c>{ id, title }</c> or null), taken off the page it was last placed on.
  /// </summary>
  public static CollectionPicker Attach(ref CollectionPicker slot, BlastlogPlugin plugin, string entryId, JObject current, Action changed) {
    if (slot == null || slot.EntryId != entryId) slot = new CollectionPicker(plugin, entryId, changed);
    var picker = slot;
    if (picker.Parent is Panel panel) panel.Children.Remove(picker);
    else if (picker.Parent is Decorator decorator) decorator.Child = null;
    else if (picker.Parent is ContentControl host) host.Content = null;
    if (!picker.saving && (string)picker.collection?["id"] != (string)current?["id"]) picker.open = false;
    picker.collection = current;
    picker.Render();
    return picker;
  }

  string CurrentId => (string)collection?["id"];

  void Render() {
    try {
      var stack = new StackPanel();
      var line = new WrapPanel();
      if (collection != null) {
        line.Children.Add(T("Part of", "Blastlog.Muted").Also(t => { t.FontSize = 14; t.VerticalAlignment = VerticalAlignment.Center; t.Margin = new Thickness(0, 0, 0, 0); }));
        var id = CurrentId;
        line.Children.Add(Fingerprint.LinkButton(((string)collection["title"] ?? "Collection") + " →", () => Nav.Collection(plugin, id, changed), game: true).Also(b => { b.FontWeight = FontWeights.Bold; b.Margin = new Thickness(-4, 0, 0, 0); }));
      }
      if (!open) line.Children.Add(CollectionUi.Small(collection != null ? "Change collection" : "Add to a collection", Open).Also(b => b.Margin = new Thickness(collection != null ? -6 : -10, 0, 0, 0)));
      stack.Children.Add(line);
      if (open) stack.Children.Add(Form());
      Content = stack;
    } catch (Exception e) { Log.Error(e, "Blastlog collection picker"); }
  }

  void Open() {
    open = true; choice = CurrentId ?? ""; name = ""; error = null;
    Render();
    if (mine == null && !loadingMine) Run(LoadMine, "collection picker list");
  }

  async Task LoadMine() {
    loadingMine = true;
    try { mine = (await plugin.Mirror("collections", false)).Value ?? new JObject(); }
    finally { loadingMine = false; }
    if (open) Render();
  }

  bool Unchanged => choice == (CurrentId ?? "") || (choice == New && string.IsNullOrWhiteSpace(name));

  /// <summary>The site's .coll-picker: Collection (None, theirs, New collection…), Name for a new one, Save, Cancel.</summary>
  FrameworkElement Form() {
    var form = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
    form.Children.Add(T("Collection", "Blastlog.Muted").Also(t => { t.FontSize = 14; t.VerticalAlignment = VerticalAlignment.Center; t.Margin = new Thickness(0, 0, 8, 6); }));
    var box = new ComboBox { MinHeight = 44, MinWidth = 220, FontSize = 15, VerticalContentAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 6), IsEnabled = mine != null && !saving };
    AutomationProperties.SetName(box, "Collection");
    box.Items.Add(new ComboBoxItem { Content = "None", Tag = "" });
    foreach (var item in mine.Objects("items")) box.Items.Add(new ComboBoxItem { Content = item.S("title") ?? "Collection", Tag = item.S("id") });
    // The current one even before the list arrives (or if the list couldn't be read).
    if (CurrentId != null && !box.Items.OfType<ComboBoxItem>().Any(i => (string)i.Tag == CurrentId)) box.Items.Add(new ComboBoxItem { Content = (string)collection["title"] ?? "Collection", Tag = CurrentId });
    box.Items.Add(new ComboBoxItem { Content = "New collection…", Tag = New });
    box.SelectedItem = box.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == choice) ?? box.Items[0];
    form.Children.Add(box);

    var save = Kit.Button(saving ? "Saving…" : "Save", Save, "Blastlog.Primary");
    save.IsEnabled = !saving && !Unchanged; save.Margin = new Thickness(0, 0, 8, 6);
    var nameHost = new ContentControl();
    void NameField() {
      if (choice != New) { nameHost.Content = null; return; }
      var field = Dash.Field(); field.Text = name;
      field.TextChanged += (s, e) => { name = field.Text; save.IsEnabled = !saving && !Unchanged; };
      field.PreviewKeyDown += (s, e) => { if (e.Key == System.Windows.Input.Key.Enter && save.IsEnabled) { e.Handled = true; Run(Save, "collection picker save"); } };
      var row = new WrapPanel();
      row.Children.Add(T("Name", "Blastlog.Muted").Also(t => { t.FontSize = 14; t.VerticalAlignment = VerticalAlignment.Center; t.Margin = new Thickness(0, 0, 8, 6); }));
      row.Children.Add(CollectionUi.Field(field, "Tomb Raider games", "Name", 260).Also(f => f.Margin = new Thickness(0, 0, 8, 6)));
      nameHost.Content = row;
      Dispatcher.BeginInvoke(new Action(() => { try { field.Focus(); } catch (Exception) { } }), System.Windows.Threading.DispatcherPriority.Input);
    }
    box.SelectionChanged += (s, e) => {
      try { if (box.SelectedItem is ComboBoxItem i) { choice = (string)i.Tag; error = null; NameField(); save.IsEnabled = !saving && !Unchanged; } }
      catch (Exception ex) { Log.Error(ex, "Blastlog collection picker choice"); }
    };
    NameField();
    form.Children.Add(nameHost);
    form.Children.Add(save);
    form.Children.Add(Kit.Button("Cancel", () => { open = false; error = null; Render(); }, "Blastlog.Ghost").Also(b => b.Margin = new Thickness(0, 0, 0, 6)));
    var stack = new StackPanel(); stack.Children.Add(form);
    if (mine == null && loadingMine) stack.Children.Add(T("Loading your collections…", "Blastlog.Small"));
    if (error != null) stack.Children.Add(CollectionUi.Error(error));
    return stack;
  }

  async Task Save() {
    if (saving || Unchanged) return;
    saving = true; error = null; Render();
    try {
      ReviewWrite r;
      if (choice == New) r = await plugin.CollectionSend(HttpMethod.Post, "collections", new JObject { ["title"] = name.Trim(), ["entryIds"] = new JArray(EntryId) }, EntryId);
      else r = await plugin.CollectionSend(HttpMethod.Put, $"entries/{Uri.EscapeDataString(EntryId)}/collection",
        new JObject { ["collectionId"] = choice.Length > 0 ? (JToken)choice : JValue.CreateNull() }, EntryId);
      if (!r.Ok) { error = r.Words; return; }
      // The new grouping until the game page's own refresh brings it.
      collection = choice == New ? new JObject { ["id"] = r.Value.S("id"), ["title"] = r.Value.S("title") ?? name.Trim() }
        : r.Value["collection"] as JObject;
      open = false; mine = null;
    } finally { saving = false; Render(); }
  }
}
}
