// SPDX-License-Identifier: GPL-3.0-or-later
using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Chummer.Portable;
public sealed record LoadedAmmunition(string WeaponId,string Weapon,string GearId,string Name,int Rounds,int Capacity);
public sealed partial class CharacterDocument
{
    private static int AmmoCapacity(XElement weapon)
    {
        var match=Regex.Match((string?)weapon.Element("ammo")??"",@"^(\d+)\((?:c|m|cy|b)\)$");
        if((string?)weapon.Element("type")!="Ranged" || !match.Success)throw new InvalidOperationException("This weapon's loading system needs additional rule support.");
        return int.Parse(match.Groups[1].Value,CultureInfo.InvariantCulture);
    }
    private XElement? LoadedAmmoNode(XElement weapon)
    {
        var clip=weapon.Element("clips")?.Element("clip");
        if(clip is null || ((int?)clip.Element("count")??0)==0)return null;
        return document.Root!.Element("gears")!.Elements("gear").SingleOrDefault(g=>(string?)g.Element("guid")== (string?)clip.Element("id"))
            ??throw new InvalidDataException("The loaded ammunition stack is missing.");
    }
    public IReadOnlyList<LoadedAmmunition> DraftLoadedAmmunition
    {
        get
        {
            RequireDraft();return (document.Root!.Element("weapons")?.Elements("weapon")??[]).Where(w=>Regex.IsMatch((string?)w.Element("ammo")??"",@"^\d+\((?:c|m|cy|b)\)$") && (string?)w.Element("type")=="Ranged")
                .Select(w=>{var ammo=LoadedAmmoNode(w);return new LoadedAmmunition(w.Element("guid")!.Value,w.Element("name")!.Value,
                    (string?)ammo?.Element("guid")??"",(string?)ammo?.Element("name")??"Unloaded",(int?)w.Element("clips")?.Element("clip")?.Element("count")??0,AmmoCapacity(w));}).ToArray();
        }
    }
    public IReadOnlyList<PurchasedGear> CompatibleDraftAmmunition(string weaponId)
    {
        var weapon=DraftWeaponNode(weaponId);_=AmmoCapacity(weapon);
        return DraftPurchasedGear.Where(g=>g.LoadedInto.Length==0 && g.Quantity>0 && g.Extra==(string?)weapon.Element("category") &&
            (string?)document.Root!.Element("gears")!.Elements().Single(n=>(string?)n.Element("guid")==g.Id).Element("ammoforweapontype")=="gun" &&
            (g.Name!="Ammo: Taurus Omni-6 Heavy" || (string?)weapon.Element("name")=="Taurus Omni-6")).ToArray();
    }
    public void LoadDraftWeapon(string weaponId,string gearId,int rounds)
    {
        var updated=Clone();updated.LoadDraftWeaponCore(weaponId,gearId,rounds);ReplaceDraftWith(updated);
    }
    private void LoadDraftWeaponCore(string weaponId,string gearId,int rounds)
    {
        var weapon=DraftWeaponNode(weaponId);var capacity=AmmoCapacity(weapon);
        if(LoadedAmmoNode(weapon) is not null)throw new InvalidOperationException("Unload this weapon before changing ammunition.");
        var stock=CompatibleDraftAmmunition(weaponId).SingleOrDefault(g=>g.Id==gearId)??throw new ArgumentException("Choose an unloaded ammunition stack for this weapon category.");
        if(rounds<1 || rounds>capacity || rounds>stock.Quantity)throw new ArgumentException("Choose a round count within the weapon capacity and available stock.");
        var reserve=document.Root!.Element("gears")!.Elements().Single(g=>(string?)g.Element("guid")==gearId);
        var loaded=new XElement(reserve);var loadedId=Guid.NewGuid().ToString("D");var cost=stock.Cost*rounds/stock.Quantity;
        loaded.SetElementValue("guid",loadedId);loaded.SetElementValue("qty",rounds);loaded.SetElementValue("androidpurchasecost",cost);loaded.SetElementValue("androidloadedweapon",weaponId);
        reserve.SetElementValue("qty",stock.Quantity-rounds);reserve.SetElementValue("androidpurchasecost",stock.Cost-cost);
        if(rounds==stock.Quantity)reserve.Remove();document.Root.Element("gears")!.Add(loaded);
        weapon.SetElementValue("activeammoslot",1);
        weapon.SetElementValue("ammoslots",1);
        var clips=new XElement("clips",new XElement("clip",new XElement("count",rounds),new XElement("location","loaded"),new XElement("id",loadedId)));
        if(weapon.Element("clips") is {} old)old.ReplaceWith(clips);else weapon.Add(clips);
        // Older desktop readers use a pre-5.222.61 migration that would duplicate this stack.
        if(!Version.TryParse(Field("appversion"),out var version) || version<=new Version(5,222,61))SetField("appversion","5.225.0");
        _=LoadedAmmoEffects(weapon,weapon.Element("damage")!.Value,(string?)weapon.Element("ap")??"0");
        _=DraftWeapons(); // Validate derived effects before committing.
    }
    public void UnloadDraftWeapon(string weaponId)
    {
        var weapon=DraftWeaponNode(weaponId);var ammo=LoadedAmmoNode(weapon);
        if(ammo is not null)ammo.Element("androidloadedweapon")?.Remove();
        weapon.Element("clips")?.Remove();weapon.SetElementValue("activeammoslot",1);
    }
    public void FireDraftWeapon(string weaponId,int rounds)
    {
        var updated=Clone();var weapon=updated.DraftWeaponNode(weaponId);
        if((string?)weapon.Element("equipped")!="True")throw new InvalidOperationException("Equip the weapon before recording fire.");
        var ammo=updated.LoadedAmmoNode(weapon)??throw new InvalidOperationException("Load ammunition before firing.");
        var modes=LoadedFiringMode(weapon).Split('/');
        var allowed=rounds switch {1=>modes.Any(m=>m is "SS" or "SA" or "BF" or "FA"),3 or 6=>modes.Any(m=>m is "BF" or "FA"),10 or 20=>modes.Contains("FA"),_=>false};
        if(!allowed)throw new ArgumentException("That firing action is not supported by this weapon's firing modes.");
        var clip=weapon.Element("clips")!.Element("clip")!;var count=(int)clip.Element("count")!;
        if(rounds>count)throw new ArgumentException("Not enough loaded rounds. Choose a shorter supported action or reload.");
        if((int)ammo.Element("qty")! != count)throw new InvalidDataException("The loaded clip and ammunition quantities disagree.");
        var oldCost=(decimal)ammo.Element("androidpurchasecost")!;var remaining=count-rounds;
        ammo.SetElementValue("qty",remaining);ammo.SetElementValue("androidpurchasecost",oldCost*remaining/count);clip.SetElementValue("count",remaining);
        if(remaining==0){ammo.Remove();weapon.Element("clips")!.Remove();}
        ReplaceDraftWith(updated);
    }
    private (string Damage,string AP) LoadedAmmoEffects(XElement weapon,string damage,string ap)
    {
        var ammo=LoadedAmmoNode(weapon);var bonus=ammo?.Element("weaponbonus");if(bonus is null)return(damage,ap);
        if(bonus.Elements().Any(b=>b.Name.LocalName is not ("damage" or "ap" or "apreplace" or "damagetype" or "damagereplace" or "modereplace")))
            throw new InvalidOperationException("This ammunition requires additional weapon-effect rules.");
        var raw=(string?)bonus.Element("damagereplace")??damage;var match=Regex.Match(raw,@"^(\d+)(P|S)$");
        if(!match.Success || !int.TryParse(ap,out var baseAP))throw new InvalidOperationException("This weapon's damage/AP expressions need additional ammunition support.");
        var amount=int.Parse(match.Groups[1].Value)+((int?)bonus.Element("damage")??0);var type=(string?)bonus.Element("damagetype")??match.Groups[2].Value;
        if(!Regex.IsMatch(type,@"^[PS](?:\([ef]\))?$"))throw new InvalidOperationException("This ammunition damage type is not supported yet.");
        return(Math.Max(0,amount)+type,(((int?)bonus.Element("apreplace")??baseAP)+((int?)bonus.Element("ap")??0)).ToString(CultureInfo.InvariantCulture));
    }
    private string LoadedFiringMode(XElement weapon)=>(string?)LoadedAmmoNode(weapon)?.Element("weaponbonus")?.Element("modereplace")??(string?)weapon.Element("mode")??"";
}
