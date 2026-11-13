// SPDX-License-Identifier: GPL-3.0-or-later
using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace Chummer.Portable;
public sealed record GearEntry(string Id,string Name,string Category,decimal Cost,decimal CostFor,bool Ammo,string Page);
public sealed record PurchasedGear(string Id,string Name,int Quantity,string Extra,decimal Cost,string LoadedInto="");
public sealed class GearRules
{
    private readonly Dictionary<string,XElement> items;
    public IReadOnlyList<string> AmmoCategories{get;}
    public GearRules(Stream gearData,Stream weaponData)
    {
        static XDocument Read(Stream stream){using var r=XmlReader.Create(stream,new XmlReaderSettings{DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null,MaxCharactersInDocument=32*1024*1024});var xml=XDocument.Load(r);if(xml.Root?.Name!="chummer")throw new InvalidDataException("Invalid gear catalog.");return xml;}
        var gear=Read(gearData);var weapons=Read(weaponData);
        AmmoCategories=weapons.Root!.Element("categories")!.Elements("category").Where(c=>(string?)c.Attribute("type")=="gun").Select(c=>c.Value).OrderBy(c=>c).ToArray();
        items=gear.Root!.Element("gears")!.Elements("gear").Where(e=>(string?)e.Element("source")=="SR5" && e.Element("hide") is null &&
            ((string?)e.Element("rating") is null or "0") &&
            !new[]{"bonus","wirelessbonus","gears","addweapon","required","forbidden","selecttext"}.Any(n=>e.Element(n) is not null) &&
            ((string?)e.Element("ammoforweapontype") is null or "gun") &&
            decimal.TryParse((string?)e.Element("cost"),NumberStyles.Number,CultureInfo.InvariantCulture,out var cost) && cost>0 &&
            (e.Element("costfor") is null || decimal.TryParse(e.Element("costfor")!.Value,NumberStyles.Number,CultureInfo.InvariantCulture,out var costFor) && costFor>0) &&
            Regex.IsMatch((string?)e.Element("avail")??"",@"^\d+[RF]?$") && int.Parse(Regex.Match(e.Element("avail")!.Value,@"\d+").Value)<=12)
            .ToDictionary(e=>e.Element("id")!.Value,e=>new XElement(e));
    }
    public IReadOnlyList<GearEntry> Catalog=>items.Select(p=>new GearEntry(p.Key,p.Value.Element("name")!.Value,p.Value.Element("category")!.Value,
        (decimal)p.Value.Element("cost")!, (decimal?)p.Value.Element("costfor")??1,p.Value.Element("ammoforweapontype") is not null,p.Value.Element("page")!.Value)).OrderBy(e=>e.Name).ToArray();
    public decimal Quote(string id,int quantity)
    {
        if(quantity is <1 or >1000000)throw new ArgumentException("Quantity must be from 1 to 1000000.");
        var item=Catalog.SingleOrDefault(e=>e.Id==id)??throw new ArgumentException("Choose supported gear.");
        if(item.CostFor<=0)throw new InvalidDataException("Invalid gear pricing divisor.");
        return item.Cost*quantity/item.CostFor;
    }
    public void Purchase(CharacterDocument c,string id,int quantity,string ammoCategory="")
    {
        _=c.DraftAttributes;var cost=Quote(id,quantity);var entry=Catalog.Single(e=>e.Id==id);
        if(entry.Ammo?!AmmoCategories.Contains(ammoCategory):ammoCategory.Length>0)throw new ArgumentException("Choose a valid ammunition weapon category; leave it blank for ordinary gear.");
        if(cost>c.Nuyen)throw new ArgumentException("Not enough Nuyen.");
        var node=new XElement(items[id]);node.Name="gear";node.Element("id")!.Remove();
        node.SetElementValue("sourceid",id);node.SetElementValue("guid",Guid.NewGuid().ToString("D"));node.SetElementValue("qty",quantity);
        node.SetElementValue("extra",ammoCategory);node.SetElementValue("equipped","False");node.SetElementValue("wirelesson","False");
        node.SetElementValue("androidbuiltin","True");node.SetElementValue("androidpurchasecost",cost);
        c.AddDraftGear(node,cost);
    }
}
public sealed partial class CharacterDocument
{
    public IReadOnlyList<PurchasedGear> DraftPurchasedGear
    {
        get{RequireDraft();return (document.Root!.Element("gears")?.Elements("gear")??[]).Where(g=>g.Element("androidpurchasecost") is not null).Select(g=>new PurchasedGear(
            g.Element("guid")!.Value,g.Element("name")!.Value,(int)g.Element("qty")!,(string?)g.Element("extra")??"",(decimal)g.Element("androidpurchasecost")!,(string?)g.Element("androidloadedweapon")??"")).ToArray();}
    }
    internal void AddDraftGear(XElement item,decimal cost)
    {
        RequireDraft();document.Root!.Element("gears")!.Add(item);SetField("nuyen",(Nuyen-cost).ToString(CultureInfo.InvariantCulture));
    }
    public void RemoveDraftPurchasedGear(string id)
    {
        var item=DraftPurchasedGear.SingleOrDefault(g=>g.Id==id)??throw new ArgumentException("Unknown purchased gear stack.");
        if(item.LoadedInto.Length>0)throw new InvalidOperationException("Unload this ammunition before removing its stack.");
        document.Root!.Element("gears")!.Elements().Single(g=>(string?)g.Element("guid")==id).Remove();SetField("nuyen",(Nuyen+item.Cost).ToString(CultureInfo.InvariantCulture));
    }
}
