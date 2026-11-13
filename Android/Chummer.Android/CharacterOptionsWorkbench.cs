// SPDX-License-Identifier: GPL-3.0-or-later
using Android.App;
using Android.Widget;
using Chummer.Portable;

namespace Chummer.AndroidApp;

public partial class WorkbenchActivity
{
    private QualityRules? qualityRules;
    private QualityRules Qualities()
    {
        if (qualityRules is not null) return qualityRules;
        using var data = Assets!.Open("data/qualities.xml"); return qualityRules = new QualityRules(data);
    }
    private void EditQualities()
    {
        var c = RequireCharacter(); _ = c.DraftAttributes;
        new AlertDialog.Builder(this).SetTitle($"Qualities — positive {c.PositiveQualityKarma}/25; negative {c.NegativeQualityKarma}/25 Karma")!
            .SetItems(new[] { "Add supported quality", "View / remove selected qualities" }, (_, args) => Guard(() =>
            {
                if (args.Which == 0) AddQuality();
                else
                {
                    var items = RequireCharacter().SelectedQualities.ToArray();
                    if (items.Length == 0) throw new InvalidOperationException("No qualities have been selected yet. Racial and talent qualities are managed by creation choices.");
                    new AlertDialog.Builder(this).SetTitle("Selected qualities")!.SetItems(items.Select(q => $"{q.Name} {q.Extra} ({q.Karma} Karma)").ToArray(), (_, selection) =>
                        new AlertDialog.Builder(this).SetTitle("Remove quality level?")!.SetMessage("Its Karma cost or reward will be reversed. Remove dependent choices first if needed.")!
                            .SetNegativeButton("Cancel", (_, _) => { })!.SetPositiveButton("Remove", (_, _) => Guard(() => Commit(updated => Qualities().Remove(updated, items[selection.Which].Id))))!.Show())!.Show();
                }
            }))!.Show();
    }
    private void AddQuality()
    {
        var all = Qualities().Catalog.ToArray(); var items = all; var form = Form();
        var search = Input(form.Layout, "Search qualities"); var choice = Select(form.Layout, "Quality", items.Select(q => q.Name).ToArray());
        var extra = Input(form.Layout, "Quality choice (when required)"); var details = new TextView(this); form.Layout.AddView(details);
        form.Layout.AddView(new TextView(this) { Text = "Each purchase adds one level. Numeric supported effects update automatically; situational, activated and narrative effects follow the sourcebook. More quality types, mentor spirits, attribute/skill limit choices and custom rules are being ported." });
        void Refresh()
        {
            if (items.Length == 0) { details.Text = "No matching supported qualities."; return; }
            var q = items[Math.Clamp(choice.SelectedItemPosition, 0, items.Length-1)];
            details.Text = $"{q.Category}; {(q.Karma >= 0 ? "cost" : "gain")} {Math.Abs(q.Karma)} Karma per level\n" +
                (q.Maximum == 0 ? "Repeatable with different choices." : $"Maximum levels: {q.Maximum}") + $"\nSR5 p. {q.Page}\n" +
                (q.Choice ? "Enter the required substance, allegiance, location or other choice." : "No extra choice is required.");
        }
        search.TextChanged += (_, _) =>
        {
            items = all.Where(q => q.Name.Contains(search.Text ?? "", StringComparison.OrdinalIgnoreCase)).ToArray();
            choice.Adapter = new ArrayAdapter<string>(this, global::Android.Resource.Layout.SimpleSpinnerDropDownItem, items.Select(q => q.Name).ToArray()); choice.SetSelection(0); extra.Text=""; Refresh();
        };
        choice.ItemSelected += (_, _) => { extra.Text=""; Refresh(); }; Refresh();
        EditDialog("Select a quality", form.Scroll, () =>
        {
            if (items.Length == 0) throw new ArgumentException("Choose a supported quality.");
            Commit(updated => Qualities().Add(updated, items[choice.SelectedItemPosition].Id, extra.Text ?? ""));
        });
    }
    private void EditSpecializations()
    {
        var c = RequireCharacter(); _ = c.DraftAttributes;
        new AlertDialog.Builder(this).SetTitle("Active skill specializations")!.SetItems(new[] { "Add specialization", "View / remove specializations" }, (_, args) => Guard(() =>
        {
            if (args.Which == 0) AddSpecialization();
            else
            {
                var specs = RequireCharacter().DraftSpecializations.ToArray();
                if (specs.Length == 0) throw new InvalidOperationException("No active skill specializations have been selected.");
                new AlertDialog.Builder(this).SetTitle("Specializations (+2 when applicable)")!.SetItems(specs.Select(s => $"{s.Skill}: {s.Name}").ToArray(), (_, selected) =>
                    new AlertDialog.Builder(this).SetMessage("Remove this specialization and refund its creation cost?")!.SetNegativeButton("Cancel", (_, _) => { })!
                        .SetPositiveButton("Remove", (_, _) => Guard(() => Commit(updated => updated.RemoveDraftSpecialization(specs[selected.Which].SkillId))))!.Show())!.Show();
            }
        }))!.Show();
    }
    private void AddSpecialization()
    {
        var c = RequireCharacter(); var trained = Creation().Skills(c).Where(s => s.Rating > 0 && !c.DraftSpecializations.Any(p => p.SkillId == s.Id)).ToArray();
        if (trained.Length == 0) throw new InvalidOperationException("Train an active skill without a specialization first.");
        var form = Form(); var skill = Select(form.Layout, "Skill", trained.Select(s => s.Name).ToArray()); var name = Input(form.Layout, "Specialization");
        var payment = Select(form.Layout, "Pay with", new[] { "1 skill point", "7 Karma" }); var details = new TextView(this); form.Layout.AddView(details);
        void Refresh()
        {
            var selected = trained[Math.Max(0,skill.SelectedItemPosition)]; var suggestions = Creation().SpecializationSuggestions(selected.Id);
            name.Text = suggestions.FirstOrDefault() ?? "";
            details.Text = $"Suggestions: {string.Join(", ", suggestions)}\nBase pool: {c.DraftSkillPool(selected)} dice. Specialization adds +2 only when applicable.\nOne specialization per active skill during standard creation. Knowledge specializations and strict-group house rules are pending.";
        }
        skill.ItemSelected += (_, _) => Refresh(); Refresh();
        EditDialog("Add specialization", form.Scroll, () => Commit(updated => updated.AddDraftSpecialization(trained[skill.SelectedItemPosition].Id, name.Text ?? "", payment.SelectedItemPosition == 1)));
    }
    private void RollCharacterSkill()
    {
        var c = RequireCharacter(); var trained = Creation().Skills(c).Where(s => s.Rating > 0 || s.Defaultable).OrderByDescending(s => s.Rating > 0).ThenBy(s => s.Name).ToArray();
        if (trained.Length == 0) throw new InvalidOperationException("No supported skill pool is available. Imported-character pools are still being ported.");
        var form = Form(); var skill = Select(form.Layout, "Active skill", trained.Select(s => s.Name).ToArray());
        var specialization = new CheckBox(this) { Text = "Apply specialization (+2 only when applicable)" }; form.Layout.AddView(specialization);
        var conditionalView = new LinearLayout(this) { Orientation = Orientation.Vertical }; form.Layout.AddView(conditionalView);
        var conditions = new List<(ConditionalSkillBonus Bonus, CheckBox Check)>();
        var info = new TextView(this); form.Layout.AddView(info); var result = new TextView(this);
        void Refresh()
        {
            var selected = trained[Math.Max(0,skill.SelectedItemPosition)]; var spec = c.DraftSpecializations.FirstOrDefault(s => s.SkillId == selected.Id);
            specialization.Checked = false; specialization.Enabled = spec is not null;
            specialization.Text = spec is null ? "No specialization on this skill" : $"Apply {spec.Name} specialization (+2 when applicable)";
            conditionalView.RemoveAllViews(); conditions.Clear();
            foreach (var bonus in c.DraftConditionalSkillBonuses(selected))
            {
                var check = new CheckBox(this) { Text = $"{bonus.Value:+0;-0;0}: {bonus.Description}" };
                conditionalView.AddView(check); conditions.Add((bonus,check));
            }
            info.Text = $"Base pool: {c.DraftSkillPool(selected)} dice{(selected.Rating == 0 ? " (defaulting: attribute minus 1)" : "")}, including supported permanent effects and wounds.\nSelect only the conditions that apply to this test. Edge and additional situational modifiers are not included.";
            result.Text = "";
        }
        skill.ItemSelected += (_, _) => Refresh(); Refresh();
        Button(form.Layout,"Roll selected skill", () =>
        {
            var selected = trained[skill.SelectedItemPosition]; var spec = specialization.Checked ? c.DraftSpecializations.Single(s => s.SkillId == selected.Id).Name : null;
            var pool = c.DraftContextualSkillPool(selected,spec,conditions.Where(p => p.Check.Checked).Select(p => p.Bonus.Id));
            if (pool == 0) throw new InvalidOperationException("The selected test has no dice after modifiers.");
            var roll = DicePool.Roll(new RollOptions(pool));
            result.Text = $"Pool: {pool}; hits: {roll.Hits}\n{(roll.CriticalGlitch ? "Critical glitch" : roll.Glitch ? "Glitch" : "No glitch")}\nDice: {string.Join(", ",roll.Dice)}";
        });
        form.Layout.AddView(result);
        new AlertDialog.Builder(this).SetTitle("Character skill test")!.SetView(form.Scroll)!.SetPositiveButton("Close",(_,_)=>{})!.Show();
    }
}
