// SPDX-License-Identifier: GPL-3.0-or-later
using Android.Widget;

namespace Chummer.AndroidApp;

public partial class WorkbenchActivity
{
    private void EditResourceKarma()
    {
        var c = RequireCharacter(); var previous = c.DraftResourceKarma; var form = Form();
        var total = Input(form.Layout, "Total Karma exchanged for Nuyen (0–10)", previous.ToString());
        total.InputType = global::Android.Text.InputTypes.ClassNumber;
        var details = new TextView(this); form.Layout.AddView(details);
        void Refresh()
        {
            if (!int.TryParse(total.Text, out var amount) || amount < 0 || amount > 10)
            { details.Text = "Enter a whole number from 0 to 10."; return; }
            details.Text = $"Standard creation: 1 Karma = 2,000 Nuyen.\nKarma after change: {c.Karma + previous - amount}\nNuyen after change: {c.Nuyen + 2000m * (amount - previous):N0}\nThis is the total allocation, not an additional purchase. Reducing it refunds Karma only if enough unspent Nuyen remains. House-rule rates and quality-based resource limits are pending.";
        }
        total.TextChanged += (_, _) => Refresh(); Refresh();
        EditDialog("Creation resources", form.Scroll, () =>
        {
            if (!int.TryParse(total.Text, out var amount)) throw new ArgumentException("Enter a whole-number Karma allocation.");
            Commit(updated => updated.SetDraftResourceKarma(amount));
        });
    }
}
