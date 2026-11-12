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
var creation = new CreationRules(priorityData, metatypeData, skillData);
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
