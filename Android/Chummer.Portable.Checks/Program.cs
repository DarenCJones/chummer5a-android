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
