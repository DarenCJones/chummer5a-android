// SPDX-License-Identifier: GPL-3.0-or-later
using Android.App;
using Android.Widget;
using Chummer.Portable;

namespace Chummer.AndroidApp;
public partial class WorkbenchActivity
{
    private void ManageAmmunition()
    {
        var weapons=RequireCharacter().DraftLoadedAmmunition.ToArray();
        if(weapons.Length==0)throw new InvalidOperationException("Purchase a supported ranged weapon with a standard magazine, cylinder or belt first.");
        new AlertDialog.Builder(this).SetTitle("Weapon ammunition")!.SetItems(weapons.Select(w=>$"{w.Weapon} — {w.Rounds}/{w.Capacity} rounds; {w.Name}").ToArray(),(_,args)=>Guard(()=>
        {
            var selected=weapons[args.Which];
            new AlertDialog.Builder(this).SetTitle(selected.Weapon)!.SetItems(new[]{"Load ammunition","Fire 1 round","Fire 3-round burst","Fire 6-round burst","Fire 10-round full auto","Fire 20-round suppressive fire","Unload ammunition"},(_,action)=>Guard(()=>
            {
                if(action.Which==0)LoadWeapon(selected.WeaponId);
                else if(action.Which==6)Commit(c=>c.UnloadDraftWeapon(selected.WeaponId));
                else{int[] rounds=[1,3,6,10,20];Commit(c=>c.FireDraftWeapon(selected.WeaponId,rounds[action.Which-1]));}
            }))!.Show();
        }))!.Show();
    }
    private void LoadWeapon(string weaponId)
    {
        var c=RequireCharacter();var state=c.DraftLoadedAmmunition.Single(w=>w.WeaponId==weaponId);
        if(state.Rounds>0)throw new InvalidOperationException("Unload the existing ammunition first.");
        var stocks=c.CompatibleDraftAmmunition(weaponId).ToArray();
        if(stocks.Length==0)throw new InvalidOperationException("Purchase unloaded ammunition for this weapon's category first. Loaded stacks cannot be reused by another weapon.");
        var form=Form();var stock=Select(form.Layout,"Ammunition stack",stocks.Select(g=>$"{g.Name} — {g.Quantity} rounds").ToArray());
        var quantity=Input(form.Layout,"Rounds to load",Math.Min(state.Capacity,stocks[0].Quantity).ToString());quantity.InputType=global::Android.Text.InputTypes.ClassNumber;
        var details=new TextView(this);form.Layout.AddView(details);
        void Refresh()
        {
            var selected=stocks[Math.Max(0,stock.SelectedItemPosition)];quantity.Text=Math.Min(state.Capacity,selected.Quantity).ToString();
            details.Text=$"Weapon capacity: {state.Capacity}; available rounds: {selected.Quantity}\nLoading splits off a separate ammunition stack. Firing consumes rounds and their refundable value; unloading keeps remaining rounds in inventory.\nThis preview records firing and supported damage/AP changes. Attack rolls, recoil penalties, tracer situational bonuses, reload action costs and unusual loading systems require manual handling.";
        }
        stock.ItemSelected+=(_,_)=>Refresh();Refresh();
        EditDialog("Load "+state.Weapon,form.Scroll,()=>
        {
            if(!int.TryParse(quantity.Text,out var count))throw new ArgumentException("Enter a whole-number round count.");
            Commit(updated=>updated.LoadDraftWeapon(weaponId,stocks[stock.SelectedItemPosition].Id,count));
        });
    }
}
