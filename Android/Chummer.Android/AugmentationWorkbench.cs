// SPDX-License-Identifier: GPL-3.0-or-later
using Android.App;
using Android.Widget;
using Chummer.Portable;

namespace Chummer.AndroidApp;

public partial class WorkbenchActivity
{
    private AugmentationRules? augmentationRules;
    private AugmentationRules Augmentations()
    {
        if(augmentationRules is not null)return augmentationRules;
        using var cyber=Assets!.Open("data/cyberware.xml");using var bio=Assets.Open("data/bioware.xml");
        return augmentationRules=new AugmentationRules(cyber,bio);
    }
    private void EditAugmentations()
    {
        var c=RequireCharacter();_=c.DraftAttributes;
        new AlertDialog.Builder(this).SetTitle($"Implants — Essence {c.DraftEssence:0.00}")!.SetItems(new[]{"Install supported implant","View / remove implants"},(_,args)=>Guard(()=>
        {
            if(args.Which==0)InstallAugmentation();
            else
            {
                var items=RequireCharacter().InstalledAugmentations.ToArray();
                if(items.Length==0)throw new InvalidOperationException("No implants have been installed.");
                new AlertDialog.Builder(this).SetTitle("Installed implants")!.SetItems(items.Select(a=>$"{a.Name} {a.Rating} — {a.Grade}; {a.Essence:0.00} Essence").ToArray(),(_,selected)=>
                    new AlertDialog.Builder(this).SetTitle("Remove creation implant?")!.SetMessage($"Refund {items[selected.Which].Cost:N0} Nuyen and restore its Essence. Its effects will be removed.")!
                        .SetNegativeButton("Cancel",(_,_)=>{})!.SetPositiveButton("Remove",(_,_)=>Guard(()=>Commit(updated=>Augmentations().Remove(updated,items[selected.Which].Id))))!.Show())!.Show();
            }
        }))!.Show();
    }
    private void InstallAugmentation()
    {
        if(RequireCharacter().Field("prioritytalent")!="Mundane")throw new InvalidOperationException("Implant editing currently supports mundane drafts. Magic/Resonance loss and power revalidation are being ported.");
        var all=Augmentations().Catalog.ToArray();var items=all;var form=Form();
        var search=Input(form.Layout,"Search implants");var choice=Select(form.Layout,"Implant",items.Select(a=>a.Name).ToArray());
        var rating=Input(form.Layout,"Implant rating","1");rating.InputType=global::Android.Text.InputTypes.ClassNumber;
        var grades=Augmentations().Grades.ToArray();var grade=Select(form.Layout,"Implant grade",grades);
        var details=new TextView(this);form.Layout.AddView(details);
        form.Layout.AddView(new TextView(this){Text="Supported core implants for mundane creation drafts. Wireless bonuses stay off. Cyberlimbs, nested implants, Essence holes, magical/technomancer implants and additional grade or house-rule effects are pending. Removal during creation refunds the purchase and restores Essence."});
        void Refresh()
        {
            if(items.Length==0){details.Text="No matching supported implant.";return;}
            var item=items[Math.Clamp(choice.SelectedItemPosition,0,items.Length-1)];
            try
            {
                if(!int.TryParse(rating.Text,out var level))throw new ArgumentException("Enter a whole-number rating.");
                var quote=Augmentations().Quote(item.Id,level,grades[Math.Max(0,grade.SelectedItemPosition)]);
                details.Text=$"{item.Kind}; rating {item.Minimum}–{item.Maximum} (0 means unrated)\nSR5 p. {item.Page}\n"+
                    $"Cost: {quote.Cost:N0} Nuyen; available: {RequireCharacter().Nuyen:N0}\nEssence cost: {quote.Essence:0.00}; after installation: {RequireCharacter().DraftEssence-quote.Essence:0.00}\nAvailability: {quote.Availability}{quote.Legality}";
            }
            catch(ArgumentException e){details.Text=e.Message;}
        }
        search.TextChanged+=(_,_)=>
        {
            items=all.Where(a=>a.Name.Contains(search.Text??"",StringComparison.OrdinalIgnoreCase)).ToArray();
            choice.Adapter=new ArrayAdapter<string>(this,global::Android.Resource.Layout.SimpleSpinnerDropDownItem,items.Select(a=>a.Name).ToArray());choice.SetSelection(0);
            if(items.Length>0)rating.Text=items[0].Minimum.ToString();Refresh();
        };
        choice.ItemSelected+=(_,_)=>{if(items.Length>0)rating.Text=items[Math.Clamp(choice.SelectedItemPosition,0,items.Length-1)].Minimum.ToString();Refresh();};
        grade.ItemSelected+=(_,_)=>Refresh();rating.TextChanged+=(_,_)=>Refresh();Refresh();
        EditDialog("Install an implant",form.Scroll,()=>
        {
            if(items.Length==0 || !int.TryParse(rating.Text,out var level))throw new ArgumentException("Choose an implant and valid rating.");
            Commit(updated=>Augmentations().Purchase(updated,items[choice.SelectedItemPosition].Id,level,grades[grade.SelectedItemPosition]));
        });
    }
}
