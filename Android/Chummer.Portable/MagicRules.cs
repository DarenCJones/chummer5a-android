// SPDX-License-Identifier: GPL-3.0-or-later
using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using System.Xml.XPath;

namespace Chummer.Portable;

public sealed record ArcaneCatalogEntry(string Id, string Name, string Category, string Duration, string Difficulty,
    string Source, string Page, bool RequiresExtra, string[] ExtraOptions);
public sealed record LearnedAbility(string Id, string Name, string Extra, string Category, string Difficulty, bool Preparation);

public sealed class MagicRules
{
    private readonly Dictionary<string, XElement> spells;
    private readonly Dictionary<string, XElement> forms;
    private readonly XElement[] traditions;
    private readonly XElement stream;
    public MagicRules(Stream spellData, Stream formData, Stream traditionData, Stream streamData)
    {
        spells = Read(spellData).Root!.Element("spells")!.Elements("spell").Where(e => (string?)e.Element("source") == "SR5")
            .ToDictionary(e => e.Element("id")!.Value, e => new XElement(e));
        forms = Read(formData).Root!.Element("complexforms")!.Elements("complexform").Where(e => (string?)e.Element("source") == "SR5")
            .ToDictionary(e => e.Element("id")!.Value, e => new XElement(e));
        traditions = Read(traditionData).Root!.Element("traditions")!.Elements("tradition")
            .Where(e => (string?)e.Element("name") is "Hermetic" or "Shamanic").Select(e => new XElement(e)).ToArray();
        stream = new XElement(Read(streamData).Root!.Element("traditions")!.Elements("tradition").Single(e => (string?)e.Element("name") == "Default"));
    }
    private static XDocument Read(Stream data)
    {
        using var reader = XmlReader.Create(data, new XmlReaderSettings
        { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 32 * 1024 * 1024 });
        var result = XDocument.Load(reader);
        if (result.Root?.Name != "chummer") throw new InvalidDataException("Invalid magic catalog.");
        return result;
    }
    private static ArcaneCatalogEntry Entry(XElement data, bool form)
    {
        var attributeChoice = data.Element("bonus")?.Element("selectattribute");
        var textChoice = data.Element("bonus")?.Element("selecttext");
        var choices = attributeChoice is not null ? new[] { "BOD", "AGI", "REA", "STR", "CHA", "INT", "LOG", "WIL" }
            : form && textChoice is not null ? new[] { "Attack", "Sleaze", "Data Processing", "Firewall" } : [];
        return new ArcaneCatalogEntry(data.Element("id")!.Value, data.Element("name")!.Value,
            (string?)data.Element(form ? "target" : "category") ?? "", (string?)data.Element("duration") ?? "",
            (string?)data.Element(form ? "fv" : "dv") ?? "", (string?)data.Element("source") ?? "",
            (string?)data.Element("page") ?? "", attributeChoice is not null || textChoice is not null, choices);
    }
    private static bool CanLearnSpell(CharacterDocument c, XElement spell, bool preparation)
    {
        if (c.Field("magenabled") != "True") return false;
        var path = c.Field("prioritytalent"); var category = (string?)spell.Element("category");
        if (preparation) return category != "Rituals" && (path is "Magician" or "Mystic Adept" || path == "Aspected Magician" && c.Field("androidaspect") == "Enchanting");
        return path is "Magician" or "Mystic Adept" || path == "Aspected Magician" && c.Field("androidaspect") == "Sorcery" || path == "Adept" && category == "Rituals";
    }
    public IReadOnlyList<ArcaneCatalogEntry> Spells(CharacterDocument character, bool preparations = false) => spells.Values
        .Where(e => CanLearnSpell(character, e, preparations)).Select(e => Entry(e, false)).OrderBy(e => e.Name).ToArray();
    public IReadOnlyList<ArcaneCatalogEntry> ComplexForms(CharacterDocument character) => character.Field("technomancer") == "True"
        ? forms.Values.Select(e => Entry(e, true)).OrderBy(e => e.Name).ToArray() : [];
    public IReadOnlyList<string> Traditions(CharacterDocument character) => character.Field("technomancer") == "True" ? ["Default"]
        : character.Field("magenabled") == "True" ? traditions.Select(t => t.Element("name")!.Value).ToArray() : [];
    public void SetTradition(CharacterDocument character, string name)
    {
        _ = character.DraftAttributes;
        var resonance = character.Field("technomancer") == "True";
        if (!Traditions(character).Contains(name)) throw new ArgumentException("This tradition or stream is not available.");
        var data = resonance ? stream : traditions.Single(t => (string?)t.Element("name") == name);
        if (data.Element("bonus")?.HasElements == true) throw new InvalidDataException("Tradition effects require additional rule support.");
        var tradition = new XElement("tradition", new XElement("sourceid", data.Element("id")!.Value),
            new XElement("guid", Guid.NewGuid().ToString("D")), new XElement("traditiontype", resonance ? "RES" : "MAG"),
            new XElement("name", name), new XElement("extra", ""), new XElement("spiritform", "Materialization"),
            new XElement("drain", data.Element("drain")!.Value), new XElement("source", (string?)data.Element("source") ?? "SR5"),
            new XElement("page", (string?)data.Element("page") ?? ""), new XElement("bonus"), new XElement("androidbuiltin", "True"));
        var spirits = new List<string>();
        foreach (var category in new[] { "combat", "detection", "health", "illusion", "manipulation" })
        {
            var spirit = (string?)data.Element("spirits")?.Element("spirit" + category) ?? "";
            tradition.Add(new XElement("spirit" + category, spirit)); if (spirit.Length > 0) spirits.Add(spirit);
        }
        spirits.AddRange(data.Element("spirits")?.Elements("spirit").Select(e => e.Value) ?? []);
        tradition.Add(new XElement("spirits", spirits.Distinct().Select(s => new XElement("spirit", s))));
        character.SetDraftTradition(tradition);
    }
    public void LearnSpell(CharacterDocument character, string id, string extra = "", bool preparation = false)
    {
        if (!spells.TryGetValue(id, out var data) || !CanLearnSpell(character, data, preparation)) throw new ArgumentException("This spell or preparation is not available to the selected talent.");
        var entry = Entry(data, false); extra = ValidateExtra(entry, extra);
        var spell = new XElement("spell", new XElement("guid", Guid.NewGuid().ToString("D")), new XElement("sourceid", id));
        foreach (var field in new[] { "name", "category", "type", "range", "damage", "elements", "duration", "dv", "source", "page" })
            spell.Add(new XElement(field, (string?)data.Element(field) ?? ""));
        spell.Add(new XElement("descriptors", (string?)data.Element("descriptor") ?? ""), new XElement("limited", "False"),
            new XElement("extended", "False"), new XElement("customextended", "False"), new XElement("alchemical", preparation ? "True" : "False"),
            new XElement("freebonus", "False"), new XElement("barehandedadept", "False"), new XElement("extra", extra), new XElement("notes", ""), new XElement("androidbuiltin", "True"));
        var improvements = new List<XElement>();
        foreach (var bonus in data.Element("bonus")?.Elements() ?? [])
        {
            if (bonus.Name.LocalName is "selecttext" or "selectattribute") continue;
            if (bonus.Name.LocalName != "addspirit" || bonus.Elements("spirit").Count() != 1 || bonus.HasAttributes)
                throw new InvalidDataException("This spell has effects that require additional rule support.");
            var spirit = bonus.Element("spirit")!.Value;
            improvements.Add(CreationRules.BuiltInImprovement("AddSpirit", "Spell", spell.Element("guid")!.Value, spirit, 0));
            if (!string.Equals((string?)bonus.Element("addtoselected"), "False", StringComparison.OrdinalIgnoreCase)) spell.SetElementValue("extra", spirit);
        }
        character.AddDraftAbility("spells", spell, (int.Parse(character.Field("spelllimit"), CultureInfo.InvariantCulture)), 5, improvements);
    }
    public void LearnComplexForm(CharacterDocument character, string id, string extra = "")
    {
        if (character.Field("technomancer") != "True" || !forms.TryGetValue(id, out var data)) throw new ArgumentException("This complex form is not available.");
        extra = ValidateExtra(Entry(data, true), extra);
        var form = new XElement("complexform", new XElement("guid", Guid.NewGuid().ToString("D")), new XElement("sourceid", id));
        foreach (var field in new[] { "name", "target", "duration", "fv", "source", "page" }) form.Add(new XElement(field, (string?)data.Element(field) ?? ""));
        form.Add(new XElement("extra", extra), new XElement("grade", 0), new XElement("notes", ""), new XElement("androidbuiltin", "True"));
        character.AddDraftAbility("complexforms", form, int.Parse(character.Field("cfplimit"), CultureInfo.InvariantCulture), 4, []);
    }
    private static string ValidateExtra(ArcaneCatalogEntry entry, string extra)
    {
        extra = extra.Trim();
        if (entry.RequiresExtra && extra.Length == 0) throw new ArgumentException("Enter the spell or form's required choice.");
        if (!entry.RequiresExtra && extra.Length > 0) throw new ArgumentException("This ability does not require an extra choice.");
        if (entry.ExtraOptions.Length > 0) return entry.ExtraOptions.FirstOrDefault(e => e.Equals(extra, StringComparison.OrdinalIgnoreCase))
            ?? throw new ArgumentException("Choose one of the listed options.");
        return extra;
    }
    public static int EvaluateDifficulty(string expression, int forceOrLevel)
    {
        if (forceOrLevel is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(forceOrLevel));
        if (!Regex.IsMatch(expression, @"^[FL](?:\s*[+-]\s*\d+)?$")) throw new InvalidOperationException("This ability uses a special difficulty rule; consult its sourcebook.");
        var arithmetic = Regex.Replace(expression, "[FL]", forceOrLevel.ToString(CultureInfo.InvariantCulture));
        return Math.Max(2, (int)(double)new XDocument(new XElement("x")).XPathEvaluate(arithmetic));
    }
}

public sealed partial class CharacterDocument
{
    internal void SetDraftTradition(XElement tradition)
    {
        RequireDraft();
        var current = document.Root!.Element("tradition");
        if (current is null) document.Root.Add(tradition); else current.ReplaceWith(tradition);
    }
    public string TraditionName => (string?)document.Root!.Element("tradition")?.Element("name") ?? "";
    public int DraftDrainPool()
    {
        var attributes = DraftAttributes.ToDictionary(a => a.Name, a => a.Rating);
        var expression = (string?)document.Root!.Element("tradition")?.Element("drain") ?? throw new InvalidOperationException("Select a tradition or stream first.");
        if (!Regex.IsMatch(expression, @"^\{WIL\}\s*\+\s*\{(?:LOG|CHA|RES)\}$")) throw new InvalidOperationException("This drain expression needs additional rule support.");
        var keys = Regex.Matches(expression, @"\{([A-Z]+)\}").Select(m => m.Groups[1].Value);
        return keys.Sum(key => attributes[key]);
    }
    public IReadOnlyList<LearnedAbility> LearnedSpells => Abilities("spells", "spell", false);
    public IReadOnlyList<LearnedAbility> LearnedForms => Abilities("complexforms", "complexform", true);
    private IReadOnlyList<LearnedAbility> Abilities(string collection, string item, bool forms) =>
        (document.Root!.Element(collection)?.Elements(item) ?? []).Select(e => new LearnedAbility((string?)e.Element("guid") ?? "",
            (string?)e.Element("name") ?? "", (string?)e.Element("extra") ?? "", (string?)e.Element(forms ? "target" : "category") ?? "",
            (string?)e.Element(forms ? "fv" : "dv") ?? "", (string?)e.Element("alchemical") == "True")).ToArray();
    internal void AddDraftAbility(string collection, XElement ability, int freeLimit, int karmaCost, IEnumerable<XElement> improvements)
    {
        RequireDraft();
        var parent = document.Root!.Element(collection);
        if (parent?.Elements().Any(e => (string?)e.Element("sourceid") == (string?)ability.Element("sourceid") &&
            ((string?)e.Element("extra") ?? "") == ((string?)ability.Element("extra") ?? "") &&
            ((string?)e.Element("alchemical") ?? "False") == ((string?)ability.Element("alchemical") ?? "False")) == true)
            throw new ArgumentException("This ability and choice are already learned.");
        var count = parent?.Elements().Count() ?? 0;
        var cost = count >= freeLimit ? karmaCost : 0;
        if (Karma < cost) throw new InvalidOperationException("Not enough Karma after using the free priority allowance.");
        var records = improvements.ToArray();
        if (parent is null) { parent = new XElement(collection); document.Root.Add(parent); }
        parent.Add(ability); document.Root.Element("improvements")!.Add(records);
        SetField("karma", (Karma - cost).ToString(CultureInfo.InvariantCulture));
    }
    public void RemoveDraftAbility(string collection, string id)
    {
        RequireDraft();
        if (collection is not ("spells" or "complexforms")) throw new ArgumentException("Unknown ability collection.");
        var parent = document.Root!.Element(collection) ?? throw new ArgumentException("Unknown ability.");
        var ability = parent.Elements().SingleOrDefault(e => (string?)e.Element("guid") == id) ?? throw new ArgumentException("Unknown ability.");
        var limit = (int)Number(collection == "spells" ? "spelllimit" : "cfplimit");
        var refund = parent.Elements().Count() > limit ? collection == "spells" ? 5 : 4 : 0;
        ability.Remove();
        document.Root.Element("improvements")!.Elements("improvement").Where(e => (string?)e.Element("sourcename") == id &&
            (string?)e.Element("androidbuiltin") == "True").Remove();
        SetField("karma", (Karma + refund).ToString(CultureInfo.InvariantCulture));
    }
}
