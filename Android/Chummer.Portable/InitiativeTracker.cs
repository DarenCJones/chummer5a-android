// SPDX-License-Identifier: GPL-3.0-or-later
namespace Chummer.Portable;

public sealed record Combatant(string Id, string Name, int Score, int TieOrder);

public sealed class InitiativeTracker
{
    private readonly List<Combatant> entries = [];
    public int Pass { get; private set; } = 1;
    public IReadOnlyList<Combatant> All => entries.ToArray();
    public static InitiativeTracker Restore(int pass, IEnumerable<Combatant> combatants)
    {
        var tracker = new InitiativeTracker();
        var saved = combatants.ToArray();
        if (pass < 1 || saved.Any(e => string.IsNullOrWhiteSpace(e.Name) || e.Score is < 0 or > 1000))
            throw new InvalidDataException("Invalid saved initiative.");
        tracker.entries.AddRange(saved); tracker.Pass = pass;
        return tracker;
    }
    public IReadOnlyList<Combatant> Order => entries.Where(e => e.Score > 0)
        .OrderByDescending(e => e.Score).ThenBy(e => e.TieOrder).ToArray();
    public void Add(string name, int score)
    {
        if (string.IsNullOrWhiteSpace(name) || score is < 0 or > 1000) throw new ArgumentException("Enter a name and initiative from 0 to 1000.");
        entries.Add(new Combatant(Guid.NewGuid().ToString("N"), name.Trim(), score, entries.Count));
    }
    public void NextPass()
    {
        for (var i = 0; i < entries.Count; i++) entries[i] = entries[i] with { Score = Math.Max(0, entries[i].Score - 10) };
        Pass++;
    }
    public void Clear() { entries.Clear(); Pass = 1; }
}
