// SPDX-License-Identifier: GPL-3.0-or-later
using Android.App;
using Android.Widget;
using Chummer.Portable;

namespace Chummer.AndroidApp;
public partial class WorkbenchActivity
{
    private GearRules? gearRules;
    private GearRules Gear()
    {
        if(gearRules is not null)return gearRules;
        using var gear=Assets!.Open("data/gear.xml");using var weapons=Assets.Open("data/weapons.xml");return gearRules=new GearRules(gear,weapons);
    }
    private void EditGear()
    {
        _=RequireCharacter().DraftAttributes;
        new AlertDialog.Builder(this).SetTitle("Gear and ammunition inventory")!.SetItems(new[]{"Buy supported gear / ammo","View / remove purchased stacks"},(_,args)=>Guard(()=>
        {
            if(args.Which==0)BuyGear();
            else
            {
                var items=RequireCharacter().DraftPurchasedGear.ToArray();if(items.Length==0)throw new InvalidOperationException("No gear stacks have been purchased.");
                new AlertDialog.Builder(this).SetTitle("Purchased gear")!.SetItems(items.Select(g=>$"{g.Name} {g.Extra} × {g.Quantity}{(g.LoadedInto.Length>0?" (loaded)":"")}").ToArray(),(_,selected)=>
                    new AlertDialog.Builder(this).SetMessage($"Remove this stack and refund {items[selected.Which].Cost:N2} Nuyen?")!.SetNegativeButton("Cancel",(_,_)=>{})!
                        .SetPositiveButton("Remove",(_,_)=>Guard(()=>Commit(c=>c.RemoveDraftPurchasedGear(items[selected.Which].Id))))!.Show())!.Show();
            }
        }))!.Show();
    }
    private void BuyGear()
    {
        var all=Gear().Catalog.ToArray();var items=all;var form=Form();
        var search=Input(form.Layout,"Search gear / ammo");var choice=Select(form.Layout,"Gear",items.Select(g=>g.Name).ToArray());
        var quantity=Input(form.Layout,"Quantity / rounds","1");quantity.InputType=global::Android.Text.InputTypes.ClassNumber;
        var categories=Gear().AmmoCategories.ToArray();var category=Select(form.Layout,"Ammunition weapon category",categories);
        var details=new TextView(this);form.Layout.AddView(details);
        form.Layout.AddView(new TextView(this){Text="Inventory purchases for supported fixed-price, unrated core gear and ammunition. Use Play → Load / fire / unload weapons to load compatible rounds and record firing. Supported ammunition modifies damage/AP only while loaded. Additional attachments, rated/active gear effects and wireless bonuses are still being ported."});
        void Refresh()
        {
            if(items.Length==0){details.Text="No matching supported gear.";return;}
            var selected=items[Math.Clamp(choice.SelectedItemPosition,0,items.Length-1)];category.Enabled=selected.Ammo;
            if(!int.TryParse(quantity.Text,out var count)){details.Text="Enter a whole-number quantity.";return;}
            try{details.Text=$"{selected.Category}; SR5 p. {selected.Page}\nPrice: {selected.Cost:N2} Nuyen per {selected.CostFor} units\n"+
                $"Purchase: {Gear().Quote(selected.Id,count):N2} Nuyen; available: {RequireCharacter().Nuyen:N2}";}
            catch(ArgumentException e){details.Text=e.Message;}
        }
        search.TextChanged+=(_,_)=>{items=all.Where(g=>g.Name.Contains(search.Text??"",StringComparison.OrdinalIgnoreCase)).ToArray();choice.Adapter=new ArrayAdapter<string>(this,global::Android.Resource.Layout.SimpleSpinnerDropDownItem,items.Select(g=>g.Name).ToArray());choice.SetSelection(0);Refresh();};
        choice.ItemSelected+=(_,_)=>Refresh();quantity.TextChanged+=(_,_)=>Refresh();Refresh();
        EditDialog("Buy gear / ammunition",form.Scroll,()=>
        {
            if(items.Length==0 || !int.TryParse(quantity.Text,out var count))throw new ArgumentException("Choose gear and a valid quantity.");
            var selected=items[choice.SelectedItemPosition];Commit(c=>Gear().Purchase(c,selected.Id,count,selected.Ammo?categories[category.SelectedItemPosition]:""));
        });
    }
}
