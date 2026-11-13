// SPDX-License-Identifier: GPL-3.0-or-later
using System.Globalization;
using System.Xml.Linq;

namespace Chummer.Portable;

public sealed record DraftSpecialization(string SkillId, string Skill, string Name, bool Karma);
public sealed record ConditionalSkillBonus(string Id, string Description, int Value);
public sealed partial class CreationRules
{
    public IReadOnlyList<string> SpecializationSuggestions(string skillId) => skills.Single(s => (string?)s.Element("id") == skillId)
        .Element("specs")?.Elements("spec").Select(s => s.Value).ToArray() ?? [];
}
public sealed partial class CharacterDocument
{
    public IReadOnlyList<DraftSpecialization> DraftSpecializations
    {
        get { RequireDraft(); return SkillNodes.SelectMany(s => s.Element("specs")?.Elements("spec").Select(p => new DraftSpecialization(
            s.Element("suid")!.Value, s.Element("name")!.Value, p.Element("name")!.Value, (string?)s.Element("buywithkarma") == "True")) ?? []).ToArray(); }
    }
    private int SpecializationPointCost => SkillNodes.Where(s => (string?)s.Element("buywithkarma") != "True")
        .Sum(s => s.Element("specs")?.Elements("spec").Count(p => (string?)p.Element("free") != "True") ?? 0);
    public void AddDraftSpecialization(string skillId, string name, bool karma)
    {
        RequireDraft(); name = name.Trim();
        if (name.Length == 0 || name.Length > 200) throw new ArgumentException("Enter a specialization name of 1–200 characters.");
        var skill = SkillNodes.SingleOrDefault(s => (string?)s.Element("suid") == skillId) ?? throw new ArgumentException("Unknown skill.");
        if (DraftSkillRating(skillId) < 1) throw new ArgumentException("Train the skill before adding a specialization.");
        if (!karma && DraftSkillKarmaLevels(skillId) > 0 && DraftSkillRating(skillId) == DraftSkillKarmaLevels(skillId))
            throw new ArgumentException("A skill bought entirely with Karma must also buy its specialization with Karma under standard rules.");
        if (skill.Element("specs")?.HasElements == true) throw new ArgumentException("Only one specialization per active skill is supported during standard creation.");
        if (karma ? Karma < 7 : SkillPointsRemaining < 1) throw new ArgumentException(karma ? "Not enough Karma; a specialization costs 7." : "Not enough skill points; a specialization costs 1.");
        if (skill.Element("specs") is null) skill.Add(new XElement("specs"));
        skill.Element("specs")!.Add(new XElement("spec", new XElement("guid", Guid.NewGuid().ToString("D")), new XElement("name", name),
            new XElement("free", "False"), new XElement("expertise", "False"), new XElement("androidbuiltin", "True")));
        skill.SetElementValue("buywithkarma", karma ? "True" : "False");
        if (karma) SetField("karma", (Karma - 7).ToString(CultureInfo.InvariantCulture));
    }
    public void RemoveDraftSpecialization(string skillId)
    {
        RequireDraft(); var skill = SkillNodes.SingleOrDefault(s => (string?)s.Element("suid") == skillId) ?? throw new ArgumentException("Unknown skill.");
        var spec = skill.Element("specs")?.Elements("spec").SingleOrDefault(p => (string?)p.Element("androidbuiltin") == "True") ?? throw new ArgumentException("No editable specialization on this skill.");
        spec.Remove();
        if ((string?)skill.Element("buywithkarma") == "True") SetField("karma", (Karma + 7).ToString(CultureInfo.InvariantCulture));
        skill.SetElementValue("buywithkarma", "False");
    }
    public int DraftSpecializedPool(DraftSkill skill, string specialization)
    {
        if (!DraftSpecializations.Any(s => s.SkillId == skill.Id && s.Name.Equals(specialization, StringComparison.OrdinalIgnoreCase))) throw new ArgumentException("Unknown specialization for this skill.");
        return Math.Max(0, DraftSkillPoolBeforeFloor(skill) + (CanApplyDraftSpecialization(skill.Id)?2:0));
    }
    public bool CanApplyDraftSpecialization(string skillId)
    {
        RequireDraft();var name=SkillNodes.Single(s=>(string?)s.Element("suid")==skillId).Element("name")!.Value;
        return !document.Root!.Element("improvements")!.Elements().Any(e=>(string?)e.Element("improvementttype")=="DisableSpecializationEffects" &&
            (string?)e.Element("improvedname")==name && (int?)e.Element("enabled")==1);
    }
    public IReadOnlyList<ConditionalSkillBonus> DraftConditionalSkillBonuses(DraftSkill skill)
    {
        RequireDraft(); var category = SkillNodes.Single(s => (string?)s.Element("suid") == skill.Id).Element("skillcategory")!.Value;
        return document.Root!.Element("improvements")!.Elements().Where(e => (int?)e.Element("enabled") == 1 &&
            !string.IsNullOrWhiteSpace((string?)e.Element("condition")) && ((string?)e.Element("improvementttype") switch {
                "Skill" => (string?)e.Element("improvedname") == skill.Name,
                "SkillCategory" => (string?)e.Element("improvedname") == category,
                "SkillAttribute" => (string?)e.Element("improvedname") == skill.Attribute, _ => false }))
            .Select(e => new ConditionalSkillBonus(string.Join("|", new[] { "sourcename", "improvementttype", "improvedname", "condition" }.Select(n => (string?)e.Element(n) ?? "")),
                e.Element("condition")!.Value.Replace("LimitCondition_", ""), (int)e.Element("val")!)).ToArray();
    }
    public bool DraftAmbidextrous
    {
        get{RequireDraft();return document.Root!.Element("improvements")!.Elements().Any(e=>(string?)e.Element("improvementttype")=="Ambidextrous" && (int?)e.Element("enabled")==1);}
    }
    public int DraftContextualSkillPool(DraftSkill skill, string? specialization, IEnumerable<string> conditions,bool offHand=false)
    {
        var selected = conditions.ToHashSet(StringComparer.Ordinal); var available = DraftConditionalSkillBonuses(skill);
        if (selected.Any(id => !available.Any(b => b.Id == id))) throw new ArgumentException("A conditional bonus does not apply to this skill.");
        if (specialization is not null && !DraftSpecializations.Any(s => s.SkillId == skill.Id && s.Name.Equals(specialization, StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException("Unknown specialization.");
        return Math.Max(0, DraftSkillPoolBeforeFloor(skill) + (specialization is not null && CanApplyDraftSpecialization(skill.Id) ? 2 : 0) + available.Where(b => selected.Contains(b.Id)).Sum(b => b.Value) - (offHand && !DraftAmbidextrous?2:0));
    }
}
