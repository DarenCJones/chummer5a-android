// SPDX-License-Identifier: GPL-3.0-or-later
using Android.App;
using Android.Widget;
using Chummer.Portable;

namespace Chummer.AndroidApp;

public partial class WorkbenchActivity
{
    private MagicRules? magicRules;
    private MagicRules Magic()
    {
        if (magicRules is not null) return magicRules;
        using var spells = Assets!.Open("data/spells.xml"); using var forms = Assets.Open("data/complexforms.xml");
        using var traditions = Assets.Open("data/traditions.xml"); using var streams = Assets.Open("data/streams.xml");
        return magicRules = new MagicRules(spells, forms, traditions, streams);
    }
    private void EditMagic()
    {
        var c = RequireCharacter(); _ = c.DraftAttributes;
        if (c.Field("magenabled") != "True" && c.Field("resenabled") != "True") throw new InvalidOperationException("This character has no Magic or Resonance talent.");
        string[] actions = ["Choose tradition / stream", "Learn spell or ritual", "Learn alchemical preparation", "Learn complex form",
            "View / remove learned abilities", "Drain / fading resistance pool"];
        new AlertDialog.Builder(this).SetTitle("Magic and resonance")!.SetItems(actions, (_, args) => Guard(() =>
        {
            switch (args.Which)
            {
                case 0: ChooseTradition(); break;
                case 1: LearnArcane(false, false); break;
                case 2: LearnArcane(false, true); break;
                case 3: LearnArcane(true, false); break;
                case 4: LearnedArcane(); break;
                case 5:
                    new AlertDialog.Builder(this).SetMessage($"{RequireCharacter().TraditionName}\nUnaugmented resistance pool: {RequireCharacter().DraftDrainPool()} dice")!
                        .SetPositiveButton("Close", (_, _) => { })!.Show(); break;
            }
        }))!.Show();
    }
    private void ChooseTradition()
    {
        var names = Magic().Traditions(RequireCharacter()).ToArray();
        if (names.Length == 0) throw new InvalidOperationException("No tradition or stream is available for this talent.");
        new AlertDialog.Builder(this).SetTitle("Tradition / stream")!.SetItems(names,
            (_, args) => Guard(() => Commit(updated => Magic().SetTradition(updated, names[args.Which]))))!.Show();
    }
    private void LearnArcane(bool form, bool preparation)
    {
        var c = RequireCharacter();
        var entries = (form ? Magic().ComplexForms(c) : Magic().Spells(c, preparation)).ToArray();
        if (entries.Length == 0) throw new InvalidOperationException("This talent cannot learn that type of ability.");
        var view = Form(); var choice = Select(view.Layout, form ? "Complex form" : preparation ? "Preparation" : "Spell / ritual", entries.Select(e => e.Name).ToArray());
        var extra = Input(view.Layout, "Extra choice, when requested"); var details = new TextView(this); view.Layout.AddView(details);
        var limit = int.Parse(c.Field(form ? "cfplimit" : "spelllimit"));
        var learned = form ? c.LearnedForms.Count : c.LearnedSpells.Count;
        view.Layout.AddView(new TextView(this) { Text = $"Free priority allowance remaining: {Math.Max(0, limit - learned)}\nKarma: {c.Karma}\nAdditional {(form ? "complex forms cost 4" : "spells, rituals and preparations cost 5")} Karma each under standard rules." });
        void Refresh()
        {
            var entry = entries[Math.Max(0, choice.SelectedItemPosition)];
            details.Text = $"{entry.Category}\nDuration: {entry.Duration}\n{(form ? "Fading" : "Drain")}: {entry.Difficulty}\nSource: {entry.Source} p. {entry.Page}\n" +
                (entry.RequiresExtra ? entry.ExtraOptions.Length > 0 ? "Choose: " + string.Join(", ", entry.ExtraOptions) : "Enter the requested life form, object or other choice." : "No extra choice is needed.");
        }
        choice.ItemSelected += (_, _) => { extra.Text = ""; Refresh(); }; Refresh();
        EditDialog(form ? "Learn complex form" : preparation ? "Learn preparation" : "Learn spell / ritual", view.Scroll, () =>
        {
            var entry = entries[choice.SelectedItemPosition];
            Commit(updated =>
            {
                if (form) Magic().LearnComplexForm(updated, entry.Id, extra.Text ?? "");
                else Magic().LearnSpell(updated, entry.Id, extra.Text ?? "", preparation);
            });
        });
    }
    private void LearnedArcane()
    {
        var c = RequireCharacter();
        var abilities = c.LearnedSpells.Select(a => (Ability: a, Collection: "spells"))
            .Concat(c.LearnedForms.Select(a => (Ability: a, Collection: "complexforms"))).ToArray();
        if (abilities.Length == 0) { new AlertDialog.Builder(this).SetMessage("No spells, preparations or complex forms learned yet.")!.SetPositiveButton("Close", (_, _) => { })!.Show(); return; }
        new AlertDialog.Builder(this).SetTitle("Learned abilities")!.SetItems(abilities.Select(a => $"{a.Ability.Name} {a.Ability.Extra}").ToArray(),
            (_, args) => Guard(() =>
            {
                var item = abilities[args.Which];
                new AlertDialog.Builder(this).SetTitle(item.Ability.Name)!.SetMessage($"{item.Ability.Extra}\n{item.Ability.Category}\n{(item.Collection == "spells" ? "Drain" : "Fading")}: {item.Ability.Difficulty}")!
                    .SetNeutralButton("Calculate value", (_, _) => Guard(() => ArcaneDifficulty(item.Ability)))!
                    .SetNegativeButton("Close", (_, _) => { })!.SetPositiveButton("Remove", (_, _) =>
                        new AlertDialog.Builder(this).SetMessage("Remove this learned ability? Any marginal creation Karma cost will be refunded.")!
                            .SetNegativeButton("Cancel", (_, _) => { })!.SetPositiveButton("Remove", (_, _) => Guard(() =>
                                Commit(updated => updated.RemoveDraftAbility(item.Collection, item.Ability.Id))))!.Show())!.Show();
            }))!.Show();
    }
    private void ArcaneDifficulty(LearnedAbility ability)
    {
        var form = Form(); var level = Input(form.Layout, "Force / level", "1"); level.InputType = global::Android.Text.InputTypes.ClassNumber;
        var result = new TextView(this);
        form.Layout.AddView(new TextView(this) { Text = $"Expression: {ability.Difficulty}. This evaluates the expression; casting/threading limits and situational modifiers are not applied." });
        Button(form.Layout, "Calculate", () =>
        {
            if (!int.TryParse(level.Text, out var value)) throw new ArgumentException("Enter a whole-number Force or level.");
            result.Text = "Drain / Fading value: " + MagicRules.EvaluateDifficulty(ability.Difficulty, value);
        });
        form.Layout.AddView(result);
        new AlertDialog.Builder(this).SetTitle(ability.Name)!.SetView(form.Scroll)!.SetPositiveButton("Close", (_, _) => { })!.Show();
    }
}
