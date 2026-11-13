// SPDX-License-Identifier: GPL-3.0-or-later
using Android.App;
using Android.Content;
using Android.OS;
using Android.Text;
using Android.Views;
using Android.Widget;
using Chummer.Portable;
using System.Globalization;
using System.Text.Json;

namespace Chummer.AndroidApp;

public partial class WorkbenchActivity : Activity
{
    private TextView status = null!;
    private ScrollView? mainScroll;
    private CharacterDocument? character;
    private string? currentFile;
    private InitiativeTracker initiative = new();
    private CreationRules? creationRules;
    private bool fileOperationInProgress;
    private readonly List<(View View, string Tab)> menuItems = [];
    private readonly Dictionary<string, Button> navigation = [];
    private string selectedTab = "Characters";
    private global::Android.Webkit.WebView? printView;
    private string Storage => FilesDir!.AbsolutePath;
    private string RosterDirectory => Path.Combine(Storage, "characters");
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    protected override void OnCreate(Bundle? state)
    {
        base.OnCreate(state);
        Directory.CreateDirectory(RosterDirectory);
        var layout = new LinearLayout(this) { Orientation = Orientation.Vertical };
        layout.SetPadding(24, 24, 24, 24);
        var shell = new LinearLayout(this) { Orientation = Orientation.Vertical };
        var tabs = new LinearLayout(this) { Orientation = Orientation.Horizontal };
        foreach (var name in new[] { "Characters", "Create", "Play", "Library" })
        {
            var tab = new Button(this) { Text = name, TextSize = 12 };
            tab.Click += (_, _) => Guard(() => SetTab(name)); navigation[name] = tab;
            tabs.AddView(tab, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1));
        }
        shell.AddView(tabs);
        var scroll = new ScrollView(this); mainScroll=scroll; scroll.AddView(layout);
        shell.AddView(scroll, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, 0, 1));
        SetContentView(shell);
        layout.AddView(new TextView(this) { Text = "Offline development preview. Create core metahuman drafts, assign talents and powers, buy supported equipment, and use play tools. Creation finalization and full calculations for imported characters are still being ported." });
        MenuButton(layout, "Create a character draft", CreateDraft, "Create");
        MenuButton(layout, "Import character", Import, "Characters");
        MenuButton(layout, "Import compressed character (.chum5lz)", () => Import(true), "Characters");
        MenuButton(layout, "Open saved character", OpenRoster, "Characters");
        MenuButton(layout, "Edit profile and background", EditProfile, "Characters");
        MenuButton(layout, "Contacts", EditContacts, "Characters");
        MenuButton(layout, "Allocate draft attributes", EditAttributes, "Create");
        MenuButton(layout, "Allocate draft skills", EditSkills, "Create");
        MenuButton(layout, "Qualities", EditQualities, "Create");
        MenuButton(layout, "Skill specializations", EditSpecializations, "Create");
        MenuButton(layout, "Allocate draft skill groups", EditGroups, "Create");
        MenuButton(layout, "Assign priority bonus skills", EditTalentSkills, "Create");
        MenuButton(layout, "Magic and resonance", EditMagic, "Create");
        MenuButton(layout, "Adept powers", EditPowers, "Create");
        MenuButton(layout, "Purchase / equip draft equipment", EditEquipment, "Create");
        MenuButton(layout, "Cyberware / bioware implants", EditAugmentations, "Create");
        MenuButton(layout, "Gear / ammunition inventory", EditGear, "Create");
        MenuButton(layout, "Weapon accessories", EditAccessories, "Create");
        MenuButton(layout, "Draft knowledge and languages", EditKnowledge, "Create");
        MenuButton(layout, "Add Karma / Nuyen transaction", AddTransaction, "Play");
        MenuButton(layout, "Track damage", EditDamage, "Play");
        MenuButton(layout, "Load / fire / unload weapons", ManageAmmunition, "Play");
        MenuButton(layout, "View character and inventory", ShowCharacter, "Characters");
        MenuButton(layout, "View career history", ShowHistory, "Play");
        MenuButton(layout, "Export character copy", Export, "Characters");
        MenuButton(layout, "Export compressed character copy", () => Export(true), "Characters");
        MenuButton(layout, "Print / save summary as PDF", PrintSummary, "Characters");
        MenuButton(layout, "Restore previous saved version", RestorePrevious, "Characters");
        MenuButton(layout, "Browse rules catalogs", Catalogs, "Library");
        MenuButton(layout, "About, source and license", About, "Library");
        MenuButton(layout, "Roll dice", Dice, "Play");
        MenuButton(layout, "Roll character skill", RollCharacterSkill, "Play");
        MenuButton(layout, "Initiative tracker", Initiative, "Play");
        status = new TextView(this); layout.AddView(status);
        try
        {
            var legacy = Path.Combine(Storage, "character.chum5");
            if (File.Exists(legacy))
            {
                var migrated = Path.Combine(RosterDirectory, "legacy.chum5");
                if (!File.Exists(migrated)) File.Copy(legacy, migrated);
            }
            var last = GetPreferences(FileCreationMode.Private)!.GetString("last", null);
            var path = last is not null && Path.GetFileName(last) == last ? Path.Combine(RosterDirectory, last) : null;
            if (path is not null && File.Exists(path)) Open(path);
            else if (File.Exists(Path.Combine(RosterDirectory, "legacy.chum5"))) Open(Path.Combine(RosterDirectory, "legacy.chum5"));
            var tracker = Path.Combine(Storage, "initiative.json");
            if (File.Exists(tracker))
            {
                var saved = JsonSerializer.Deserialize(File.ReadAllText(tracker), AppJsonContext.Default.InitiativeState);
                if (saved is not null) initiative = InitiativeTracker.Restore(saved.Pass, saved.Entries);
            }
        }
        catch (Exception e) { status.Text = $"Unable to restore saved state: {e.Message}"; }
        try { SetTab(state?.GetString("tab") ?? "Characters"); }
        catch (Exception e) { status.Text = $"Unable to display character summary: {e.Message}. Use Open saved character or export the file to recover it."; }
    }

    protected override void OnSaveInstanceState(Bundle state)
    {
        state.PutString("tab", selectedTab); base.OnSaveInstanceState(state);
    }
    private void SetTab(string name)
    {
        selectedTab = navigation.ContainsKey(name) ? name : "Characters";
        foreach (var (view, tab) in menuItems) view.Visibility = tab == selectedTab ? ViewStates.Visible : ViewStates.Gone;
        foreach (var (tab, button) in navigation) button.Enabled = tab != selectedTab;
        if(mainScroll is {} scroll)scroll.Post(()=>scroll.ScrollTo(0,0));
        if (character is not null) ShowCharacter();
        else status.Text = selectedTab switch
        {
            "Create" => "Create a draft to begin allocating character points.",
            "Play" => "Dice and initiative work without a character. Open a character for damage and career records.",
            "Library" => "Browse and search the bundled rules catalogs without an internet connection.",
            _ => "Import a character or use Create to start a draft. Saved characters remain on this device."
        };
    }

    private void Guard(Action action)
    {
        try
        {
            if (fileOperationInProgress) throw new InvalidOperationException("A character file is being processed. Please wait.");
            action();
        }
        catch (Exception e)
        {
            status.Text = e.Message;
            new AlertDialog.Builder(this).SetMessage(e.Message)!.SetPositiveButton("OK", (_, _) => { })!.Show();
        }
    }
    private Button Button(LinearLayout layout, string label, Action action)
    {
        var button = new Button(this) { Text = label };
        button.Click += (_, _) => Guard(action); layout.AddView(button);
        return button;
    }
    private void MenuButton(LinearLayout layout, string label, Action action, string tab) => menuItems.Add((Button(layout, label, action), tab));
    private CharacterDocument RequireCharacter() => character ?? throw new InvalidOperationException("Open or import a character first.");
    private void Open(string path)
    {
        using var input = File.OpenRead(path);
        var loaded = CharacterDocument.Load(input);
        SelectCharacter(path, loaded);
    }
    private void SelectCharacter(string path, CharacterDocument loaded)
    {
        character = loaded; currentFile = path;
        GetPreferences(FileCreationMode.Private)!.Edit()!.PutString("last", Path.GetFileName(path))!.Apply();
        ShowCharacter();
    }
    private void Commit(Action<CharacterDocument> edit)
    {
        var updated = RequireCharacter().Clone(); edit(updated);
        if (currentFile is null) throw new InvalidOperationException("No local file selected.");
        var temporary = currentFile + ".tmp";
        using (var output = File.Create(temporary)) updated.Save(output);
        File.Copy(currentFile, currentFile + ".bak", overwrite: true);
        File.Move(temporary, currentFile, overwrite: true);
        character = updated; ShowCharacter();
    }
    private void Import() => Import(false);
    private void Import(bool compressed) => StartActivityForResult(new Intent(Intent.ActionOpenDocument)
        .AddCategory(Intent.CategoryOpenable)!.SetType("*/*"), compressed ? 3 : 1);
    private void Export() => Export(false);
    private void Export(bool compressed)
    {
        RequireCharacter();
        StartActivityForResult(new Intent(Intent.ActionCreateDocument)
            .AddCategory(Intent.CategoryOpenable)!.SetType(compressed ? "application/octet-stream" : "application/xml")
            .PutExtra(Intent.ExtraTitle, compressed ? "character.chum5lz" : "character.chum5"), compressed ? 4 : 2);
    }
    private void OpenRoster()
    {
        var files = Directory.GetFiles(RosterDirectory, "*.chum5");
        if (files.Length == 0) { status.Text = "No saved characters. Import a .chum5 file to start."; return; }
        var labels = files.Select(p =>
        {
            try { using var input = File.OpenRead(p); var c = CharacterDocument.Load(input); return $"{c.Alias} — {c.Name}"; }
            catch { return $"Unreadable file: {Path.GetFileName(p)}"; }
        }).ToArray();
        new AlertDialog.Builder(this)!.SetTitle("Saved characters")!.SetItems(labels, (_, args) => Guard(() => Open(files[args.Which])))!.Show();
    }
    private CreationRules Creation()
    {
        if (creationRules is not null) return creationRules;
        using var priorities = Assets!.Open("data/priorities.xml");
        using var metatypes = Assets.Open("data/metatypes.xml");
        using var skills = Assets.Open("data/skills.xml");
        using var qualities = Assets.Open("data/qualities.xml");
        using var gear = Assets.Open("data/gear.xml");
        return creationRules = new CreationRules(priorities, metatypes, skills, qualities, gear);
    }
    private Spinner Select(LinearLayout layout, string label, string[] choices, int selected = 0)
    {
        layout.AddView(new TextView(this) { Text = label });
        var spinner = new Spinner(this) { Adapter = new ArrayAdapter<string>(this, global::Android.Resource.Layout.SimpleSpinnerDropDownItem, choices) };
        spinner.SetSelection(selected); layout.AddView(spinner); return spinner;
    }
    private void CreateDraft()
    {
        var rules = Creation(); var form = Form();
        form.Layout.AddView(new TextView(this) { Text = "Create an unfinished metahuman under standard core priority rules. Racial and talent traits are included. Magical and technomancer creation remains in development; character finalization is pending." });
        var name = Input(form.Layout, "Name"); var alias = Input(form.Layout, "Alias");
        var metatypeNames = CreationRules.CoreMetatypes.ToArray();
        var metatype = Select(form.Layout, "Metatype", metatypeNames);
        var method = Select(form.Layout, "Build method", ["Priority", "SumtoTen"]);
        string[] letters = ["A", "B", "C", "D", "E"];
        var heritage = Select(form.Layout, "Heritage priority", letters, 3);
        var attributes = Select(form.Layout, "Attribute priority", letters, 0);
        var skills = Select(form.Layout, "Skill priority", letters, 1);
        var resources = Select(form.Layout, "Resource priority", letters, 2);
        var talentPriority = Select(form.Layout, "Talent priority", letters, 4);
        var talentOptions = rules.Talents("E").ToArray();
        var talent = Select(form.Layout, "Talent", talentOptions.Select(t => t.Name).ToArray());
        string[] aspects = ["Sorcery", "Conjuring", "Enchanting"];
        var aspect = Select(form.Layout, "Aspected magician group (when applicable)", aspects);
        var budget = new TextView(this); form.Layout.AddView(budget);
        PriorityChoice Choice() => new(method.SelectedItemPosition == 0 ? "Priority" : "SumtoTen",
            letters[heritage.SelectedItemPosition], letters[attributes.SelectedItemPosition],
            letters[skills.SelectedItemPosition], letters[resources.SelectedItemPosition], letters[talentPriority.SelectedItemPosition],
            talentOptions[Math.Max(0, talent.SelectedItemPosition)].Path, aspects[aspect.SelectedItemPosition]);
        void Refresh()
        {
            try
            {
                var b = rules.Budget(Choice(), metatypeNames[metatype.SelectedItemPosition]);
                var t = talentOptions[Math.Max(0, talent.SelectedItemPosition)];
                budget.Text = $"Attribute points: {b.Attributes}\nSpecial points: {b.Special}\nSkill points: {b.Skills}\nSkill group points: {b.SkillGroups}\nResources: {b.Nuyen:N0} Nuyen\n" +
                    $"Magic: {t.Magic}; Resonance: {t.Resonance}\nSpells: {t.Spells}; Complex forms: {t.ComplexForms}\nBonus skills: {t.FreeSkillCount} at {t.FreeSkillRating}; bonus groups: {t.FreeGroupCount} at {t.FreeGroupRating}";
            }
            catch (ArgumentException e) { budget.Text = e.Message; }
        }
        foreach (var spinner in new[] { metatype, method, heritage, attributes, skills, resources }) spinner.ItemSelected += (_, _) => Refresh();
        talentPriority.ItemSelected += (_, _) =>
        {
            talentOptions = rules.Talents(letters[talentPriority.SelectedItemPosition]).ToArray();
            talent.Adapter = new ArrayAdapter<string>(this, global::Android.Resource.Layout.SimpleSpinnerDropDownItem, talentOptions.Select(t => t.Name).ToArray());
            talent.SetSelection(0); Refresh();
        };
        talent.ItemSelected += (_, _) => Refresh(); aspect.ItemSelected += (_, _) => Refresh();
        Refresh();
        EditDialog("New character draft", form.Scroll, () =>
        {
            var draft = rules.CreateDraft(name.Text ?? "", alias.Text ?? "", Choice(), metatypeNames[metatype.SelectedItemPosition]);
            if (draft.Field("technomancer") == "True") Magic().SetTradition(draft, "Default");
            var file = Path.Combine(RosterDirectory, Guid.NewGuid().ToString("N") + ".chum5");
            using (var output = File.Create(file + ".tmp")) draft.Save(output);
            File.Move(file + ".tmp", file); Open(file);
        });
    }
    private void EditAttributes()
    {
        var c = RequireCharacter(); var attributes = c.DraftAttributes; var form = Form();
        form.Layout.AddView(new TextView(this) { Text = $"Enter final natural ratings. Attribute points remaining: {c.AttributePointsRemaining}; special points remaining: {c.SpecialPointsRemaining}." });
        var fields = attributes.ToDictionary(a => a.Name, a => Input(form.Layout, $"{a.Name} ({a.Minimum}–{a.Maximum})", a.Rating.ToString()));
        foreach (var input in fields.Values) input.InputType = InputTypes.ClassNumber;
        EditDialog("Draft attributes", form.Scroll, () =>
        {
            var ratings = new Dictionary<string, int>();
            foreach (var (key, input) in fields)
            {
                if (!int.TryParse(input.Text, out var rating)) throw new ArgumentException($"Enter a whole number for {key}.");
                ratings[key] = rating;
            }
            Commit(updated => updated.SetDraftAttributes(ratings));
        });
    }
    private void EditSkills()
    {
        var c = RequireCharacter();
        if (!c.IsAndroidDraft) throw new InvalidOperationException("Skill allocation currently supports core priority drafts created in this app.");
        var skills = Creation().Skills(c).ToArray();
        new AlertDialog.Builder(this).SetTitle($"Active skills — {c.SkillPointsRemaining} points left")!
            .SetItems(skills.Select(s => $"{s.Name}: {s.Rating}").ToArray(), (_, args) => Guard(() =>
            {
                var skill = skills[args.Which]; var form = Form();
                form.Layout.AddView(new TextView(this) { Text = $"Linked attribute: {skill.Attribute}. Group: {skill.Group}. Reset the group to zero before assigning individual points. Use Skill specializations to add a specialization after training this skill." });
                var value = Input(form.Layout, "Skill rating (0–6)", skill.Rating.ToString()); value.InputType = InputTypes.ClassNumber;
                EditDialog(skill.Name, form.Scroll, () =>
                {
                    if (!int.TryParse(value.Text, out var rating)) throw new ArgumentException("Enter a whole-number rating.");
                    Commit(updated => updated.SetDraftSkill(skill.Id, rating));
                });
            }))!.Show();
    }
    private void EditTalentSkills()
    {
        var c = RequireCharacter(); var count = c.TalentSkillChoicesRequired;
        if (count == 0) throw new InvalidOperationException("This talent grants no individual bonus skill choices. Aspected magician group grants are applied automatically.");
        var candidates = Creation().TalentSkillCandidates(c).ToArray();
        var selected = candidates.Select(s => c.TalentSkillChoices.Contains(s.Name)).ToArray();
        var dialog = new AlertDialog.Builder(this).SetTitle($"Choose {count} skills at rating {c.TalentFreeSkillRating}")!
            .SetMultiChoiceItems(candidates.Select(s => s.Name).ToArray(), selected, (_, args) => selected[args.Which] = args.IsChecked)!
            .SetNegativeButton("Cancel", (_, _) => { })!.SetPositiveButton("Save", (_, _) => { })!.Create()!;
        dialog.Show();
        dialog.GetButton((int)DialogButtonType.Positive)!.Click += (_, _) =>
        {
            try
            {
                var names = candidates.Where((_, index) => selected[index]).Select(s => s.Name).ToArray();
                Commit(updated => Creation().AssignTalentSkills(updated, names)); dialog.Dismiss();
            }
            catch (Exception e) { new AlertDialog.Builder(this).SetMessage(e.Message)!.SetPositiveButton("OK", (_, _) => { })!.Show(); }
        };
    }
    private void EditGroups()
    {
        var c = RequireCharacter(); var groups = c.DraftGroups.ToArray();
        new AlertDialog.Builder(this).SetTitle($"Skill groups — {c.GroupPointsRemaining} points left")!
            .SetItems(groups.Select(g => $"{g.Name}: {g.Rating}").ToArray(), (_, args) => Guard(() =>
            {
                var group = groups[args.Which]; var form = Form();
                form.Layout.AddView(new TextView(this) { Text = "A group rating applies to all its member skills. Group and individual points cannot be mixed in this creation editor." });
                var value = Input(form.Layout, "Group rating (0–6)", group.Rating.ToString()); value.InputType = InputTypes.ClassNumber;
                EditDialog(group.Name, form.Scroll, () =>
                {
                    if (!int.TryParse(value.Text, out var rating)) throw new ArgumentException("Enter a whole-number rating.");
                    Commit(updated => updated.SetDraftGroup(group.Name, rating));
                });
            }))!.Show();
    }
    private void EditKnowledge()
    {
        var c = RequireCharacter(); var knowledge = c.DraftKnowledgeSkills.ToArray();
        var labels = new[] { "Add a knowledge skill or language" }.Concat(knowledge.Select(k => $"{k.Name} ({k.Type}): {(k.Native ? "Native" : k.Rating.ToString())}")).ToArray();
        new AlertDialog.Builder(this).SetTitle($"Knowledge — {c.KnowledgePointsRemaining} points left")!
            .SetItems(labels, (_, args) => Guard(() =>
            {
                var existing = args.Which == 0 ? null : knowledge[args.Which - 1];
                var form = Form(); var name = Input(form.Layout, "Name", existing?.Name ?? "");
                string[] types = ["Academic", "Interest", "Language", "Professional", "Street"];
                var type = Select(form.Layout, "Category", types, Math.Max(0, Array.IndexOf(types, existing?.Type ?? "Academic")));
                var rating = Input(form.Layout, "Rating (1–6, ignored for native language)", (existing?.Rating ?? 1).ToString()); rating.InputType = InputTypes.ClassNumber;
                var native = new CheckBox(this) { Text = "Native language (one free under standard rules)", Checked = existing?.Native ?? false }; form.Layout.AddView(native);
                AlertDialog? editor = null;
                if (existing is not null)
                    Button(form.Layout, "Remove this skill", () => new AlertDialog.Builder(this).SetMessage("Remove this knowledge skill or language?")!
                        .SetNegativeButton("Cancel", (_, _) => { })!.SetPositiveButton("Remove", (_, _) => Guard(() =>
                        { Commit(updated => updated.RemoveDraftKnowledge(existing.Id)); editor?.Dismiss(); }))!.Show());
                editor = EditDialog("Knowledge and language", form.Scroll, () =>
                {
                    if (!int.TryParse(rating.Text, out var value)) throw new ArgumentException("Enter a whole-number rating.");
                    Commit(updated => updated.SetDraftKnowledge(existing?.Id, name.Text ?? "", types[type.SelectedItemPosition], value, native.Checked));
                });
            }))!.Show();
    }
    private void EditContacts()
    {
        var c = RequireCharacter(); var contacts = c.Contacts.ToArray();
        if (!c.IsAndroidDraft)
        {
            status.Text = "Contact editing for imported characters is pending.\n\n" + string.Join("\n\n", contacts.Select(r => $"{r.Name}\n{r.Role} — {r.Location}\nConnection {r.Connection}; loyalty {r.Loyalty}"));
            return;
        }
        var labels = new[] { "Add contact" }.Concat(contacts.Select(r => $"{r.Name} — C{r.Connection} / L{r.Loyalty}")).ToArray();
        new AlertDialog.Builder(this).SetTitle($"Contacts — {c.DraftContactPointsRemaining} points left")!
            .SetItems(labels, (_, args) => Guard(() =>
            {
                var existing = args.Which == 0 ? null : contacts[args.Which - 1];
                var form = Form(); var name = Input(form.Layout, "Name", existing?.Name ?? "");
                var role = Input(form.Layout, "Role", existing?.Role ?? ""); var location = Input(form.Layout, "Location", existing?.Location ?? "");
                var connection = Input(form.Layout, "Connection (1–6)", (existing?.Connection ?? 1).ToString());
                var loyalty = Input(form.Layout, "Loyalty (1–6)", (existing?.Loyalty ?? 1).ToString());
                connection.InputType = loyalty.InputType = InputTypes.ClassNumber;
                AlertDialog? editor = null;
                if (existing is not null)
                    Button(form.Layout, "Remove contact", () => new AlertDialog.Builder(this).SetMessage("Remove this contact?")!
                        .SetNegativeButton("Cancel", (_, _) => { })!.SetPositiveButton("Remove", (_, _) => Guard(() =>
                        { Commit(updated => updated.RemoveDraftContact(existing.Id)); editor?.Dismiss(); }))!.Show());
                editor = EditDialog("Draft contact", form.Scroll, () =>
                {
                    if (!int.TryParse(connection.Text, out var conn) || !int.TryParse(loyalty.Text, out var loy)) throw new ArgumentException("Enter whole-number connection and loyalty ratings.");
                    Commit(updated => updated.SetDraftContact(existing?.Id, name.Text ?? "", role.Text ?? "", location.Text ?? "", conn, loy));
                });
            }))!.Show();
    }
    private (LinearLayout Layout, ScrollView Scroll) Form()
    {
        var layout = new LinearLayout(this) { Orientation = Orientation.Vertical };
        layout.SetPadding(24, 8, 24, 8); var scroll = new ScrollView(this); scroll.AddView(layout);
        return (layout, scroll);
    }
    private EditText Input(LinearLayout layout, string label, string value = "", bool multiline = false)
    {
        layout.AddView(new TextView(this) { Text = label });
        var field = new EditText(this) { Text = value, InputType = multiline ? InputTypes.ClassText | InputTypes.TextFlagMultiLine : InputTypes.ClassText };
        field.SetSingleLine(!multiline); layout.AddView(field); return field;
    }
    // Keep dialogs open if validation or saving fails so user input is retained.
    private AlertDialog EditDialog(string title, ScrollView view, Action save)
    {
        var dialog = new AlertDialog.Builder(this)!.SetTitle(title)!.SetView(view)!
            .SetNegativeButton("Cancel", (_, _) => { })!.SetPositiveButton("Save", (_, _) => { })!.Create()!;
        dialog.Show();
        dialog.GetButton((int)DialogButtonType.Positive)!.Click += (_, _) =>
        {
            try { save(); dialog.Dismiss(); }
            catch (Exception e) { new AlertDialog.Builder(this)!.SetMessage(e.Message)!.SetPositiveButton("OK", (_, _) => { })!.Show(); }
        };
        return dialog;
    }
    private void EditProfile()
    {
        var c = RequireCharacter(); var form = Form();
        var definitions = new (string Key, string Label, bool Multi)[] {
            ("name", "Name", false), ("alias", "Alias", false), ("playername", "Player", false),
            ("age", "Age", false), ("gender", "Gender", false), ("height", "Height", false),
            ("weight", "Weight", false), ("eyes", "Eyes", false), ("hair", "Hair", false),
            ("skin", "Skin", false), ("concept", "Concept", true),
            ("description", "Description", true), ("background", "Background", true) };
        var fields = definitions.ToDictionary(d => d.Key, d => Input(form.Layout, d.Label, c.Field(d.Key), d.Multi));
        EditDialog("Profile and background", form.Scroll, () => Commit(updated => updated.SetProfile(
            fields.ToDictionary(pair => pair.Key, pair => pair.Value.Text ?? ""))));
    }
    private void AddTransaction()
    {
        var c = RequireCharacter();
        if (!c.IsCareer) throw new InvalidOperationException("This character is still in creation mode. Career transactions are unavailable.");
        var form = Form();
        form.Layout.AddView(new TextView(this) { Text = $"Karma: {c.Karma}\nNuyen: {c.Nuyen}\nUse a negative amount to spend. This records a manual adjustment; it does not purchase or upgrade items." });
        var currency = new Spinner(this);
        currency.Adapter = new ArrayAdapter<string>(this, global::Android.Resource.Layout.SimpleSpinnerDropDownItem, new[] { "Karma", "Nuyen" });
        form.Layout.AddView(currency);
        var amount = Input(form.Layout, "Amount", "0");
        amount.InputType = InputTypes.ClassNumber | InputTypes.NumberFlagDecimal | InputTypes.NumberFlagSigned;
        var reason = Input(form.Layout, "Reason");
        EditDialog("Career transaction", form.Scroll, () =>
        {
            if (!decimal.TryParse(amount.Text, NumberStyles.Number, CultureInfo.CurrentCulture, out var value))
                throw new ArgumentException("Enter a valid amount.");
            Commit(updated => updated.AddTransaction(currency.SelectedItemPosition == 0 ? "Karma" : "Nuyen", value, reason.Text ?? "", DateTime.Now));
        });
    }
    private void EditDamage()
    {
        var c = RequireCharacter(); var form = Form();
        form.Layout.AddView(new TextView(this) { Text = "Record filled damage boxes. Automatic wound modifiers, overflow and damage conversion are still pending." });
        var physical = Input(form.Layout, "Physical damage", c.PhysicalDamage.ToString());
        var stun = Input(form.Layout, "Stun damage", c.StunDamage.ToString());
        physical.InputType = stun.InputType = InputTypes.ClassNumber;
        EditDialog("Damage tracking", form.Scroll, () =>
        {
            if (!int.TryParse(physical.Text, out var p) || !int.TryParse(stun.Text, out var s)) throw new ArgumentException("Enter whole numbers.");
            Commit(updated => updated.SetDamage(p, s));
        });
    }
    private void ShowCharacter()
    {
        var c = RequireCharacter(); var lines = new List<string>();
        try { lines.Add(c.Summary()); }
        catch { lines.Add($"{c.Name}\nAlias: {c.Alias}\nMetatype: {c.Metatype}\nSome stored values are not recognized by this preview. The character file can still be exported."); }
        if (c.IsAndroidDraft)
        {
            try
            {
            var stats = c.DraftStats();
            lines.Add(EffectsSummary(c));
            lines.Add($"\nSelected qualities: {c.PositiveQualityKarma}/25 positive; {c.NegativeQualityKarma}/25 negative Karma\n" + string.Join("\n", c.SelectedQualities.Select(q => $"{q.Name} {q.Extra} ({q.Karma} Karma)")));
            lines.Add("\nSpecializations (+2 only when applicable)\n" + string.Join("\n", c.DraftSpecializations.Select(s => $"{s.Skill}: {s.Name}; applicable pool {c.DraftSpecializedPool(Creation().Skills(c).Single(k => k.Id == s.SkillId), s.Name)}")));
            lines.Add($"\nDraft — creation is not finalized\nAttributes: {string.Join(", ", c.DraftAttributes.Select(a => $"{a.Name} {a.Rating}"))}\n" +
                $"Points remaining: {c.AttributePointsRemaining} attributes; {c.SpecialPointsRemaining} special; {c.SkillPointsRemaining} skills; {c.GroupPointsRemaining} groups; {c.KnowledgePointsRemaining} knowledge\n" +
                $"Unaugmented limits: physical {stats.PhysicalLimit}, mental {stats.MentalLimit}, social {stats.SocialLimit}\n" +
                $"Initiative: {stats.Initiative} + 1D6\nCondition monitors: {stats.PhysicalBoxes} physical / {stats.StunBoxes} stun\n" +
                $"Knowledge-point budget: {stats.KnowledgePoints}; contact-point budget: {stats.ContactPoints}");
            var trained = Creation().Skills(c).Where(s => s.Rating > 0);
            lines.Add("\nActive skills\n" + string.Join("\n", trained.Select(s => $"{s.Name}: rating {s.Rating}; pool {c.DraftSkillPool(s)}")));
            lines.Add("\nKnowledge and languages\n" + string.Join("\n", c.DraftKnowledgeSkills.Select(k => $"{k.Name}: {(k.Native ? "Native" : k.Rating.ToString())}")));
            lines.Add($"\nRacial traits: {string.Join(", ", c.DraftRacialQualities)}\nNatural armor: {c.DraftRacialBonus("Armor")}; reach bonus: {c.DraftRacialBonus("Reach")}\nLifestyle cost adjustment: {c.DraftRacialBonus("LifestyleCost")}%");
            if (c.Field("magenabled") == "True" || c.Field("resenabled") == "True")
                lines.Add($"\nTradition / stream: {c.TraditionName}\nPriority bonus skills chosen: {c.TalentSkillChoices.Count}/{c.TalentSkillChoicesRequired}\n" +
                    string.Join("\n", c.LearnedSpells.Select(s => $"{s.Name} {s.Extra} — {(s.Preparation ? "Preparation" : "Spell / ritual")}; Drain {s.Difficulty}")) +
                    "\n" + string.Join("\n", c.LearnedForms.Select(f => $"{f.Name} {f.Extra} — Fading {f.Difficulty}")));
            }
            catch { lines.Add("This preview cannot calculate all values in this file. Its saved data is retained; profile editing and export remain available."); }
        }
        foreach (var (collection, item, title) in new[] { ("weapons", "weapon", "Weapons"), ("armors", "armor", "Armor"),
            ("gears", "gear", "Gear"), ("cyberwares", "cyberware", "Augmentations"), ("vehicles", "vehicle", "Vehicles"),
            ("spells", "spell", "Spells"), ("qualities", "quality", "Qualities") })
        {
            var names = c.OwnedItems(collection, item);
            if (names.Count > 0) lines.Add($"\n{title}\n{string.Join("\n", names)}");
        }
        status.Text = string.Join("\n", lines);
    }
    private void ShowHistory()
    {
        var records = RequireCharacter().Transactions.OrderByDescending(t => t.Date).ToArray();
        status.Text = records.Length == 0 ? "No career transactions." : string.Join("\n\n", records.Select(t =>
            $"{t.Date}\n{t.Currency}: {t.Amount.ToString(Invariant)}\n{t.Reason}"));
    }
    private void PrintSummary()
    {
        var summary = CharacterSheet.SummaryHtml(RequireCharacter().Clone());
        if (printView is not null) throw new InvalidOperationException("A character summary is already being prepared for printing.");
        var manager = GetSystemService(PrintService) as global::Android.Print.PrintManager
            ?? throw new InvalidOperationException("Android printing is not available on this device.");
        var view = new global::Android.Webkit.WebView(this); printView = view;
        view.Settings.JavaScriptEnabled = false;
        view.Settings.BlockNetworkLoads = true;
        view.Settings.AllowFileAccess = false;
        view.Settings.AllowContentAccess = false;
        void Finished() { view.Destroy(); if (printView == view) printView = null; }
        view.SetWebViewClient(new SummaryPageClient(() =>
        {
            try
            {
                var adapter = view.CreatePrintDocumentAdapter("Chummer character summary");
                manager.Print("Chummer character summary", new SummaryPrintAdapter(adapter, Finished), new global::Android.Print.PrintAttributes.Builder().Build());
            }
            catch (Exception e) { Finished(); if (!IsDestroyed) status.Text = $"Unable to print summary: {e.Message}"; }
        }));
        view.LoadDataWithBaseURL(null, summary, "text/html", "UTF-8", null);
    }
    private void RestorePrevious()
    {
        RequireCharacter();
        if (currentFile is null || !File.Exists(currentFile + ".bak")) throw new InvalidOperationException("No previous version available.");
        new AlertDialog.Builder(this)!.SetTitle("Restore previous saved version?")!
            .SetMessage("This swaps the current character with its previous saved version. Export a copy first if you want to keep both.")!
            .SetNegativeButton("Cancel", (_, _) => { })!.SetPositiveButton("Restore", (_, _) => Guard(() =>
            {
                CharacterDocument previous;
                using (var input = File.OpenRead(currentFile + ".bak")) previous = CharacterDocument.Load(input);
                var temporary = currentFile + ".tmp";
                using (var output = File.Create(temporary)) previous.Save(output);
                File.Copy(currentFile, currentFile + ".bak", overwrite: true);
                File.Move(temporary, currentFile, overwrite: true); Open(currentFile);
            }))!.Show();
    }
    private void About()
    {
        var form = Form();
        form.Layout.AddView(new TextView(this) { Text = "Chummer Offline Preview 0.8\nBased on Chummer5a, GPL-3.0-or-later.\nhttps://github.com/chummer5a/chummer5a\n\nThis development preview is not a feature-complete replacement. New drafts support core metahumans and mundane, magical or technomancer talents under standard Priority or Sum-to-Ten rules. Core traditions, spell/preparation and complex-form learning are available. Core adept powers and supported armor/weapon purchases are available. Supported gear/ammo inventory, basic weapon accessories and core implants with supported Essence loss for priority drafts are available. Full magic/Matrix play, advanced ammunition/attack workflows, advanced accessories, cyberlimbs and burnout are pending. Character creation cannot yet be finalized. Imported-character effects and advancement are not recalculated.\n\nAll character storage and play tools work on the device. No account or internet connection is required. Export backups through the Characters section; Android clears app-private storage when the app is uninstalled.\n\nLicense\n" });
        using var license = Assets!.Open("LICENSE.txt"); using var reader = new StreamReader(license);
        form.Layout.AddView(new TextView(this) { Text = reader.ReadToEnd() });
        new AlertDialog.Builder(this).SetTitle("About this preview")!.SetView(form.Scroll)!.SetPositiveButton("Close", (_, _) => { })!.Show();
    }
    private void Catalogs()
    {
        var files = Assets!.List("data")!.Where(f => f.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)).OrderBy(f => f).ToArray();
        new AlertDialog.Builder(this)!.SetTitle("Offline catalogs")!.SetItems(files.Select(Path.GetFileNameWithoutExtension).ToArray()!,
            (_, args) => Guard(() => SearchCatalog(files[args.Which])))!.Show();
    }
    private void SearchCatalog(string file)
    {
        using var input = Assets!.Open("data/" + file);
        var entries = RuleCatalog.LoadAll(input); var form = Form();
        var search = Input(form.Layout, "Search name"); var results = new TextView(this); form.Layout.AddView(results);
        void Refresh()
        {
            var matches = entries.Where(e => e.Name.Contains(search.Text ?? "", StringComparison.OrdinalIgnoreCase)).ToArray();
            results.Text = $"{matches.Length} matches (showing up to 100)\n\n" + string.Join("\n\n", matches.Take(100).Select(e => $"{e.Name}\n{e.Details}"));
        }
        search.TextChanged += (_, _) => Refresh(); Refresh();
        new AlertDialog.Builder(this)!.SetTitle(Path.GetFileNameWithoutExtension(file))!.SetView(form.Scroll)!.SetPositiveButton("Close", (_, _) => { })!.Show();
    }
    private void Dice()
    {
        var form = Form(); var count = Input(form.Layout, "Dice pool", "10"); count.InputType = InputTypes.ClassNumber;
        var gremlins = Input(form.Layout, "Gremlins rating", "0"); gremlins.InputType = InputTypes.ClassNumber;
        var method = Select(form.Layout,"Roll method",new[] { "Standard", "Large", "Really Large" });
        var threshold = Input(form.Layout,"Success threshold (0 disables)","0"); threshold.InputType = InputTypes.ClassNumber;
        CheckBox Check(string label) { var c = new CheckBox(this) { Text = label }; form.Layout.AddView(c); return c; }
        var exploding = Check("Exploding sixes"); var cinematic = Check("Cinematic hits (4+)");
        var rushed = Check("Rushed job"); var variable = Check("Variable glitch threshold"); var bubble = Check("Bubble die");
        var results = new TextView(this);
        PoolRoll? lastRoll = null; RollOptions? lastOptions = null; var lastThreshold = 0;
        void Display(PoolRoll roll)
        {
            results.Text = (lastThreshold > 0 ? $"{(roll.MeetsThreshold(lastThreshold) ? "Success" : "Failure")} (threshold {lastThreshold})\n" : "") +
                $"Hits: {roll.Hits}\n{(roll.CriticalGlitch ? "Critical glitch" : roll.Glitch ? "Glitch" : "No glitch")}\nDice: {string.Join(", ", roll.Dice)}" +
                (roll.BubbleDie is null ? "" : $"\nBubble die: {roll.BubbleDie}");
        }
        Button(form.Layout, "Roll", () =>
        {
            if (!int.TryParse(count.Text, out var pool) || !int.TryParse(gremlins.Text, out var g)) throw new ArgumentException("Enter whole numbers.");
            if (!int.TryParse(threshold.Text,out var successThreshold) || successThreshold is < 0 or > 100000) throw new ArgumentException("Enter a success threshold from 0 to 100000.");
            var options = new RollOptions(pool, exploding.Checked, cinematic.Checked, rushed.Checked, g, variable.Checked, bubble.Checked, (RollMethod)method.SelectedItemPosition);
            var roll = DicePool.Roll(options);
            lastOptions = options; lastRoll = roll; lastThreshold = successThreshold; Display(roll);
        });
        Button(form.Layout,"Reroll misses", () =>
        {
            if (lastRoll is null || lastOptions is null) throw new InvalidOperationException("Roll a dice pool first.");
            var roll = DicePool.RerollMisses(lastRoll,lastOptions);
            lastRoll = roll; Display(roll);
        });
        form.Layout.AddView(new TextView(this) { Text = "Reroll misses keeps hits and uses the options from the original roll. Edge costs are handled manually." });
        form.Layout.AddView(new TextView(this) { Text = "Desktop modes: Standard counts 5–6 (4–6 for cinematic play); Large counts 3–6; Really Large totals die faces and uses the desktop mode's glitch rules." });
        form.Layout.AddView(results);
        new AlertDialog.Builder(this)!.SetTitle("Dice pool")!.SetView(form.Scroll)!.SetPositiveButton("Close", (_, _) => { })!.Show();
    }
    private void SaveInitiative()
    {
        var file = Path.Combine(Storage, "initiative.json");
        File.WriteAllText(file + ".tmp", JsonSerializer.Serialize(new InitiativeState(initiative.Pass, initiative.All.ToArray()), AppJsonContext.Default.InitiativeState));
        File.Move(file + ".tmp", file, overwrite: true);
    }
    private void Initiative()
    {
        var form = Form(); var name = Input(form.Layout, "Combatant name");
        var score = Input(form.Layout, "Initiative score (roll separately)", "10"); score.InputType = InputTypes.ClassNumber;
        var results = new TextView(this);
        void Refresh() => results.Text = $"Pass {initiative.Pass}\nTies use entry order; resolve ties at the table.\n\n" +
            string.Join("\n", initiative.Order.Select(e => $"{e.Score}: {e.Name}"));
        void Change(Action action)
        {
            var before = initiative;
            initiative = InitiativeTracker.Restore(before.Pass, before.All);
            try { action(); SaveInitiative(); Refresh(); }
            catch { initiative = before; throw; }
        }
        Button(form.Layout, "Add combatant", () => Change(() =>
        {
            if (!int.TryParse(score.Text, out var value)) throw new ArgumentException("Enter a whole-number score.");
            initiative.Add(name.Text ?? "", value);
        }));
        Button(form.Layout, "Next pass (subtract 10)", () => Change(() => initiative.NextPass()));
        Button(form.Layout, "New combat", () => new AlertDialog.Builder(this)!.SetMessage("Clear all combatants?")!
            .SetNegativeButton("Cancel", (_, _) => { })!.SetPositiveButton("Clear", (_, _) => Guard(() => Change(() => initiative.Clear())))!.Show());
        form.Layout.AddView(results); Refresh();
        new AlertDialog.Builder(this)!.SetTitle("Initiative tracker")!.SetView(form.Scroll)!.SetPositiveButton("Close", (_, _) => { })!.Show();
    }

    protected override async void OnActivityResult(int requestCode, Result resultCode, Intent? data)
    {
        base.OnActivityResult(requestCode, resultCode, data);
        if (resultCode != Result.Ok || data?.Data is null) return;
        if (requestCode is < 1 or > 4) return;
        fileOperationInProgress = true;
        status.Text = "Processing character file…";
        try
        {
            if (requestCode is 1 or 3)
            {
                using var input = ContentResolver!.OpenInputStream(data.Data) ?? throw new IOException("Cannot open character.");
                var file = Path.Combine(RosterDirectory, Guid.NewGuid().ToString("N") + ".chum5");
                var imported = await Task.Run(() =>
                {
                    var loaded = requestCode == 3 ? CharacterDocument.LoadCompressed(input) : CharacterDocument.Load(input);
                    using (var output = File.Create(file + ".tmp")) loaded.Save(output);
                    File.Move(file + ".tmp", file); return loaded;
                });
                if (!IsDestroyed) SelectCharacter(file, imported);
            }
            else
            {
                var snapshot = RequireCharacter().Clone();
                using var output = ContentResolver!.OpenOutputStream(data.Data, "wt") ?? throw new IOException("Cannot write character.");
                await Task.Run(() => { if (requestCode == 4) snapshot.SaveCompressed(output); else snapshot.Save(output); });
                if (!IsDestroyed) status.Text = "Character copy exported.";
            }
        }
        catch (Exception e) { if (!IsDestroyed) status.Text = $"Unable to process file: {e.Message}"; }
        finally { fileOperationInProgress = false; }
    }
    public sealed record InitiativeState(int Pass, Combatant[] Entries);
}
