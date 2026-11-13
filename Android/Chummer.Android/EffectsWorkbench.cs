// SPDX-License-Identifier: GPL-3.0-or-later
using Android.App;
using Android.Widget;
using Chummer.Portable;

namespace Chummer.AndroidApp;

public partial class WorkbenchActivity
{
    private PowerRules? powerRules;
    private EquipmentRules? equipmentRules;
    private PowerRules Powers()
    {
        if (powerRules is not null) return powerRules;
        using var data = Assets!.Open("data/powers.xml"); return powerRules = new PowerRules(data);
    }
    private EquipmentRules Equipment()
    {
        if (equipmentRules is not null) return equipmentRules;
        using var armor = Assets!.Open("data/armor.xml"); using var weapons = Assets.Open("data/weapons.xml");
        return equipmentRules = new EquipmentRules(armor, weapons);
    }
    private void EditPowers()
    {
        var c = RequireCharacter(); _ = c.DraftAttributes;
        if (c.Field("adept") != "True") throw new InvalidOperationException("This talent has no adept powers.");
        new AlertDialog.Builder(this).SetTitle($"Adept powers — {c.PowerPointsRemaining:0.##} PP remaining")!
            .SetItems(new[] { "Learn an adept power", "View / remove powers", "Mystic adept power points" }, (_, args) => Guard(() =>
            {
                if (args.Which == 0) LearnPower();
                else if (args.Which == 1) ViewPowers();
                else
                {
                    var form = Form(); var points = Input(form.Layout, "Total purchased power points", c.Field("magsplitadept").Length == 0 ? "0" : c.Field("magsplitadept"));
                    form.Layout.AddView(new TextView(this) { Text = "Mystic adepts pay 5 creation Karma per power point, up to Magic. Lowering this total refunds the cost; remove powers first if needed." });
                    EditDialog("Mystic adept power points", form.Scroll, () =>
                    {
                        if (!int.TryParse(points.Text, out var count)) throw new ArgumentException("Enter a whole number of power points.");
                        Commit(updated => updated.SetMysticPowerPoints(count));
                    });
                }
            }))!.Show();
    }
    private void LearnPower()
    {
        var c = RequireCharacter(); var entries = Powers().Catalog.ToArray(); var form = Form();
        var choice = Select(form.Layout, "Power", entries.Select(p => p.Name).ToArray());
        var rating = Input(form.Layout, "Power rating", "1"); rating.InputType = global::Android.Text.InputTypes.ClassNumber;
        var extra = Input(form.Layout, "Power choice (when needed)"); var details = new TextView(this); form.Layout.AddView(details);
        form.Layout.AddView(new TextView(this) { Text = "Permanent supported effects update the calculated statistics. Activated powers, individual sense benefits and situational tests still need manual play handling. Way discounts and initiation are pending." });
        void Refresh()
        {
            var entry = entries[Math.Max(0, choice.SelectedItemPosition)]; var choices = Powers().Choices(entry.Id, c, Creation());
            extra.Text = choices.FirstOrDefault() ?? "";
            details.Text = $"{entry.PointsPerLevel:0.##} PP per level" + (entry.ExtraCost > 0 ? $" + {entry.ExtraCost:0.##} PP" : "") +
                $"\n{(entry.Levels ? "Rating is limited by Magic and the power's rules." : "This power has rating 1.")}\nSR5 p. {entry.Page}\n" +
                (choices.Count > 0 ? "Choices: " + string.Join(", ", choices) : Powers().FreeTextChoice(entry.Id) ? "Enter the improved sense." : "No extra choice is needed.");
        }
        choice.ItemSelected += (_, _) => Refresh(); Refresh();
        EditDialog("Learn adept power", form.Scroll, () =>
        {
            if (!int.TryParse(rating.Text, out var level)) throw new ArgumentException("Enter a whole-number rating.");
            Commit(updated => Powers().Learn(updated, Creation(), entries[choice.SelectedItemPosition].Id, level, extra.Text ?? ""));
        });
    }
    private void ViewPowers()
    {
        var powers = RequireCharacter().LearnedPowers.ToArray();
        if (powers.Length == 0) throw new InvalidOperationException("No adept powers have been learned yet.");
        new AlertDialog.Builder(this).SetTitle("Learned powers")!.SetItems(powers.Select(p => $"{p.Name} {p.Extra} {p.Rating} — {p.Cost:0.##} PP").ToArray(),
            (_, args) => new AlertDialog.Builder(this).SetTitle("Remove power?")!.SetMessage("Its power points will become available again.")!
                .SetNegativeButton("Cancel", (_, _) => { })!.SetPositiveButton("Remove", (_, _) => Guard(() => Commit(c => c.RemoveDraftPower(powers[args.Which].Id))))!.Show())!.Show();
    }
    private void EditEquipment()
    {
        _ = RequireCharacter().DraftAttributes;
        new AlertDialog.Builder(this).SetTitle("Draft equipment")!.SetItems(new[] { "Buy supported armor", "Buy supported weapon", "Equip / remove purchased items" },
            (_, args) => Guard(() => { if (args.Which < 2) BuyEquipment(args.Which == 0 ? "armor" : "weapon"); else OwnedEquipment(); }))!.Show();
    }
    private void BuyEquipment(string kind)
    {
        var all = Equipment().Catalog.Where(e => e.Kind == kind).ToArray(); var form = Form();
        var query = Input(form.Layout, "Search equipment"); var items = all;
        var choice = Select(form.Layout, "Equipment", items.Select(e => e.Name).ToArray()); var details = new TextView(this); form.Layout.AddView(details);
        var equipped = new CheckBox(this) { Text = "Equip on purchase", Checked = true }; form.Layout.AddView(equipped);
        form.Layout.AddView(new TextView(this) { Text = "This catalog supports fixed-price core armor and weapons at availability 12 or less, without bundled accessories or special effects. Ammunition, attachments, modifications, wireless bonuses and carrying-weight penalties are pending. Removing an item refunds its purchase price during creation." });
        void Refresh()
        {
            details.Text = items.Length == 0 ? "No matching supported equipment." : $"{items[Math.Max(0, choice.SelectedItemPosition)].Details}\n" +
                $"Cost: {items[Math.Max(0, choice.SelectedItemPosition)].Cost:N0} Nuyen; available: {RequireCharacter().Nuyen:N0}\nAvailability: {items[Math.Max(0, choice.SelectedItemPosition)].Availability}; SR5 p. {items[Math.Max(0, choice.SelectedItemPosition)].Page}";
        }
        query.TextChanged += (_, _) =>
        {
            items = all.Where(e => e.Name.Contains(query.Text ?? "", StringComparison.OrdinalIgnoreCase)).ToArray();
            choice.Adapter = new ArrayAdapter<string>(this, global::Android.Resource.Layout.SimpleSpinnerDropDownItem, items.Select(e => e.Name).ToArray()); choice.SetSelection(0); Refresh();
        };
        choice.ItemSelected += (_, _) => Refresh(); Refresh();
        EditDialog("Buy " + kind, form.Scroll, () =>
        {
            if (items.Length == 0) throw new ArgumentException("Choose supported equipment.");
            Commit(c => Equipment().Purchase(c, items[choice.SelectedItemPosition].Id, equipped.Checked));
        });
    }
    private void OwnedEquipment()
    {
        var items = RequireCharacter().DraftEquipment.ToArray();
        if (items.Length == 0) throw new InvalidOperationException("No armor or weapons have been purchased yet.");
        new AlertDialog.Builder(this).SetTitle("Purchased equipment")!.SetItems(items.Select(e => $"{e.Name} — {(e.Equipped ? "Equipped" : "Stored")}").ToArray(), (_, args) =>
        {
            var item = items[args.Which];
            new AlertDialog.Builder(this).SetTitle(item.Name)!.SetItems(new[] { item.Equipped ? "Unequip" : "Equip", "Remove and refund purchase" }, (_, action) => Guard(() =>
            {
                if (action.Which == 0) Commit(c => c.EquipDraftItem(item.Id, !item.Equipped));
                else new AlertDialog.Builder(this).SetMessage($"Remove {item.Name} and refund {item.PurchaseCost:N0} Nuyen?")!.SetNegativeButton("Cancel", (_, _) => { })!
                    .SetPositiveButton("Remove", (_, _) => Guard(() => Commit(c => c.RemoveDraftEquipment(item.Id))))!.Show();
            }))!.Show();
        })!.Show();
    }
    private string EffectsSummary(CharacterDocument c)
    {
        var s = c.EffectiveStats();
        return $"\nCalculated permanent effects\nAttributes: {string.Join(", ", s.Attributes.Select(a => $"{a.Key} {a.Value}"))}\n" +
            $"Limits: physical {s.PhysicalLimit}, mental {s.MentalLimit}, social {s.SocialLimit}\nInitiative: {s.Initiative} + {s.InitiativeDice}D6\n" +
            $"Armor: {s.Armor}; armor penalty: {s.ArmorPenalty}; wound penalty: {s.WoundPenalty}\nDefense pool: {s.DefensePool}; damage resistance: {s.SoakPool} (before AP)\n" +
            $"Condition monitors: {s.PhysicalBoxes} physical / {s.StunBoxes} stun\n" +
            (c.Field("adept") == "True" ? $"Power points: {c.PowerPointsRemaining:0.##}/{c.PowerPointsTotal:0.##} remaining\n" + string.Join("\n", c.LearnedPowers.Select(p => $"{p.Name} {p.Extra} {p.Rating} — {p.Cost:0.##} PP")) : "") +
            "\n" + string.Join("\n", c.DraftWeapons().Prepend(c.DraftUnarmedAttack()).Select(w => $"{w.Name}: {w.Damage}; accuracy {w.Accuracy}; AP {w.AP}; reach {w.Reach}")) +
            "\nPower and quality effects\n" + string.Join("\n", c.DraftEffectDescriptions()) +
            "\nActive powers, ammunition, situational modifiers and carrying-weight penalties require manual handling.";
    }
}
