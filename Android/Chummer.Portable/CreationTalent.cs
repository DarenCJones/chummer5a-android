// SPDX-License-Identifier: GPL-3.0-or-later
using System.Xml.Linq;
using System.Xml.XPath;

namespace Chummer.Portable;

public sealed record TalentDefinition(string Path, string Name, int Magic, int Resonance, int Spells, int ComplexForms,
    int FreeSkillCount, int FreeSkillRating, int FreeGroupCount, int FreeGroupRating);

public sealed partial class CreationRules
{
    private static readonly string[] SupportedPaths = ["Mundane", "Magician", "Adept", "Mystic Adept", "Aspected Magician", "Technomancer"];
    private IEnumerable<XElement> TalentNodes(string priority) => Priority("Talent", priority).Element("talents")!.Elements("talent")
        .Where(t => SupportedPaths.Contains((string?)t.Element("value")));
    private XElement TalentData(string priority, string path) => TalentNodes(priority).SingleOrDefault(t => (string?)t.Element("value") == path)
        ?? throw new ArgumentException($"{path} is not available at talent priority {priority}.");
    public IReadOnlyList<TalentDefinition> Talents(string priority) => TalentNodes(priority).Select(t =>
        new TalentDefinition(t.Element("value")!.Value, t.Element("name")!.Value, (int?)t.Element("magic") ?? 0,
            (int?)t.Element("resonance") ?? 0, (int?)t.Element("spells") ?? 0, (int?)t.Element("cfp") ?? 0,
            (int?)t.Element("skillqty") ?? 0, (int?)t.Element("skillval") ?? 0,
            (int?)t.Element("skillgroupqty") ?? 0, (int?)t.Element("skillgroupval") ?? 0)).ToArray();
    private static bool AllowsSkill(XElement skill, string path, string aspect)
    {
        var category = (string?)skill.Element("category");
        if (category == "Resonance Active") return path == "Technomancer";
        if (category != "Magical Active") return true;
        var group = (string?)skill.Element("skillgroup") ?? "";
        return path switch
        {
            "Magician" or "Mystic Adept" => true,
            "Adept" => group.Length == 0,
            "Aspected Magician" => group.Length == 0 || group == aspect,
            _ => false
        };
    }
    private void AddTalent(XElement root, XElement talent, PriorityChoice choice)
    {
        if (choice.Path == "Mundane") return;
        AddBuiltInQuality(root, choice.Path, "Heritage", choice.Path == "Aspected Magician" ? choice.Aspect : "");
        var freeGroup = (int?)talent.Element("skillgroupval") ?? 0;
        if (choice.Path == "Aspected Magician")
        {
            root.Add(new XElement("priorityskills", new XElement("priorityskill", choice.Aspect)));
            if (freeGroup > 0) root.Element("improvements")!.Add(BuiltInImprovement("SkillGroupBase", "Heritage", "", choice.Aspect, freeGroup));
        }
    }
    public IReadOnlyList<DraftSkill> TalentSkillCandidates(CharacterDocument character)
    {
        var data = TalentData(character.Field("androidtalentpriority"), character.Field("prioritytalent"));
        var filter = data.Element("skilltype");
        if (filter is null) return [];
        var available = Skills(character).Select(s => s.Id).ToHashSet();
        return skills.Where(s => available.Contains(s.Element("id")!.Value) && (filter.Value switch
        {
            "magic" => (string?)s.Element("category") == "Magical Active",
            "resonance" => (string?)s.Element("category") == "Resonance Active",
            "xpath" => (bool)s.XPathEvaluate(filter.Attribute("xpath")!.Value),
            _ => false
        })).Select(s => new DraftSkill(s.Element("id")!.Value, s.Element("name")!.Value, s.Element("attribute")!.Value,
            character.DraftSkillRating(s.Element("id")!.Value))).OrderBy(s => s.Name).ToArray();
    }
    public void AssignTalentSkills(CharacterDocument character, IEnumerable<string> names)
    {
        var selected = names.ToArray();
        var candidates = TalentSkillCandidates(character).Select(s => s.Name).ToHashSet(StringComparer.Ordinal);
        if (selected.Any(name => !candidates.Contains(name))) throw new ArgumentException("A selected skill is not eligible for this talent grant.");
        character.ReplaceTalentFreeSkills(selected);
    }
}

public sealed partial class CharacterDocument
{
    private int FreeRating(string type, string name) => document.Root!.Element("improvements")!.Elements("improvement")
        .Where(e => (string?)e.Element("improvementttype") == type && (string?)e.Element("improvedname") == name &&
            (string?)e.Element("androidbuiltin") == "True" && (int?)e.Element("enabled") == 1)
        .Sum(e => (int?)e.Element("val") ?? 0);
    private int FreeSkillRating(string name) => FreeRating("SkillBase", name);
    private int FreeGroupRating(string name) => FreeRating("SkillGroupBase", name);
    public int TalentSkillChoicesRequired => (int)Number("androidtalentfreeqty");
    public int TalentFreeSkillRating => (int)Number("androidtalentfreerating");
    public IReadOnlyList<string> TalentSkillChoices => document.Root!.Element("improvements")!.Elements("improvement")
        .Where(e => (string?)e.Element("improvementttype") == "SkillBase" && (string?)e.Element("improvementsource") == "Heritage")
        .Select(e => e.Element("improvedname")!.Value).ToArray();
    internal void ReplaceTalentFreeSkills(string[] names)
    {
        RequireDraft();
        var count = TalentSkillChoicesRequired; var rating = TalentFreeSkillRating;
        if (count == 0) throw new InvalidOperationException("This talent grants no individual bonus skill choices.");
        if (names.Length != count || names.Distinct().Count() != count) throw new ArgumentException($"Select exactly {count} different bonus skills.");
        foreach (var node in SkillNodes)
        {
            var name = node.Element("name")!.Value; var group = (string?)node.Element("skillgroup") ?? "";
            if (((int?)node.Element("base") ?? 0) + (names.Contains(name) ? rating : 0) + GroupRating(group) > 6)
                throw new ArgumentException("Reset some paid skill or group points before changing priority bonus skills.");
        }
        var proposedSkills = SkillNodes.ToDictionary(s => s.Element("name")!.Value, s =>
            DraftSkillRating(s.Element("suid")!.Value) - FreeSkillRating(s.Element("name")!.Value) + (names.Contains(s.Element("name")!.Value) ? rating : 0));
        ValidatePowerConstraints(skillRatings: proposedSkills);
        var grants = names.Select(name => CreationRules.BuiltInImprovement("SkillBase", "Heritage", "", name, rating)).ToArray();
        var improvements = document.Root!.Element("improvements")!;
        improvements.Elements("improvement").Where(e => (string?)e.Element("improvementttype") == "SkillBase" &&
            (string?)e.Element("improvementsource") == "Heritage" && (string?)e.Element("androidbuiltin") == "True").Remove();
        improvements.Add(grants);
        document.Root.Elements("priorityskills").Where(e => e.HasElements).Remove();
        document.Root.Add(new XElement("priorityskills", names.Select(name => new XElement("priorityskill", name))));
    }
}
