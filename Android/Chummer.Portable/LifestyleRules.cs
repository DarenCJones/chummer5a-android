// SPDX-License-Identifier: GPL-3.0-or-later
using System.Globalization;
using System.Xml;
using System.Xml.Linq;

namespace Chummer.Portable;

public sealed record LifestyleEntry(string Id, string Name, decimal Cost, int Dice, decimal Multiplier);
public sealed record DraftLifestyle(string Id, string SourceId, string Name, string Base, int Months,
    int Roommates, bool Split, string City, decimal MonthlyCost, decimal PurchaseCost);

public sealed class LifestyleRules
{
    private readonly Dictionary<string,XElement> entries;
    public LifestyleRules(Stream data)
    {
        using var reader = XmlReader.Create(data, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 32*1024*1024 });
        var xml = XDocument.Load(reader);
        if (xml.Root?.Name != "chummer") throw new InvalidDataException("Invalid lifestyle catalog.");
        string[] names = ["Street", "Squatter", "Low", "Medium", "High", "Luxury"];
        entries = xml.Root.Element("lifestyles")!.Elements("lifestyle").Where(e => (string?)e.Element("source") == "SR5" && names.Contains((string?)e.Element("name")))
            .ToDictionary(e => e.Element("id")!.Value, e => new XElement(e));
    }
    public IReadOnlyList<LifestyleEntry> Catalog => entries.Values.Select(e => new LifestyleEntry(e.Element("id")!.Value,
        e.Element("name")!.Value, (decimal)e.Element("cost")!, (int)e.Element("dice")!, (decimal)e.Element("multiplier")!)).OrderBy(e => e.Cost).ToArray();
    public decimal Quote(CharacterDocument character, string sourceId, int roommates = 0, bool split = false)
    {
        _ = character.DraftAttributes;
        if (!entries.TryGetValue(sourceId, out var data)) throw new ArgumentException("Choose a supported core lifestyle.");
        if (roommates < 0 || roommates > 100) throw new ArgumentException("Roommates must be 0–100.");
        return character.DraftLifestyleMonthlyCost((decimal)data.Element("cost")!, data.Element("name")!.Value, roommates, split);
    }
    public void Set(CharacterDocument character, string? id, string sourceId, string name, int months, int roommates = 0, bool split = false, string city = "")
    {
        var monthly = Quote(character, sourceId, roommates, split);
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 200) throw new ArgumentException("Enter a lifestyle name of 1–200 characters.");
        if (months < 1 || months > 1200) throw new ArgumentException("Prepaid months must be 1–1200.");
        var existing = id is null ? null : character.DraftLifestyles.SingleOrDefault(l => l.Id == id) ?? throw new ArgumentException("Unknown lifestyle.");
        var cost = monthly * months;
        if (character.Nuyen + (existing?.PurchaseCost ?? 0) < cost) throw new ArgumentException("Not enough Nuyen for this lifestyle.");
        var data = entries[sourceId];
        var node = new XElement("lifestyle", new XElement("guid", id ?? Guid.NewGuid().ToString("D")), new XElement("sourceid", sourceId),
            new XElement("name", name.Trim()), new XElement("baselifestyle", data.Element("name")!.Value), new XElement("cost", data.Element("cost")!.Value),
            new XElement("dice", data.Element("dice")!.Value), new XElement("multiplier", data.Element("multiplier")!.Value), new XElement("lp", data.Element("lp")!.Value),
            new XElement("months", months), new XElement("roommates", roommates), new XElement("splitcostwithroommates", split ? "True" : "False"),
            new XElement("percentage", 100), new XElement("type", "Standard"), new XElement("increment", "Month"), new XElement("trustfund", "False"),
            new XElement("city", city.Trim()), new XElement("district", ""), new XElement("borough", ""), new XElement("source", "SR5"), new XElement("page", data.Element("page")!.Value),
            new XElement("lifestylequalities"), new XElement("notes", ""), new XElement("androidbuiltin", "True"), new XElement("androidpurchasecost", cost));
        var updated = character.Clone(); updated.SetDraftLifestyle(node, existing?.PurchaseCost ?? 0, cost); character.ReplaceDraftWith(updated);
    }
    public void Remove(CharacterDocument character, string id)
    {
        var updated = character.Clone(); updated.RemoveDraftLifestyle(id); character.ReplaceDraftWith(updated);
    }
}

public sealed partial class CharacterDocument
{
    private IEnumerable<XElement> LifestyleNodes => document.Root!.Element("lifestyles")?.Elements("lifestyle") ?? [];
    public IReadOnlyList<DraftLifestyle> DraftLifestyles
    {
        get { RequireDraft(); return LifestyleNodes.Select(e => new DraftLifestyle(e.Element("guid")!.Value, e.Element("sourceid")!.Value,
            e.Element("name")!.Value, e.Element("baselifestyle")!.Value, (int)e.Element("months")!, (int)e.Element("roommates")!,
            (string?)e.Element("splitcostwithroommates") == "True", (string?)e.Element("city") ?? "", LifestyleMonthlyCost(e), (decimal)e.Element("androidpurchasecost")!)).ToArray(); }
    }
    private decimal LifestyleMonthlyCost(XElement e) => DraftLifestyleMonthlyCost((decimal)e.Element("cost")!, e.Element("baselifestyle")!.Value,
        (int)e.Element("roommates")!, (string?)e.Element("splitcostwithroommates") == "True");
    internal decimal DraftLifestyleMonthlyCost(decimal cost, string name, int roommates, bool split)
    {
        var dependents = SelectedQualities.Where(q => q.Name.Contains("Dependent", StringComparison.Ordinal)).Select(q => q.Id).ToHashSet();
        decimal dependent = 0, metatype = 0, other = 1;
        foreach (var effect in document.Root!.Element("improvements")!.Elements().Where(e => (int?)e.Element("enabled") == 1 &&
            (string?)e.Element("improvementttype") is "LifestyleCost" or "BasicLifestyleCost" &&
            (string.IsNullOrEmpty((string?)e.Element("improvedname")) || (string?)e.Element("improvedname") == name)))
        {
            if (!string.IsNullOrEmpty((string?)effect.Element("condition"))) throw new InvalidOperationException("Conditional lifestyle costs need additional rule support.");
            var value = (decimal)effect.Element("val")!;
            if ((string?)effect.Element("improvementsource") == "Quality" && dependents.Contains((string?)effect.Element("sourcename") ?? "")) dependent += value;
            else if ((string?)effect.Element("improvementsource") is "Heritage" or "Metatype" or "Metavariant") metatype += value;
            else other *= 1 + value / 100;
        }
        return Math.Max(0, cost * (1 + roommates / 10m) / (split ? roommates + 1 : 1) * (1 + dependent / 100) * (1 + metatype / 100) * other);
    }
    internal void SetDraftLifestyle(XElement node, decimal refund, decimal cost)
    {
        RequireDraft(); var parent = document.Root!.Element("lifestyles");
        if (parent is null) { parent = new XElement("lifestyles"); document.Root.Add(parent); }
        var old = parent.Elements().SingleOrDefault(e => (string?)e.Element("guid") == (string?)node.Element("guid"));
        if (old is null) parent.Add(node);
        else
        {
            // Retain notes and any unedited fields when updating a supported lifestyle.
            var merged = new XElement(old);
            foreach (var field in node.Elements().Where(e => e.Name.LocalName is not ("notes" or "district" or "borough" or "lifestylequalities"))) merged.SetElementValue(field.Name, field.Value);
            old.ReplaceWith(merged);
        }
        SetField("nuyen", (Nuyen + refund - cost).ToString(CultureInfo.InvariantCulture));
    }
    internal void RemoveDraftLifestyle(string id)
    {
        RequireDraft(); var entry = DraftLifestyles.SingleOrDefault(l => l.Id == id) ?? throw new ArgumentException("Unknown lifestyle.");
        LifestyleNodes.Single(e => (string?)e.Element("guid") == id).Remove();
        SetField("nuyen", (Nuyen + entry.PurchaseCost).ToString(CultureInfo.InvariantCulture));
    }
    internal void RepriceDraftLifestyles()
    {
        RequireDraft(); var prices = LifestyleNodes.Select(e => (Node:e, Cost:LifestyleMonthlyCost(e) * (int)e.Element("months")!)).ToArray();
        var balance = Nuyen + prices.Sum(p => (decimal)p.Node.Element("androidpurchasecost")! - p.Cost);
        if (balance < 0) throw new ArgumentException("Not enough Nuyen for the changed lifestyle costs. Reduce prepaid months or remove a lifestyle first.");
        foreach (var price in prices) price.Node.SetElementValue("androidpurchasecost", price.Cost);
        SetField("nuyen", balance.ToString(CultureInfo.InvariantCulture));
    }
}
