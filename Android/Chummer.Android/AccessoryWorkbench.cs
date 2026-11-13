// SPDX-License-Identifier: GPL-3.0-or-later
using Android.App;
using Android.Widget;
using Chummer.Portable;

namespace Chummer.AndroidApp;
public partial class WorkbenchActivity
{
    private AccessoryRules? accessoryRules;
    private AccessoryRules Accessories()
    {
        if(accessoryRules is not null)return accessoryRules;
        using var data=Assets!.Open("data/weapons.xml");return accessoryRules=new AccessoryRules(data);
    }
    private void EditAccessories()
    {
        _=RequireCharacter().DraftAttributes;
        new AlertDialog.Builder(this).SetTitle("Weapon accessories")!.SetItems(new[]{"Buy / attach supported accessory","View / equip / remove accessories"},(_,args)=>Guard(()=>
        {
            if(args.Which==0)AttachAccessory();
            else
            {
                var items=RequireCharacter().DraftAccessories.ToArray();if(items.Length==0)throw new InvalidOperationException("No supported weapon accessories are installed.");
                new AlertDialog.Builder(this).SetTitle("Installed accessories")!.SetItems(items.Select(a=>$"{a.Weapon}: {a.Name} ({a.Mount})").ToArray(),(_,selected)=>
                {
                    var item=items[selected.Which];new AlertDialog.Builder(this).SetTitle(item.Name)!.SetItems(new[]{item.Equipped?"Disable effect":"Enable effect","Remove and refund"},(_,action)=>Guard(()=>
                    {
                        if(action.Which==0)Commit(c=>c.EquipDraftAccessory(item.WeaponId,item.Id,!item.Equipped));
                        else new AlertDialog.Builder(this).SetMessage($"Remove this accessory and refund {item.Cost:N0} Nuyen?")!.SetNegativeButton("Cancel",(_,_)=>{})!
                            .SetPositiveButton("Remove",(_,_)=>Guard(()=>Commit(c=>c.RemoveDraftAccessory(item.WeaponId,item.Id))))!.Show();
                    }))!.Show();
                })!.Show();
            }
        }))!.Show();
    }
    private void AttachAccessory()
    {
        var c=RequireCharacter();var weapons=Accessories().Weapons(c).ToArray();
        if(weapons.Length==0)throw new InvalidOperationException("Purchase a supported ranged weapon first.");
        var accessories=Accessories().Catalog.ToArray();var form=Form();
        var weapon=Select(form.Layout,"Parent weapon",weapons.Select(w=>w.Name).ToArray());
        var accessory=Select(form.Layout,"Accessory",accessories.Select(a=>a.Name).ToArray());
        string[] mounts=[];var mount=Select(form.Layout,"Weapon mount",Array.Empty<string>());var details=new TextView(this);form.Layout.AddView(details);
        form.Layout.AddView(new TextView(this){Text="Laser Sight, Bipod and Tripod are supported here. Occupied mounts cannot be reused. Bipod/tripod recoil bonuses apply only when deployed; the sheet shows them separately. Smartguns, scopes, suppressors, bundled accessories and additional compatibility rules are still being ported."});
        void Refresh()
        {
            var selected=accessories[Math.Max(0,accessory.SelectedItemPosition)];mounts=Accessories().AvailableMounts(c,weapons[Math.Max(0,weapon.SelectedItemPosition)].Id,selected.Id).ToArray();
            mount.Adapter=new ArrayAdapter<string>(this,global::Android.Resource.Layout.SimpleSpinnerDropDownItem,mounts);mount.SetSelection(0);
            details.Text=$"{selected.Cost:N0} Nuyen; available: {RequireCharacter().Nuyen:N0}\nSR5 p. {selected.Page}\n"+(mounts.Length==0?"No compatible unoccupied mount on this weapon.":"Choose the mount for this accessory.");
        }
        weapon.ItemSelected+=(_,_)=>Refresh();accessory.ItemSelected+=(_,_)=>Refresh();Refresh();
        EditDialog("Attach a weapon accessory",form.Scroll,()=>
        {
            if(mounts.Length==0)throw new ArgumentException("Choose a weapon with a compatible free mount.");
            Commit(updated=>Accessories().Attach(updated,weapons[weapon.SelectedItemPosition].Id,accessories[accessory.SelectedItemPosition].Id,mounts[mount.SelectedItemPosition]));
        });
    }
}
