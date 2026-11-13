// SPDX-License-Identifier: GPL-3.0-or-later
using System.Globalization;
using System.Xml;
using System.Xml.Linq;

namespace Chummer.Portable;

public sealed record QualityEntry(string Id, string Name, string Category, int Karma, int Maximum, bool Choice, string Page);
public sealed record SelectedQuality(string Id, string Name, string Extra, int Karma);

public sealed class QualityRules
{
    private readonly Dictionary<string, XElement> qualities;
    private static readonly Dictionary<string, string> NumericEffects = new() {
        ["notoriety"]="Notoriety", ["memory"]="Memory", ["spellresistance"]="SpellResistance", ["damageresistance"]="DamageResistance",
        ["lifestylecost"]="LifestyleCost", ["nativelanguagelimit"]="NativeLanguageLimit", ["physicalcmrecovery"]="PhysicalCMRecovery", ["stuncmrecovery"]="StunCMRecovery",
        ["physicallimit"]="PhysicalLimit", ["mentallimit"]="MentalLimit", ["sociallimit"]="SocialLimit", ["surprise"]="Surprise",
        ["pathogencontactresist"]="PathogenContactResist", ["pathogeningestionresist"]="PathogenIngestionResist", ["pathogeninhalationresist"]="PathogenInhalationResist", ["pathogeninjectionresist"]="PathogenInjectionResist",
        ["toxincontactresist"]="ToxinContactResist", ["toxiningestionresist"]="ToxinIngestionResist", ["toxininhalationresist"]="ToxinInhalationResist", ["toxininjectionresist"]="ToxinInjectionResist"
    };
    private static bool Supported(XElement q) => ((string?)q.Element("name") is "Aptitude" or "Exceptional Attribute" or "Lucky" or "Loss of Confidence" or "Sensitive System") || q.Element("firstlevelbonus") is null && q.Element("naturalweapons") is null &&
        (q.Element("bonus")?.Elements() ?? []).All(e => NumericEffects.ContainsKey(e.Name.LocalName) && decimal.TryParse(e.Value, NumberStyles.Number, CultureInfo.InvariantCulture, out _) ||
            e.Name.LocalName is "selecttext" or "ambidextrous" || e.Name.LocalName is "specificskill" or "skillcategory" or "skillattribute" &&
                e.Elements().All(c => c.Name.LocalName is "name" or "bonus" or "condition") && int.TryParse((string?)e.Element("bonus"), out _) ||
            e.Name.LocalName == "conditionmonitor" && e.Elements().All(c => c.Name.LocalName is "threshold" or "thresholdoffset" or "overflow" && int.TryParse(c.Value, out _)));
    public QualityRules(Stream data)
    {
        using var reader = XmlReader.Create(data, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 32*1024*1024 });
        var xml = XDocument.Load(reader);
        if (xml.Root?.Name != "chummer") throw new InvalidDataException("Invalid quality catalog.");
        qualities = xml.Root.Element("qualities")!.Elements("quality").Where(q => (string?)q.Element("source") == "SR5" && q.Element("hide") is null &&
            (string?)q.Element("category") is "Positive" or "Negative" && Supported(q) &&
            (q.Element("limit") is null || (string?)q.Element("limit") == "False" || int.TryParse((string?)q.Element("limit"), out _) ||
                (string?)q.Element("name")=="Ambidextrous" && (string?)q.Element("limit")=="{arm} - 1"))
            .ToDictionary(q => q.Element("id")!.Value, q => new XElement(q));
    }
    public IReadOnlyList<QualityEntry> Catalog => qualities.Values.Select(q => new QualityEntry(q.Element("id")!.Value, q.Element("name")!.Value,
        q.Element("category")!.Value, (int)q.Element("karma")!, (string?)q.Element("limit") == "False" ? 0 : (string?)q.Element("name")=="Ambidextrous"?1:(int?)q.Element("limit") ?? 1,
        q.Element("bonus")?.Element("selecttext") is not null || (string?)q.Element("name") is "Aptitude" or "Exceptional Attribute" or "Loss of Confidence", q.Element("page")!.Value)).OrderBy(q => q.Name).ToArray();
    public IReadOnlyList<string> Choices(CharacterDocument c,string sourceId)=>c.QualityRatingChoices(Catalog.Single(q=>q.Id==sourceId).Name);
    private static bool Predicate(XElement e, CharacterDocument c) => e.Name.LocalName switch {
        "oneof" => e.Elements().Any(p => Predicate(p,c)), "allof" or "required" => e.Elements().All(p => Predicate(p,c)),
        "forbidden" => e.Elements().Any(p => Predicate(p,c)),
        "quality" => c.HasQuality(e.Value), "power" => c.LearnedPowers.Any(p => p.Name == e.Value),
        "metatype" => c.Metatype == e.Value, "metatypecategory" => c.Field("metatypecategory") == e.Value,
        "magenabled" => c.Field("magenabled") == "True", "resenabled" => c.Field("resenabled") == "True", "depenabled" => false,
        "bioware" or "critterpower" => false,
        _ => throw new InvalidOperationException($"A prerequisite needs additional rule support: {e.Name}") };
    public void Validate(CharacterDocument c)
    {
        c.ValidateDraftRatingLimits();
        foreach (var q in c.SelectedQualities)
        {
            var source = c.SelectedQualitySource(q.Id);
            if (!qualities.TryGetValue(source, out var data)) throw new InvalidOperationException("A selected quality needs additional rule support.");
            if (data.Element("required") is { } r && !Predicate(r,c)) throw new ArgumentException($"Requirements are not met for {q.Name}.");
            if (data.Element("forbidden") is { } f && Predicate(f,c)) throw new ArgumentException($"{q.Name} conflicts with another character option.");
        }
    }
    public void Add(CharacterDocument character, string sourceId, string extra = "")
    {
        _ = character.DraftAttributes;
        if (!qualities.TryGetValue(sourceId, out var q)) throw new ArgumentException("This quality needs additional rule support.");
        var entry = Catalog.Single(e => e.Id == sourceId); extra = extra.Trim();
        var options=character.QualityRatingChoices(entry.Name);
        if(entry.Name=="Loss of Confidence" && options.Count==0)throw new ArgumentException("Train an active skill to at least rating 4 before choosing Loss of Confidence.");
        if(options.Count>0)extra=options.FirstOrDefault(v=>v.Equals(extra,StringComparison.OrdinalIgnoreCase))??throw new ArgumentException("Choose a valid skill or attribute for this quality.");
        if (entry.Choice ? extra.Length == 0 || extra.Length > 200 : extra.Length > 0) throw new ArgumentException("Enter the quality's required choice, or leave it blank when no choice is needed.");
        var count = character.SelectedQualities.Count(e => e.Name == entry.Name);
        if (entry.Maximum > 0 && count >= entry.Maximum || entry.Maximum == 0 && character.SelectedQualities.Any(e => e.Name == entry.Name && e.Extra.Equals(extra, StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException("This quality or choice has reached its level limit.");
        if (character.PositiveQualityKarma + Math.Max(0, entry.Karma) > 25 || character.NegativeQualityKarma + Math.Max(0, -entry.Karma) > 25)
            throw new ArgumentException("Standard creation allows up to 25 Karma each in positive and negative qualities.");
        if (entry.Karma > character.Karma) throw new ArgumentException("Not enough creation Karma.");
        var updated = character.Clone(); var id = Guid.NewGuid().ToString("D");
        var quality = new XElement("quality", new XElement("sourceid", sourceId), new XElement("guid", id), new XElement("name", entry.Name),
            new XElement("extra", extra), new XElement("bp", entry.Karma), new XElement("implemented", "True"), new XElement("contributetobp", "True"),
            new XElement("contributetolimit", "True"), new XElement("qualitytype", entry.Category), new XElement("qualitysource", "Selected"),
            new XElement("source", "SR5"), new XElement("page", entry.Page), new XElement("androidbuiltin", "True"),
            q.Element("bonus") is { } bonus ? new XElement(bonus) : new XElement("bonus"), new XElement("notes", ""));
        updated.AddSelectedQuality(quality, Effects(q.Element("bonus"), id,extra), entry.Karma);
        updated.RefreshDraftEssenceEffects();Validate(updated);updated.ValidateAugmentationEffects(); character.ReplaceDraftWith(updated);
    }
    public void Remove(CharacterDocument character, string id)
    {
        var updated = character.Clone(); updated.RemoveSelectedQuality(id);updated.RefreshDraftEssenceEffects(); Validate(updated);updated.ValidateAugmentationEffects(); character.ReplaceDraftWith(updated);
    }
    private static XElement[] Effects(XElement? bonus, string id,string extra)
    {
        var effects = new List<XElement>();
        foreach (var b in bonus?.Elements() ?? [])
        {
            if (b.Name == "selecttext") continue;
            if(b.Name=="cyberwaretotalessmultiplier")
            {
                effects.Add(CreationRules.BuiltInImprovement("CyberwareTotalEssMultiplier","Quality",id,"",(decimal)b));continue;
            }
            if(b.Name=="disablebioware")
            {
                effects.Add(CreationRules.BuiltInImprovement("DisableBioware","Quality",id,"",0,"disablebioware"));continue;
            }
            if(b.Name.LocalName is "selectskill" or "selectattributes" or "specificattribute")
            {
                var skill=b.Name=="selectskill";var target=skill||b.Name=="selectattributes"?extra:b.Element("name")!.Value;
                var effect=CreationRules.BuiltInImprovement(skill?"Skill":"Attribute","Quality",id,target,skill?(decimal?)b.Element("val")??0:0);
                effect.SetElementValue("max",skill?(int?)b.Element("max")??0:1);effects.Add(effect);
                if(b.Element("disablespecializationeffects") is not null)effects.Add(CreationRules.BuiltInImprovement("DisableSpecializationEffects","Quality",id,target,0));
                continue;
            }
            if (b.Name == "conditionmonitor")
            {
                foreach (var child in b.Elements()) effects.Add(CreationRules.BuiltInImprovement(child.Name.LocalName switch {
                    "threshold" => "CMThreshold", "thresholdoffset" => "CMThresholdOffset", "overflow" => "CMOverflow", _ => throw new InvalidDataException() }, "Quality", id, "", (decimal)child));
                continue;
            }
            if (b.Name.LocalName is "specificskill" or "skillcategory" or "skillattribute")
                effects.Add(CreationRules.BuiltInImprovement(b.Name.LocalName switch { "specificskill" => "Skill", "skillcategory" => "SkillCategory", _ => "SkillAttribute" }, "Quality", id,
                    b.Element("name")!.Value, (decimal)b.Element("bonus")!, condition: (string?)b.Element("condition") ?? ""));
            else
            {
                var type = b.Name == "ambidextrous" ? "Ambidextrous" : NumericEffects[b.Name.LocalName];
                var name = type switch { "PhysicalLimit" => "Physical", "MentalLimit" => "Mental", "SocialLimit" => "Social", _ => "" };
                effects.Add(CreationRules.BuiltInImprovement(type, "Quality", id, name, b.Name == "ambidextrous" ? 0 : (decimal)b));
            }
        }
        return effects.ToArray();
    }
}

public sealed partial class CharacterDocument
{
    internal bool HasQuality(string name) => document.Root!.Element("qualities")!.Elements().Any(q => (string?)q.Element("name") == name || (string?)q.Element("sourceid") == name);
    public IReadOnlyList<SelectedQuality> SelectedQualities
    {
        get { RequireDraft(); return document.Root!.Element("qualities")!.Elements().Where(q => (string?)q.Element("qualitysource") == "Selected")
            .Select(q => new SelectedQuality(q.Element("guid")!.Value, q.Element("name")!.Value, (string?)q.Element("extra") ?? "", (int)q.Element("bp")!)).ToArray(); }
    }
    public int PositiveQualityKarma => SelectedQualities.Sum(q => Math.Max(0,q.Karma));
    public int NegativeQualityKarma => SelectedQualities.Sum(q => Math.Max(0,-q.Karma));
    internal string SelectedQualitySource(string id) => document.Root!.Element("qualities")!.Elements().Single(q => (string?)q.Element("guid") == id).Element("sourceid")!.Value;
    internal void ReplaceDraftWith(CharacterDocument updated) { RequireDraft(); document.Root!.ReplaceWith(new XElement(updated.document.Root!)); }
    internal void AddSelectedQuality(XElement quality, XElement[] effects, int cost)
    {
        RequireDraft(); document.Root!.Element("qualities")!.Add(quality); document.Root.Element("improvements")!.Add(effects);
        SetField("karma", (Karma-cost).ToString(CultureInfo.InvariantCulture));
    }
    internal void RemoveSelectedQuality(string id)
    {
        RequireDraft(); var quality = SelectedQualities.SingleOrDefault(q => q.Id == id) ?? throw new ArgumentException("Unknown selected quality.");
        if (Karma + quality.Karma < 0) throw new ArgumentException("Refund other Karma purchases before removing this negative quality.");
        document.Root!.Element("qualities")!.Elements().Single(q => (string?)q.Element("guid") == id).Remove();
        document.Root.Element("improvements")!.Elements().Where(e => (string?)e.Element("improvementsource") == "Quality" && (string?)e.Element("sourcename") == id).Remove();
        if (DraftKnowledgeSkills.Count(s => s.Native) > 1 + DraftEffect("NativeLanguageLimit")) throw new ArgumentException("Remove the additional native language before removing Bilingual.");
        SetField("karma", (Karma+quality.Karma).ToString(CultureInfo.InvariantCulture));
    }
}
