// SPDX-License-Identifier: GPL-3.0-or-later
using Android.App;
using Android.Widget;
using Chummer.Portable;

namespace Chummer.AndroidApp;

public partial class WorkbenchActivity
{
    private LifestyleRules? lifestyleRules;
    private LifestyleRules Lifestyles()
    {
        if (lifestyleRules is not null) return lifestyleRules;
        using var data = Assets!.Open("data/lifestyles.xml"); return lifestyleRules = new LifestyleRules(data);
    }
    private void EditLifestyles()
    {
        var c = RequireCharacter(); var existing = c.DraftLifestyles.ToArray();
        new AlertDialog.Builder(this).SetTitle("Creation lifestyles")!.SetItems(new[] { "Add a lifestyle" }.Concat(existing.Select(l => $"{l.Name} — {l.Base}; {l.Months} months")).ToArray(), (_, args) => Guard(() =>
        {
            var selected = args.Which == 0 ? null : existing[args.Which - 1];
            var items = Lifestyles().Catalog.ToArray(); var form = Form();
            var name = Input(form.Layout, "Lifestyle name", selected?.Name ?? "Home");
            var choice = Select(form.Layout, "Base lifestyle", items.Select(l => l.Name).ToArray(), selected is null ? 0 : Array.FindIndex(items, l => l.Id == selected.SourceId));
            var months = Input(form.Layout, "Prepaid months (1–1200)", (selected?.Months ?? 1).ToString());
            var roommates = Input(form.Layout, "Roommates (0–100)", (selected?.Roommates ?? 0).ToString());
            months.InputType = roommates.InputType = global::Android.Text.InputTypes.ClassNumber;
            var split = new CheckBox(this) { Text = "Share cost equally with roommates", Checked = selected?.Split ?? false }; form.Layout.AddView(split);
            var city = Input(form.Layout, "City", selected?.City ?? ""); var details = new TextView(this); form.Layout.AddView(details);
            void Refresh()
            {
                try
                {
                    if (!int.TryParse(months.Text, out var count) || !int.TryParse(roommates.Text, out var people) || count < 1 || count > 1200) throw new ArgumentException("Enter valid whole-number months and roommates.");
                    var monthly = Lifestyles().Quote(c, items[Math.Max(0, choice.SelectedItemPosition)].Id, people, split.Checked);
                    details.Text = $"Monthly cost: {monthly:N2} Nuyen\nPrepaid cost: {monthly * count:N2} Nuyen\nAvailable including this lifestyle's refund: {c.Nuyen + (selected?.PurchaseCost ?? 0):N2}\nIncludes supported metatype/dependent adjustments and roommate sharing. Advanced lifestyle qualities, trust funds, free grids, recurring career payments and starting-Nuyen rolls are pending.";
                }
                catch (ArgumentException e) { details.Text = e.Message; }
            }
            choice.ItemSelected += (_, _) => Refresh(); months.TextChanged += (_, _) => Refresh(); roommates.TextChanged += (_, _) => Refresh(); split.CheckedChange += (_, _) => Refresh(); Refresh();
            AlertDialog? editor = null;
            if (selected is not null) Button(form.Layout, "Remove lifestyle and refund", () => new AlertDialog.Builder(this).SetMessage("Remove this creation lifestyle and refund its prepaid cost?")!
                .SetNegativeButton("Cancel", (_, _) => { })!.SetPositiveButton("Remove", (_, _) => Guard(() => { Commit(updated => Lifestyles().Remove(updated, selected.Id)); editor?.Dismiss(); }))!.Show());
            editor = EditDialog("Lifestyle", form.Scroll, () =>
            {
                if (!int.TryParse(months.Text, out var count) || !int.TryParse(roommates.Text, out var people)) throw new ArgumentException("Enter whole-number months and roommates.");
                Commit(updated => Lifestyles().Set(updated, selected?.Id, items[choice.SelectedItemPosition].Id, name.Text ?? "", count, people, split.Checked, city.Text ?? ""));
            });
        }))!.Show();
    }
}
