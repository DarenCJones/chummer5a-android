// SPDX-License-Identifier: GPL-3.0-or-later
using System.Globalization;
using System.Xml.Linq;

namespace Chummer.Portable;

public sealed partial class CreationRules
{
    private void AddMetatypeTraits(XElement root, XElement metatype)
    {
        AddBuiltInBonuses(root, metatype.Element("bonus"), "Metatype", metatype.Element("id")!.Value);
        foreach (var reference in metatype.Element("qualities")?.Elements().SelectMany(e => e.Elements("quality")) ?? [])
        {
            AddBuiltInQuality(root, reference.Value, "Metatype");
        }
    }
    private string AddBuiltInQuality(XElement root, string name, string origin, string extra = "")
    {
            if (!qualities.TryGetValue(name, out var data)) throw new InvalidDataException($"Missing quality: {name}");
            var id = Guid.NewGuid().ToString("D");
            var quality = new XElement("quality", new XElement("guid", id), new XElement("sourceid", data.Element("id")!.Value),
                new XElement("name", name), new XElement("extra", extra), new XElement("bp", (string?)data.Element("karma") ?? "0"),
                new XElement("implemented", "True"), new XElement("contributetobp", "False"), new XElement("contributetolimit", "False"),
                new XElement("qualitytype", (string?)data.Element("category") ?? "Positive"), new XElement("qualitysource", origin),
                new XElement("source", (string?)data.Element("source") ?? "SR5"), new XElement("page", (string?)data.Element("page") ?? ""),
                new XElement("androidbuiltin", "True"), data.Element("bonus") is { } bonus ? new XElement(bonus) : new XElement("bonus"),
                new XElement("firstlevelbonus"), new XElement("naturalweapons"), new XElement("notes", ""));
            root.Element("qualities")!.Add(quality);
            AddBuiltInBonuses(root, data.Element("bonus"), "Quality", id, extra);
            return id;
    }
    private void AddBuiltInBonuses(XElement root, XElement? bonus, string source, string sourceName, string selection = "")
    {
        foreach (var item in bonus?.Elements() ?? [])
        {
            if (item.Name.LocalName == "enableattribute")
            {
                root.Element("improvements")!.Add(BuiltInImprovement("Attribute", source, sourceName, item.Element("name")!.Value, 0, "enableattribute", rating: 0));
                continue;
            }
            if (item.Name.LocalName == "enabletab")
            {
                foreach (var tab in item.Elements("name"))
                {
                    var title = tab.Value switch { "magician" => "Magician", "adept" => "Adept", "technomancer" => "Technomancer", _ => throw new InvalidDataException("Unsupported talent tab.") };
                    root.Element("improvements")!.Add(BuiltInImprovement("SpecialTab", source, sourceName, title, 0, "enabletab", rating: 0));
                }
                continue;
            }
            if (item.Name.LocalName is "unlockskills" or "blockspelldescriptor" or "limitspellcategory")
            {
                var typeName = item.Name.LocalName switch { "unlockskills" => "SpecialSkills", "blockspelldescriptor" => "BlockSpellDescriptor", _ => "LimitSpellCategory" };
                var name = item.Value.Contains(',') ? selection : item.Value;
                if (name.Length == 0) throw new InvalidDataException("A talent choice is required.");
                root.Element("improvements")!.Add(BuiltInImprovement(typeName, source, sourceName, name, 0));
                continue;
            }
            if (item.Name.LocalName == "specificskill")
            {
                root.Element("improvements")!.Add(BuiltInImprovement("Skill", source, sourceName, item.Element("name")!.Value,
                    decimal.Parse(item.Element("bonus")!.Value, CultureInfo.InvariantCulture), condition: (string?)item.Element("condition") ?? ""));
                continue;
            }
            if (item.Name.LocalName == "addgear")
            {
                if ((string?)item.Element("name") != "Living Persona" || livingPersona is null) throw new InvalidDataException("The living persona catalog is required.");
                var gear = new XElement(livingPersona); var id = Guid.NewGuid().ToString("D");
                gear.Element("id")!.Remove(); gear.SetElementValue("sourceid", livingPersona.Element("id")!.Value);
                gear.SetElementValue("guid", id); gear.SetElementValue("qty", 1); gear.SetElementValue("equipped", "True");
                gear.SetElementValue("parentid", sourceName); gear.SetElementValue("androidbuiltin", "True");
                root.Element("gears")!.Add(gear); root.SetElementValue("activecommlink", id);
                continue;
            }
            var type = item.Name.LocalName switch
            {
                "armor" => "Armor", "reach" => "Reach", "lifestylecost" => "LifestyleCost",
                "pathogencontactresist" => "PathogenContactResist", "pathogeningestionresist" => "PathogenIngestionResist",
                "pathogeninhalationresist" => "PathogenInhalationResist", "pathogeninjectionresist" => "PathogenInjectionResist",
                "toxincontactresist" => "ToxinContactResist", "toxiningestionresist" => "ToxinIngestionResist",
                "toxininhalationresist" => "ToxinInhalationResist", "toxininjectionresist" => "ToxinInjectionResist",
                _ => throw new InvalidDataException($"Racial effect is not supported yet: {item.Name.LocalName}")
            };
            if (!decimal.TryParse(item.Value, NumberStyles.Number, CultureInfo.InvariantCulture, out var value))
                throw new InvalidDataException("A racial bonus requires expression support that is not implemented yet.");
            var improvement = BuiltInImprovement(type, source, sourceName, "", value);
            if (type == "Armor" && item.Attribute("group") is { } group) improvement.Add(new XElement("unique", "group" + group.Value));
            root.Element("improvements")!.Add(improvement);
        }
    }
    internal static XElement BuiltInImprovement(string type, string source, string sourceName, string name, decimal value,
        string unique = "", string condition = "", int rating = 1)
    {
        var improvement = new XElement("improvement", new XElement("target", ""), new XElement("improvedname", name),
                new XElement("sourcename", sourceName), new XElement("min", 0), new XElement("max", 0), new XElement("aug", 0),
                new XElement("augmax", 0), new XElement("val", value.ToString(CultureInfo.InvariantCulture)), new XElement("rating", rating),
                new XElement("exclude", ""), new XElement("condition", condition), new XElement("improvementttype", type),
                new XElement("improvementsource", source), new XElement("custom", "False"), new XElement("addtorating", 0),
                new XElement("enabled", 1), new XElement("order", 0), new XElement("androidbuiltin", "True"));
        if (unique.Length > 0) improvement.Add(new XElement("unique", unique));
        return improvement;
    }
}

public sealed partial class CharacterDocument
{
    public decimal DraftRacialBonus(string type)
    {
        RequireDraft();
        return document.Root!.Element("improvements")!.Elements("improvement")
            .Where(e => (string?)e.Element("androidbuiltin") == "True" && (string?)e.Element("improvementttype") == type && (int?)e.Element("enabled") == 1)
            .Sum(e => decimal.Parse(e.Element("val")!.Value, CultureInfo.InvariantCulture));
    }
    public IReadOnlyList<string> DraftRacialQualities
    {
        get { RequireDraft(); return document.Root!.Element("qualities")!.Elements("quality").Where(e => (string?)e.Element("qualitysource") == "Metatype").Select(e => e.Element("name")!.Value).ToArray(); }
    }
}
