// SPDX-License-Identifier: GPL-3.0-or-later
using System.Globalization;
using System.Xml;
using System.Xml.Linq;

namespace Chummer.Portable;

public sealed record PowerEntry(string Id, string Name, decimal PointsPerLevel, decimal ExtraCost, bool Levels, int Maximum, string Page);
public sealed record LearnedPower(string Id, string Name, string Extra, int Rating, decimal Cost);

public sealed class PowerRules
{
    private readonly Dictionary<string, XElement> powers;
    public PowerRules(Stream data)
    {
        using var reader = XmlReader.Create(data, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 32 * 1024 * 1024 });
        var xml = XDocument.Load(reader);
        if (xml.Root?.Name != "chummer") throw new InvalidDataException("Invalid power catalog.");
        powers = xml.Root.Element("powers")!.Elements("power").Where(p => (string?)p.Element("source") == "SR5" && p.Element("hide") is null)
            .ToDictionary(p => p.Element("id")!.Value, p => new XElement(p));
    }
    public IReadOnlyList<PowerEntry> Catalog => powers.Values.Select(p => new PowerEntry(p.Element("id")!.Value, p.Element("name")!.Value,
        (decimal)p.Element("points")!, (decimal?)p.Element("extrapointcost") ?? 0, string.Equals((string?)p.Element("levels"), "True", StringComparison.OrdinalIgnoreCase),
        (int?)p.Element("maxlevels") ?? 0, p.Element("page")!.Value)).OrderBy(p => p.Name).ToArray();
    public IReadOnlyList<string> Choices(string sourceId, CharacterDocument character, CreationRules creation)
    {
        var p = powers[sourceId]; var b = p.Element("bonus");
        if (b?.Element("selectattribute") is { } a) return a.Elements("attribute").Select(e => e.Value).ToArray();
        if (b?.Element("weaponcategorydv") is { } w) return w.Element("selectcategory")!.Elements("category").Select(e => e.Value).ToArray();
        var s = b?.Element("selectskill") ?? b?.Element("weaponskillaccuracy")?.Element("selectskill");
        if (s is null) return [];
        var allowed = s.Element("skillcategories")?.Elements("category").Select(e => e.Value).ToArray();
        return creation.Skills(character).Where(skill =>
            (p.Element("name")!.Value != "Improved Ability (skill)" || skill.Rating > 0) &&
            ((string?)s.Attribute("excludeskill") != skill.Name) && creation.SkillCategoryAllowed(skill.Id, (string?)s.Attribute("skillcategory"), allowed))
            .Select(skill => skill.Name).ToArray();
    }
    public bool FreeTextChoice(string id) => powers[id].Element("bonus")?.Element("selecttext") is not null;
    public void Learn(CharacterDocument character, CreationRules creation, string sourceId, int rating, string extra = "")
    {
        _ = character.DraftAttributes;
        if (character.Field("adept") != "True") throw new InvalidOperationException("This talent has no adept powers.");
        if (!powers.TryGetValue(sourceId, out var p)) throw new ArgumentException("Unknown core power.");
        var entry = Catalog.Single(e => e.Id == sourceId); extra = extra.Trim();
        if(entry.Name=="Improved Reflexes" && character.InstalledAugmentations.Any(a=>a.Name is "Wired Reflexes" or "Reaction Enhancers" or "Synaptic Booster"))
            throw new InvalidOperationException("Combining Improved Reflexes with initiative implants needs additional interaction support.");
        if (entry.Name == "Pain Resistance" && character.HasQuality("High Pain Tolerance")) throw new ArgumentException("Pain Resistance conflicts with High Pain Tolerance.");
        var choices = Choices(sourceId, character, creation);
        if (choices.Count > 0) extra = choices.FirstOrDefault(c => c.Equals(extra, StringComparison.OrdinalIgnoreCase)) ?? throw new ArgumentException("Choose one of the listed power options.");
        else if (FreeTextChoice(sourceId)) { if (extra.Length == 0) throw new ArgumentException("Enter the sense to improve."); }
        else if (extra.Length > 0 || p.Descendants("selectskill").Any()) throw new ArgumentException("No eligible power choice is available.");
        if (rating < 1 || !entry.Levels && rating != 1 || entry.Maximum > 0 && rating > entry.Maximum)
            throw new ArgumentException("Invalid power rating.");
        if (character.LearnedPowers.Any(e => e.Name == entry.Name && e.Extra.Equals(extra, StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException("This power and choice already exist. Remove it before changing its rating.");
        if (character.LearnedPowers.Count(e => e.Name == entry.Name) >= ((int?)p.Element("limit") ?? 1)) throw new ArgumentException("This power's instance limit has been reached.");
        var id = Guid.NewGuid().ToString("D");
        var node = new XElement("power", new XElement("sourceid", sourceId), new XElement("guid", id), new XElement("name", entry.Name),
            new XElement("extra", extra), new XElement("pointsperlevel", entry.PointsPerLevel), new XElement("extrapointcost", entry.ExtraCost),
            new XElement("rating", rating), new XElement("levels", entry.Levels ? "True" : "False"), new XElement("maxlevels", entry.Maximum),
            new XElement("action", (string?)p.Element("action") ?? ""), new XElement("source", "SR5"), new XElement("page", entry.Page),
            new XElement("adeptway", (string?)p.Element("adeptway") ?? "0"), new XElement("discounted", "False"), new XElement("discountedgeas", "False"),
            new XElement("freepoints", 0), new XElement("bonussource", ""), new XElement("notes", ""), new XElement("androidbuiltin", "True"),
            p.Element("bonus") is { } bonus ? new XElement(bonus) : new XElement("bonus"));
        character.AddDraftPower(node, Effects(node));
    }
    private static XElement[] Effects(XElement power)
    {
        var result = new List<XElement>(); var id = power.Element("guid")!.Value; var extra = power.Element("extra")!.Value; var rating = (int)power.Element("rating")!;
        void Add(string type, string name, decimal value, bool attribute = false, string unique = "")
        {
            var e = CreationRules.BuiltInImprovement(type, "Power", id, name, attribute ? 0 : value, unique, rating: rating);
            if (attribute) e.SetElementValue("aug", value);
            result.Add(e);
        }
        foreach (var b in power.Element("bonus")!.Elements())
        {
            switch (b.Name.LocalName)
            {
                case "selectattribute": if (b.Element("val") is not null) Add("Attribute", extra, rating, true); break;
                case "specificattribute": Add("Attribute", b.Element("name")!.Value, rating, true); break;
                case "selectskill": Add("Skill", extra, rating); break;
                case "specificskill": Add("Skill", b.Element("name")!.Value, rating); break;
                case "weaponskillaccuracy": Add("WeaponSkillAccuracy", extra, 1); break;
                case "weaponcategorydv": Add("WeaponCategoryDV", extra, 1); break;
                case "unlockskills": Add("SpecialSkills", b.Attribute("name")!.Value, 0); break;
                case "disablequality": Add("DisableQuality", b.Value, 0); break;
                case "selecttext": break;
                case "conditionmonitor": Add("CMThresholdOffset", "", rating); break;
                default:
                    var type = b.Name.LocalName switch {
                        "dodge" => "Dodge", "surprise" => "Surprise", "sociallimit" => "SocialLimit", "mentallimit" => "MentalLimit", "physicallimit" => "PhysicalLimit",
                        "initiativepass" => "InitiativeDice", "armor" => "Armor", "unarmeddvphysical" => "UnarmedDVPhysical", "judgeintentionsdefense" => "JudgeIntentionsDefense",
                        "physicalcmrecovery" => "PhysicalCMRecovery", "stuncmrecovery" => "StunCMRecovery", "spellresistance" => "SpellResistance",
                        "pathogencontactresist" => "PathogenContactResist", "pathogeningestionresist" => "PathogenIngestionResist", "pathogeninhalationresist" => "PathogenInhalationResist", "pathogeninjectionresist" => "PathogenInjectionResist",
                        "toxincontactresist" => "ToxinContactResist", "toxiningestionresist" => "ToxinIngestionResist", "toxininhalationresist" => "ToxinInhalationResist", "toxininjectionresist" => "ToxinInjectionResist",
                        _ => throw new InvalidDataException($"Unsupported power effect: {b.Name}") };
                    var improvedName = type switch { "PhysicalLimit" => "Physical", "MentalLimit" => "Mental", "SocialLimit" => "Social", _ => "" };
                    Add(type, improvedName, type == "UnarmedDVPhysical" ? 0 : rating, unique: type == "InitiativeDice" ? "precedence0" : ""); break;
            }
        }
        return result.ToArray();
    }
}

public sealed partial class CreationRules
{
    internal bool SkillCategoryAllowed(string id, string? category, string[]? allowed) => skills.Any(s => (string?)s.Element("id") == id &&
        (category is null || (string?)s.Element("category") == category) && (allowed is null || allowed.Contains((string?)s.Element("category"))));
}

public sealed partial class CharacterDocument
{
    public IReadOnlyList<LearnedPower> LearnedPowers => (document.Root!.Element("powers")?.Elements("power") ?? []).Select(p => new LearnedPower(
        p.Element("guid")!.Value, p.Element("name")!.Value, (string?)p.Element("extra") ?? "", (int)p.Element("rating")!,
        (decimal)p.Element("pointsperlevel")! * (int)p.Element("rating")! + ((decimal?)p.Element("extrapointcost") ?? 0))).ToArray();
    public decimal PowerPointsTotal => Field("adept") != "True" ? 0 : Field("prioritytalent") == "Mystic Adept" ? Number("magsplitadept") : DraftAttributes.Single(a => a.Name == "MAG").Rating;
    public decimal PowerPointsRemaining => PowerPointsTotal - LearnedPowers.Sum(p => p.Cost);
    internal void AddDraftPower(XElement power, XElement[] effects)
    {
        var proposed = Clone(); var root = proposed.document.Root!;
        if (root.Element("powers") is null) root.Add(new XElement("powers"));
        root.Element("powers")!.Add(power); root.Element("improvements")!.Add(effects);
        proposed.ValidatePowerConstraints(); document.Root!.ReplaceWith(new XElement(root));
    }
    public void RemoveDraftPower(string id)
    {
        RequireDraft();
        var node = document.Root!.Element("powers")?.Elements("power").SingleOrDefault(p => (string?)p.Element("guid") == id) ?? throw new ArgumentException("Unknown power.");
        node.Remove(); document.Root.Element("improvements")!.Elements().Where(e => (string?)e.Element("improvementsource") == "Power" && (string?)e.Element("sourcename") == id).Remove();
    }
    public void SetMysticPowerPoints(int points)
    {
        RequireDraft(); if (Field("prioritytalent") != "Mystic Adept") throw new InvalidOperationException("Only mystic adepts buy power points with creation Karma.");
        if (points < 0 || points > DraftAttributes.Single(a => a.Name == "MAG").Rating || points < LearnedPowers.Sum(p => p.Cost)) throw new ArgumentException("Power points must cover learned powers and cannot exceed Magic.");
        var cost = (points - Number("magsplitadept")) * 5;
        if (cost > Karma) throw new ArgumentException("Not enough creation Karma.");
        SetField("magsplitadept", points.ToString(CultureInfo.InvariantCulture)); SetField("karma", (Karma - cost).ToString(CultureInfo.InvariantCulture));
    }
    internal void ValidatePowerConstraints(IReadOnlyDictionary<string, int>? attributes = null, IReadOnlyDictionary<string, int>? skillRatings = null)
    {
        var values = attributes ?? DraftAttributes.ToDictionary(a => a.Name, a => a.Rating);
        if (skillRatings is not null && DraftSpecializations.Any(s => skillRatings.GetValueOrDefault(s.Skill) < 1))
            throw new ArgumentException("Remove the specialization before reducing its skill to zero.");
        if(skillRatings is not null && SelectedQualities.Any(q=>q.Name=="Loss of Confidence" && skillRatings.GetValueOrDefault(q.Extra)<4))
            throw new ArgumentException("Remove Loss of Confidence before reducing its selected skill below rating 4.");
        if (LearnedPowers.Count == 0 && Number("magsplitadept") == 0) return;
        var magic = values.GetValueOrDefault("MAG");
        if (Number("magsplitadept") > magic || LearnedPowers.Sum(p => p.Cost) > (Field("prioritytalent") == "Mystic Adept" ? Number("magsplitadept") : magic)) throw new ArgumentException("Remove powers or reduce purchased power points before lowering Magic.");
        foreach (var p in LearnedPowers)
        {
            var node = document.Root!.Element("powers")!.Elements().Single(e => (string?)e.Element("guid") == p.Id);
            if ((string?)node.Element("levels") == "True" && p.Rating > magic) throw new ArgumentException("A power's rating cannot exceed Magic.");
            if (p.Name == "Improved Ability (skill)")
            {
                var skill = SkillNodes.Single(s => (string?)s.Element("name") == p.Extra);
                var rating = skillRatings?.GetValueOrDefault(p.Extra) ?? DraftSkillRating(skill.Element("suid")!.Value);
                if (rating == 0 || p.Rating > (rating + 1) / 2) throw new ArgumentException("Improved Ability cannot exceed half the learned skill rating, rounded up.");
            }
        }
        foreach (var name in StandardAttributeNames)
            if (DraftEffect("Attribute", name, "aug") > 4) throw new ArgumentException("Attribute augmentations cannot exceed +4.");
    }
    internal decimal DraftEffect(string type, string name = "", string field = "val")
    {
        var effects=(document.Root!.Element("improvements")?.Elements()??[]).Where(e=>(string?)e.Element("androidbuiltin")=="True" && (int?)e.Element("enabled")==1 &&
            (string?)e.Element("improvementttype")==type && (string?)e.Element("improvedname")==name && string.IsNullOrEmpty((string?)e.Element("condition"))).ToArray();
        return effects.Where(e=>string.IsNullOrEmpty((string?)e.Element("unique"))).Sum(e=>(decimal?)e.Element(field)??0)+
            effects.Where(e=>!string.IsNullOrEmpty((string?)e.Element("unique"))).GroupBy(e=>e.Element("unique")!.Value).Sum(g=>g.Max(e=>(decimal?)e.Element(field)??0));
    }
}
