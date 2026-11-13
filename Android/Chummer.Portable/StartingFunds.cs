// SPDX-License-Identifier: GPL-3.0-or-later
using System.Xml.Linq;

namespace Chummer.Portable;

public sealed record StartingFundsOption(string Id, string SourceId, string Name, int Dice, decimal Multiplier)
{
    public decimal ExpectedNuyen => Dice * 3.5m * Multiplier;
}
public sealed record StartingFundsPlan(string LifestyleId, string Lifestyle, int DiceResult, decimal LifestyleNuyen,
    decimal CarryoverNuyen, decimal CareerNuyen, decimal CarryoverKarma, decimal DiscardedNuyen, decimal DiscardedKarma);

public sealed partial class CharacterDocument
{
    public IReadOnlyList<StartingFundsOption> DraftStartingFundsOptions
    {
        get
        {
            RequireDraft();
            if (!LifestyleNodes.Any()) return [new("", "559653df-a9af-44e2-9e04-3044c1d1b421", "Street (no purchased lifestyle)", 1, 20)];
            return LifestyleNodes.Select(e => new StartingFundsOption(e.Element("guid")!.Value, e.Element("sourceid")!.Value,
                e.Element("name")!.Value, (int)e.Element("dice")!, (decimal)e.Element("multiplier")!))
                .OrderByDescending(e => e.ExpectedNuyen).ToArray();
        }
    }
    public StartingFundsPlan QuoteDraftStartingFunds(string lifestyleId, int diceResult)
    {
        var option = DraftStartingFundsOptions.SingleOrDefault(o => o.Id == lifestyleId) ?? throw new ArgumentException("Choose an available lifestyle.");
        if (option.Dice < 1 || option.Dice > 6 || option.Multiplier < 0) throw new InvalidOperationException("These starting-funds rules need additional support.");
        if (diceResult < option.Dice || diceResult > option.Dice * 6) throw new ArgumentException($"Enter a dice sum from {option.Dice} to {option.Dice * 6} for {option.Dice}D6.");
        var carryover = Math.Min(5000, Nuyen); var rolled = diceResult * option.Multiplier;
        return new(option.Id, option.Name, diceResult, rolled, carryover, carryover + rolled, Math.Min(7, Karma), Math.Max(0, Nuyen - carryover), Math.Max(0, Karma - 7));
    }
    public StartingFundsPlan? DraftStartingFunds
    {
        get
        {
            RequireDraft(); var node = document.Root!.Element("androidstartingfunds");
            if (node is null) return null;
            var option = DraftStartingFundsOptions.SingleOrDefault(o => o.Id == (string?)node.Element("lifestyleid"));
            if (option is null || option.SourceId != (string?)node.Element("sourceid") || option.Dice != (int?)node.Element("dice") || option.Multiplier != (decimal?)node.Element("multiplier")) return null;
            return QuoteDraftStartingFunds(option.Id, (int)node.Element("result")!);
        }
    }
    public void SetDraftStartingFunds(string lifestyleId, int diceResult)
    {
        _ = QuoteDraftStartingFunds(lifestyleId, diceResult);
        var option = DraftStartingFundsOptions.Single(o => o.Id == lifestyleId);
        var node = new XElement("androidstartingfunds", new XElement("lifestyleid", option.Id), new XElement("sourceid", option.SourceId),
            new XElement("dice", option.Dice), new XElement("multiplier", option.Multiplier), new XElement("result", diceResult));
        document.Root!.Element("androidstartingfunds")?.Remove(); document.Root.Add(node);
    }
    public StartingFundsPlan RollDraftStartingFunds(string lifestyleId, Func<int>? next = null)
    {
        var option = DraftStartingFundsOptions.SingleOrDefault(o => o.Id == lifestyleId) ?? throw new ArgumentException("Choose an available lifestyle.");
        _ = QuoteDraftStartingFunds(option.Id, option.Dice); next ??= DicePool.D6;
        var total = 0;
        for (var i = 0; i < option.Dice; i++)
        {
            var value = next(); if (value < 1 || value > 6) throw new InvalidOperationException("Invalid die result."); total += value;
        }
        SetDraftStartingFunds(option.Id, total); return DraftStartingFunds!;
    }
    public void ClearDraftStartingFunds()
    {
        RequireDraft(); document.Root!.Element("androidstartingfunds")?.Remove();
    }
    private void InvalidateDraftStartingFunds()
    {
        if (DraftStartingFunds is null) document.Root!.Element("androidstartingfunds")?.Remove();
    }
}
