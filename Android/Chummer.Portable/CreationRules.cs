// SPDX-License-Identifier: GPL-3.0-or-later
using System.Globalization;
using System.Xml;
using System.Xml.Linq;

namespace Chummer.Portable;

public sealed record PriorityChoice(string Method, string Heritage, string Attributes, string Skills, string Resources,
    string Talent = "E", string Path = "Mundane", string Aspect = "Sorcery");
public sealed record CreationBudget(int Attributes, int Special, int Skills, int SkillGroups, decimal Nuyen);
public sealed record DraftAttribute(string Name, int Minimum, int Maximum, int Rating);
public sealed record DraftSkill(string Id, string Name, string Attribute, int Rating, string Group = "", bool Defaultable = false);
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
    private readonly XElement? livingPersona;
    public static IReadOnlyList<string> CoreMetatypes { get; } = Array.AsReadOnly(new[] { "Human", "Elf", "Dwarf", "Ork", "Troll" });
    private readonly XElement[] skills;
    public CreationRules(Stream priorityData, Stream metatypeData, Stream skillData, Stream qualityData, Stream? gearData = null)
    {
        priorities = Read(priorityData);
        metatypes = Read(metatypeData).Root!.Element("metatypes")!.Elements("metatype")
            .Where(e => CoreMetatypes.Contains((string?)e.Element("name") ?? ""))
            .ToDictionary(e => e.Element("name")!.Value, e => new XElement(e));
        qualities = Read(qualityData).Root!.Element("qualities")!.Elements("quality")
            .GroupBy(e => e.Element("name")!.Value).ToDictionary(g => g.Key, g => new XElement(g.First()));
        livingPersona = gearData is null ? null : Read(gearData).Root!.Element("gears")!.Elements("gear")
            .Where(e => (string?)e.Element("name") == "Living Persona").Select(e => new XElement(e)).Single();
        skills = Read(skillData).Root!.Element("skills")!.Elements("skill")
            .Where(e => (string?)e.Element("source") == "SR5" &&
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
        var letters = new[] { choice.Heritage, choice.Attributes, choice.Skills, choice.Resources, choice.Talent };
        if (letters.Any(v => v.Length != 1 || !"ABCDE".Contains(v, StringComparison.Ordinal)))
            throw new ArgumentException("Each priority must be A, B, C, D or E.");
        if (choice.Method == "Priority")
        {
            if (letters.Distinct().Count() != 5) throw new ArgumentException("Priority creation must use A, B, C, D and E exactly once.");
        }
        else if (choice.Method == "SumtoTen")
        {
            if (letters.Sum(Weight) != 10) throw new ArgumentException("Sum-to-Ten priorities must total 10.");
        }
        else throw new ArgumentException("Choose Priority or SumtoTen.");
        _ = TalentData(choice.Talent, choice.Path);
        if (choice.Path == "Aspected Magician" && choice.Aspect is not ("Sorcery" or "Conjuring" or "Enchanting"))
            throw new ArgumentException("Choose Sorcery, Conjuring or Enchanting for an aspected magician.");
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
        if (choice.Path != "Mundane") throw new ArgumentException("Use the talent-aware creation method for awakened or emerged characters.");
        return CreateDraft(name, alias, choice, metatype);
    }
    public CharacterDocument CreateDraft(string name, string alias, PriorityChoice choice, string metatype)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Enter a character name.");
        var budget = Budget(choice, metatype);
        var selectedMetatype = metatypes[metatype];
        var talent = TalentData(choice.Talent, choice.Path);
        var magic = (int?)talent.Element("magic") ?? 0;
        var resonance = (int?)talent.Element("resonance") ?? 0;
        var selectedSkills = skills.Where(s => AllowsSkill(s, choice.Path, choice.Aspect)).ToArray();
        var root = new XElement("character",
            new XElement("createdversion", "5.225.0"), new XElement("appversion", "5.225.0"),
            new XElement("gameedition", "SR5"), new XElement("settings", "default.xml"),
            new XElement("androidcreationdraft", "core-priority-v3"),
            new XElement("androidtalentpriority", choice.Talent), new XElement("androidaspect", choice.Aspect),
            new XElement("androidtalentfreeqty", (int?)talent.Element("skillqty") ?? 0),
            new XElement("androidtalentfreerating", (int?)talent.Element("skillval") ?? 0),
            new XElement("name", name.Trim()), new XElement("alias", alias),
            new XElement("metatype", metatype), new XElement("metatypeid", selectedMetatype.Element("id")!.Value),
            new XElement("metavariant", ""), new XElement("metavariantid", Guid.Empty.ToString("D")),
            new XElement("metatypecategory", "Metahuman"), new XElement("metatypebp", "0"),
            new XElement("created", "False"), new XElement("buildmethod", choice.Method),
            new XElement("prioritymetatype", $"{choice.Heritage},{Weight(choice.Heritage)}"),
            new XElement("priorityattributes", $"{choice.Attributes},{Weight(choice.Attributes)}"),
            new XElement("priorityskills", $"{choice.Skills},{Weight(choice.Skills)}"),
            new XElement("priorityresources", $"{choice.Resources},{Weight(choice.Resources)}"),
            new XElement("priorityspecial", $"{choice.Talent},{Weight(choice.Talent)}"), new XElement("prioritytalent", choice.Path),
            new XElement("spelllimit", (int?)talent.Element("spells") ?? 0), new XElement("cfplimit", (int?)talent.Element("cfp") ?? 0),
            new XElement("gameplayoption", "Standard"), new XElement("sumtoten", "10"),
            new XElement("totalattributes", budget.Attributes), new XElement("totalspecial", budget.Special),
            new XElement("special", budget.Special), new XElement("karma", "25"), new XElement("buildkarma", "25"),
            new XElement("nuyen", budget.Nuyen.ToString(CultureInfo.InvariantCulture)),
            new XElement("startingnuyen", budget.Nuyen.ToString(CultureInfo.InvariantCulture)),
            new XElement("magician", choice.Path is "Magician" or "Mystic Adept" or "Aspected Magician" ? "True" : "False"),
            new XElement("adept", choice.Path is "Adept" or "Mystic Adept" ? "True" : "False"),
            new XElement("technomancer", choice.Path == "Technomancer" ? "True" : "False"),
            new XElement("magenabled", magic > 0 ? "True" : "False"), new XElement("resenabled", resonance > 0 ? "True" : "False"),
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
            if (attribute == "MAG") minimum = magic;
            if (attribute == "RES") minimum = resonance;
            var maximum = Int(selectedMetatype, key + "max");
            root.Element("attributes")!.Add(new XElement("attribute", new XElement("name", attribute),
                new XElement("metatypemin", minimum), new XElement("metatypemax", maximum),
                new XElement("metatypeaugmax", Int(selectedMetatype, key + "aug")),
                new XElement("base", 0), new XElement("karma", 0), new XElement("metatypecategory", "Standard"),
                new XElement("totalvalue", attribute == "ESS" ? 6 : minimum)));
        }
        root.Add(new XElement("newskills", new XElement("skillptsmax", budget.Skills), new XElement("skillgrpsmax", budget.SkillGroups),
            new XElement("skills", selectedSkills.Select(skill => new XElement("skill", new XElement("guid", Guid.NewGuid().ToString("D")),
                new XElement("suid", skill.Element("id")!.Value), new XElement("name", skill.Element("name")!.Value),
                new XElement("isknowledge", "False"), new XElement("skillcategory", skill.Element("category")!.Value),
                new XElement("karma", 0), new XElement("base", 0), new XElement("skillgroup", (string?)skill.Element("skillgroup") ?? ""), new XElement("specs")))),
            new XElement("knoskills"), new XElement("skilljackknowledgeskills"),
            new XElement("groups", selectedSkills.Select(s => (string?)s.Element("skillgroup") ?? "").Where(g => g.Length > 0).Distinct()
                .Select(g => new XElement("group", new XElement("karma", 0), new XElement("base", 0),
                    new XElement("isbroken", "False"), new XElement("id", Guid.NewGuid().ToString("D")), new XElement("name", g))))));
        AddMetatypeTraits(root, selectedMetatype);
        AddTalent(root, talent, choice);
        using var buffer = new MemoryStream(); new XDocument(root).Save(buffer); buffer.Position = 0;
        var result = CharacterDocument.Load(buffer);
        if (result.TalentSkillChoicesRequired > 0)
        {
            var eligible = TalentSkillCandidates(result);
            if (eligible.Count == result.TalentSkillChoicesRequired) AssignTalentSkills(result, eligible.Select(s => s.Name));
        }
        return result;
    }
    public IReadOnlyList<DraftSkill> Skills(CharacterDocument character) => skills
        .Where(e => AllowsSkill(e, character.Field("prioritytalent"), character.Field("androidaspect")) && character.HasDraftSkill(e.Element("id")!.Value))
        .Select(e =>
        new DraftSkill(e.Element("id")!.Value, e.Element("name")!.Value, e.Element("attribute")!.Value,
            character.DraftSkillRating(e.Element("id")!.Value), (string?)e.Element("skillgroup") ?? "", (string?)e.Element("default") == "True")).OrderBy(e => e.Name).ToArray();
}

public sealed partial class CharacterDocument
{
    public bool IsAndroidDraft => Field("androidcreationdraft") is "mundane-human-v1" or "mundane-core-v2" or "core-priority-v3" &&
        CreationRules.CoreMetatypes.Contains(Metatype) && !IsCareer;
    private void RequireDraft()
    {
        if (!IsAndroidDraft) throw new InvalidOperationException("This editor currently supports core priority drafts created in the Android app.");
        if (document.Root!.Element("improvements")?.Elements().Any(e => (string?)e.Element("androidbuiltin") != "True") == true ||
            document.Root.Element("qualities")?.Elements().Any(e => (string?)e.Element("androidbuiltin") != "True") == true ||
            document.Root.Element("gears")?.Elements().Any(e => (string?)e.Element("androidbuiltin") != "True") == true ||
            new[] { "powers", "weapons", "armors", "cyberwares" }.Any(collection => document.Root.Element(collection)?.Elements().Any(e => (string?)e.Element("androidbuiltin") != "True") == true) ||
            new[] { "biowares", "vehicles" }.Any(collection => document.Root.Element(collection)?.HasElements == true))
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
            return AttributeNodes.Where(e => StandardAttributeNames.Contains((string?)e.Element("name")) || (string?)e.Element("name") == "EDG" ||
                    ((string?)e.Element("name") == "MAG" && Field("magenabled") == "True") || ((string?)e.Element("name") == "RES" && Field("resenabled") == "True"))
                .Select(ReadDraftAttribute).ToArray();
        }
    }
    public int AttributePointsRemaining => (int)Number("totalattributes") - DraftAttributes.Where(e => StandardAttributeNames.Contains(e.Name)).Sum(e => e.Rating - e.Minimum);
    public int SpecialPointsRemaining => (int)Number("totalspecial") - DraftAttributes.Where(e => !StandardAttributeNames.Contains(e.Name)).Sum(e => e.Rating - e.Minimum);
    public void SetDraftAttributes(IReadOnlyDictionary<string, int> ratings)
    {
        var available = DraftAttributes.ToDictionary(e => e.Name);
        if (ratings.Keys.Any(k => !available.ContainsKey(k))) throw new ArgumentException("Unknown attribute.");
        var proposed = available.Values.Select(e => e with { Rating = ratings.TryGetValue(e.Name, out var r) ? r : e.Rating }).ToArray();
        if (proposed.Any(e => e.Rating < e.Minimum || e.Rating > e.Maximum)) throw new ArgumentException("An attribute is outside its metatype limits.");
        if(InstalledAugmentations.Count>0 && proposed.Any(a=>a.Name is "MAG" or "RES" && a.Rating<1))throw new InvalidOperationException("Burning Magic or Resonance to zero is not supported yet.");
        if (proposed.Where(e => StandardAttributeNames.Contains(e.Name)).Sum(e => e.Rating - e.Minimum) > Number("totalattributes")) throw new ArgumentException("Not enough attribute points.");
        if (proposed.Where(e => !StandardAttributeNames.Contains(e.Name)).Sum(e => e.Rating - e.Minimum) > Number("totalspecial")) throw new ArgumentException("Not enough special attribute points.");
        if (proposed.Count(e => StandardAttributeNames.Contains(e.Name) && e.Rating == e.Maximum) > 1) throw new ArgumentException("Only one physical or mental attribute may start at its natural maximum under the standard rules.");
        var knowledgeBudget = 2 * (proposed.Single(e => e.Name == "INT").Rating + proposed.Single(e => e.Name == "LOG").Rating);
        if (DraftKnowledgeSkills.Sum(k => k.Native ? 0 : k.Rating) > knowledgeBudget)
            throw new ArgumentException("Lower knowledge or language ratings before reducing their point budget.");
        if (Contacts.Sum(c => c.Connection + c.Loyalty) > 3 * proposed.Single(e => e.Name == "CHA").Rating)
            throw new ArgumentException("Lower contact costs before reducing Charisma.");
        ValidatePowerConstraints(proposed.ToDictionary(a => a.Name, a => a.Rating));
        foreach (var a in proposed)
        {
            var node = AttributeNodes.Single(e => (string?)e.Element("name") == a.Name);
            node.SetElementValue("base", a.Rating - a.Minimum); node.SetElementValue("totalvalue", a.Rating);
        }
        SetField("special", SpecialPointsRemaining.ToString(CultureInfo.InvariantCulture));
    }
    private IEnumerable<XElement> SkillNodes => document.Root!.Element("newskills")!.Element("skills")!.Elements("skill");
    internal bool HasDraftSkill(string id) => document.Root!.Element("newskills")?.Element("skills")?.Elements("skill")
        .Any(e => (string?)e.Element("suid") == id) == true;
    private IEnumerable<XElement> GroupNodes => document.Root!.Element("newskills")!.Element("groups")!.Elements("group");
    internal int DraftSkillRating(string id)
    {
        var skill = SkillNodes.Single(e => (string?)e.Element("suid") == id);
        var group = (string?)skill.Element("skillgroup");
        return ((int?)skill.Element("base") ?? 0) + FreeSkillRating((string?)skill.Element("name") ?? "") + (group is not null ? GroupRating(group) : 0);
    }
    private int GroupRating(string name) => ((int?)GroupNodes.SingleOrDefault(e => (string?)e.Element("name") == name)?.Element("base") ?? 0) + FreeGroupRating(name);
    public IReadOnlyList<DraftSkillGroup> DraftGroups
    {
        get { RequireDraft(); return GroupNodes.Select(g => new DraftSkillGroup(g.Element("name")!.Value, GroupRating(g.Element("name")!.Value))).OrderBy(g => g.Name).ToArray(); }
    }
    public int GroupPointsRemaining => ((int?)document.Root!.Element("newskills")?.Element("skillgrpsmax") ?? 0) - GroupNodes.Sum(g => (int?)g.Element("base") ?? 0);
    public void SetDraftGroup(string name, int rating)
    {
        RequireDraft();
        if (rating is < 0 or > 6) throw new ArgumentException("Group rating must be from 0 to 6.");
        var group = GroupNodes.SingleOrDefault(g => (string?)g.Element("name") == name) ?? throw new ArgumentException("Unknown skill group.");
        if (rating > 0 && SkillNodes.Any(s => (string?)s.Element("skillgroup") == name && ((int?)s.Element("base") ?? 0) > 0))
            throw new ArgumentException("Reset individual member skills before allocating group points.");
        if (SkillNodes.Any(s => (string?)s.Element("skillgroup") == name && FreeSkillRating(s.Element("name")!.Value) + rating > 6))
            throw new ArgumentException("This group rating would exceed a member skill's creation limit.");
        var current = (int?)group.Element("base") ?? 0;
        var free = FreeGroupRating(name);
        if (rating < free) throw new ArgumentException($"This talent grants a free group rating of {free}.");
        if (rating - free - current > GroupPointsRemaining) throw new ArgumentException("Not enough skill group points.");
        var memberRatings = SkillNodes.ToDictionary(s => s.Element("name")!.Value, s => DraftSkillRating(s.Element("suid")!.Value) +
            ((string?)s.Element("skillgroup") == name ? rating - GroupRating(name) : 0));
        ValidatePowerConstraints(skillRatings: memberRatings);
        group.SetElementValue("base", rating - free);
    }
    public int SkillPointsRemaining => ((int?)document.Root!.Element("newskills")?.Element("skillptsmax") ?? 0) -
        (document.Root!.Element("newskills")?.Element("skills")?.Elements("skill").Sum(e => (int?)e.Element("base") ?? 0) ?? 0) - SpecializationPointCost;
    public void SetDraftSkill(string id, int rating)
    {
        RequireDraft();
        if (rating is < 0 or > 6) throw new ArgumentException("Skill rating must be from 0 to 6.");
        var node = document.Root!.Element("newskills")!.Element("skills")!.Elements("skill").SingleOrDefault(e => (string?)e.Element("suid") == id)
            ?? throw new ArgumentException("Unknown skill.");
        var current = (int?)node.Element("base") ?? 0;
        if (GroupRating((string?)node.Element("skillgroup") ?? "") > 0)
            throw new ArgumentException("This skill follows its group rating. Edit the group to change the rating.");
        var free = FreeSkillRating(node.Element("name")!.Value);
        if (rating < free) throw new ArgumentException($"This talent grants a free skill rating of {free}.");
        if (rating - free - current > SkillPointsRemaining) throw new ArgumentException("Not enough skill points.");
        var proposedSkills = SkillNodes.ToDictionary(s => s.Element("name")!.Value, s => s == node ? rating : DraftSkillRating(s.Element("suid")!.Value));
        ValidatePowerConstraints(skillRatings: proposedSkills);
        node.SetElementValue("base", rating - free);
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
        if (native && existing.Count(k => k.Id != id && k.Native) >= 1 + DraftEffect("NativeLanguageLimit")) throw new ArgumentException("The character's native-language limit has been reached.");
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
