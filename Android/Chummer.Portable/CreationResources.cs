// SPDX-License-Identifier: GPL-3.0-or-later
using System.Globalization;

namespace Chummer.Portable;

public sealed partial class CharacterDocument
{
    public int DraftResourceKarma
    {
        get
        {
            RequireDraft(); var value = Number("nuyenbp");
            if (value < 0 || value > 10 || decimal.Truncate(value) != value)
                throw new InvalidOperationException("This resource allocation requires additional creation rule support.");
            return (int)value;
        }
    }
    public void SetDraftResourceKarma(int total)
    {
        var old = DraftResourceKarma;
        if (total < 0 || total > 10) throw new ArgumentException("Standard creation allows 0–10 Karma for extra Nuyen.");
        var karma = Karma + old - total;
        var nuyen = Nuyen + 2000m * (total - old);
        if (karma < 0) throw new ArgumentException("Not enough Karma for this resource allocation.");
        if (nuyen < 0) throw new ArgumentException("Refund or remove purchases before reducing the Karma-funded resources.");
        // Desktop stores the allocation in nuyenbp; startingnuyen remains the priority grant.
        SetField("nuyenbp", total.ToString(CultureInfo.InvariantCulture));
        SetField("karma", karma.ToString(CultureInfo.InvariantCulture));
        SetField("nuyen", nuyen.ToString(CultureInfo.InvariantCulture));
    }
}
