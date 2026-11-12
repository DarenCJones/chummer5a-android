// SPDX-License-Identifier: GPL-3.0-or-later
using System.Globalization;
using System.Xml;
using System.Xml.Linq;

namespace Chummer.Portable;

public sealed record PriorityChoice(string Method, string Heritage, string Attributes, string Skills, string Resources);
public sealed record CreationBudget(int Attributes, int Special, int Skills, int SkillGroups, decimal Nuyen);
public sealed record DraftAttribute(string Name, int Minimum, int Maximum, int Rating);
public sealed record DraftSkill(string Id, string Name, string Attribute, int Rating, string Group = "");
public sealed record DraftSkillGroup(string Name, int Rating);
public sealed record DraftKnowledge(string Id, string Name, string Type, int Rating, bool Native);
public sealed record UnaugmentedStats(int PhysicalLimit, int MentalLimit, int SocialLimit, int Initiative,
    int PhysicalBoxes, int StunBoxes, int KnowledgePoints, int ContactPoints);

/// <summary>Core mundane metahuman drafts under the standard priority table.</summary>
public sealed partial class CreationRules
{
    private readonly XDocument priorities;
    private readonly Dictionary<string, XElement> metatypes;
    private readonly Dictionary<string, XElement> qualities;
    public static IReadOnlyList<string> CoreMetatypes { get; } = Array.AsReadOnly(new[] { "Human", "Elf", "Dwarf", "Ork", "Troll" });
    private readonly XElement[] skills;
    public CreationRules(Stream priorityData, Stream metatypeData, Stream skillData, Stream qualityData)
    {
        priorities = Read(priorityData);
        metatypes = Read(metatypeData).Root!.Element("metatypes")!.Elements("metatype")
            .Where(e => CoreMetatypes.Contains((string?)e.Element("name") ?? ""))
            .ToDictionary(e => e.Element("name")!.Value, e => new XElement(e));
        qualities = Read(qualityData).Root!.Element("qualities")!.Elements("quality")
            .GroupBy(e => e.Element("name")!.Value).ToDictionary(g => g.Key, g => new XElement(g.First()));
        skills = Read(skillData).Root!.Element("skills")!.Elements("skill")
            .Where(e => (string?)e.Element("source") == "SR5" &&
                !((string?)e.Element("category") ?? "").Contains("Magical", StringComparison.OrdinalIgnoreCase) &&
                (string?)e.Element("category") != "Resonance Active" &&
                !string.Equals((string?)e.Element("exotic"), "True", StringComparison.OrdinalIgnoreCase))
            .Select(e => new XElement(e)).ToArray();
    }
    private static XDocument Read(Stream source)
    {
        using var reader = XmlReader.Create(source, new XmlReaderSettings
        { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 32 * 1024 * 1024 });
        var document = XDocument.Load(reader);
        if (document.Root?.Name != "chummer") throw new InvalidDataException("Invalid creation data.");
        return document;
    }
    private XElement Priority(string category, string value) => priorities.Root!.Element("priorities")!.Elements("priority")
        .Single(e => (string?)e.Element("category") == category && (string?)e.Element("value") == value &&
            (e.Element("prioritytable") is null || (string?)e.Element("prioritytable") == "Standard"));
    private int Weight(string letter) => int.Parse(priorities.Root!.Element("priortysumtotenvalues")!.Element(letter)!.Value, CultureInfo.InvariantCulture);
    private static int Int(XElement e, string field) => int.Parse(e.Element(field)!.Value, CultureInfo.InvariantCulture);
    public CreationBudget Budget(PriorityChoice choice, string metatype = "Human")
    {
        var letters = new[] { choice.Heritage, choice.Attributes, choice.Skills, choice.Resources, "E" };
        if (letters.Any(v => v.Length != 1 || !"ABCDE".Contains(v, StringComparison.Ordinal)))
            throw new ArgumentException("Each priority must be A, B, C, D or E.");
        if (choice.Method == "Priority")
        {
            if (letters.Distinct().Count() != 5) throw new ArgumentException("Priority creation must use A, B, C, D and E exactly once. Talent is E for a mundane draft.");
        }
        else if (choice.Method == "SumtoTen")
        {
            if (letters.Sum(Weight) != 10) throw new ArgumentException("Sum-to-Ten priorities must total 10. Talent is E for a mundane draft.");
        }
        else throw new ArgumentException("Choose Priority or SumtoTen.");
        if (!CoreMetatypes.Contains(metatype)) throw new ArgumentException("Choose a supported core metatype.");
        var heritage = Priority("Heritage", choice.Heritage).Element("metatypes")!.Elements("metatype")
            .SingleOrDefault(e => (string?)e.Element("name") == metatype)
            ?? throw new ArgumentException($"{metatype} is not available at heritage priority {choice.Heritage}.");
        var skill = Priority("Skills", choice.Skills);
        return new CreationBudget(Int(Priority("Attributes", choice.Attributes), "attributes"), Int(heritage, "value"),
            Int(skill, "skills"), Int(skill, "skillgroups"), decimal.Parse(Priority("Resources", choice.Resources).Element("resources")!.Value, CultureInfo.InvariantCulture));
    }
    public CharacterDocument CreateHumanDraft(string name, string alias, PriorityChoice choice) => CreateMundaneDraft(name, alias, choice, "Human");
    public CharacterDocument CreateMundaneDraft(string name, string alias, PriorityChoice choice, string metatype)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Enter a character name.");
        var budget = Budget(choice, metatype);
        var selectedMetatype = metatypes[metatype];
        var root = new XElement("character",
            new XElement("createdversion", "5.225.0"), new XElement("appversion", "5.225.0"),
            new XElement("gameedition", "SR5"), new XElement("settings", "default.xml"),
            new XElement("androidcreationdraft", "mundane-core-v2"),
            new XElement("name", name.Trim()), new XElement("alias", alias),
            new XElement("metatype", metatype), new XElement("metatypeid", selectedMetatype.Element("id")!.Value),
            new XElement("metavariant", ""), new XElement("metavariantid", Guid.Empty.ToString("D")),
            new XElement("metatypecategory", "Metahuman"), new XElement("metatypebp", "0"),
            new XElement("created", "False"), new XElement("buildmethod", choice.Method),
            new XElement("prioritymetatype", $"{choice.Heritage},{Weight(choice.Heritage)}"),
            new XElement("priorityattributes", $"{choice.Attributes},{Weight(choice.Attributes)}"),
            new XElement("priorityskills", $"{choice.Skills},{Weight(choice.Skills)}"),
            new XElement("priorityresources", $"{choice.Resources},{Weight(choice.Resources)}"),
            new XElement("priorityspecial", "E,0"), new XElement("prioritytalent", "Mundane"),
            new XElement("gameplayoption", "Standard"), new XElement("sumtoten", "10"),
            new XElement("totalattributes", budget.Attributes), new XElement("totalspecial", budget.Special),
            new XElement("special", budget.Special), new XElement("karma", "25"), new XElement("buildkarma", "25"),
            new XElement("nuyen", budget.Nuyen.ToString(CultureInfo.InvariantCulture)),
            new XElement("startingnuyen", budget.Nuyen.ToString(CultureInfo.InvariantCulture)),
            new XElement("magician", "False"), new XElement("adept", "False"), new XElement("technomancer", "False"),
            new XElement("attributes"), new XElement("qualities"), new XElement("improvements"),
            new XElement("expenses"), new XElement("weapons"), new XElement("armors"), new XElement("gears"),
            new XElement("physicalcmfilled", 0), new XElement("stuncmfilled", 0));
        foreach (var field in new[] { "walk", "run", "sprint" })
        {
            root.Add(new XElement(field, selectedMetatype.Element(field)!.Value));
            root.Add(new XElement(field + "alt", selectedMetatype.Element(field)!.Value));
        }
        foreach (var attribute in new[] { "BOD", "AGI", "REA", "STR", "CHA", "INT", "LOG", "WIL", "EDG", "MAG", "MAGAdept", "RES", "ESS", "DEP" })
        {
            var key = attribute == "MAGAdept" ? "mag" : attribute.ToLowerInvariant();
            var minimum = attribute is "MAG" or "MAGAdept" or "RES" or "DEP" ? 0 : Int(selectedMetatype, key + "min");
            var maximum = Int(selectedMetatype, key + "max");
            root.Element("attributes")!.Add(new XElement("attribute", new XElement("name", attribute),
                new XElement("metatypemin", minimum), new XElement("metatypemax", maximum),
                new XElement("metatypeaugmax", Int(selectedMetatype, key + "aug")),
                new XElement("base", 0), new XElement("karma", 0), new XElement("metatypecategory", "Standard"),
                new XElement("totalvalue", attribute == "ESS" ? 6 : minimum)));
        }
        root.Add(new XElement("newskills", new XElement("skillptsmax", budget.Skills), new XElement("skillgrpsmax", budget.SkillGroups),
            new XElement("skills", skills.Select(skill => new XElement("skill", new XElement("guid", Guid.NewGuid().ToString("D")),
                new XElement("suid", skill.Element("id")!.Value), new XElement("name", skill.Element("name")!.Value),
                new XElement("isknowledge", "False"), new XElement("skillcategory", skill.Element("category")!.Value),
                new XElement("karma", 0), new XElement("base", 0), new XElement("skillgroup", (string?)skill.Element("skillgroup") ?? ""), new XElement("specs")))),
            new XElement("knoskills"), new XElement("skilljackknowledgeskills"),
            new XElement("groups", skills.Select(s => (string?)s.Element("skillgroup") ?? "").Where(g => g.Length > 0).Distinct()
                .Select(g => new XElement("group", new XElement("karma", 0), new XElement("base", 0),
                    new XElement("isbroken", "False"), new XElement("id", Guid.NewGuid().ToString("D")), new XElement("name", g))))));
        AddMetatypeTraits(root, selectedMetatype);
        using var buffer = new MemoryStream(); new XDocument(root).Save(buffer); buffer.Position = 0;
        return CharacterDocument.Load(buffer);
    }
    public IReadOnlyList<DraftSkill> Skills(CharacterDocument character) => skills.Select(e =>
        new DraftSkill(e.Element("id")!.Value, e.Element("name")!.Value, e.Element("attribute")!.Value,
            character.DraftSkillRating(e.Element("id")!.Value), (string?)e.Element("skillgroup") ?? "")).OrderBy(e => e.Name).ToArray();
}

public sealed partial class CharacterDocument
{
    public bool IsAndroidDraft => Field("androidcreationdraft") is "mundane-human-v1" or "mundane-core-v2" &&
        CreationRules.CoreMetatypes.Contains(Metatype) && !IsCareer;
    private void RequireDraft()
    {
        if (!IsAndroidDraft) throw new InvalidOperationException("This editor currently supports core mundane drafts created in the Android app.");
        if (document.Root!.Element("improvements")?.Elements().Any(e => (string?)e.Element("androidbuiltin") != "True") == true ||
            document.Root.Element("qualities")?.Elements().Any(e => (string?)e.Element("androidbuiltin") != "True") == true ||
            new[] { "gears", "cyberwares", "biowares", "weapons", "armors", "vehicles", "spells", "powers" }.Any(collection => document.Root.Element(collection)?.HasElements == true))
            throw new InvalidOperationException("This draft has effects that the creation editor cannot yet calculate.");
    }
    private IEnumerable<XElement> AttributeNodes => document.Root!.Element("attributes")!.Elements("attribute");
    private static int AttributeInt(XElement attribute, string field) => (int?)attribute.Element(field) ?? 0;
    private static readonly string[] StandardAttributeNames = ["BOD", "AGI", "REA", "STR", "CHA", "INT", "LOG", "WIL"];
    public IReadOnlyList<DraftAttribute> DraftAttributes
    {
        get
        {
            RequireDraft();
            return AttributeNodes.Where(e => StandardAttributeNames.Contains((string?)e.Element("name")) || (string?)e.Element("name") == "EDG")
                .Select(e => new DraftAttribute(e.Element("name")!.Value, AttributeInt(e, "metatypemin"), AttributeInt(e, "metatypemax"),
                    AttributeInt(e, "metatypemin") + AttributeInt(e, "base") + AttributeInt(e, "karma"))).ToArray();
        }
    }
    public int AttributePointsRemaining => (int)Number("totalattributes") - DraftAttributes.Where(e => e.Name != "EDG").Sum(e => e.Rating - e.Minimum);
    public int SpecialPointsRemaining => (int)Number("totalspecial") - DraftAttributes.Where(e => e.Name == "EDG").Sum(e => e.Rating - e.Minimum);
    public void SetDraftAttributes(IReadOnlyDictionary<string, int> ratings)
    {
        var available = DraftAttributes.ToDictionary(e => e.Name);
        if (ratings.Keys.Any(k => !available.ContainsKey(k))) throw new ArgumentException("Unknown attribute.");
        var proposed = available.Values.Select(e => e with { Rating = ratings.TryGetValue(e.Name, out var r) ? r : e.Rating }).ToArray();
        if (proposed.Any(e => e.Rating < e.Minimum || e.Rating > e.Maximum)) throw new ArgumentException("An attribute is outside its metatype limits.");
        if (proposed.Where(e => e.Name != "EDG").Sum(e => e.Rating - e.Minimum) > Number("totalattributes")) throw new ArgumentException("Not enough attribute points.");
        if (proposed.Single(e => e.Name == "EDG") is var edge && edge.Rating - edge.Minimum > Number("totalspecial")) throw new ArgumentException("Not enough special attribute points.");
        if (proposed.Count(e => e.Name != "EDG" && e.Rating == e.Maximum) > 1) throw new ArgumentException("Only one physical or mental attribute may start at its natural maximum under the standard rules.");
        var knowledgeBudget = 2 * (proposed.Single(e => e.Name == "INT").Rating + proposed.Single(e => e.Name == "LOG").Rating);
        if (DraftKnowledgeSkills.Sum(k => k.Native ? 0 : k.Rating) > knowledgeBudget)
            throw new ArgumentException("Lower knowledge or language ratings before reducing their point budget.");
        if (Contacts.Sum(c => c.Connection + c.Loyalty) > 3 * proposed.Single(e => e.Name == "CHA").Rating)
            throw new ArgumentException("Lower contact costs before reducing Charisma.");
        foreach (var a in proposed)
        {
            var node = AttributeNodes.Single(e => (string?)e.Element("name") == a.Name);
            node.SetElementValue("base", a.Rating - a.Minimum); node.SetElementValue("totalvalue", a.Rating);
        }
        SetField("special", SpecialPointsRemaining.ToString(CultureInfo.InvariantCulture));
    }
    private IEnumerable<XElement> SkillNodes => document.Root!.Element("newskills")!.Element("skills")!.Elements("skill");
    private IEnumerable<XElement> GroupNodes => document.Root!.Element("newskills")!.Element("groups")!.Elements("group");
    internal int DraftSkillRating(string id)
    {
        var skill = SkillNodes.Single(e => (string?)e.Element("suid") == id);
        var group = (string?)skill.Element("skillgroup");
        return ((int?)skill.Element("base") ?? 0) + (group is not null ? GroupRating(group) : 0);
    }
    private int GroupRating(string name) => (int?)GroupNodes.SingleOrDefault(e => (string?)e.Element("name") == name)?.Element("base") ?? 0;
    public IReadOnlyList<DraftSkillGroup> DraftGroups
    {
        get { RequireDraft(); return GroupNodes.Select(g => new DraftSkillGroup(g.Element("name")!.Value, (int?)g.Element("base") ?? 0)).OrderBy(g => g.Name).ToArray(); }
    }
    public int GroupPointsRemaining => ((int?)document.Root!.Element("newskills")?.Element("skillgrpsmax") ?? 0) - DraftGroups.Sum(g => g.Rating);
    public void SetDraftGroup(string name, int rating)
    {
        RequireDraft();
        if (rating is < 0 or > 6) throw new ArgumentException("Group rating must be from 0 to 6.");
        var group = GroupNodes.SingleOrDefault(g => (string?)g.Element("name") == name) ?? throw new ArgumentException("Unknown skill group.");
        if (rating > 0 && SkillNodes.Any(s => (string?)s.Element("skillgroup") == name && ((int?)s.Element("base") ?? 0) > 0))
            throw new ArgumentException("Reset individual member skills before allocating group points.");
        var current = (int?)group.Element("base") ?? 0;
        if (rating - current > GroupPointsRemaining) throw new ArgumentException("Not enough skill group points.");
        group.SetElementValue("base", rating);
    }
    public int SkillPointsRemaining => (int?)document.Root!.Element("newskills")?.Element("skillptsmax") -
        document.Root!.Element("newskills")?.Element("skills")?.Elements("skill").Sum(e => (int?)e.Element("base") ?? 0) ?? 0;
    public void SetDraftSkill(string id, int rating)
    {
        RequireDraft();
        if (rating is < 0 or > 6) throw new ArgumentException("Skill rating must be from 0 to 6.");
        var node = document.Root!.Element("newskills")!.Element("skills")!.Elements("skill").SingleOrDefault(e => (string?)e.Element("suid") == id)
            ?? throw new ArgumentException("Unknown skill.");
        var current = (int?)node.Element("base") ?? 0;
        if (GroupRating((string?)node.Element("skillgroup") ?? "") > 0)
            throw new ArgumentException("Reset this skill's group to zero before allocating individual skill points.");
        if (rating - current > SkillPointsRemaining) throw new ArgumentException("Not enough skill points.");
        node.SetElementValue("base", rating);
    }
    public IReadOnlyList<DraftKnowledge> DraftKnowledgeSkills
    {
        get
        {
            RequireDraft();
            return document.Root!.Element("newskills")!.Element("knoskills")!.Elements("skill")
                .Select(k => new DraftKnowledge(k.Element("guid")!.Value, k.Element("name")!.Value, k.Element("type")!.Value,
                    (int?)k.Element("base") ?? 0, string.Equals((string?)k.Element("isnativelanguage"), "True", StringComparison.OrdinalIgnoreCase)))
                .ToArray();
        }
    }
    public int KnowledgePointsRemaining => DraftStats().KnowledgePoints - DraftKnowledgeSkills.Sum(k => k.Native ? 0 : k.Rating);
    public void SetDraftKnowledge(string? id, string name, string type, int rating, bool native)
    {
        RequireDraft();
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Enter a skill or language name.");
        if (type is not ("Academic" or "Interest" or "Language" or "Professional" or "Street")) throw new ArgumentException("Unknown knowledge category.");
        if (native && type != "Language") throw new ArgumentException("Only a language can be native.");
        if (!native && rating is < 1 or > 6) throw new ArgumentException("Non-native rating must be from 1 to 6.");
        var existing = DraftKnowledgeSkills;
        if (id is not null && !existing.Any(k => k.Id == id)) throw new ArgumentException("Unknown knowledge skill.");
        if (existing.Any(k => k.Id != id && k.Type == type && k.Name.Equals(name.Trim(), StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException("That skill or language already exists.");
        if (native && existing.Any(k => k.Id != id && k.Native)) throw new ArgumentException("The standard rules allow one native language.");
        if (existing.Where(k => k.Id != id).Sum(k => k.Native ? 0 : k.Rating) + (native ? 0 : rating) > DraftStats().KnowledgePoints)
            throw new ArgumentException("Not enough knowledge points.");
        var updated = new XElement("skill", new XElement("guid", id ?? Guid.NewGuid().ToString("D")), new XElement("suid", Guid.Empty.ToString("D")),
            new XElement("name", name.Trim()), new XElement("isknowledge", "True"), new XElement("skillcategory", type),
            new XElement("type", type), new XElement("isnativelanguage", native ? "True" : "False"),
            new XElement("base", native ? 0 : rating), new XElement("karma", 0), new XElement("buywithkarma", "False"));
        var parent = document.Root!.Element("newskills")!.Element("knoskills")!;
        if (id is null) parent.Add(updated);
        else parent.Elements("skill").Single(k => (string?)k.Element("guid") == id).ReplaceWith(updated);
    }
    public void RemoveDraftKnowledge(string id)
    {
        RequireDraft();
        var node = document.Root!.Element("newskills")!.Element("knoskills")!.Elements("skill").SingleOrDefault(k => (string?)k.Element("guid") == id)
            ?? throw new ArgumentException("Unknown knowledge skill.");
        node.Remove();
    }
    public UnaugmentedStats DraftStats()
    {
        var values = DraftAttributes.ToDictionary(e => e.Name, e => e.Rating);
        int CeilingThird(int value) => (value + 2) / 3;
        return new UnaugmentedStats(CeilingThird(2 * values["STR"] + values["BOD"] + values["REA"]),
            CeilingThird(2 * values["LOG"] + values["INT"] + values["WIL"]), CeilingThird(2 * values["CHA"] + values["WIL"] + 6),
            values["REA"] + values["INT"], 8 + (values["BOD"] + 1) / 2, 8 + (values["WIL"] + 1) / 2,
            2 * (values["INT"] + values["LOG"]), 3 * values["CHA"]);
    }
}
