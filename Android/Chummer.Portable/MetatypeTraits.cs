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
            if (!qualities.TryGetValue(reference.Value, out var data)) throw new InvalidDataException($"Missing racial quality: {reference.Value}");
            var id = Guid.NewGuid().ToString("D");
            var quality = new XElement("quality", new XElement("guid", id), new XElement("sourceid", data.Element("id")!.Value),
                new XElement("name", reference.Value), new XElement("extra", ""), new XElement("bp", (string?)data.Element("karma") ?? "0"),
                new XElement("implemented", "True"), new XElement("contributetobp", "False"), new XElement("contributetolimit", "False"),
                new XElement("qualitytype", (string?)data.Element("category") ?? "Positive"), new XElement("qualitysource", "Metatype"),
                new XElement("source", (string?)data.Element("source") ?? "SR5"), new XElement("page", (string?)data.Element("page") ?? ""),
                new XElement("androidbuiltin", "True"), data.Element("bonus") is { } bonus ? new XElement(bonus) : new XElement("bonus"),
                new XElement("firstlevelbonus"), new XElement("naturalweapons"), new XElement("notes", ""));
            root.Element("qualities")!.Add(quality);
            AddBuiltInBonuses(root, data.Element("bonus"), "Quality", id);
        }
    }
    private static void AddBuiltInBonuses(XElement root, XElement? bonus, string source, string sourceName)
    {
        foreach (var item in bonus?.Elements() ?? [])
        {
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
            var improvement = new XElement("improvement", new XElement("target", ""), new XElement("improvedname", ""),
                new XElement("sourcename", sourceName), new XElement("min", 0), new XElement("max", 0), new XElement("aug", 0),
                new XElement("augmax", 0), new XElement("val", value.ToString(CultureInfo.InvariantCulture)), new XElement("rating", 1),
                new XElement("exclude", ""), new XElement("condition", ""), new XElement("improvementttype", type),
                new XElement("improvementsource", source), new XElement("custom", "False"), new XElement("addtorating", 0),
                new XElement("enabled", 1), new XElement("order", 0), new XElement("androidbuiltin", "True"));
            if (type == "Armor" && item.Attribute("group") is { } group) improvement.Add(new XElement("unique", "group" + group.Value));
            root.Element("improvements")!.Add(improvement);
        }
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
        get { RequireDraft(); return document.Root!.Element("qualities")!.Elements("quality").Select(e => e.Element("name")!.Value).ToArray(); }
    }
}
