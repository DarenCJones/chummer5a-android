// SPDX-License-Identifier: GPL-3.0-or-later
using Android.App;
using Android.Widget;

namespace Chummer.AndroidApp;

public partial class WorkbenchActivity
{
    private void EditStartingFunds()
    {
        var c = RequireCharacter(); var options = c.DraftStartingFundsOptions.ToArray(); var saved = c.DraftStartingFunds;
        var selectedIndex = saved is null ? 0 : Math.Max(0, Array.FindIndex(options, o => o.Id == saved.LifestyleId));
        var form = Form(); var choice = Select(form.Layout, "Starting funds lifestyle", options.Select(o => $"{o.Name}: {o.Dice}D6 × {o.Multiplier}").ToArray(), selectedIndex);
        var result = Input(form.Layout, "Dice sum", (saved?.DiceResult ?? options[0].Dice).ToString()); result.InputType = global::Android.Text.InputTypes.ClassNumber;
        var details = new TextView(this); form.Layout.AddView(details);
        void Refresh()
        {
            try
            {
                if (!int.TryParse(result.Text, out var sum)) throw new ArgumentException("Enter a whole-number dice sum.");
                var quote = c.QuoteDraftStartingFunds(options[Math.Max(0, choice.SelectedItemPosition)].Id, sum);
                details.Text = $"Lifestyle roll: {quote.LifestyleNuyen:N2} Nuyen\nCarryover: {quote.CarryoverNuyen:N2} Nuyen; {quote.CarryoverKarma:N0} Karma\nPlanned career balance: {quote.CareerNuyen:N2} Nuyen\nExcess discarded at finalization: {quote.DiscardedNuyen:N2} Nuyen; {quote.DiscardedKarma:N0} Karma\nThis saves a starting-funds selection. Draft balances stay unchanged; creation finalization is still pending. The first lifestyle is the desktop's best expected-value default. You may enter a physical dice roll or roll here.";
            }
            catch (ArgumentException e) { details.Text = e.Message; }
        }
        choice.ItemSelected += (_, _) =>
        {
            var index = Math.Max(0, choice.SelectedItemPosition);
            if (index != selectedIndex) { selectedIndex = index; result.Text = options[index].Dice.ToString(); }
            Refresh();
        };
        result.TextChanged += (_, _) => Refresh();
        Button(form.Layout, "Roll starting funds dice", () =>
        {
            var count = options[choice.SelectedItemPosition].Dice; var sum = 0;
            for (var i = 0; i < count; i++) sum += Chummer.Portable.DicePool.D6();
            result.Text = sum.ToString();
        });
        AlertDialog? editor = null;
        if (saved is not null) Button(form.Layout, "Clear saved starting-funds selection", () => { Commit(updated => updated.ClearDraftStartingFunds()); editor?.Dismiss(); });
        Refresh();
        editor = EditDialog("Starting Nuyen plan", form.Scroll, () =>
        {
            if (!int.TryParse(result.Text, out var sum)) throw new ArgumentException("Enter a whole-number dice sum.");
            Commit(updated => updated.SetDraftStartingFunds(options[choice.SelectedItemPosition].Id, sum));
        });
    }
}
