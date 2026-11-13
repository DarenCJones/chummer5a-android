// SPDX-License-Identifier: GPL-3.0-or-later
using System.Text;
using System.Xml;
using System.Xml.Linq;
using Chummer.Portable;

var root = Path.GetFullPath(args.Length > 0 ? args[0] : "..");
int checkedFiles = 0;
foreach (var file in Directory.EnumerateFiles(Path.Combine(root, "Chummer.Tests", "TestFiles"), "*.chum5"))
{
    using var input = File.OpenRead(file);
    var character = CharacterDocument.Load(input);
    using var saved = new MemoryStream();
    character.Save(saved);
    saved.Position = 0;
    var original = XDocument.Load(file);
    var roundTrip = XDocument.Load(saved);
    if (!XNode.DeepEquals(original, roundTrip)) throw new Exception($"Round-trip changed XML: {file}");
    checkedFiles++;
}
if (checkedFiles == 0) throw new Exception("No fixtures tested.");
using (var data = File.OpenRead(Path.Combine(root, "Chummer", "data", "weapons.xml")))
    if (RuleCatalog.Load(data, "weapons", "weapon").Count == 0) throw new Exception("Empty weapons catalog.");
foreach (var invalid in new[] { "<other/>", "<!DOCTYPE character [<!ENTITY x SYSTEM 'file:///missing'>]><character>&x;</character>" })
{
    try
    {
        using var input = new MemoryStream(Encoding.UTF8.GetBytes(invalid));
        CharacterDocument.Load(input);
        throw new Exception("Unsafe or invalid document accepted.");
    }
    catch (InvalidDataException) { }
    catch (XmlException) { }
}
Console.WriteLine($"PASS: {checkedFiles} upstream characters preserved; weapons catalog loaded; invalid root and DTD rejected.");

static void Assert(bool condition, string message) { if (!condition) throw new Exception(message); }
static CharacterDocument Parse(string xml) => CharacterDocument.Load(new MemoryStream(Encoding.UTF8.GetBytes(xml)));
static XDocument Xml(CharacterDocument c)
{
    using var stream = new MemoryStream(); c.Save(stream); stream.Position = 0; return XDocument.Load(stream);
}
static void Reject(Action action)
{
    try { action(); } catch (ArgumentException) { return; } catch (InvalidOperationException) { return; }
    throw new Exception("Invalid edit was accepted.");
}
var edited = Parse("<character><name>A</name><created>True</created><karma>5</karma><nuyen>20.500</nuyen><future><nested flag='keep'>unknown</nested></future></character>");
var unknown = new XElement(Xml(edited).Root!.Element("future")!);
edited.SetProfile(new Dictionary<string, string> { ["name"] = "<New & Name>", ["background"] = "Line 1\nLine 2" });
edited.SetDamage(3, 2);
edited.AddTransaction("Karma", 4, "Session award", new DateTime(2080, 1, 2, 3, 4, 5));
edited.AddTransaction("Nuyen", -2.125m, "Supplies", new DateTime(2080, 1, 2));
Assert(edited.Karma == 9 && edited.Nuyen == 18.375m && edited.Transactions.Count == 2, "Incorrect career balance/history.");
Assert(edited.PhysicalDamage == 3 && edited.StunDamage == 2, "Damage not saved.");
Assert(XNode.DeepEquals(unknown, Xml(edited).Root!.Element("future")), "Unknown character data changed during editing.");
Assert(Xml(edited).Root!.Element("name")!.Value == "<New & Name>", "Profile XML escaping failed.");
Assert(Xml(edited).Descendants("karmatype").First().Value == "ManualAdd", "Desktop undo schema mismatch.");
Assert(Xml(edited).Descendants("nuyentype").Last().Value == "ManualSubtract", "Desktop Nuyen schema mismatch.");
var beforeRejection = Xml(edited);
Reject(() => edited.AddTransaction("Karma", -100, "Overspend", DateTime.Now));
Reject(() => edited.AddTransaction("Karma", 0.5m, "Fraction", DateTime.Now));
Reject(() => edited.AddTransaction("Nuyen", 0.0001m, "Precision", DateTime.Now));
Reject(() => edited.SetProfile(new Dictionary<string, string> { ["name"] = "changed", ["karma"] = "9999" }));
Reject(() => edited.SetDamage(-1, 0));
Assert(XNode.DeepEquals(beforeRejection, Xml(edited)), "Rejected operation changed character.");
Reject(() => Parse("<character><created>False</created></character>").AddTransaction("Karma", 1, "Invalid", DateTime.Now));
var clone = edited.Clone(); clone.SetProfile(new Dictionary<string, string> { ["alias"] = "Copy" });
Assert(edited.Alias != clone.Alias, "Clone shares mutable state.");

PoolRoll Roll(RollOptions options, params int[] values)
{
    var queue = new Queue<int>(values); return DicePool.Roll(options, () => queue.Dequeue());
}
var standard = Roll(new RollOptions(4), 1, 1, 5, 6);
Assert(standard.Hits == 2 && standard.Glitch && !standard.CriticalGlitch, "Standard glitch threshold mismatch.");
Assert(Roll(new RollOptions(3), 1, 1, 2).CriticalGlitch, "Critical glitch mismatch.");
var explosion = Roll(new RollOptions(2, Exploding: true), 6, 6, 5, 2);
Assert(explosion.Hits == 3 && explosion.Dice.Length == 4, "Exploding dice mismatch.");
var bubbleRoll = Roll(new RollOptions(4, Bubble: true), 1, 3, 5, 6, 1);
Assert(bubbleRoll.Hits == 2 && bubbleRoll.Glitch && bubbleRoll.BubbleDie == 1, "Bubble die incorrectly counted as hit or ignored.");
Assert(Roll(new RollOptions(2, Cinematic: true, Rushed: true), 2, 4).Glitch, "Optional dice thresholds mismatch.");
Assert(Roll(new RollOptions(4, Gremlins: 1), 1, 3, 5, 6).Glitch, "Gremlins mismatch.");
Assert(Roll(new RollOptions(3, VariableGlitch: true), 1, 2, 3).CriticalGlitch, "Variable glitch mismatch.");
Reject(() => DicePool.Roll(new RollOptions(0)));
Reject(() => DicePool.Roll(new RollOptions(1), () => 7));
PoolRoll Reroll(PoolRoll previous, RollOptions options, params int[] values)
{
    var queue = new Queue<int>(values); return DicePool.RerollMisses(previous,options,()=>queue.Dequeue());
}
var rerollOriginal = Roll(new RollOptions(4),1,1,5,6);
var rerollOriginalDice = rerollOriginal.Dice.ToArray();
var rerolledGlitch = Reroll(rerollOriginal,new RollOptions(4),5,6);
Assert(rerolledGlitch.Hits == 4 && rerolledGlitch.Glitch && !rerolledGlitch.CriticalGlitch && rerollOriginal.Dice.SequenceEqual(rerollOriginalDice), "Reroll lost old hits/glitch or mutated the original roll.");
var rerolledCritical = Reroll(Roll(new RollOptions(3),1,1,2),new RollOptions(3),5,5,5);
Assert(rerolledCritical.Hits == 3 && rerolledCritical.CriticalGlitch, "Desktop critical-glitch preservation mismatch.");
var rerolledNewGlitch = Reroll(Roll(new RollOptions(4),2,3,5,6),new RollOptions(4),1,1);
Assert(rerolledNewGlitch.Hits == 2 && rerolledNewGlitch.Glitch, "New reroll glitch was not detected.");
var rerolledExplosion = Reroll(Roll(new RollOptions(2,Exploding:true),5,2),new RollOptions(2,Exploding:true),6,6,5);
Assert(rerolledExplosion.Hits == 4 && rerolledExplosion.Dice.Length == 4, "Exploding reroll did not retain hits or continue sixes.");
var rerolledCinematic = Reroll(Roll(new RollOptions(2,Cinematic:true),4,2),new RollOptions(2,Cinematic:true),5);
Assert(rerolledCinematic.Dice.SequenceEqual(new[]{4,5}) && rerolledCinematic.Hits == 2, "Cinematic reroll discarded a hit.");
var rerolledBubble = Reroll(bubbleRoll,new RollOptions(4,Bubble:true),5,5);
Assert(!rerolledBubble.Glitch && rerolledBubble.Hits == 4 && rerolledBubble.BubbleDie is null, "Old bubble die was included in the desktop reroll.");
var variableReroll = Reroll(Roll(new RollOptions(3,VariableGlitch:true),5,2,3),new RollOptions(3,VariableGlitch:true,Bubble:true),5,5,1);
Assert(variableReroll.Hits == 3 && variableReroll.BubbleDie == 1 && !variableReroll.Glitch, "Variable threshold/bubble reroll mismatch.");
Reject(()=>DicePool.RerollMisses(Roll(new RollOptions(2),5,6),new RollOptions(2)));
Reject(()=>DicePool.RerollMisses(standard,new RollOptions(4),()=>7));
Console.WriteLine("PASS: miss rerolls retain hits and desktop glitch state; exploding/cinematic dice, new glitches, variable thresholds and old/new bubble handling.");
var largeRoll = Roll(new RollOptions(4,Method:RollMethod.Large),2,3,4,6);
Assert(largeRoll.Hits == 3 && !largeRoll.Glitch && largeRoll.MeetsThreshold(3) && !largeRoll.MeetsThreshold(4), "Large mode or success threshold mismatch.");
var largeReroll = Reroll(largeRoll,new RollOptions(4,Method:RollMethod.Large),3);
Assert(largeReroll.Hits == 4 && largeReroll.Dice.SequenceEqual(new[]{3,4,6,3}), "Large-mode reroll discarded its 3/4 hits.");
var reallyLargeRoll = Roll(new RollOptions(3,Method:RollMethod.ReallyLarge),1,3,6);
Assert(reallyLargeRoll.Hits == 10 && reallyLargeRoll.Glitch && !reallyLargeRoll.CriticalGlitch, "Really Large total or desktop glitch behavior mismatch.");
Assert(!Roll(new RollOptions(3,VariableGlitch:true,Method:RollMethod.ReallyLarge),1,3,6).Glitch, "Really Large variable threshold mismatch.");
Reject(()=>DicePool.RerollMisses(reallyLargeRoll,new RollOptions(3,Method:RollMethod.ReallyLarge)));
Reject(()=>DicePool.Roll(new RollOptions(2,Method:(RollMethod)99)));
Reject(()=>largeRoll.MeetsThreshold(0));
Console.WriteLine("PASS: desktop Large/Really Large roll methods, method-specific rerolls and threshold success/failure comparisons.");

var tracker = new InitiativeTracker(); tracker.Add("A", 21); tracker.Add("B", 11); tracker.Add("C", 21);
Assert(string.Join(",", tracker.Order.Select(e => e.Name)) == "A,C,B", "Initiative order/tie mismatch.");
tracker.NextPass(); tracker.NextPass();
Assert(tracker.Pass == 3 && tracker.Order.Count == 2 && tracker.Order[0].Score == 1, "Initiative passes mismatch.");
var restored = InitiativeTracker.Restore(tracker.Pass, tracker.All);
Assert(restored.Order.SequenceEqual(tracker.Order), "Initiative persistence mismatch.");
restored.Clear(); Assert(restored.Pass == 1 && restored.Order.Count == 0, "Initiative reset failed.");

int catalogs = 0;
foreach (var file in Directory.EnumerateFiles(Path.Combine(root, "Chummer", "data"), "*.xml"))
{
    using var input = File.OpenRead(file); RuleCatalog.LoadAll(input); catalogs++;
}
Console.WriteLine($"PASS: profile/ledger/damage edits preserve unknown data; rejected edits are atomic; dice options and initiative pass checks; {catalogs} catalogs parsed.");

using var priorityData = File.OpenRead(Path.Combine(root, "Chummer", "data", "priorities.xml"));
using var metatypeData = File.OpenRead(Path.Combine(root, "Chummer", "data", "metatypes.xml"));
using var skillData = File.OpenRead(Path.Combine(root, "Chummer", "data", "skills.xml"));
using var qualityData = File.OpenRead(Path.Combine(root, "Chummer", "data", "qualities.xml"));
using var gearData = File.OpenRead(Path.Combine(root, "Chummer", "data", "gear.xml"));
var creation = new CreationRules(priorityData, metatypeData, skillData, qualityData, gearData);
var priorities = new PriorityChoice("Priority", "D", "A", "B", "C");
var budget = creation.Budget(priorities);
Assert(budget.Attributes == 24 && budget.Special == 3 && budget.Skills == 36 && budget.SkillGroups == 5 && budget.Nuyen == 140000,
    "Standard priority budgets mismatch upstream data.");
Reject(() => creation.Budget(priorities with { Heritage = "A" }));
Reject(() => creation.Budget(priorities with { Method = "Karma" }));
Reject(() => creation.Budget(priorities with { Resources = "Z" }));
Assert(creation.Budget(new PriorityChoice("SumtoTen", "B", "C", "B", "C")).Attributes == 16, "Sum-to-Ten budget mismatch.");
Reject(() => creation.Budget(new PriorityChoice("SumtoTen", "A", "A", "A", "A")));
var draft = creation.CreateHumanDraft("New human", "Runner", priorities);
Assert(draft.IsAndroidDraft && !draft.IsCareer && draft.Nuyen == 140000 && draft.DraftAttributes.Single(e => e.Name == "EDG").Rating == 2,
    "Draft initialization mismatch.");
draft.SetDraftAttributes(new Dictionary<string, int> {
    ["BOD"] = 4, ["AGI"] = 4, ["REA"] = 4, ["STR"] = 4, ["CHA"] = 4, ["INT"] = 4, ["LOG"] = 4, ["WIL"] = 4, ["EDG"] = 5 });
Assert(draft.AttributePointsRemaining == 0 && draft.SpecialPointsRemaining == 0, "Attribute budget accounting mismatch.");
var stats = draft.DraftStats();
Assert(stats.PhysicalLimit == 6 && stats.MentalLimit == 6 && stats.SocialLimit == 6 && stats.Initiative == 8 &&
    stats.PhysicalBoxes == 10 && stats.StunBoxes == 10 && stats.KnowledgePoints == 16 && stats.ContactPoints == 12,
    "Unaugmented derived statistics mismatch.");
var draftBefore = Xml(draft);
Reject(() => draft.SetDraftAttributes(new Dictionary<string, int> { ["BOD"] = 5 }));
Reject(() => draft.SetDraftAttributes(new Dictionary<string, int> { ["EDG"] = 6 }));
Reject(() => draft.SetDraftAttributes(new Dictionary<string, int> { ["BOD"] = 7 }));
Assert(XNode.DeepEquals(draftBefore, Xml(draft)), "Rejected creation edit changed data.");
var maximumDraft = creation.CreateHumanDraft("Max test", "", priorities);
Reject(() => maximumDraft.SetDraftAttributes(new Dictionary<string, int> { ["BOD"] = 6, ["AGI"] = 6 }));
var pistols = creation.Skills(draft).Single(s => s.Name == "Pistols");
draft.SetDraftSkill(pistols.Id, 6);
Assert(draft.SkillPointsRemaining == 30 && creation.Skills(draft).Single(s => s.Id == pistols.Id).Rating == 6, "Skill allocation accounting mismatch.");
Reject(() => draft.SetDraftSkill(pistols.Id, 7));
Reject(() => edited.SetDraftAttributes(new Dictionary<string, int> { ["BOD"] = 3 }));
using (var roundTrip = new MemoryStream())
{
    draft.Save(roundTrip); roundTrip.Position = 0;
    Assert(CharacterDocument.Load(roundTrip).DraftStats() == stats, "Draft statistics changed after reopening.");
}
Console.WriteLine("PASS: Priority/Sum-to-Ten budgets, mundane Human drafts, attribute/skill limits and unaugmented derived statistics.");

draft.SetDraftGroup("Cracking", 3);
Assert(draft.GroupPointsRemaining == 2 && creation.Skills(draft).Single(s => s.Name == "Hacking").Rating == 3, "Group rating not applied to member skills.");
var hacking = creation.Skills(draft).Single(s => s.Name == "Hacking");
Reject(() => draft.SetDraftSkill(hacking.Id, 4));
Reject(() => draft.SetDraftGroup("Firearms", 2));
Reject(() => draft.SetDraftGroup("Cracking", 6));
draft.SetDraftGroup("Cracking", 0); draft.SetDraftSkill(hacking.Id, 2);
Reject(() => draft.SetDraftGroup("Cracking", 1));
draft.SetDraftKnowledge(null, "English", "Language", 0, true);
draft.SetDraftKnowledge(null, "History", "Academic", 6, false);
draft.SetDraftKnowledge(null, "Security", "Professional", 6, false);
draft.SetDraftKnowledge(null, "Japanese", "Language", 4, false);
Assert(draft.KnowledgePointsRemaining == 0, "Knowledge budget mismatch.");
var knowledgeBefore = Xml(draft);
Reject(() => draft.SetDraftKnowledge(null, "French", "Language", 0, true));
Reject(() => draft.SetDraftKnowledge(null, "History", "Academic", 1, false));
Reject(() => draft.SetDraftKnowledge(null, "Politics", "Interest", 1, false));
Reject(() => draft.SetDraftAttributes(new Dictionary<string, int> { ["LOG"] = 3 }));
Assert(XNode.DeepEquals(knowledgeBefore, Xml(draft)), "Rejected knowledge/group/attribute edit mutated data.");
var japanese = draft.DraftKnowledgeSkills.Single(k => k.Name == "Japanese");
draft.SetDraftKnowledge(japanese.Id, "Japanese", "Language", 3, false);
Assert(draft.KnowledgePointsRemaining == 1, "Editing knowledge failed to refund points.");
draft.RemoveDraftKnowledge(japanese.Id);
Assert(draft.KnowledgePointsRemaining == 4, "Removing knowledge failed to refund points.");
Console.WriteLine("PASS: skill group allocation/conflicts, knowledge budgets, native languages and refunds after editing/removal.");

int compressedCount = 0;
foreach (var file in Directory.EnumerateFiles(Path.Combine(root, "Chummer.Tests", "TestFiles"), "*.chum5"))
{
    using var input = File.OpenRead(file); var plain = CharacterDocument.Load(input);
    using var compressed = new MemoryStream(); plain.SaveCompressed(compressed); compressed.Position = 0;
    var decompressed = CharacterDocument.LoadCompressed(compressed);
    Assert(XNode.DeepEquals(Xml(plain), Xml(decompressed)), $"Compressed XML changed: {file}");
    compressedCount++;
}
using (var compressed = new MemoryStream())
{
    draft.SaveCompressed(compressed);
    var data = compressed.ToArray();
    foreach (var bad in new[] { data[..5], data[..(data.Length / 2)] })
    {
        bool rejected = false;
        try { CharacterDocument.LoadCompressed(new MemoryStream(bad)); }
        catch (IOException) { rejected = true; }
        catch (ArgumentException) { rejected = true; }
        Assert(rejected, "Truncated compressed data accepted.");
    }
    var huge = data.ToArray();
    System.Buffers.Binary.BinaryPrimitives.WriteInt64LittleEndian(huge.AsSpan(5, 8), long.MaxValue);
    bool sizeRejected = false;
    try { CharacterDocument.LoadCompressed(new MemoryStream(huge)); } catch (InvalidDataException) { sizeRejected = true; }
    Assert(sizeRejected, "Oversized decompressed length accepted.");
}
Console.WriteLine($"PASS: {compressedCount} compressed character round trips using upstream LZMA code; truncated and oversized inputs rejected.");
var htmlCharacter = Parse("<character><name>&lt;script&gt;alert(1)&lt;/script&gt;</name><background>&lt;img src='https://example.invalid/x'&gt;</background></character>");
var summaryHtml = CharacterSheet.SummaryHtml(htmlCharacter);
Assert(!summaryHtml.Contains("<script>") && !summaryHtml.Contains("<img ") && summaryHtml.Contains("&lt;script&gt;"), "Summary HTML did not escape character text.");
Assert(CharacterSheet.SummaryHtml(draft).Contains("Unfinished mundane Human draft"), "Printed draft lacks creation status.");
Console.WriteLine("PASS: printable summaries escape user text and identify unfinished drafts.");
draft.SetDraftContact(null, "Fixer", "Broker", "Seattle", 4, 3);
draft.SetDraftContact(null, "Medic", "Street doc", "Seattle", 3, 2);
Assert(draft.DraftContactPointsRemaining == 0, "Contact point budget mismatch.");
var contactsBefore = Xml(draft);
Reject(() => draft.SetDraftContact(null, "Extra", "", "", 1, 1));
Reject(() => draft.SetDraftContact(null, "Too costly", "", "", 6, 6));
Reject(() => draft.SetDraftAttributes(new Dictionary<string, int> { ["CHA"] = 3 }));
Assert(XNode.DeepEquals(contactsBefore, Xml(draft)), "Rejected contact edit mutated character data.");
var medic = draft.Contacts.Single(c => c.Name == "Medic");
draft.SetDraftContact(medic.Id, "Medic", "Street doc", "Tacoma", 2, 2);
Assert(draft.DraftContactPointsRemaining == 1, "Contact editing did not refund points.");
draft.RemoveDraftContact(medic.Id);
Assert(draft.DraftContactPointsRemaining == 5, "Contact removal did not refund points.");
Console.WriteLine("PASS: standard draft contacts, per-contact caps, Charisma budget protection and refunds.");
var allMetatypePriorities = new PriorityChoice("Priority", "A", "B", "C", "D");
foreach (var race in CreationRules.CoreMetatypes)
{
    var racialDraft = creation.CreateMundaneDraft("Racial test", "", allMetatypePriorities, race);
    Assert(racialDraft.IsAndroidDraft && racialDraft.Metatype == race && racialDraft.DraftStats().PhysicalBoxes > 0,
        $"Core metatype initialization failed: {race}");
    Assert(Xml(racialDraft).Root!.Element("metavariantid") is not null && racialDraft.Field("walk") == "2/1/0", "Desktop metatype/movement schema mismatch.");
}
var troll = creation.CreateMundaneDraft("Troll", "", allMetatypePriorities, "Troll");
Assert(troll.DraftRacialBonus("Armor") == 1 && troll.DraftRacialBonus("Reach") == 1 && troll.DraftRacialBonus("LifestyleCost") == 100,
    "Troll racial effects mismatch.");
Assert(troll.DraftAttributes.Single(a => a.Name == "BOD").Minimum == 5 && troll.DraftStats().PhysicalBoxes == 11 &&
    troll.DraftRacialQualities.Contains("Thermographic Vision"), "Troll natural limits/traits mismatch.");
var dwarf = creation.CreateMundaneDraft("Dwarf", "", allMetatypePriorities, "Dwarf");
Assert(dwarf.DraftRacialBonus("PathogenContactResist") == 2 && dwarf.DraftRacialBonus("ToxinInjectionResist") == 2 &&
    dwarf.DraftRacialBonus("LifestyleCost") == 20, "Dwarf racial effects mismatch.");
var elf = creation.CreateMundaneDraft("Elf", "", allMetatypePriorities, "Elf");
Assert(elf.DraftAttributes.Single(a => a.Name == "AGI").Minimum == 2 && elf.DraftAttributes.Single(a => a.Name == "CHA").Minimum == 3 &&
    elf.DraftRacialQualities.Contains("Low-Light Vision"), "Elf natural limits/traits mismatch.");
Reject(() => creation.CreateMundaneDraft("Invalid", "", priorities, "Troll"));
var legacyGender = Parse("<character><sex>Female</sex></character>");
Assert(legacyGender.Field("gender") == "Female", "Legacy gender fallback failed.");
legacyGender.SetProfile(new Dictionary<string,string> { ["gender"] = "Nonbinary" });
Assert(legacyGender.Field("gender") == "Nonbinary" && legacyGender.Field("sex") == "Nonbinary", "Modern/legacy gender fields diverged.");
Console.WriteLine("PASS: five core mundane metatypes, racial improvement records, natural limits and legacy/modern profile compatibility.");
var magicianPriority = new PriorityChoice("Priority", "E", "B", "C", "D", "A", "Magician");
var mage = creation.CreateDraft("Mage", "", magicianPriority, "Human");
Assert(mage.DraftAttributes.Single(a => a.Name == "MAG").Rating == 6 && mage.Field("magician") == "True" && mage.TalentSkillChoicesRequired == 2,
    "Magician starting Magic/flags/grants mismatch.");
creation.AssignTalentSkills(mage, ["Spellcasting", "Counterspelling"]);
var spellcasting = creation.Skills(mage).Single(s => s.Name == "Spellcasting");
Assert(spellcasting.Rating == 5 && mage.SkillPointsRemaining == 28, "Free priority skills consumed paid points.");
mage.SetDraftSkill(spellcasting.Id, 6);
Assert(mage.SkillPointsRemaining == 27, "Skill increase above free priority rating charged incorrectly.");
Reject(() => mage.SetDraftSkill(spellcasting.Id, 4));
Reject(() => creation.AssignTalentSkills(mage, ["Pistols", "Counterspelling"]));
Reject(() => creation.AssignTalentSkills(mage, ["Spellcasting", "Spellcasting"]));
var aspectedPriority = new PriorityChoice("Priority", "D", "A", "C", "E", "B", "Aspected Magician", "Sorcery");
var aspected = creation.CreateDraft("Aspected", "", aspectedPriority, "Human");
Assert(aspected.DraftAttributes.Single(a => a.Name == "MAG").Rating == 5 && aspected.DraftGroups.Single(g => g.Name == "Sorcery").Rating == 4 &&
    aspected.GroupPointsRemaining == 2 && !creation.Skills(aspected).Any(s => s.Name == "Summoning"), "Aspected magician skill/group restrictions mismatch.");
aspected.SetDraftGroup("Sorcery", 6);
Assert(aspected.GroupPointsRemaining == 0 && creation.Skills(aspected).Single(s => s.Name == "Spellcasting").Rating == 6, "Free group increase charged incorrectly.");
var techno = creation.CreateDraft("Technomancer", "", magicianPriority with { Path = "Technomancer" }, "Human");
Assert(techno.DraftAttributes.Single(a => a.Name == "RES").Rating == 6 && techno.TalentSkillChoices.Count == 3 &&
    creation.Skills(techno).Single(s => s.Name == "Compiling").Rating == 5 && techno.SkillPointsRemaining == 28,
    "Technomancer starting Resonance/free skills mismatch.");
Assert(techno.OwnedItems("gears", "gear").Contains("Living Persona") && techno.Field("activecommlink").Length > 0, "Technomancer living persona missing.");
techno.SetDraftGroup("Tasking", 1);
Reject(() => techno.SetDraftGroup("Tasking", 2));
var adept = creation.CreateDraft("Adept", "", new PriorityChoice("Priority", "D", "A", "B", "E", "C", "Adept"), "Human");
Assert(adept.Field("adept") == "True" && adept.Field("magician") == "False" && adept.DraftAttributes.Single(a => a.Name == "MAG").Rating == 4 &&
    !creation.Skills(adept).Any(s => s.Name == "Spellcasting"), "Adept flags/skill restrictions mismatch.");
var mystic = creation.CreateDraft("Mystic", "", aspectedPriority with { Path = "Mystic Adept" }, "Human");
Assert(mystic.Field("adept") == "True" && mystic.Field("magician") == "True", "Mystic adept talent flags mismatch.");
Reject(() => creation.Budget(magicianPriority with { Talent = "D" }));
Console.WriteLine("PASS: Magician, Adept, Mystic Adept, Aspected Magician and Technomancer priorities, skill grants and living persona setup.");
using var spellData = File.OpenRead(Path.Combine(root, "Chummer", "data", "spells.xml"));
using var formData = File.OpenRead(Path.Combine(root, "Chummer", "data", "complexforms.xml"));
using var traditionData = File.OpenRead(Path.Combine(root, "Chummer", "data", "traditions.xml"));
using var streamData = File.OpenRead(Path.Combine(root, "Chummer", "data", "streams.xml"));
var magicRules = new MagicRules(spellData, formData, traditionData, streamData);
magicRules.SetTradition(mage, "Hermetic");
Assert(mage.TraditionName == "Hermetic" && mage.DraftDrainPool() == 2, "Hermetic tradition/drain mismatch.");
mage.SetDraftAttributes(new Dictionary<string,int> { ["WIL"] = 4, ["LOG"] = 4 });
Assert(mage.DraftDrainPool() == 8, "Drain pool did not follow attribute changes.");
magicRules.SetTradition(mage, "Shamanic");
Assert(mage.DraftDrainPool() == 5, "Shamanic drain pool mismatch.");
var plainSpells = magicRules.Spells(mage).Where(s => !s.RequiresExtra).Take(11).ToArray();
foreach (var spell in plainSpells) magicRules.LearnSpell(mage, spell.Id);
Assert(mage.LearnedSpells.Count == 11 && mage.Karma == 20, "Free spell allowance / extra Karma cost mismatch.");
var beforeSpellReject = Xml(mage);
Reject(() => magicRules.LearnSpell(mage, plainSpells[0].Id));
Assert(XNode.DeepEquals(beforeSpellReject, Xml(mage)), "Rejected duplicate spell changed data.");
mage.RemoveDraftAbility("spells", mage.LearnedSpells[0].Id);
Assert(mage.Karma == 25 && mage.LearnedSpells.Count == 10, "Spell removal did not refund the marginal Karma cost.");
var increase = magicRules.Spells(mage).Single(s => s.Name == "Increase [Attribute]");
Reject(() => magicRules.LearnSpell(mage, increase.Id));
Reject(() => magicRules.LearnSpell(mage, increase.Id, "EDG"));
magicRules.LearnSpell(mage, increase.Id, "bod");
Assert(mage.LearnedSpells.Last().Extra == "BOD", "Attribute spell choice was not normalized.");
var watcher = magicRules.Spells(mage).Single(s => s.Name == "Watcher");
if (!mage.LearnedSpells.Any(s => s.Name == "Watcher")) magicRules.LearnSpell(mage, watcher.Id);
var watcherLearned = mage.LearnedSpells.Single(s => s.Name == "Watcher");
Assert(Xml(mage).Descendants("improvement").Any(i => (string?)i.Element("improvementttype") == "AddSpirit" &&
    (string?)i.Element("sourcename") == watcherLearned.Id), "Watcher ritual spirit grant missing.");
mage.RemoveDraftAbility("spells", watcherLearned.Id);
Assert(!Xml(mage).Descendants("improvement").Any(i => (string?)i.Element("sourcename") == watcherLearned.Id), "Ritual removal left its spirit grant behind.");
magicRules.SetTradition(techno, "Default");
Assert(techno.DraftDrainPool() == 7, "Technomancer default fading pool mismatch.");
foreach (var form in magicRules.ComplexForms(techno).Where(f => !f.RequiresExtra).Take(8)) magicRules.LearnComplexForm(techno, form.Id);
Assert(techno.LearnedForms.Count == 8 && techno.Karma == 21, "Complex form free allowance / Karma cost mismatch.");
techno.RemoveDraftAbility("complexforms", techno.LearnedForms[0].Id);
Assert(techno.Karma == 25, "Complex form removal did not refund marginal Karma cost.");
var diffusion = magicRules.ComplexForms(techno).Single(f => f.Name == "Diffusion of [Matrix Attribute]");
magicRules.LearnComplexForm(techno, diffusion.Id, "Firewall");
Assert(techno.LearnedForms.Last().Extra == "Firewall", "Complex form matrix-attribute choice missing.");
Assert(MagicRules.EvaluateDifficulty("F-3", 6) == 3 && MagicRules.EvaluateDifficulty("L-2", 1) == 2, "Drain/fading expression evaluation mismatch.");
Reject(() => MagicRules.EvaluateDifficulty("Special", 3));
Reject(() => magicRules.LearnSpell(draft, plainSpells[0].Id));
Assert(magicRules.Spells(adept).All(s => s.Category == "Rituals"), "Adept spell-category restriction mismatch.");
Console.WriteLine("PASS: traditions, drain/fading pools, spell/form learning, priority allowances, Karma refunds, parameter choices and ritual spirit grants.");
var legacyDraftXml = Xml(draft);
var removedSkill = legacyDraftXml.Root!.Element("newskills")!.Element("skills")!.Elements("skill").First();
var removedSkillId = removedSkill.Element("suid")!.Value; removedSkill.Remove();
using (var legacyBuffer = new MemoryStream())
{
    legacyDraftXml.Save(legacyBuffer); legacyBuffer.Position = 0;
    var olderDraft = CharacterDocument.Load(legacyBuffer);
    Assert(!creation.Skills(olderDraft).Any(s => s.Id == removedSkillId), "A missing legacy skill was treated as a persisted skill.");
}
Console.WriteLine("PASS: older drafts remain readable when the current catalog contains additional skills.");
var magicalSheet = CharacterSheet.SummaryHtml(mage);
Assert(magicalSheet.Contains("Magic and resonance") && magicalSheet.Contains("Shamanic") &&
    magicalSheet.Contains("Unaugmented drain / fading resistance: 5 dice") && magicalSheet.Contains("F-"),
    "Magical summary omitted the tradition, resistance pool or learned ability difficulty.");
var technoSheet = CharacterSheet.SummaryHtml(techno);
Assert(technoSheet.Contains("Default") && technoSheet.Contains("Firewall") && technoSheet.Contains("Complex form"),
    "Technomancer summary omitted the stream or parameterized ability.");
Console.WriteLine("PASS: magical and technomancer summaries include traditions/streams, resistance pools and learned ability details.");
using var powerData = File.OpenRead(Path.Combine(root, "Chummer", "data", "powers.xml"));
var powerRules = new PowerRules(powerData);
var powerAdept = adept.Clone();
var pistol = creation.Skills(powerAdept).Single(s => s.Name == "Pistols");
powerAdept.SetDraftSkill(pistol.Id, 3);
foreach (var entry in powerRules.Catalog)
{
    var candidate = powerAdept.Clone(); var choices = powerRules.Choices(entry.Id, candidate, creation);
    var extra = choices.FirstOrDefault() ?? (powerRules.FreeTextChoice(entry.Id) ? "Low-Light Vision" : "");
    powerRules.Learn(candidate, creation, entry.Id, 1, extra);
    Assert(candidate.LearnedPowers.Count == 1 && candidate.PowerPointsRemaining == 4 - entry.PointsPerLevel - entry.ExtraCost,
        "Core power cost mismatch: " + entry.Name);
    using var compressed = new MemoryStream(); candidate.SaveCompressed(compressed); compressed.Position = 0;
    var reopenedPower = CharacterDocument.LoadCompressed(compressed);
    Assert(XNode.DeepEquals(Xml(candidate), Xml(reopenedPower)), "Power data or effects changed during compressed export.");
    _ = reopenedPower.EffectiveStats();
}
Assert(powerRules.Catalog.Count == 27, "Unexpected visible core adept power coverage.");
string PowerId(string name) => powerRules.Catalog.Single(p => p.Name == name).Id;
powerRules.Learn(powerAdept, creation, PowerId("Improved Reflexes"), 2);
powerRules.Learn(powerAdept, creation, PowerId("Mystic Armor"), 2);
powerRules.Learn(powerAdept, creation, PowerId("Combat Sense"), 1);
var augmented = powerAdept.EffectiveStats();
Assert(augmented.Attributes["REA"] == 3 && augmented.Initiative == 4 && augmented.InitiativeDice == 3 &&
    augmented.Armor == 2 && augmented.DefensePool == 5 && powerAdept.PowerPointsRemaining == 0,
    "Adept attribute, initiative, armor or defense effects mismatch.");
Assert(powerAdept.DraftRacialBonus("Armor") == 0, "Adept armor was reported as racial armor.");
var beforePowerReject = Xml(powerAdept);
Reject(() => powerRules.Learn(powerAdept, creation, PowerId("Danger Sense"), 1));
Reject(() => powerRules.Learn(powerAdept, creation, PowerId("Improved Reflexes"), 4));
Assert(XNode.DeepEquals(beforePowerReject, Xml(powerAdept)), "Rejected power changed character data.");
powerAdept.RemoveDraftPower(powerAdept.LearnedPowers.Single(p => p.Name == "Mystic Armor").Id);
Assert(powerAdept.EffectiveStats().Armor == 0 && powerAdept.PowerPointsRemaining == 1, "Power removal did not remove effects/refund PP.");
var skilledAdept = adept.Clone(); skilledAdept.SetDraftSkill(pistol.Id, 3);
powerRules.Learn(skilledAdept, creation, PowerId("Improved Ability (skill)"), 2, "Pistols");
Assert(skilledAdept.DraftSkillPool(creation.Skills(skilledAdept).Single(s => s.Name == "Pistols")) == 6 && skilledAdept.SkillPointsRemaining == adept.SkillPointsRemaining - 3,
    "Improved Ability did not affect the pool independently of paid skill points.");
Reject(() => skilledAdept.SetDraftSkill(pistol.Id, 2));
var physicalAdept = adept.Clone(); powerRules.Learn(physicalAdept, creation, PowerId("Improved Physical Attribute"), 1, "BOD");
Assert(physicalAdept.EffectiveStats().Attributes["BOD"] == 2 && physicalAdept.DraftAttributes.Single(a => a.Name == "BOD").Rating == 1 &&
    physicalAdept.AttributePointsRemaining == adept.AttributePointsRemaining, "Power effects changed natural allocation.");
powerRules.Learn(physicalAdept, creation, PowerId("Killing Hands"), 1);
powerRules.Learn(physicalAdept, creation, PowerId("Critical Strike"), 1, "Unarmed Combat");
Assert(physicalAdept.DraftUnarmedAttack().Damage == "2P", "Unarmed adept damage/type mismatch.");
var mysticPowers = mystic.Clone(); mysticPowers.SetMysticPowerPoints(4);
Assert(mysticPowers.Karma == 5 && mysticPowers.PowerPointsTotal == 4 && mysticPowers.Field("magsplitadept") == "4", "Mystic adept PP purchase/save field mismatch.");
powerRules.Learn(mysticPowers, creation, PowerId("Improved Reflexes"), 1);
Reject(() => mysticPowers.SetMysticPowerPoints(1)); mysticPowers.SetMysticPowerPoints(2);
Assert(mysticPowers.Karma == 15 && mysticPowers.PowerPointsRemaining == 0.5m, "Mystic adept PP refund mismatch.");
Reject(() => draft.SetMysticPowerPoints(1));
var limitedAdept = adept.Clone(); limitedAdept.SetDraftAttributes(new Dictionary<string,int> { ["MAG"] = 5 });
powerRules.Learn(limitedAdept, creation, PowerId("Improved Reflexes"), 3);
powerRules.Learn(limitedAdept, creation, PowerId("Mystic Armor"), 2);
var beforeMagicReduction = Xml(limitedAdept);
Reject(() => limitedAdept.SetDraftAttributes(new Dictionary<string,int> { ["MAG"] = 4 }));
Assert(XNode.DeepEquals(beforeMagicReduction, Xml(limitedAdept)), "Lowering Magic overspent power points or partially changed attributes.");
var painAdept = adept.Clone(); painAdept.SetDamage(6, 3);
Assert(painAdept.EffectiveStats().WoundPenalty == 3, "Separate monitor wound penalties mismatch.");
powerRules.Learn(painAdept, creation, PowerId("Pain Resistance"), 2);
Assert(painAdept.EffectiveStats().WoundPenalty == 1, "Pain Resistance did not offset both monitors.");
var limitAdept = adept.Clone(); powerRules.Learn(limitAdept, creation, PowerId("Improved Potential (Social)"), 1);
Assert(limitAdept.EffectiveStats().SocialLimit == limitAdept.DraftStats().SocialLimit + 1 && Xml(limitAdept).Descendants("improvement").Any(e =>
    (string?)e.Element("improvementttype") == "SocialLimit" && (string?)e.Element("improvedname") == "Social"), "Limit bonus or desktop improvement name mismatch.");
Console.WriteLine("PASS: 27 visible core adept powers, costs/choices, compatible improvement records, augmented statistics, removal and mystic adept Karma purchases/refunds.");
using var armorData = File.OpenRead(Path.Combine(root, "Chummer", "data", "armor.xml"));
using var weaponData = File.OpenRead(Path.Combine(root, "Chummer", "data", "weapons.xml"));
var equipmentRules = new EquipmentRules(armorData, weaponData);
var equippedDraft = physicalAdept.Clone(); equippedDraft.SetDraftAttributes(new Dictionary<string,int> { ["STR"] = 4 });
var jacket = equipmentRules.Catalog.Single(e => e.Name == "Armor Jacket");
var knife = equipmentRules.Catalog.Single(e => e.Name == "Combat Knife");
var helmet = equipmentRules.Catalog.Single(e => e.Name == "Helmet");
var startCash = equippedDraft.Nuyen;
equipmentRules.Purchase(equippedDraft, jacket.Id, true); equipmentRules.Purchase(equippedDraft, knife.Id, true); equipmentRules.Purchase(equippedDraft, helmet.Id, true);
Assert(equippedDraft.Nuyen == startCash - jacket.Cost - knife.Cost - helmet.Cost && equippedDraft.EffectiveStats().Armor == 14 &&
    equippedDraft.DraftWeapons().Single().Damage == "6P" && equippedDraft.DraftWeapons().Single().Accuracy == 6, "Equipment costs, stacking or weapon expressions mismatch.");
equippedDraft.SetDraftAttributes(new Dictionary<string,int> { ["STR"] = 1 });
equipmentRules.Purchase(equippedDraft, helmet.Id, true);
Assert(equippedDraft.EffectiveStats().Armor == 13 && equippedDraft.EffectiveStats().ArmorPenalty == 1 && equippedDraft.DraftWeapons().Single().Damage == "3P",
    "Bonus armor cap/encumbrance or Strength-linked damage did not update.");
var jacketItem = equippedDraft.DraftEquipment.Single(e => e.Name == "Armor Jacket");
equippedDraft.EquipDraftItem(jacketItem.Id, false);
Assert(equippedDraft.EffectiveStats().Armor == 1, "Unequipping armor left its rating active.");
var cashBeforeRefund = equippedDraft.Nuyen; equippedDraft.RemoveDraftEquipment(jacketItem.Id);
Assert(equippedDraft.Nuyen == cashBeforeRefund + jacket.Cost, "Creation equipment removal did not refund its purchase price.");
var emptyWallet = adept.Clone();
while (emptyWallet.Nuyen >= jacket.Cost) equipmentRules.Purchase(emptyWallet, jacket.Id, false);
var beforePurchaseReject = Xml(emptyWallet);
Reject(() => equipmentRules.Purchase(emptyWallet, jacket.Id, true));
Assert(XNode.DeepEquals(beforePurchaseReject, Xml(emptyWallet)), "Rejected purchase changed the wallet or inventory.");
Assert(!equipmentRules.Catalog.Any(e => e.Name == "Ares Predator V"), "Bundled smartgun accessory was silently discarded.");
var effectSheet = CharacterSheet.SummaryHtml(equippedDraft);
Assert(effectSheet.Contains("Adept powers") && effectSheet.Contains("Killing Hands") && effectSheet.Contains("Combat Knife: 3P") &&
    effectSheet.Contains("Helmet: Equipped"), "Printable summary omitted powers, weapon effects or equipment state.");
using (var equipmentBuffer = new MemoryStream())
{
    equippedDraft.SaveCompressed(equipmentBuffer); equipmentBuffer.Position = 0;
    var reopenedEquipment = CharacterDocument.LoadCompressed(equipmentBuffer);
    Assert(XNode.DeepEquals(Xml(equippedDraft), Xml(reopenedEquipment)) && reopenedEquipment.DraftWeapons().Single().Damage == "3P" &&
        reopenedEquipment.EffectiveStats().Armor == 1, "Equipment/power state or calculated effects changed after reopening.");
}
Console.WriteLine($"PASS: {equipmentRules.Catalog.Count} supported core armor/weapons, Nuyen budgets/refunds, equipped effects, bonus armor caps/penalties and Strength-linked weapon statistics.");
var specialist = adept.Clone(); specialist.SetDraftSkill(pistol.Id, 3);
var pointsBeforeSpec = specialist.SkillPointsRemaining; var karmaBeforeSpec = specialist.Karma;
specialist.AddDraftSpecialization(pistol.Id, "Semi-Automatics", false);
var specialistSkill = creation.Skills(specialist).Single(s => s.Id == pistol.Id);
Assert(specialist.SkillPointsRemaining == pointsBeforeSpec - 1 && specialist.Karma == karmaBeforeSpec &&
    specialist.DraftSpecializedPool(specialistSkill, "Semi-Automatics") == specialist.DraftSkillPool(specialistSkill) + 2, "Specialization point cost or pool bonus mismatch.");
var specBeforeReject = Xml(specialist);
Reject(() => specialist.AddDraftSpecialization(pistol.Id, "Revolvers", true));
Reject(() => specialist.SetDraftSkill(pistol.Id, 0));
Assert(XNode.DeepEquals(specBeforeReject, Xml(specialist)), "Rejected specialization/skill change modified data.");
specialist.RemoveDraftSpecialization(pistol.Id);
Assert(specialist.SkillPointsRemaining == pointsBeforeSpec && specialist.Karma == karmaBeforeSpec, "Point-paid specialization refund mismatch.");
specialist.AddDraftSpecialization(pistol.Id, "Revolvers", true);
Assert(specialist.Karma == karmaBeforeSpec - 7 && specialist.SkillPointsRemaining == pointsBeforeSpec &&
    Xml(specialist).Descendants("spec").Any(s => (string?)s.Element("name") == "Revolvers" && (string?)s.Element("free") == "False"), "Karma specialization cost or save schema mismatch.");
specialist.RemoveDraftSpecialization(pistol.Id);
Assert(specialist.Karma == karmaBeforeSpec, "Karma-paid specialization did not refund its cost.");
using var qualityDataForOptions = File.OpenRead(Path.Combine(root,"Chummer","data","qualities.xml"));
var qualityRules = new QualityRules(qualityDataForOptions);
string QualityId(string name) => qualityRules.Catalog.Single(q => q.Name == name).Id;
var qualityRunner = adept.Clone(); var sneak = creation.Skills(qualityRunner).Single(s => s.Name == "Sneaking"); qualityRunner.SetDraftSkill(sneak.Id, 2);
var sneakPool = qualityRunner.DraftSkillPool(creation.Skills(qualityRunner).Single(s => s.Id == sneak.Id));
qualityRules.Add(qualityRunner, QualityId("Catlike"));
Assert(qualityRunner.Karma == 18 && qualityRunner.PositiveQualityKarma == 7 && qualityRunner.DraftSkillPool(creation.Skills(qualityRunner).Single(s => s.Id == sneak.Id)) == sneakPool+2,
    "Catlike cost/limit or skill-pool effect mismatch.");
qualityRules.Add(qualityRunner, QualityId("Bad Rep"));
Assert(qualityRunner.Karma == 25 && qualityRunner.NegativeQualityKarma == 7, "Negative quality reward mismatch.");
qualityRules.Remove(qualityRunner, qualityRunner.SelectedQualities.Single(q => q.Name == "Catlike").Id);
Assert(qualityRunner.Karma == 32 && qualityRunner.DraftSkillPool(creation.Skills(qualityRunner).Single(s => s.Id == sneak.Id)) == sneakPool,
    "Quality removal did not reverse cost/effects.");
qualityRules.Remove(qualityRunner, qualityRunner.SelectedQualities.Single().Id);
Assert(qualityRunner.Karma == 25 && qualityRunner.SelectedQualities.Count == 0, "Negative quality removal did not reverse reward.");
var bilingualRunner = adept.Clone(); qualityRules.Add(bilingualRunner, QualityId("Bilingual"));
bilingualRunner.SetDraftKnowledge(null,"English","Language",0,true); bilingualRunner.SetDraftKnowledge(null,"Spanish","Language",0,true);
Reject(() => bilingualRunner.SetDraftKnowledge(null,"French","Language",0,true));
var bilingualBeforeReject = Xml(bilingualRunner);
Reject(() => qualityRules.Remove(bilingualRunner, bilingualRunner.SelectedQualities.Single().Id));
Assert(XNode.DeepEquals(bilingualBeforeReject,Xml(bilingualRunner)), "Removing Bilingual partially removed a required native-language grant.");
bilingualRunner.RemoveDraftKnowledge(bilingualRunner.DraftKnowledgeSkills.Single(s => s.Name == "Spanish").Id);
qualityRules.Remove(bilingualRunner,bilingualRunner.SelectedQualities.Single().Id);
var painRunner = adept.Clone(); qualityRules.Add(painRunner,QualityId("High Pain Tolerance")); painRunner.SetDamage(3,3);
Assert(painRunner.EffectiveStats().WoundPenalty == 0, "High Pain Tolerance threshold offsets were not applied.");
Reject(() => powerRules.Learn(painRunner,creation,PowerId("Pain Resistance"),1));
var resistedRunner = adept.Clone(); powerRules.Learn(resistedRunner,creation,PowerId("Pain Resistance"),1);
Reject(() => qualityRules.Add(resistedRunner,QualityId("High Pain Tolerance")));
var blandRunner = adept.Clone(); qualityRules.Add(blandRunner,QualityId("Blandness"));
Reject(() => qualityRules.Add(blandRunner,QualityId("Distinctive Style")));
var distinctiveRunner = adept.Clone(); qualityRules.Add(distinctiveRunner,QualityId("Distinctive Style"));
Reject(() => qualityRules.Add(distinctiveRunner,QualityId("Blandness")));
Reject(() => qualityRules.Add(adept.Clone(),QualityId("Human-Looking")));
var qualityLimitRunner = adept.Clone(); qualityRules.Add(qualityLimitRunner,QualityId("Bad Rep"));
qualityRules.Add(qualityLimitRunner,QualityId("Allergy (Common, Moderate)"),"Peanuts");
var limitBeforeReject = Xml(qualityLimitRunner);
Reject(() => qualityRules.Add(qualityLimitRunner,QualityId("Low Pain Tolerance")));
Assert(XNode.DeepEquals(limitBeforeReject,Xml(qualityLimitRunner)), "Negative quality limit failure changed Karma or effects.");
var conditionRunner = adept.Clone(); var logicSkill = creation.Skills(conditionRunner).First(s => s.Attribute == "LOG"); conditionRunner.SetDraftSkill(logicSkill.Id,1);
var beforeCondition = conditionRunner.DraftSkillPool(creation.Skills(conditionRunner).Single(s => s.Id == logicSkill.Id));
qualityRules.Add(conditionRunner,QualityId("Analytical Mind"));
Assert(conditionRunner.DraftSkillPool(creation.Skills(conditionRunner).Single(s => s.Id == logicSkill.Id)) == beforeCondition &&
    conditionRunner.DraftEffectDescriptions().Any(s => s.Contains("pattern recognition")), "Conditional quality was applied unconditionally or lost its condition.");
var logicTestSkill = creation.Skills(conditionRunner).Single(s => s.Id == logicSkill.Id);
var logicConditions = conditionRunner.DraftConditionalSkillBonuses(logicTestSkill);
Assert(logicConditions.Count == 1 && conditionRunner.DraftContextualSkillPool(logicTestSkill,null,logicConditions.Select(c => c.Id)) == beforeCondition+2,
    "Selected conditional quality bonus was not applied to its test.");
Reject(() => conditionRunner.DraftContextualSkillPool(logicTestSkill,null,["not-a-valid-condition"]));
var defaultRunner = adept.Clone(); defaultRunner.SetDraftAttributes(new Dictionary<string,int> { ["AGI"] = 4 });
var defaultPistol = creation.Skills(defaultRunner).Single(s => s.Name == "Pistols");
Assert(defaultPistol.Defaultable && defaultRunner.DraftSkillPool(defaultPistol) == 3, "Defaultable skill did not use attribute minus one.");
var noDefaultSkill = creation.Skills(defaultRunner).First(s => !s.Defaultable && s.Rating == 0);
Reject(() => defaultRunner.DraftSkillPool(noDefaultSkill));
defaultRunner.SetDamage(3,0);
Assert(defaultRunner.DraftSkillPool(defaultPistol) == 2, "Defaulting ignored wound penalties.");
var toughnessRunner = adept.Clone(); var soakBeforeQuality = toughnessRunner.EffectiveStats().SoakPool; qualityRules.Add(toughnessRunner,QualityId("Toughness"));
Assert(toughnessRunner.EffectiveStats().SoakPool == soakBeforeQuality+1, "Toughness damage resistance missing.");
specialist.AddDraftSpecialization(pistol.Id,"Semi-Automatics",false); qualityRules.Add(specialist,QualityId("Catlike"));
using (var optionsBuffer = new MemoryStream())
{
    specialist.SaveCompressed(optionsBuffer); optionsBuffer.Position=0; var reopenedOptions = CharacterDocument.LoadCompressed(optionsBuffer);
    Assert(XNode.DeepEquals(Xml(specialist),Xml(reopenedOptions)) && reopenedOptions.SelectedQualities.Count == 1 && reopenedOptions.DraftSpecializations.Count == 1,
        "Quality/specialization data changed after compressed save/reopen.");
}
Assert(CharacterSheet.SummaryHtml(specialist).Contains("Semi-Automatics (+2 only when applicable)") && CharacterSheet.SummaryHtml(specialist).Contains("Catlike"),
    "Character summary omitted selected qualities/specializations.");
Console.WriteLine($"PASS: active skill specializations, point/Karma costs and refunds; {qualityRules.Catalog.Count} supported quality templates, requirements/conflicts, limits, conditions, language grants and effect removal.");
using var cyberData=File.OpenRead(Path.Combine(root,"Chummer","data","cyberware.xml"));
using var bioData=File.OpenRead(Path.Combine(root,"Chummer","data","bioware.xml"));
var augmentationRules=new AugmentationRules(cyberData,bioData);
string ImplantId(string name)=>augmentationRules.Catalog.Single(a=>a.Name==name).Id;
var wiredQuote=augmentationRules.Quote(ImplantId("Wired Reflexes"),1,"Alphaware");
Assert(wiredQuote.Cost==46800 && wiredQuote.Essence==1.6m && wiredQuote.Availability==10,"Alphaware cost/Essence/availability multipliers mismatch.");
var usedBoneQuote=augmentationRules.Quote(ImplantId("Bone Lacing (Titanium)"),0,"Used");
Assert(usedBoneQuote.Cost==22500 && usedBoneQuote.Essence==1.88m && usedBoneQuote.Availability==12,"Used grade multipliers/rounding mismatch.");
Reject(()=>augmentationRules.Quote(ImplantId("Synaptic Booster"),1,"Used"));
Reject(()=>augmentationRules.Quote(ImplantId("Wired Reflexes"),2,"Alphaware"));
Reject(()=>augmentationRules.Quote(ImplantId("Muscle Toner"),5,"Standard"));
var augmentedRunner=draft.Clone();var cashBeforeImplants=augmentedRunner.Nuyen;
augmentationRules.Purchase(augmentedRunner,ImplantId("Wired Reflexes"),1,"Alphaware");
augmentationRules.Purchase(augmentedRunner,ImplantId("Muscle Toner"),1,"Standard");
augmentationRules.Purchase(augmentedRunner,ImplantId("Dermal Plating"),1,"Standard");
Assert(augmentedRunner.DraftEssence==3.7m && augmentedRunner.Nuyen==cashBeforeImplants-81800 &&
    augmentedRunner.EffectiveStats().Attributes["AGI"]==5 && augmentedRunner.EffectiveStats().Attributes["REA"]==5 &&
    augmentedRunner.EffectiveStats().InitiativeDice==2 && augmentedRunner.EffectiveStats().Armor==1,"Implant wallet, Essence or permanent effects mismatch.");
var implantBeforeReject=Xml(augmentedRunner);
Reject(()=>augmentationRules.Purchase(augmentedRunner,ImplantId("Orthoskin"),1,"Standard"));
Reject(()=>augmentationRules.Purchase(augmentedRunner,ImplantId("Wired Reflexes"),1,"Standard"));
Assert(XNode.DeepEquals(implantBeforeReject,Xml(augmentedRunner)),"Rejected implant changed inventory, Essence or wallet.");
var wiredInstalled=augmentedRunner.InstalledAugmentations.Single(a=>a.Name=="Wired Reflexes");
augmentationRules.Remove(augmentedRunner,wiredInstalled.Id);
Assert(augmentedRunner.DraftEssence==5.3m && augmentedRunner.EffectiveStats().InitiativeDice==1 && augmentedRunner.EffectiveStats().Attributes["REA"]==4 &&
    augmentedRunner.Nuyen==cashBeforeImplants-35000,"Removing implant did not reverse cost, Essence and effects.");
var richPriorities=new PriorityChoice("Priority","D","B","C","A");
var heavilyAugmented=creation.CreateDraft("Augmented","",richPriorities,"Human");
augmentationRules.Purchase(heavilyAugmented,ImplantId("Wired Reflexes"),2,"Standard");
augmentationRules.Purchase(heavilyAugmented,ImplantId("Muscle Replacement"),2,"Standard");
Assert(heavilyAugmented.DraftEssence==1 && heavilyAugmented.EffectiveStats().SocialLimit==2,"Essence reduction did not affect the social limit.");
var beforeEssenceReject=Xml(heavilyAugmented);
Reject(()=>augmentationRules.Purchase(heavilyAugmented,ImplantId("Bone Lacing (Titanium)"),0,"Used"));
Assert(XNode.DeepEquals(beforeEssenceReject,Xml(heavilyAugmented)),"Essence overspend was not atomic.");
augmentationRules.Purchase(heavilyAugmented,ImplantId("Muscle Toner"),1,"Standard");
Assert(heavilyAugmented.EffectiveStats().Attributes["AGI"]==3,"Muscle replacement and toner were incorrectly added instead of using the unique effect group.");
var reactions=creation.CreateDraft("Reaction test","",richPriorities,"Human");
augmentationRules.Purchase(reactions,ImplantId("Wired Reflexes"),1,"Standard");
augmentationRules.Purchase(reactions,ImplantId("Reaction Enhancers"),2,"Standard");
Assert(reactions.EffectiveStats().Attributes["REA"]==3 && reactions.EffectiveStats().InitiativeDice==2,"Non-wireless reaction enhancement precedence mismatch.");
using(var implantBuffer=new MemoryStream())
{
    augmentedRunner.SaveCompressed(implantBuffer);implantBuffer.Position=0;var reopenedImplants=CharacterDocument.LoadCompressed(implantBuffer);
    Assert(XNode.DeepEquals(Xml(augmentedRunner),Xml(reopenedImplants)) && reopenedImplants.DraftEssence==5.3m && reopenedImplants.InstalledAugmentations.Any(a=>a.Kind=="Bioware"),"Implant save/reopen lost source kind, Essence or effects.");
}
var augmentedAdept=adept.Clone();var adeptSpecialBefore=augmentedAdept.SpecialPointsRemaining;
augmentationRules.Purchase(augmentedAdept,ImplantId("Dermal Plating"),1,"Standard");
Assert(augmentedAdept.DraftAttributes.Single(a=>a.Name=="MAG").Rating==3 && augmentedAdept.DraftAttributes.Single(a=>a.Name=="MAG").Maximum==5 &&
    augmentedAdept.SpecialPointsRemaining==adeptSpecialBefore && augmentedAdept.PowerPointsTotal==3,"Adept Essence loss changed point spending or omitted Magic/PP reduction.");
var essenceXml=Xml(augmentedAdept).Descendants("improvement").Single(e=>(string?)e.Element("improvementsource")=="EssenceLossChargen" && (string?)e.Element("improvedname")=="MAG");
Assert((int?)essenceXml.Element("min")==-1 && (int?)essenceXml.Element("max")==-1 && (int?)essenceXml.Element("aug")==0,"Essence loss did not use desktop creation min/max improvement records.");
augmentationRules.Remove(augmentedAdept,augmentedAdept.InstalledAugmentations.Single().Id);
Assert(augmentedAdept.DraftAttributes.Single(a=>a.Name=="MAG").Rating==4 && augmentedAdept.PowerPointsTotal==4,"Removing a creation implant did not restore Magic and power points.");
var wealthyAdept=creation.CreateDraft("Wealthy adept","",new PriorityChoice("Priority","D","B","E","A","C","Adept"),"Human");
var fullPowerAdept=wealthyAdept.Clone();powerRules.Learn(fullPowerAdept,creation,PowerId("Mystic Armor"),4);
powerRules.Learn(fullPowerAdept,creation,PowerId("Improved Physical Attribute"),2,"STR");
var fullPowerBefore=Xml(fullPowerAdept);
Reject(()=>augmentationRules.Purchase(fullPowerAdept,ImplantId("Wired Reflexes"),1,"Standard"));
Assert(XNode.DeepEquals(fullPowerBefore,Xml(fullPowerAdept)),"Implant Essence loss left an overspent power pool or partially changed data.");
var augmentedTechno=techno.Clone();var originalRES=augmentedTechno.DraftAttributes.Single(a=>a.Name=="RES").Rating;
augmentationRules.Purchase(augmentedTechno,ImplantId("Toxin Extractor"),1,"Standard");
Assert(augmentedTechno.DraftAttributes.Single(a=>a.Name=="RES").Rating==originalRES-1 && augmentedTechno.DraftDrainPool()==techno.DraftDrainPool()-1,"Technomancer Essence loss did not affect Resonance/fading.");
var augmentedMage=creation.CreateDraft("Augmented mage","",magicianPriority,"Human");
augmentationRules.Purchase(augmentedMage,ImplantId("Dermal Plating"),1,"Alphaware");
Assert(augmentedMage.DraftAttributes.Single(a=>a.Name=="MAG").Rating==5 && augmentedMage.DraftAttributes.Single(a=>a.Name=="MAG").Minimum==5,"Magician priority minimum was not adjusted for Essence loss.");
using(var awakenedBuffer=new MemoryStream())
{
    augmentedMage.SaveCompressed(awakenedBuffer);awakenedBuffer.Position=0;var reopenedAwakened=CharacterDocument.LoadCompressed(awakenedBuffer);
    Assert(XNode.DeepEquals(Xml(augmentedMage),Xml(reopenedAwakened)) && reopenedAwakened.DraftAttributes.Single(a=>a.Name=="MAG").Rating==5,
        "Awakened Essence loss changed after reopening.");
    augmentationRules.Remove(reopenedAwakened,reopenedAwakened.InstalledAugmentations.Single().Id);
    Assert(reopenedAwakened.DraftAttributes.Single(a=>a.Name=="MAG").Rating==6,"Removing a saved awakened implant did not restore Magic.");
}
var mysticWithPoints=mystic.Clone();mysticWithPoints.SetMysticPowerPoints(4);var mysticBeforeLoss=Xml(mysticWithPoints);
Reject(()=>augmentationRules.Purchase(mysticWithPoints,ImplantId("Dermal Plating"),1,"Standard"));
Assert(XNode.DeepEquals(mysticBeforeLoss,Xml(mysticWithPoints)),"Implant reduced Magic below purchased mystic adept PP without revalidation.");
var initiativeAdept=wealthyAdept.Clone();powerRules.Learn(initiativeAdept,creation,PowerId("Improved Reflexes"),1);
Reject(()=>augmentationRules.Purchase(initiativeAdept,ImplantId("Reaction Enhancers"),1,"Standard"));
var initiativeImplantAdept=wealthyAdept.Clone();augmentationRules.Purchase(initiativeImplantAdept,ImplantId("Reaction Enhancers"),1,"Standard");
Reject(()=>powerRules.Learn(initiativeImplantAdept,creation,PowerId("Improved Reflexes"),1));
var burnoutDraft=creation.CreateDraft("Burnout guard","",new PriorityChoice("Priority","E","B","C","A","D","Adept"),"Human");
var beforeBurnout=Xml(burnoutDraft);Reject(()=>augmentationRules.Purchase(burnoutDraft,ImplantId("Wired Reflexes"),2,"Standard"));
Assert(XNode.DeepEquals(beforeBurnout,Xml(burnoutDraft)),"Unsupported burnout partially changed the character.");
Console.WriteLine($"PASS: {augmentationRules.Catalog.Count} core implant templates, grades, cost/Essence/availability, conflicts, non-stacking effects, social limit and atomic removal/refunds.");
using var gearInventoryData=File.OpenRead(Path.Combine(root,"Chummer","data","gear.xml"));
using var gearWeaponData=File.OpenRead(Path.Combine(root,"Chummer","data","weapons.xml"));
var gearRules=new GearRules(gearInventoryData,gearWeaponData);
var regularAmmo=gearRules.Catalog.Single(g=>g.Name=="Ammo: Regular Ammo");
var apdsAmmo=gearRules.Catalog.Single(g=>g.Name=="Ammo: APDS");
Assert(gearRules.Quote(regularAmmo.Id,100)==200 && gearRules.Quote(regularAmmo.Id,1)==2 && gearRules.Quote(apdsAmmo.Id,10)==120,"Ammunition pack pricing mismatch.");
var gearRunner=draft.Clone();var beforeGearCash=gearRunner.Nuyen;
gearRules.Purchase(gearRunner,regularAmmo.Id,100,"Heavy Pistols");gearRules.Purchase(gearRunner,apdsAmmo.Id,10,"Assault Rifles");
Assert(gearRunner.Nuyen==beforeGearCash-320 && gearRunner.DraftPurchasedGear.Sum(g=>g.Quantity)==110 &&
    Xml(gearRunner).Descendants("gear").Any(g=>(string?)g.Element("name")=="Ammo: APDS" && (string?)g.Element("weaponbonus")?.Element("ap")=="-4"),"Ammo purchase lost price, quantity, category or damage/AP metadata.");
var beforeGearReject=Xml(gearRunner);
Reject(()=>gearRules.Purchase(gearRunner,regularAmmo.Id,1,"invalid-category"));Reject(()=>gearRules.Purchase(gearRunner,regularAmmo.Id,1000000,"Heavy Pistols"));
Assert(XNode.DeepEquals(beforeGearReject,Xml(gearRunner)),"Rejected gear purchase changed the wallet or inventory.");
gearRunner.RemoveDraftPurchasedGear(gearRunner.DraftPurchasedGear.Single(g=>g.Name==regularAmmo.Name).Id);
Assert(gearRunner.Nuyen==beforeGearCash-120 && gearRunner.DraftPurchasedGear.Single().Quantity==10,"Gear stack removal/refund mismatch.");
using(var gearBuffer=new MemoryStream())
{
    gearRunner.SaveCompressed(gearBuffer);gearBuffer.Position=0;var reopenedGear=CharacterDocument.LoadCompressed(gearBuffer);
    Assert(XNode.DeepEquals(Xml(gearRunner),Xml(reopenedGear)) && reopenedGear.DraftPurchasedGear.Single().Extra=="Assault Rifles","Ammo/gear quantities or category changed after reopening.");
}
Assert(System.Net.WebUtility.HtmlDecode(CharacterSheet.SummaryHtml(gearRunner)).Contains("Ammo: APDS Assault Rifles × 10"),"Printed summary omitted ammunition quantities.");
Console.WriteLine($"PASS: {gearRules.Catalog.Count} supported gear/ammo inventory entries, pack pricing, quantities, categories, metadata preservation and atomic refunds.");
using var accessoryData=File.OpenRead(Path.Combine(root,"Chummer","data","weapons.xml"));var accessoryRules=new AccessoryRules(accessoryData);
string AccessoryId(string name)=>accessoryRules.Catalog.Single(a=>a.Name==name).Id;
var gunRunner=draft.Clone();var gunCash=gunRunner.Nuyen;var ak=equipmentRules.Catalog.Single(w=>w.Name=="AK-97");
equipmentRules.Purchase(gunRunner,ak.Id,true);var gun=gunRunner.DraftEquipment.Single(w=>w.Name=="AK-97");
accessoryRules.Attach(gunRunner,gun.Id,AccessoryId("Laser Sight"),"Top");accessoryRules.Attach(gunRunner,gun.Id,AccessoryId("Bipod"),"Under");
Assert(gunRunner.DraftWeapons().Single().Accuracy==6 && gunRunner.DraftWeapons().Single().AccessoryRecoil==0 && gunRunner.DraftWeapons().Single().DeployedAccessoryRecoil==2 &&
    gunRunner.Nuyen==gunCash-1275,"Accessory accuracy, deployed recoil or purchase cost mismatch.");
var beforeMountReject=Xml(gunRunner);Reject(()=>accessoryRules.Attach(gunRunner,gun.Id,AccessoryId("Tripod"),"Under"));
Reject(()=>accessoryRules.Attach(gunRunner,gun.Id,AccessoryId("Laser Sight"),"Side"));
Assert(XNode.DeepEquals(beforeMountReject,Xml(gunRunner)),"Invalid mount purchase changed weapon or cash.");
var sight=gunRunner.DraftAccessories.Single(a=>a.Name=="Laser Sight");gunRunner.EquipDraftAccessory(gun.Id,sight.Id,false);
Assert(gunRunner.DraftWeapons().Single().Accuracy==5,"Disabled accessory still affected accuracy.");gunRunner.EquipDraftAccessory(gun.Id,sight.Id,true);
using(var accessoryBuffer=new MemoryStream())
{
    gunRunner.SaveCompressed(accessoryBuffer);accessoryBuffer.Position=0;var reopenedAccessories=CharacterDocument.LoadCompressed(accessoryBuffer);
    Assert(XNode.DeepEquals(Xml(gunRunner),Xml(reopenedAccessories)) && reopenedAccessories.DraftWeapons().Single().Accuracy==6 && reopenedAccessories.DraftAccessories.Count==2,"Accessory state or effects changed after reopening.");
}
var parentRemoval=gunRunner.Clone();parentRemoval.RemoveDraftEquipment(gun.Id);
Assert(parentRemoval.Nuyen==gunCash && parentRemoval.DraftAccessories.Count==0,"Removing a weapon did not refund its purchased accessories.");
gunRunner.RemoveDraftAccessory(gun.Id,sight.Id);Assert(gunRunner.Nuyen==gunCash-1150 && gunRunner.DraftWeapons().Single().Accuracy==5,"Accessory removal/refund did not reverse effects.");
Console.WriteLine("PASS: weapon accessory mounts, costs, accuracy, deployed recoil, disabled effects, parent/child refunds and compressed round trips.");
var firingRunner=draft.Clone();var firingStartCash=firingRunner.Nuyen;
equipmentRules.Purchase(firingRunner,ak.Id,true);var firingWeapon=firingRunner.DraftEquipment.Single(w=>w.Name=="AK-97");
gearRules.Purchase(firingRunner,apdsAmmo.Id,100,"Assault Rifles");var reserveAmmo=firingRunner.DraftPurchasedGear.Single();
var walletBeforeLoad=firingRunner.Nuyen;firingRunner.LoadDraftWeapon(firingWeapon.Id,reserveAmmo.Id,38);
var loadedState=firingRunner.DraftLoadedAmmunition.Single();
Assert(loadedState.Rounds==38 && loadedState.Capacity==38 && firingRunner.DraftPurchasedGear.Sum(g=>g.Quantity)==100 &&
    firingRunner.DraftPurchasedGear.Sum(g=>g.Cost)==1200 && firingRunner.Nuyen==walletBeforeLoad && firingRunner.DraftWeapons().Single().AP=="-6",
    "Loading duplicated/lost rounds or money, or did not apply APDS penetration.");
var clipXml=Xml(firingRunner).Descendants("clip").Single();
Assert((string?)clipXml.Element("id")==loadedState.GearId && (string?)clipXml.Element("location")=="loaded" &&
    Xml(firingRunner).Descendants("gear").Single(g=>(string?)g.Element("guid")==loadedState.GearId).Element("qty")!.Value=="38",
    "Loaded ammunition did not use the desktop clip/gear reference schema.");
var beforeLoadReject=Xml(firingRunner);
Reject(()=>firingRunner.LoadDraftWeapon(firingWeapon.Id,reserveAmmo.Id,1));
Reject(()=>firingRunner.RemoveDraftPurchasedGear(loadedState.GearId));
Reject(()=>firingRunner.FireDraftWeapon(firingWeapon.Id,2));
Assert(XNode.DeepEquals(beforeLoadReject,Xml(firingRunner)),"Rejected loading/firing operation partially changed data.");
firingRunner.FireDraftWeapon(firingWeapon.Id,3);
Assert(firingRunner.DraftLoadedAmmunition.Single().Rounds==35 && firingRunner.DraftPurchasedGear.Sum(g=>g.Quantity)==97 &&
    firingRunner.DraftPurchasedGear.Sum(g=>g.Cost)==1164 && firingRunner.Nuyen==walletBeforeLoad,"Firing did not consume exactly three rounds and their refundable value.");
using(var firingBuffer=new MemoryStream())
{
    firingRunner.SaveCompressed(firingBuffer);firingBuffer.Position=0;var reopenedFire=CharacterDocument.LoadCompressed(firingBuffer);
    Assert(XNode.DeepEquals(Xml(firingRunner),Xml(reopenedFire)) && reopenedFire.DraftLoadedAmmunition.Single().Rounds==35 && reopenedFire.DraftWeapons().Single().AP=="-6",
        "Loaded rounds, clip references or AP effects changed after reopening.");
}
Assert(CharacterSheet.SummaryHtml(firingRunner).Contains("AK-97: 35/38 rounds; Ammo: APDS"),"Printed summary omitted loaded rounds.");
firingRunner.UnloadDraftWeapon(firingWeapon.Id);
Assert(firingRunner.DraftLoadedAmmunition.Single().Rounds==0 && firingRunner.DraftWeapons().Single().AP=="-2" && firingRunner.DraftPurchasedGear.All(g=>g.LoadedInto.Length==0),
    "Unloading did not preserve remaining rounds or remove the ammunition effect.");
foreach(var stock in firingRunner.DraftPurchasedGear.ToArray())firingRunner.RemoveDraftPurchasedGear(stock.Id);
firingRunner.RemoveDraftEquipment(firingWeapon.Id);
Assert(firingRunner.Nuyen==firingStartCash-36,"Consumed APDS rounds were refunded after removing the remaining stocks and weapon.");
foreach(var (ammoName,expectedDamage,expectedAP) in new[]{("Ammo: Gel Rounds","10S","-1"),("Ammo: Stick-n-Shock","8S(e)","-5"),
    ("Ammo: Flechette Rounds","12P(f)","3"),("Ammo: Explosive Rounds","11P","-3")})
{
    var ammoTest=draft.Clone();equipmentRules.Purchase(ammoTest,ak.Id,true);var w=ammoTest.DraftEquipment.Single(w=>w.Name=="AK-97");
    gearRules.Purchase(ammoTest,gearRules.Catalog.Single(g=>g.Name==ammoName).Id,10,"Assault Rifles");ammoTest.LoadDraftWeapon(w.Id,ammoTest.DraftPurchasedGear.Single().Id,10);
    Assert(ammoTest.DraftWeapons().Single().Damage==expectedDamage && ammoTest.DraftWeapons().Single().AP==expectedAP,"Ammunition damage/AP modifier mismatch: "+ammoName);
    ammoTest.FireDraftWeapon(w.Id,10);Assert(ammoTest.DraftLoadedAmmunition.Single().Rounds==0 && ammoTest.DraftPurchasedGear.Count==0,"Empty magazine left a dangling gear stack or clip.");
    Reject(()=>ammoTest.FireDraftWeapon(w.Id,1));
}
var reloadTest=draft.Clone();equipmentRules.Purchase(reloadTest,ak.Id,true);var reloadWeapon=reloadTest.DraftEquipment.Single(w=>w.Name=="AK-97");
gearRules.Purchase(reloadTest,regularAmmo.Id,100,"Heavy Pistols");var wrongStock=reloadTest.DraftPurchasedGear.Single();
var beforeWrongAmmo=Xml(reloadTest);Reject(()=>reloadTest.LoadDraftWeapon(reloadWeapon.Id,wrongStock.Id,10));
Assert(XNode.DeepEquals(beforeWrongAmmo,Xml(reloadTest)),"Wrong-category ammunition changed inventory.");
gearRules.Purchase(reloadTest,regularAmmo.Id,100,"Assault Rifles");var rightStock=reloadTest.DraftPurchasedGear.Single(g=>g.Extra=="Assault Rifles");
Reject(()=>reloadTest.LoadDraftWeapon(reloadWeapon.Id,rightStock.Id,39));reloadTest.LoadDraftWeapon(reloadWeapon.Id,rightStock.Id,5);
var beforeOverfire=Xml(reloadTest);Reject(()=>reloadTest.FireDraftWeapon(reloadWeapon.Id,6));
Assert(XNode.DeepEquals(beforeOverfire,Xml(reloadTest)),"Insufficient loaded rounds partially consumed ammunition.");
reloadTest.EquipDraftItem(reloadWeapon.Id,false);Reject(()=>reloadTest.FireDraftWeapon(reloadWeapon.Id,1));reloadTest.EquipDraftItem(reloadWeapon.Id,true);
equipmentRules.Purchase(reloadTest,ak.Id,true);var secondGun=reloadTest.DraftEquipment.Single(w=>w.Id!=reloadWeapon.Id);
Reject(()=>reloadTest.LoadDraftWeapon(secondGun.Id,reloadTest.DraftLoadedAmmunition.Single(w=>w.WeaponId==reloadWeapon.Id).GearId,1));
reloadTest.RemoveDraftEquipment(reloadWeapon.Id);
Assert(reloadTest.DraftPurchasedGear.Sum(g=>g.Quantity)==200 && reloadTest.DraftPurchasedGear.All(g=>g.LoadedInto.Length==0),"Removing a loaded weapon lost or duplicated its ammunition.");
Console.WriteLine("PASS: ammunition category/capacity, separate loaded stacks, desktop clip references, firing consumption, damage/AP changes, no double loading/refunding, unloading and save/reopen.");
var limitedModeEntry=equipmentRules.Catalog.First(e=>e.Kind=="weapon" && gearRules.AmmoCategories.Contains(e.Category) &&
    (e.Details.EndsWith("; SS",StringComparison.Ordinal) || e.Details.EndsWith("; SA",StringComparison.Ordinal)));
var limitedModeRunner=draft.Clone();equipmentRules.Purchase(limitedModeRunner,limitedModeEntry.Id,true);var limitedWeapon=limitedModeRunner.DraftEquipment.Single();
gearRules.Purchase(limitedModeRunner,regularAmmo.Id,20,limitedModeEntry.Category);
limitedModeRunner.LoadDraftWeapon(limitedWeapon.Id,limitedModeRunner.DraftPurchasedGear.Single().Id,Math.Min(20,limitedModeRunner.DraftLoadedAmmunition.Single().Capacity));
var beforeInvalidBurst=Xml(limitedModeRunner);Reject(()=>limitedModeRunner.FireDraftWeapon(limitedWeapon.Id,3));
Assert(XNode.DeepEquals(beforeInvalidBurst,Xml(limitedModeRunner)),"Burst fire on a semi-auto/single-shot weapon changed the loaded rounds.");
var aptitudeRunner=creation.CreateDraft("Aptitude","",priorities,"Human");
qualityRules.Add(aptitudeRunner,QualityId("Aptitude"),"pistols");aptitudeRunner.SetDraftSkill(pistol.Id,7);
Assert(aptitudeRunner.DraftSkillMaximum("Pistols")==7 && aptitudeRunner.SkillPointsRemaining==29 && aptitudeRunner.Karma==11 &&
    aptitudeRunner.DraftSkillPool(creation.Skills(aptitudeRunner).Single(s=>s.Id==pistol.Id))==8,"Aptitude limit/cost or ordinary skill pool mismatch.");
var blades=creation.Skills(aptitudeRunner).Single(s=>s.Name=="Blades");Reject(()=>aptitudeRunner.SetDraftSkill(blades.Id,7));
var beforeAptitudeRemoval=Xml(aptitudeRunner);Reject(()=>qualityRules.Remove(aptitudeRunner,aptitudeRunner.SelectedQualities.Single().Id));
Assert(XNode.DeepEquals(beforeAptitudeRemoval,Xml(aptitudeRunner)),"Removing a required skill maximum partially changed the character.");
aptitudeRunner.SetDraftSkill(pistol.Id,6);qualityRules.Remove(aptitudeRunner,aptitudeRunner.SelectedQualities.Single().Id);
Assert(aptitudeRunner.DraftSkillMaximum("Pistols")==6 && aptitudeRunner.Karma==25,"Aptitude removal did not restore the limit/refund.");
var exceptionalRunner=creation.CreateDraft("Exceptional","",priorities,"Human");
Reject(()=>qualityRules.Add(exceptionalRunner,QualityId("Exceptional Attribute"),"EDG"));
qualityRules.Add(exceptionalRunner,QualityId("Exceptional Attribute"),"bod");exceptionalRunner.SetDraftAttributes(new Dictionary<string,int>{["BOD"]=7});
Assert(exceptionalRunner.DraftAttributes.Single(a=>a.Name=="BOD").Maximum==7 && exceptionalRunner.AttributePointsRemaining==18 && exceptionalRunner.DraftStats().PhysicalBoxes==12,
    "Exceptional Attribute maximum, allocation or condition monitor mismatch.");
var beforeExceptionalRemoval=Xml(exceptionalRunner);Reject(()=>qualityRules.Remove(exceptionalRunner,exceptionalRunner.SelectedQualities.Single().Id));
Assert(XNode.DeepEquals(beforeExceptionalRemoval,Xml(exceptionalRunner)),"Exceptional Attribute removal left an invalid rating.");
exceptionalRunner.SetDraftAttributes(new Dictionary<string,int>{["BOD"]=6,["AGI"]=6});
Reject(()=>qualityRules.Remove(exceptionalRunner,exceptionalRunner.SelectedQualities.Single().Id));
exceptionalRunner.SetDraftAttributes(new Dictionary<string,int>{["BOD"]=5});qualityRules.Remove(exceptionalRunner,exceptionalRunner.SelectedQualities.Single().Id);
Assert(exceptionalRunner.Karma==25 && exceptionalRunner.DraftAttributes.Single(a=>a.Name=="BOD").Maximum==6,"Exceptional Attribute removal/refund mismatch.");
var luckyRunner=creation.CreateDraft("Lucky","",new PriorityChoice("Priority","A","B","C","D"),"Human");
qualityRules.Add(luckyRunner,QualityId("Lucky"));luckyRunner.SetDraftAttributes(new Dictionary<string,int>{["EDG"]=8});
Assert(luckyRunner.DraftAttributes.Single(a=>a.Name=="EDG").Maximum==8 && luckyRunner.Karma==13,"Lucky did not raise the human Edge maximum to 8.");
using(var limitsBuffer=new MemoryStream())
{
    luckyRunner.SaveCompressed(limitsBuffer);limitsBuffer.Position=0;var reopenedLimits=CharacterDocument.LoadCompressed(limitsBuffer);
    Assert(XNode.DeepEquals(Xml(luckyRunner),Xml(reopenedLimits)) && reopenedLimits.DraftAttributes.Single(a=>a.Name=="EDG").Rating==8,"Quality-modified rating limits changed after reopening.");
}
Reject(()=>qualityRules.Remove(luckyRunner,luckyRunner.SelectedQualities.Single().Id));
luckyRunner.SetDraftAttributes(new Dictionary<string,int>{["EDG"]=7});qualityRules.Remove(luckyRunner,luckyRunner.SelectedQualities.Single().Id);
Assert(luckyRunner.Karma==25 && luckyRunner.DraftAttributes.Single(a=>a.Name=="EDG").Maximum==7,"Lucky removal failed to restore its cost/limit.");
var giftedMage=creation.CreateDraft("Exceptional magic","",magicianPriority,"Human");
qualityRules.Add(giftedMage,QualityId("Exceptional Attribute"),"MAG");giftedMage.SetDraftAttributes(new Dictionary<string,int>{["MAG"]=7});
augmentationRules.Purchase(giftedMage,ImplantId("Dermal Plating"),1,"Standard");
Assert(giftedMage.DraftAttributes.Single(a=>a.Name=="MAG").Rating==6 && giftedMage.DraftAttributes.Single(a=>a.Name=="MAG").Maximum==6,
    "Exceptional Magic and Essence loss did not combine correctly.");
Reject(()=>qualityRules.Remove(giftedMage,giftedMage.SelectedQualities.Single().Id));
var giftedSkillsMage=creation.CreateDraft("Gifted skill","",magicianPriority,"Human");
qualityRules.Add(giftedSkillsMage,QualityId("Aptitude"),"Spellcasting");creation.AssignTalentSkills(giftedSkillsMage,["Spellcasting","Summoning"]);
giftedSkillsMage.SetDraftGroup("Sorcery",2);
Assert(creation.Skills(giftedSkillsMage).Single(s=>s.Name=="Spellcasting").Rating==7,"Aptitude did not allow a priority grant/group combination at rating 7.");
Reject(()=>creation.AssignTalentSkills(giftedSkillsMage,["Counterspelling","Summoning"]));
Console.WriteLine("PASS: Aptitude, Exceptional Attribute and Lucky choices/limits, point costs, safe removal, maximum-count rules, priority grants and Essence-loss interactions.");
var confidenceRunner=creation.CreateDraft("Confidence","",priorities,"Human");
Reject(()=>qualityRules.Add(confidenceRunner,QualityId("Loss of Confidence"),"Pistols"));
confidenceRunner.SetDraftSkill(pistol.Id,4);confidenceRunner.AddDraftSpecialization(pistol.Id,"Semi-Automatics",false);
var confidentSkill=creation.Skills(confidenceRunner).Single(s=>s.Id==pistol.Id);
Assert(confidenceRunner.DraftSpecializedPool(confidentSkill,"Semi-Automatics")==7,"Specialization setup pool mismatch.");
qualityRules.Add(confidenceRunner,QualityId("Loss of Confidence"),"Pistols");
Assert(confidenceRunner.Karma==35 && confidenceRunner.DraftSkillPool(confidentSkill)==3 &&
    confidenceRunner.DraftSpecializedPool(confidentSkill,"Semi-Automatics")==3 && !confidenceRunner.CanApplyDraftSpecialization(pistol.Id) &&
    confidenceRunner.DraftContextualSkillPool(confidentSkill,"Semi-Automatics",[])==3,"Loss of Confidence failed to penalize the skill or disable specialization bonuses.");
var beforeConfidenceReduction=Xml(confidenceRunner);Reject(()=>confidenceRunner.SetDraftSkill(pistol.Id,3));
Assert(XNode.DeepEquals(beforeConfidenceReduction,Xml(confidenceRunner)),"Skill reduction bypassed the quality's rating prerequisite.");
using(var confidenceBuffer=new MemoryStream())
{
    confidenceRunner.SaveCompressed(confidenceBuffer);confidenceBuffer.Position=0;var reopenedConfidence=CharacterDocument.LoadCompressed(confidenceBuffer);
    Assert(XNode.DeepEquals(Xml(confidenceRunner),Xml(reopenedConfidence)) && !reopenedConfidence.CanApplyDraftSpecialization(pistol.Id),"Specialization suppression was lost after reopening.");
}
qualityRules.Remove(confidenceRunner,confidenceRunner.SelectedQualities.Single().Id);
Assert(confidenceRunner.Karma==25 && confidenceRunner.DraftSpecializedPool(confidentSkill,"Semi-Automatics")==7,"Removing Loss of Confidence did not restore the specialization/pool or reverse its reward.");
Assert(confidenceRunner.DraftContextualSkillPool(confidentSkill,null,[],true)==3,"Default off-hand penalty mismatch.");
qualityRules.Add(confidenceRunner,QualityId("Ambidextrous"));
Assert(confidenceRunner.Karma==21 && confidenceRunner.DraftAmbidextrous && confidenceRunner.DraftContextualSkillPool(confidentSkill,null,[],true)==5,"Ambidextrous cost or off-hand penalty removal mismatch.");
Reject(()=>qualityRules.Add(confidenceRunner,QualityId("Ambidextrous")));
qualityRules.Remove(confidenceRunner,confidenceRunner.SelectedQualities.Single().Id);
Assert(!confidenceRunner.DraftAmbidextrous && confidenceRunner.DraftContextualSkillPool(confidentSkill,null,[],true)==3,"Ambidextrous removal did not restore the off-hand penalty.");
Console.WriteLine("PASS: Loss of Confidence prerequisite/penalty/specialization suppression, save/reopen and removal; Ambidextrous costs, core-body limits and off-hand pools.");
var sensitiveRunner=creation.CreateDraft("Sensitive","",richPriorities,"Human");
augmentationRules.Purchase(sensitiveRunner,ImplantId("Bone Lacing (Plastic)"),0,"Used");
Assert(sensitiveRunner.DraftEssence==5.37m,"Used plastic bone lacing rounding setup mismatch.");
qualityRules.Add(sensitiveRunner,QualityId("Sensitive System"));
Assert(sensitiveRunner.Karma==37 && sensitiveRunner.DraftEssence==4.75m && sensitiveRunner.InstalledAugmentations.Single().Essence==1.25m,
    "Sensitive System did not update existing cyberware before rounding.");
var sensitiveQuote=augmentationRules.Quote(ImplantId("Bone Lacing (Plastic)"),0,"Used",sensitiveRunner);
Assert(sensitiveQuote.Essence==1.25m,"Sensitive System purchase quote multiplied an already rounded Essence cost.");
var beforeBioReject=Xml(sensitiveRunner);Reject(()=>augmentationRules.Purchase(sensitiveRunner,ImplantId("Muscle Toner"),1,"Standard"));
Reject(()=>augmentationRules.Quote(ImplantId("Muscle Toner"),1,"Standard",sensitiveRunner));
Assert(XNode.DeepEquals(beforeBioReject,Xml(sensitiveRunner)),"A prohibited bioware purchase changed character data.");
using(var sensitiveBuffer=new MemoryStream())
{
    sensitiveRunner.SaveCompressed(sensitiveBuffer);sensitiveBuffer.Position=0;var reopenedSensitive=CharacterDocument.LoadCompressed(sensitiveBuffer);
    Assert(XNode.DeepEquals(Xml(sensitiveRunner),Xml(reopenedSensitive)) && reopenedSensitive.DraftEssence==4.75m,"Sensitive System cost changed or doubled again after reopening.");
}
qualityRules.Remove(sensitiveRunner,sensitiveRunner.SelectedQualities.Single().Id);
Assert(sensitiveRunner.DraftEssence==5.37m && sensitiveRunner.Karma==25,"Removing Sensitive System did not restore original Essence and reverse its Karma reward.");
var bioFirst=creation.CreateDraft("Existing bioware","",richPriorities,"Human");augmentationRules.Purchase(bioFirst,ImplantId("Muscle Toner"),1,"Standard");
var beforeSensitiveReject=Xml(bioFirst);Reject(()=>qualityRules.Add(bioFirst,QualityId("Sensitive System")));
Assert(XNode.DeepEquals(beforeSensitiveReject,Xml(bioFirst)),"Selecting Sensitive System over existing bioware was not atomic.");
var sensitiveMage=creation.CreateDraft("Sensitive mage","",magicianPriority,"Human");
augmentationRules.Purchase(sensitiveMage,ImplantId("Dermal Plating"),1,"Standard");
augmentationRules.Purchase(sensitiveMage,ImplantId("Bone Lacing (Plastic)"),0,"Standard");
Assert(sensitiveMage.DraftAttributes.Single(a=>a.Name=="MAG").Rating==5,"Sensitive mage setup Magic mismatch.");
qualityRules.Add(sensitiveMage,QualityId("Sensitive System"));
Assert(sensitiveMage.DraftEssence==4 && sensitiveMage.DraftAttributes.Single(a=>a.Name=="MAG").Rating==4,"Retroactive Sensitive System Essence did not update Magic loss.");
qualityRules.Remove(sensitiveMage,sensitiveMage.SelectedQualities.Single().Id);
Assert(sensitiveMage.DraftAttributes.Single(a=>a.Name=="MAG").Rating==5,"Removing Sensitive System did not restore creation Magic.");
Console.WriteLine("PASS: Sensitive System retroactive/new cyberware costs, multiplier-before-rounding, bioware conflicts, Essence/Magic restoration and save/reopen.");

var knowledgeRunner = creation.CreateHumanDraft("Knowledge QA", "", priorities);
knowledgeRunner.SetDraftKnowledge(null, "Seattle", "Street", 4, false, 2, "Gangs", true);
var boughtKnowledge = knowledgeRunner.DraftKnowledgeSkills.Single();
Assert(knowledgeRunner.Karma == 11 && knowledgeRunner.KnowledgePointsRemaining == 2 && boughtKnowledge.Rating == 4 && boughtKnowledge.KarmaRating == 2,
    "Mixed knowledge allocation must cost 3+4+7 Karma and two knowledge points.");
Assert((int)Xml(knowledgeRunner).Root!.Element("newskills")!.Element("knoskills")!.Element("skill")!.Element("karma")! == 2,
    "Desktop knowledge Karma field must store levels, not their currency cost.");
var knowledgeFundingBefore = Xml(knowledgeRunner);
Reject(() => knowledgeRunner.SetDraftKnowledge(boughtKnowledge.Id, "Seattle", "Street", 6, false, 6, "Gangs", true));
Reject(() => knowledgeRunner.SetDraftKnowledge(boughtKnowledge.Id, "Seattle", "Street", 2, false, 3));
Reject(() => knowledgeRunner.SetDraftKnowledge(boughtKnowledge.Id, "Seattle", "Street", 4, false, 4, "Gangs", false));
Reject(() => knowledgeRunner.SetDraftKnowledge(boughtKnowledge.Id, "Seattle", "Language", 0, true, 0, "Native specialization"));
Assert(XNode.DeepEquals(knowledgeFundingBefore, Xml(knowledgeRunner)), "Rejected knowledge edits must preserve balances, ratings and specialization.");
using (var encodedKnowledge = new MemoryStream())
{
    knowledgeRunner.SaveCompressed(encodedKnowledge); encodedKnowledge.Position = 0;
    var reopenedKnowledge = CharacterDocument.LoadCompressed(encodedKnowledge);
    Assert(reopenedKnowledge.DraftKnowledgeSkills.Single() == boughtKnowledge && reopenedKnowledge.Karma == 11 && reopenedKnowledge.KnowledgePointsRemaining == 2,
        "Knowledge allocation and specialization must survive compressed saves.");
}
knowledgeRunner.SetDraftKnowledge(boughtKnowledge.Id, "Seattle", "Street", 4, false, 1, "Gangs", false);
Assert(knowledgeRunner.Karma == 21 && knowledgeRunner.KnowledgePointsRemaining == 0,
    "Changing funding must refund old Karma before charging the new highest level and point specialization.");
knowledgeRunner.SetDraftKnowledge(boughtKnowledge.Id, "English", "Language", 0, true);
Assert(knowledgeRunner.Karma == 25 && knowledgeRunner.KnowledgePointsRemaining == 4 && knowledgeRunner.DraftKnowledgeSkills.Single().Specialization == "",
    "Converting to a native language must release paid ratings and specialization.");
knowledgeRunner.RemoveDraftKnowledge(boughtKnowledge.Id);
knowledgeRunner.SetDraftKnowledge(null, "History", "Academic", 4, false, 4);
Assert(knowledgeRunner.Karma == 15 && knowledgeRunner.KnowledgePointsRemaining == 4, "All-Karma knowledge costs 1+2+3+4 and consumes no free points.");
knowledgeRunner.RemoveDraftKnowledge(knowledgeRunner.DraftKnowledgeSkills.Single().Id);
Assert(knowledgeRunner.Karma == 25, "Deleting knowledge must refund its paid creation Karma.");
knowledgeRunner.SetDraftAttributes(new Dictionary<string, int> { ["LOG"] = 2 });
knowledgeRunner.SetDraftKnowledge(null, "History", "Academic", 6, false, 1, "Ancient", false);
var loweringKnowledge = Xml(knowledgeRunner);
Reject(() => knowledgeRunner.SetDraftAttributes(new Dictionary<string, int> { ["LOG"] = 1 }));
Assert(XNode.DeepEquals(loweringKnowledge, Xml(knowledgeRunner)), "Attribute reduction must include knowledge specialization point costs.");
knowledgeRunner.SetDraftKnowledge(knowledgeRunner.DraftKnowledgeSkills.Single().Id, "History", "Academic", 6, false, 3, "Ancient", false);
knowledgeRunner.SetDraftAttributes(new Dictionary<string, int> { ["LOG"] = 1 });
Assert(knowledgeRunner.KnowledgePointsRemaining == 0, "Karma-funded knowledge levels must not block a valid reduced point budget.");
Console.WriteLine("PASS: knowledge/language mixed point and Karma allocation, highest-level pricing, specializations, funding changes, refunds, budget safety and compressed persistence.");

var karmaSkillRunner = creation.CreateHumanDraft("Active Karma QA", "", priorities);
var karmaPistols = creation.Skills(karmaSkillRunner).Single(s => s.Name == "Pistols");
karmaSkillRunner.SetDraftSkill(karmaPistols.Id, 4, 2);
Assert(karmaSkillRunner.Karma == 11 && karmaSkillRunner.SkillPointsRemaining == 34 && creation.Skills(karmaSkillRunner).Single(s => s.Id == karmaPistols.Id).Rating == 4,
    "Active skill mixed funding must cost 2*(3+4) Karma and two skill points.");
var activeFundingBefore = Xml(karmaSkillRunner);
Reject(() => karmaSkillRunner.SetDraftSkill(karmaPistols.Id, 6, 3));
Reject(() => karmaSkillRunner.SetDraftGroup("Firearms", 2));
Assert(XNode.DeepEquals(activeFundingBefore, Xml(karmaSkillRunner)), "Rejected Karma skill purchases or group conflicts must be atomic.");
karmaSkillRunner.SetDraftSkill(karmaPistols.Id, 3, 3);
Assert(karmaSkillRunner.Karma == 13 && karmaSkillRunner.SkillPointsRemaining == 36, "All-Karma active skill must cost 2*(1+2+3).");
Reject(() => karmaSkillRunner.AddDraftSpecialization(karmaPistols.Id, "Semi-Automatics", false));
karmaSkillRunner.AddDraftSpecialization(karmaPistols.Id, "Semi-Automatics", true);
Assert(karmaSkillRunner.Karma == 6, "All-Karma specialization purchase mismatch.");
karmaSkillRunner.RemoveDraftSpecialization(karmaPistols.Id);
karmaSkillRunner.SetDraftSkill(karmaPistols.Id, 3, 1);
karmaSkillRunner.AddDraftSpecialization(karmaPistols.Id, "Semi-Automatics", false);
var pointSpecBefore = Xml(karmaSkillRunner);
Reject(() => karmaSkillRunner.SetDraftSkill(karmaPistols.Id, 3, 3));
Assert(XNode.DeepEquals(pointSpecBefore, Xml(karmaSkillRunner)), "Funding changes cannot silently reprice a point specialization as Karma.");
using (var karmaSkillSave = new MemoryStream())
{
    karmaSkillRunner.SaveCompressed(karmaSkillSave); karmaSkillSave.Position = 0;
    var reopenedSkill = CharacterDocument.LoadCompressed(karmaSkillSave);
    Assert(creation.Skills(reopenedSkill).Single(s => s.Id == karmaPistols.Id).KarmaRating == 1 && reopenedSkill.Karma == 19 && reopenedSkill.SkillPointsRemaining == 33,
        "Active Karma allocation and point specialty must persist.");
}
karmaSkillRunner.RemoveDraftSpecialization(karmaPistols.Id);
karmaSkillRunner.SetDraftSkill(karmaPistols.Id, 0);
Assert(karmaSkillRunner.Karma == 25 && karmaSkillRunner.SkillPointsRemaining == 36, "Resetting active allocations must refund only spent creation Karma.");
var karmaGrantMage = creation.CreateDraft("Grant Karma QA", "", magicianPriority, "Human");
var grantCandidates = creation.TalentSkillCandidates(karmaGrantMage).ToArray();
var grantNames = grantCandidates.Take(karmaGrantMage.TalentSkillChoicesRequired).Select(s => s.Name).ToArray();
creation.AssignTalentSkills(karmaGrantMage, grantNames);
var grantSkill = creation.Skills(karmaGrantMage).Single(s => s.Name == grantNames[0]);
karmaGrantMage.SetDraftSkill(grantSkill.Id, grantSkill.Rating + 1, 1);
var grantCost = 2 * (grantSkill.Rating + 1);
Assert(karmaGrantMage.Karma == 25 - grantCost, "Karma levels above a free talent grant must use the full new rating.");
var replacementNames = grantCandidates.Where(s => s.Name != grantNames[0]).Take(karmaGrantMage.TalentSkillChoicesRequired).Select(s => s.Name).ToArray();
creation.AssignTalentSkills(karmaGrantMage, replacementNames);
Assert(karmaGrantMage.Karma == 23 && creation.Skills(karmaGrantMage).Single(s => s.Id == grantSkill.Id).Rating == 1,
    "Changing a priority grant must reprice the remaining Karma level and refund the difference.");
creation.AssignTalentSkills(karmaGrantMage, grantNames);
Assert(karmaGrantMage.Karma == 25 - grantCost, "Restoring a grant must charge the higher Karma level again.");
Console.WriteLine("PASS: active skill point/Karma splits, pricing, specialization funding restrictions, group conflicts, refunds, priority-grant repricing and compressed persistence.");

var karmaGroupRunner = creation.CreateHumanDraft("Group Karma QA", "", priorities);
karmaGroupRunner.SetDraftGroup("Firearms", 2, 2);
Assert(karmaGroupRunner.Karma == 10 && karmaGroupRunner.GroupPointsRemaining == 5 && creation.Skills(karmaGroupRunner).Single(s => s.Name == "Pistols").Rating == 2,
    "Group Karma must cost 5*(1+2), leave group points untouched and train all members.");
var groupFundingBefore = Xml(karmaGroupRunner);
Reject(() => karmaGroupRunner.SetDraftGroup("Firearms", 3, 3));
Reject(() => karmaGroupRunner.SetDraftSkill(creation.Skills(karmaGroupRunner).Single(s => s.Name == "Pistols").Id, 3, 1));
Assert(XNode.DeepEquals(groupFundingBefore, Xml(karmaGroupRunner)), "Unaffordable group and member edits must be atomic.");
karmaGroupRunner.SetDraftGroup("Firearms", 3, 1);
Assert(karmaGroupRunner.Karma == 10 && karmaGroupRunner.GroupPointsRemaining == 3, "Mixed group funding must charge the highest new rating.");
using (var groupSave = new MemoryStream())
{
    karmaGroupRunner.SaveCompressed(groupSave); groupSave.Position = 0;
    var reopenedGroup = CharacterDocument.LoadCompressed(groupSave);
    Assert(reopenedGroup.DraftGroups.Single(g => g.Name == "Firearms") == new DraftSkillGroup("Firearms", 3, 1) && reopenedGroup.Karma == 10,
        "Group Karma levels and costs must persist.");
}
karmaGroupRunner.SetDraftGroup("Firearms", 0);
Assert(karmaGroupRunner.Karma == 25 && karmaGroupRunner.GroupPointsRemaining == 5, "Resetting a group must refund creation Karma and points.");
var karmaAspected = creation.CreateDraft("Aspected group Karma", "", magicianPriority with { Path = "Aspected Magician", Talent = "B", Attributes = "A" }, "Human");
var freeSorcery = karmaAspected.DraftGroups.Single(g => g.Name == "Sorcery").Rating;
karmaAspected.SetDraftGroup("Sorcery", freeSorcery + 1, 1);
Assert(karmaAspected.Karma == 25 - 5 * (freeSorcery + 1), "A Karma group level above a free grant must pay the full new rating.");
Reject(() => karmaAspected.SetDraftGroup("Sorcery", freeSorcery, freeSorcery));
Console.WriteLine("PASS: skill group Karma/mixed funding, member ratings, free grants, conflicts, atomic rejection, reset refunds and compressed persistence.");

var karmaAttributes = creation.CreateHumanDraft("Attribute Karma QA", "", priorities);
karmaAttributes.SetDraftAttributes(new Dictionary<string,int> { ["BOD"] = 2, ["EDG"] = 3 }, new Dictionary<string,int> { ["BOD"] = 1, ["EDG"] = 1 });
Assert(karmaAttributes.Karma == 0 && karmaAttributes.AttributePointsRemaining == 24 && karmaAttributes.SpecialPointsRemaining == 3,
    "Karma-funded Body 2 / Edge 3 must cost 10+15 without using priority points.");
var karmaAttributesBefore = Xml(karmaAttributes);
Reject(() => karmaAttributes.SetDraftAttributes(new Dictionary<string,int> { ["BOD"] = 3 }));
Reject(() => karmaAttributes.SetDraftAttributes(new Dictionary<string,int>(), new Dictionary<string,int> { ["BOD"] = 2 }));
Assert(XNode.DeepEquals(karmaAttributesBefore, Xml(karmaAttributes)), "Invalid/unaffordable attribute Karma edits must be atomic.");
karmaAttributes.SetDraftAttributes(new Dictionary<string,int>(), new Dictionary<string,int> { ["BOD"] = 0, ["EDG"] = 0 });
Assert(karmaAttributes.Karma == 25 && karmaAttributes.AttributePointsRemaining == 23 && karmaAttributes.SpecialPointsRemaining == 2,
    "Switching attribute levels to priority points must refund their Karma.");
karmaAttributes.SetDraftAttributes(new Dictionary<string,int> { ["BOD"] = 3 }, new Dictionary<string,int> { ["BOD"] = 1 });
Assert(karmaAttributes.Karma == 10 && karmaAttributes.AttributePointsRemaining == 23, "Mixed attribute funding must charge the highest level at five times rating.");
using (var attributeSave = new MemoryStream())
{
    karmaAttributes.SaveCompressed(attributeSave); attributeSave.Position = 0;
    var reopenedAttributes = CharacterDocument.LoadCompressed(attributeSave);
    Assert(reopenedAttributes.DraftAttributes.Single(a => a.Name == "BOD") == new DraftAttribute("BOD", 1, 6, 3, 1) && reopenedAttributes.Karma == 10,
        "Attribute Karma funding must survive compressed saves.");
}
var karmaMagicPriority = new PriorityChoice("Priority", "E", "B", "D", "A", "C", "Magician");
var karmaMagic = creation.CreateDraft("Magic cost QA", "", karmaMagicPriority, "Human");
karmaMagic.SetDraftAttributes(new Dictionary<string,int> { ["MAG"] = 4 }, new Dictionary<string,int> { ["MAG"] = 1 });
Assert(karmaMagic.Karma == 5 && karmaMagic.SpecialPointsRemaining == 1, "Magic 4 above a free rating 3 must cost 20 Karma without consuming the heritage point.");
augmentationRules.Purchase(karmaMagic, ImplantId("Dermal Plating"), 1, "Standard");
Assert(karmaMagic.Karma == 10 && karmaMagic.DraftAttributes.Single(a => a.Name == "MAG").Rating == 3,
    "Creation Essence loss must reprice the Karma-funded Magic level to its reduced rating.");
qualityRules.Add(karmaMagic, QualityId("Catlike"));
Assert(karmaMagic.Karma == 3, "Unrelated quality changes must not charge attribute Karma twice.");
var cannotRestoreMagic = Xml(karmaMagic);
Reject(() => augmentationRules.Remove(karmaMagic, karmaMagic.InstalledAugmentations.Single().Id));
Assert(XNode.DeepEquals(cannotRestoreMagic, Xml(karmaMagic)), "Unaffordable restoration of Karma-funded Magic must not remove implants or change money.");
qualityRules.Remove(karmaMagic, karmaMagic.SelectedQualities.Single().Id);
augmentationRules.Remove(karmaMagic, karmaMagic.InstalledAugmentations.Single().Id);
Assert(karmaMagic.Karma == 5 && karmaMagic.DraftAttributes.Single(a => a.Name == "MAG").Rating == 4,
    "Implant removal must restore Magic and charge the restored attribute cost.");
Console.WriteLine("PASS: attribute/special Karma splits, priority budgets, mixed costs, refunds, save/reopen and Essence-driven Magic repricing with atomic restoration rejection.");

var resourceRunner = creation.CreateHumanDraft("Resource Karma QA", "", new PriorityChoice("SumtoTen", "C", "A", "A", "E"));
Assert(resourceRunner.Nuyen == 6000 && resourceRunner.DraftResourceKarma == 0, "Resource test priority grant mismatch.");
resourceRunner.SetDraftResourceKarma(10);
Assert(resourceRunner.Karma == 15 && resourceRunner.Nuyen == 26000 && resourceRunner.Field("startingnuyen") == "6000" && resourceRunner.Field("nuyenbp") == "10",
    "Resource exchange must preserve the priority grant and save the desktop Karma allocation.");
resourceRunner.SetDraftResourceKarma(10);
Assert(resourceRunner.Karma == 15 && resourceRunner.Nuyen == 26000, "Saving the same resource allocation must be idempotent.");
augmentationRules.Purchase(resourceRunner, ImplantId("Muscle Replacement"), 1, "Standard");
var resourceSpentBefore = Xml(resourceRunner);
Reject(() => resourceRunner.SetDraftResourceKarma(0));
Reject(() => resourceRunner.SetDraftResourceKarma(11));
Reject(() => resourceRunner.SetDraftResourceKarma(-1));
Assert(XNode.DeepEquals(resourceSpentBefore, Xml(resourceRunner)), "Spent-resource refunds and invalid allocation rejection must be atomic.");
using (var resourceSave = new MemoryStream())
{
    resourceRunner.SaveCompressed(resourceSave); resourceSave.Position = 0;
    var reopenedResource = CharacterDocument.LoadCompressed(resourceSave);
    Assert(reopenedResource.DraftResourceKarma == 10 && reopenedResource.Karma == resourceRunner.Karma && reopenedResource.Nuyen == resourceRunner.Nuyen,
        "Resource allocation and remaining balances must persist.");
}
augmentationRules.Remove(resourceRunner, resourceRunner.InstalledAugmentations.Single().Id);
resourceRunner.SetDraftResourceKarma(0);
Assert(resourceRunner.Karma == 25 && resourceRunner.Nuyen == 6000, "Reversing purchases and resource funding must restore both balances.");
resourceRunner.SetDraftAttributes(new Dictionary<string,int> { ["BOD"] = 3 }, new Dictionary<string,int> { ["BOD"] = 2 });
var poorResourceBefore = Xml(resourceRunner);
Reject(() => resourceRunner.SetDraftResourceKarma(1));
Assert(XNode.DeepEquals(poorResourceBefore, Xml(resourceRunner)), "Resource conversion cannot overspend Karma already allocated elsewhere.");
Console.WriteLine("PASS: standard creation Karma/Nuyen conversion, maximums, idempotence, spent-funds rejection, refunds and compressed persistence.");

using var lifestyleData = File.OpenRead(Path.Combine(root, "Chummer", "data", "lifestyles.xml"));
var lifestyleRules = new LifestyleRules(lifestyleData);
string LifestyleId(string name) => lifestyleRules.Catalog.Single(l => l.Name == name).Id;
var lifestyleRunner = creation.CreateHumanDraft("Lifestyle QA", "", priorities);
Assert(lifestyleRules.Catalog.Count == 6 && lifestyleRules.Quote(lifestyleRunner, LifestyleId("Low")) == 2000, "Six core lifestyles or base prices mismatch.");
lifestyleRules.Set(lifestyleRunner, null, LifestyleId("Low"), "Home <safe>", 2, 1, true, "Seattle");
var home = lifestyleRunner.DraftLifestyles.Single();
Assert(home.MonthlyCost == 1100 && home.PurchaseCost == 2200 && lifestyleRunner.Nuyen == 137800,
    "Roommate surcharge must apply before splitting and charging prepaid months.");
Assert(CharacterSheet.SummaryHtml(lifestyleRunner).Contains("Home &lt;safe&gt;"), "Lifestyle summary must escape names.");
var lifestyleBefore = Xml(lifestyleRunner);
Reject(() => lifestyleRules.Set(lifestyleRunner, home.Id, LifestyleId("Luxury"), "Too expensive", 2));
Reject(() => lifestyleRules.Set(lifestyleRunner, home.Id, LifestyleId("Low"), "Home", 0));
Reject(() => lifestyleRules.Set(lifestyleRunner, home.Id, LifestyleId("Low"), "Home", 1, -1));
Assert(XNode.DeepEquals(lifestyleBefore, Xml(lifestyleRunner)), "Invalid lifestyle edits must preserve purchase and wallet.");
using (var lifestyleSave = new MemoryStream())
{
    lifestyleRunner.SaveCompressed(lifestyleSave); lifestyleSave.Position = 0;
    var reopenedHome = CharacterDocument.LoadCompressed(lifestyleSave);
    Assert(reopenedHome.DraftLifestyles.Single() == home && reopenedHome.Nuyen == 137800, "Lifestyle schema/pricing must persist through compressed saves.");
}
qualityRules.Add(lifestyleRunner, QualityId("Dependent (Nuisance)"));
Assert(lifestyleRunner.DraftLifestyles.Single().MonthlyCost == 1210 && lifestyleRunner.Nuyen == 137580 && lifestyleRunner.Karma == 28,
    "Adding dependents must reprice existing prepaid lifestyles once.");
qualityRules.Add(lifestyleRunner, QualityId("Catlike"));
Assert(lifestyleRunner.Nuyen == 137580, "Unrelated qualities must not charge lifestyles twice.");
qualityRules.Remove(lifestyleRunner, lifestyleRunner.SelectedQualities.Single(q => q.Name == "Dependent (Nuisance)").Id);
Assert(lifestyleRunner.Nuyen == 137800, "Removing dependents must refund the creation cost difference.");
lifestyleRules.Set(lifestyleRunner, home.Id, LifestyleId("Medium"), "New home", 1, city: "Denver");
Assert(lifestyleRunner.DraftLifestyles.Single().Id == home.Id && lifestyleRunner.Nuyen == 135000,
    "Lifestyle editing must preserve its identity and refund the old prepaid amount before charging.");
lifestyleRules.Remove(lifestyleRunner, home.Id);
Assert(lifestyleRunner.Nuyen == 140000 && lifestyleRunner.DraftLifestyles.Count == 0, "Lifestyle removal must refund its current prepaid cost.");
var trollHome = creation.CreateMundaneDraft("Troll lifestyle", "", allMetatypePriorities, "Troll");
var dwarfHome = creation.CreateMundaneDraft("Dwarf lifestyle", "", allMetatypePriorities, "Dwarf");
Assert(lifestyleRules.Quote(trollHome, LifestyleId("Low")) == 4000 && lifestyleRules.Quote(dwarfHome, LifestyleId("Low")) == 2400,
    "Metatype lifestyle costs must apply.");
qualityRules.Add(trollHome, QualityId("Dependent (Nuisance)"));
Assert(lifestyleRules.Quote(trollHome, LifestyleId("Low"), 1, true) == 2420,
    "Dependent and metatype costs must multiply separately after roommate sharing.");
var brokeHome = creation.CreateHumanDraft("Lifestyle atomic QA", "", priorities);
lifestyleRules.Set(brokeHome, null, LifestyleId("Low"), "Long lease", 70);
var brokeHomeBefore = Xml(brokeHome);
Reject(() => qualityRules.Add(brokeHome, QualityId("Dependent (Nuisance)")));
Assert(XNode.DeepEquals(brokeHomeBefore, Xml(brokeHome)) && brokeHome.Karma == 25,
    "Unaffordable lifestyle repricing must reject the quality atomically, including its Karma reward.");
var advancedHomeXml = Xml(brokeHome); advancedHomeXml.Root!.Element("lifestyles")!.Element("lifestyle")!.SetElementValue("area", 1);
using (var unsupportedHome = new MemoryStream(Encoding.UTF8.GetBytes(advancedHomeXml.ToString())))
{
    var advancedHome = CharacterDocument.Load(unsupportedHome);
    Reject(() => _ = advancedHome.DraftAttributes);
}
Console.WriteLine("PASS: six standard lifestyles, prepaid purchases/edit/refunds, roommate sharing, metatype/dependent pricing, atomic quality repricing, summaries and compressed persistence.");
