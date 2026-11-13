// SPDX-License-Identifier: GPL-3.0-or-later
using System.Xml.Linq;
using System.Text.RegularExpressions;

namespace Chummer.Portable;

public sealed record EffectiveStatistics(IReadOnlyDictionary<string,int> Attributes, int PhysicalLimit, int MentalLimit, int SocialLimit,
    int Initiative, int InitiativeDice, int PhysicalBoxes, int StunBoxes, int Armor, int ArmorPenalty, int DefensePool, int SoakPool, int WoundPenalty);

public sealed partial class CharacterDocument
{
    /// <summary>Supported permanent effects only. Active powers, spell effects and situational modifiers require manual play handling.</summary>
    public EffectiveStatistics EffectiveStats()
    {
        RequireDraft(); ValidatePowerConstraints();
        var a = DraftAttributes.ToDictionary(v => v.Name, v => v.Rating + (int)DraftEffect("Attribute", v.Name, "aug"));
        var armors = EquipmentNodes.Where(e => e.Name == "armor" && (string?)e.Element("equipped") == "True").ToArray();
        var baseArmor = armors.Where(e => !e.Element("armor")!.Value.StartsWith('+')).Select(e => (int)e.Element("armor")!).DefaultIfEmpty(0).Max();
        var bonusArmor = armors.Where(e => e.Element("armor")!.Value.StartsWith('+')).Sum(e => int.Parse(e.Element("armor")!.Value[1..]));
        var armorPenalty = Math.Max(0, (bonusArmor - a["STR"]) / 2);
        a["AGI"] = Math.Max(1, a["AGI"] - armorPenalty); a["REA"] = Math.Max(1, a["REA"] - armorPenalty);
        var armor = baseArmor + Math.Min(bonusArmor, a["STR"]) + (int)DraftEffect("Armor");
        var resistance = (int)DraftEffect("CMThresholdOffset");
        var woundThreshold = Math.Max(1, 3 + (int)DraftEffect("CMThreshold"));
        var wounds = Math.Max(0, (Math.Min(PhysicalDamage, 8 + (a["BOD"] + 1)/2) - resistance) / woundThreshold) +
            Math.Max(0, (Math.Min(StunDamage, 8 + (a["WIL"] + 1)/2) - resistance) / woundThreshold);
        int Third(int n) => (n + 2) / 3;
        return new EffectiveStatistics(a, Third(2*a["STR"] + a["BOD"] + a["REA"]) + (int)DraftEffect("PhysicalLimit", "Physical"),
            Third(2*a["LOG"] + a["INT"] + a["WIL"]) + (int)DraftEffect("MentalLimit", "Mental"),
            Third(2*a["CHA"] + a["WIL"] + (int)decimal.Ceiling(DraftEssence)) + (int)DraftEffect("SocialLimit", "Social"),
            Math.Max(0, a["REA"] + a["INT"] - wounds), 1 + (int)DraftEffect("InitiativeDice"),
            8 + (a["BOD"] + 1)/2, 8 + (a["WIL"] + 1)/2, armor, armorPenalty,
            Math.Max(0, a["REA"] + a["INT"] + (int)DraftEffect("Dodge") - wounds), a["BOD"] + armor + (int)DraftEffect("DamageResistance"), wounds);
    }
    public int DraftSkillPool(DraftSkill skill)
        => Math.Max(0, DraftSkillPoolBeforeFloor(skill));
    private int DraftSkillPoolBeforeFloor(DraftSkill skill)
    {
        var stats = EffectiveStats();
        if (skill.Rating == 0 && !skill.Defaultable) throw new InvalidOperationException("This skill cannot be used without training.");
        return skill.Rating + stats.Attributes[skill.Attribute] + (int)DraftEffect("Skill", skill.Name) +
            (int)DraftEffect("SkillAttribute", skill.Attribute) + (int)DraftEffect("SkillCategory", SkillNodes.Single(s => (string?)s.Element("suid") == skill.Id).Element("skillcategory")!.Value) - stats.WoundPenalty - (skill.Rating == 0 ? 1 : 0);
    }
    public WeaponStats DraftUnarmedAttack()
    {
        var s = EffectiveStats();
        var physical = document.Root!.Element("improvements")!.Elements().Any(e => (string?)e.Element("improvementttype") == "UnarmedDVPhysical" && (int?)e.Element("enabled") == 1);
        return new WeaponStats("Unarmed", "Unarmed Combat", (s.Attributes["STR"] + (int)DraftEffect("WeaponCategoryDV", "Unarmed Combat") + (int)DraftEffect("UnarmedDV")) + (physical ? "P" : "S"),
            s.PhysicalLimit + (int)DraftEffect("WeaponSkillAccuracy", "Unarmed Combat"), "0", "", "", (int)DraftEffect("Reach"));
    }
    public IReadOnlyList<string> DraftEffectDescriptions()
    {
        RequireDraft();
        var selectedSources = SelectedQualities.Select(q => q.Id).ToHashSet();
        return document.Root!.Element("improvements")!.Elements().Where(e => ((string?)e.Element("improvementsource") is "Power" or "Cyberware" or "Bioware" ||
            (string?)e.Element("improvementsource") == "Quality" && selectedSources.Contains((string?)e.Element("sourcename") ?? "")) &&
            (string?)e.Element("improvementttype") != "DisableQuality").Select(e => {
                var type = e.Element("improvementttype")!.Value; var name = e.Element("improvedname")!.Value;
                var description = type switch {
                    "UnarmedDVPhysical" => "Unarmed attacks inflict Physical damage",
                    "Attribute" when (int?)e.Element("max")>0 => $"{name} natural maximum increase: +{e.Element("max")!.Value}",
                    "Attribute" => $"{name} increase: +{e.Element("aug")!.Value}",
                    "SpecialSkills" => $"Unlocked skill: {name}",
                    "CMThresholdOffset" => $"Wound penalty offset: {e.Element("val")!.Value} damage boxes per monitor",
                    "InitiativeDice" => $"Extra initiative dice: {e.Element("val")!.Value}",
                    "Skill" when (int?)e.Element("max")>0 => $"{name} skill maximum increase: +{e.Element("max")!.Value}",
                    "Skill" => $"{name} skill pool bonus: {(e.Element("val")!.Value.StartsWith('-')?"":"+")}{e.Element("val")!.Value}",
                    "DisableSpecializationEffects" => $"{name}: specialization bonuses disabled",
                    "Ambidextrous" => "Ambidextrous: off-hand penalty ignored",
                    "WeaponCategoryDV" => $"{name} damage bonus: +{e.Element("val")!.Value}",
                    "WeaponSkillAccuracy" => $"{name} accuracy bonus: +{e.Element("val")!.Value}",
                    _ => $"{Regex.Replace(type, "([a-z])([A-Z])", "$1 $2")} {name} modifier: {e.Element("val")!.Value}"
                };
                var condition = (string?)e.Element("condition") ?? "";
                return description + (condition.Length > 0 ? " (only when " + condition.Replace("LimitCondition_", "") + ")" : "");
            }).ToArray();
    }
}
