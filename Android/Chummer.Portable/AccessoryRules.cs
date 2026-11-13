// SPDX-License-Identifier: GPL-3.0-or-later
using System.Globalization;
using System.Xml;
using System.Xml.Linq;

namespace Chummer.Portable;
public sealed record AccessoryEntry(string Id,string Name,decimal Cost,string Page);
public sealed record InstalledAccessory(string WeaponId,string Id,string Weapon,string Name,string Mount,decimal Cost,bool Equipped);
public sealed class AccessoryRules
{
    private readonly Dictionary<string,XElement> accessories;
    private readonly Dictionary<string,XElement> weapons;
    public AccessoryRules(Stream data)
    {
        using var r=XmlReader.Create(data,new XmlReaderSettings{DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null,MaxCharactersInDocument=32*1024*1024});
        var xml=XDocument.Load(r);if(xml.Root?.Name!="chummer")throw new InvalidDataException("Invalid accessory catalog.");
        accessories=xml.Root.Element("accessories")!.Elements("accessory").Where(a=>(string?)a.Element("source")=="SR5" && (string?)a.Element("name") is "Laser Sight" or "Bipod" or "Tripod")
            .ToDictionary(a=>a.Element("id")!.Value,a=>new XElement(a));
        weapons=xml.Root.Element("weapons")!.Elements("weapon").ToDictionary(w=>w.Element("id")!.Value,w=>new XElement(w));
    }
    public IReadOnlyList<AccessoryEntry> Catalog=>accessories.Select(p=>new AccessoryEntry(p.Key,p.Value.Element("name")!.Value,(decimal)p.Value.Element("cost")!,p.Value.Element("page")!.Value)).OrderBy(a=>a.Name).ToArray();
    public IReadOnlyList<OwnedEquipment> Weapons(CharacterDocument c)=>c.DraftEquipment.Where(w=>w.Kind=="weapon" &&
        (string?)c.DraftWeaponNode(w.Id).Element("type")=="Ranged").ToArray();
    public IReadOnlyList<string> AvailableMounts(CharacterDocument c,string weaponId,string accessoryId)
    {
        var weapon=c.DraftWeaponNode(weaponId);if((string?)weapon.Element("type")!="Ranged")return [];
        if(!accessories.TryGetValue(accessoryId,out var accessory) || !weapons.TryGetValue(weapon.Element("sourceid")!.Value,out var definition))return [];
        var occupied=weapon.Element("accessories")?.Elements("accessory").Select(a=>(string?)a.Element("mount")??"").ToHashSet()??[];
        return (definition.Element("accessorymounts")?.Elements("mount").Select(m=>m.Value)??[])
            .Where(m=>accessory.Element("mount")!.Value.Split('/').Contains(m) && !occupied.Contains(m)).ToArray();
    }
    public void Attach(CharacterDocument c,string weaponId,string accessoryId,string mount)
    {
        var weapon=c.DraftWeaponNode(weaponId);
        if(!accessories.TryGetValue(accessoryId,out var definition))throw new ArgumentException("Choose a supported accessory.");
        if(!AvailableMounts(c,weaponId,accessoryId).Contains(mount))throw new ArgumentException("That weapon mount is incompatible or occupied.");
        var name=definition.Element("name")!.Value;
        if(c.DraftAccessories.Any(a=>a.WeaponId==weaponId && a.Name==name))throw new ArgumentException("This accessory is already installed on the weapon.");
        var cost=(decimal)definition.Element("cost")!;if(cost>c.Nuyen)throw new ArgumentException("Not enough Nuyen.");
        var node=new XElement("accessory",new XElement("guid",Guid.NewGuid().ToString("D")),new XElement("sourceid",accessoryId),new XElement("mount",mount),
            new XElement("rating",0),new XElement("included","False"),new XElement("equipped","True"),new XElement("androidbuiltin","True"),new XElement("androidpurchasecost",cost));
        foreach(var field in new[]{"name","avail","cost","source","page","accuracy","rc","rcgroup","rcdeployable"})if(definition.Element(field) is {} value)node.Add(new XElement(value));
        c.AddDraftAccessory(weapon,node,cost);
    }
}
public sealed partial class CharacterDocument
{
    internal XElement DraftWeaponNode(string id)
    {
        RequireDraft();return document.Root!.Element("weapons")!.Elements("weapon").SingleOrDefault(w=>(string?)w.Element("guid")==id)??throw new ArgumentException("Unknown weapon.");
    }
    public IReadOnlyList<InstalledAccessory> DraftAccessories
    {
        get{RequireDraft();return (document.Root!.Element("weapons")?.Elements("weapon")??[]).SelectMany(w=>w.Element("accessories")?.Elements("accessory")
            .Where(a=>a.Element("androidpurchasecost") is not null).Select(a=>new InstalledAccessory(w.Element("guid")!.Value,a.Element("guid")!.Value,w.Element("name")!.Value,
                a.Element("name")!.Value,a.Element("mount")!.Value,(decimal)a.Element("androidpurchasecost")!,(string?)a.Element("equipped")=="True"))??[]).ToArray();}
    }
    internal void AddDraftAccessory(XElement weapon,XElement accessory,decimal cost)
    {
        RequireDraft();if(weapon.Element("accessories") is null)weapon.Add(new XElement("accessories"));weapon.Element("accessories")!.Add(accessory);
        SetField("nuyen",(Nuyen-cost).ToString(CultureInfo.InvariantCulture));
    }
    public void EquipDraftAccessory(string weaponId,string id,bool equipped)
    {
        var accessory=DraftWeaponNode(weaponId).Element("accessories")?.Elements().SingleOrDefault(a=>(string?)a.Element("guid")==id && (string?)a.Element("androidbuiltin")=="True")??throw new ArgumentException("Unknown editable accessory.");
        accessory.SetElementValue("equipped",equipped?"True":"False");
    }
    public void RemoveDraftAccessory(string weaponId,string id)
    {
        var selected=DraftAccessories.SingleOrDefault(a=>a.WeaponId==weaponId && a.Id==id)??throw new ArgumentException("Unknown purchased accessory.");
        DraftWeaponNode(weaponId).Element("accessories")!.Elements().Single(a=>(string?)a.Element("guid")==id).Remove();SetField("nuyen",(Nuyen+selected.Cost).ToString(CultureInfo.InvariantCulture));
    }
    private static int AccessoryBonus(XElement weapon,string field,bool deployed=false)
    {
        var items=(weapon.Element("accessories")?.Elements("accessory")??[]).Where(a=>(string?)a.Element("equipped")=="True" &&
            (field!="rc" || deployed || (string?)a.Element("rcdeployable")!="True")).ToArray();
        if(field=="accuracy")return items.Select(a=>(int?)a.Element(field)??0).DefaultIfEmpty(0).Max();
        return items.GroupBy(a=>(string?)a.Element("rcgroup")??"0").Sum(g=>g.Key=="0"?g.Sum(a=>(int?)a.Element(field)??0):g.Max(a=>(int?)a.Element(field)??0));
    }
}
