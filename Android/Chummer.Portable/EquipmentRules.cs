// SPDX-License-Identifier: GPL-3.0-or-later
using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace Chummer.Portable;

public sealed record EquipmentEntry(string Id, string Name, string Kind, string Category, decimal Cost, string Availability, string Details, string Page);
public sealed record OwnedEquipment(string Id, string Name, string Kind, bool Equipped, decimal PurchaseCost);
public sealed record WeaponStats(string Name, string Skill, string Damage, int Accuracy, string AP, string Mode, string Ammo, int Reach,int AccessoryRecoil=0,int DeployedAccessoryRecoil=0);

/// <summary>Fixed-price core armor and weapons without bundled accessories, modifications or selected effects.</summary>
public sealed class EquipmentRules
{
    private readonly Dictionary<string, (string Kind, XElement Data)> items = new();
    public EquipmentRules(Stream armor, Stream weapons)
    {
        Load(armor, "armors", "armor"); Load(weapons, "weapons", "weapon");
    }
    private void Load(Stream stream, string collection, string kind)
    {
        using var reader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 32 * 1024 * 1024 });
        var xml = XDocument.Load(reader);
        if (xml.Root?.Name != "chummer") throw new InvalidDataException("Invalid equipment catalog.");
        foreach (var e in xml.Root.Element(collection)!.Elements(kind))
        {
            if ((string?)e.Element("source") != "SR5" || e.Element("hide") is not null || e.Element("cyberware") is not null ||
                new[] { "bonus", "wirelessbonus", "wirelessweaponbonus", "accessories", "underbarrels", "gears", "mods", "addmodcategory", "addweapon", "required", "forbidden" }.Any(n => e.Element(n) is not null) ||
                !decimal.TryParse((string?)e.Element("cost"), NumberStyles.Number, CultureInfo.InvariantCulture, out var cost) || cost <= 0 ||
                !Regex.IsMatch((string?)e.Element("avail") ?? "", @"^\d+[RF]?$") || int.Parse(Regex.Match(e.Element("avail")!.Value, @"\d+").Value) > 12) continue;
            if (kind == "armor" && !Regex.IsMatch((string?)e.Element("armor") ?? "", @"^\+?\d+$")) continue;
            if (kind == "weapon" && ((string?)e.Element("category") == "Bows" || Skill(e).Length == 0 || !Regex.IsMatch((string?)e.Element("accuracy") ?? "", @"^(\d+|Physical)$") ||
                !Regex.IsMatch((string?)e.Element("damage") ?? "", @"^(\d+|\(\{STR\}\+\d+\)|\{STR\})[PS]$"))) continue;
            items.Add(e.Element("id")!.Value, (kind, new XElement(e)));
        }
    }
    internal static string Skill(XElement e) => (string?)e.Element("useskill") ?? ((string?)e.Element("category") switch {
        "Blades" => "Blades", "Clubs" => "Clubs", "Unarmed" or "Unarmed Combat" => "Unarmed Combat",
        "Heavy Pistols" or "Light Pistols" or "Holdouts" or "Tasers" => "Pistols",
        "Machine Pistols" or "Submachine Guns" or "Assault Rifles" => "Automatics",
        "Shotguns" or "Sniper Rifles" or "Sporting Rifles" => "Longarms",
        "Assault Cannons" or "Grenade Launchers" or "Light Machine Guns" or "Medium Machine Guns" or "Heavy Machine Guns" or "Missile Launchers" => "Heavy Weapons",
        "Throwing Weapons" => "Throwing Weapons", "Bows" or "Crossbows" => "Archery", _ => "" });
    public IReadOnlyList<EquipmentEntry> Catalog => items.Select(pair => {
        var (kind, e) = pair.Value;
        return new EquipmentEntry(pair.Key, e.Element("name")!.Value, kind, e.Element("category")!.Value, (decimal)e.Element("cost")!, e.Element("avail")!.Value,
            kind == "armor" ? $"Armor {e.Element("armor")!.Value}" : $"Damage {e.Element("damage")!.Value}; accuracy {e.Element("accuracy")!.Value}; AP {(string?)e.Element("ap")}; {(string?)e.Element("mode")}", e.Element("page")!.Value);
    }).OrderBy(e => e.Name).ToArray();
    public void Purchase(CharacterDocument character, string sourceId, bool equipped)
    {
        _ = character.DraftAttributes;
        if (!items.TryGetValue(sourceId, out var item)) throw new ArgumentException("This item requires equipment rules that are not supported yet.");
        var (kind, e) = item; var cost = (decimal)e.Element("cost")!;
        if (cost > character.Nuyen) throw new ArgumentException("Not enough Nuyen.");
        var node = new XElement(kind, new XElement("guid", Guid.NewGuid().ToString("D")), new XElement("sourceid", sourceId));
        foreach (var field in new[] { "name", "category", "type", "spec", "spec2", "armor", "armorcapacity", "avail", "cost", "weight", "source", "page", "accuracy", "reach", "damage", "ap", "mode", "rc", "ammo", "conceal", "range", "alternaterange", "ammocategory", "sizecategory", "useskillspec", "mount" })
            if (e.Element(field) is { } value) node.Add(new XElement(value));
        node.Add(new XElement("equipped", equipped ? "True" : "False"), new XElement("androidbuiltin", "True"), new XElement("androidpurchasecost", cost),
            new XElement("rating", 0), new XElement("stolen", "False"), new XElement("wirelesson", "False"), new XElement("discountedcost", "False"), new XElement("notes", ""));
        if (kind == "armor") node.Add(new XElement("encumbrance", "True"), new XElement("armormods"), new XElement("gears"), new XElement("bonus"));
        else node.Add(new XElement("useskill", Skill(e)), new XElement("requireammo", (string?)e.Element("requireammo") ?? ((string?)e.Element("type") == "Ranged" ? "True" : "False")),
            new XElement("allowaccessory", (string?)e.Element("allowaccessory") ?? "True"), new XElement("accessories"));
        character.AddDraftEquipment(node, cost);
    }
}

public sealed partial class CharacterDocument
{
    private IEnumerable<XElement> EquipmentNodes => new[] { "armors", "weapons" }.SelectMany(c => document.Root!.Element(c)?.Elements() ?? []);
    public IReadOnlyList<OwnedEquipment> DraftEquipment
    {
        get { RequireDraft(); return EquipmentNodes.Select(e => new OwnedEquipment(e.Element("guid")!.Value, e.Element("name")!.Value, e.Name.LocalName,
            (string?)e.Element("equipped") == "True", (decimal?)e.Element("androidpurchasecost") ?? 0)).ToArray(); }
    }
    internal void AddDraftEquipment(XElement item, decimal cost)
    {
        RequireDraft(); var collection = item.Name.LocalName == "armor" ? "armors" : "weapons";
        if (document.Root!.Element(collection) is null) document.Root.Add(new XElement(collection));
        document.Root.Element(collection)!.Add(item); SetField("nuyen", (Nuyen - cost).ToString(CultureInfo.InvariantCulture));
    }
    public void EquipDraftItem(string id, bool equipped)
    {
        RequireDraft(); var item = EquipmentNodes.SingleOrDefault(e => (string?)e.Element("guid") == id) ?? throw new ArgumentException("Unknown equipment item.");
        item.SetElementValue("equipped", equipped ? "True" : "False");
    }
    public void RemoveDraftEquipment(string id)
    {
        RequireDraft(); var item = EquipmentNodes.SingleOrDefault(e => (string?)e.Element("guid") == id) ?? throw new ArgumentException("Unknown equipment item.");
        var refund = (decimal?)item.Element("androidpurchasecost") ?? 0;
        refund+=item.Element("accessories")?.Elements("accessory").Sum(a=>(decimal?)a.Element("androidpurchasecost")??0)??0;
        item.Remove(); SetField("nuyen", (Nuyen + refund).ToString(CultureInfo.InvariantCulture));
    }
    public IReadOnlyList<WeaponStats> DraftWeapons()
    {
        var stats = EffectiveStats();
        return EquipmentNodes.Where(e => e.Name == "weapon" && (string?)e.Element("equipped") == "True").Select(e => {
            var skill = EquipmentRules.Skill(e); var raw = e.Element("damage")!.Value; var damage = Regex.Match(raw, @"^(\d+|\(\{STR\}\+(\d+)\)|\{STR\})([PS])$");
            if (!damage.Success) throw new InvalidOperationException("Unsupported weapon damage expression.");
            var value = damage.Groups[1].Value.Contains("STR") ? stats.Attributes["STR"] + (damage.Groups[2].Success ? int.Parse(damage.Groups[2].Value) : 0) : int.Parse(damage.Groups[1].Value);
            var category = e.Element("category")!.Value;
            value += (int)DraftEffect("WeaponCategoryDV", category);
            var accuracy = (string?)e.Element("accuracy") == "Physical" ? stats.PhysicalLimit : (int)e.Element("accuracy")!;
            accuracy += (int)DraftEffect("WeaponSkillAccuracy", skill);
            accuracy+=AccessoryBonus(e,"accuracy");
            return new WeaponStats(e.Element("name")!.Value, skill, value + damage.Groups[3].Value, accuracy, (string?)e.Element("ap") ?? "0",
                (string?)e.Element("mode") ?? "", (string?)e.Element("ammo") ?? "", ((int?)e.Element("reach") ?? 0) + (int)DraftEffect("Reach"),AccessoryBonus(e,"rc"),AccessoryBonus(e,"rc",true));
        }).ToArray();
    }
}
