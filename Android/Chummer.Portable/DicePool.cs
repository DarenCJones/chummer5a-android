// SPDX-License-Identifier: GPL-3.0-or-later
using System.Security.Cryptography;

namespace Chummer.Portable;

public enum RollMethod { Standard, Large, ReallyLarge }
public sealed record RollOptions(int Dice, bool Exploding = false, bool Cinematic = false,
    bool Rushed = false, int Gremlins = 0, bool VariableGlitch = false, bool Bubble = false, RollMethod Method = RollMethod.Standard);
public sealed record PoolRoll(int[] Dice, int? BubbleDie, int Hits, int GlitchThreshold, bool Glitch, bool CriticalGlitch)
{
    public bool MeetsThreshold(int threshold)
    {
        if (threshold < 1) throw new ArgumentOutOfRangeException(nameof(threshold));
        return Hits >= threshold;
    }
}

/// <summary>Normal dice-pool mode follows the desktop DiceRoller thresholds and bubble-die behavior.</summary>
public static class DicePool
{
    public static int D6() => RandomNumberGenerator.GetInt32(1, 7);
    private static int Target(RollOptions o) => o.Method switch { RollMethod.Large => 3, RollMethod.ReallyLarge => 1, _ => o.Cinematic ? 4 : 5 };
    private static int GlitchValue(RollOptions o) => o.Method == RollMethod.ReallyLarge ? 7 : o.Rushed ? 2 : 1;
    private static int CountHits(IEnumerable<int> dice, RollOptions o) => o.Method == RollMethod.ReallyLarge ? dice.Sum() : dice.Count(v => v >= Target(o));
    /// <summary>Desktop normal-mode reroll: keep hits, discard the old bubble die,
    /// and preserve glitches established from the original non-bubble dice.</summary>
    public static PoolRoll RerollMisses(PoolRoll previous, RollOptions options, Func<int>? next = null)
    {
        if (options.Dice is < 1 or > 500 || options.Gremlins is < 0 or > 100 || !Enum.IsDefined(options.Method) || previous.Dice.Length is < 1 or > 10000 || previous.Dice.Any(v => v is < 1 or > 6))
            throw new ArgumentOutOfRangeException(nameof(previous));
        next ??= D6;
        int Next()
        {
            var value = next();
            if (value is < 1 or > 6) throw new InvalidOperationException("Invalid die result.");
            return value;
        }
        var target = Target(options); var glitchValue = GlitchValue(options);
        var retained = previous.Dice.Where(v => v >= target).ToList();
        var misses = previous.Dice.Length - retained.Count;
        if (misses == 0) throw new InvalidOperationException("There are no misses to reroll.");
        var threshold = Math.Max(1, (options.VariableGlitch ? retained.Count + 1 : (previous.Dice.Length + 1)/2) - options.Gremlins);
        var oldGlitch = previous.Dice.Count(v => v <= glitchValue) >= threshold;
        var oldCritical = oldGlitch && retained.Count == 0;
        for (var i=0; i<misses; i++)
        {
            int value;
            do
            {
                if (retained.Count >= 10000) throw new InvalidOperationException("Too many exploding dice.");
                value = Next(); retained.Add(value);
            } while (options.Exploding && value == 6);
        }
        var hits = CountHits(retained, options); int? bubble = null;
        if (oldGlitch) return new PoolRoll(retained.ToArray(),null,hits,threshold,true,oldCritical);
        threshold = Math.Max(1,(options.VariableGlitch ? hits+1 : (retained.Count+1)/2)-options.Gremlins);
        var ones = retained.Count(v => v <= glitchValue);
        if (options.Bubble && (options.VariableGlitch || ones == threshold-1 && retained.Count%2 == 0))
        {
            bubble = Next(); if (bubble <= glitchValue) ones++;
        }
        var glitch = ones >= threshold;
        return new PoolRoll(retained.ToArray(),bubble,hits,threshold,glitch,glitch && hits == 0);
    }
    public static PoolRoll Roll(RollOptions options, Func<int>? next = null)
    {
        if (options.Dice is < 1 or > 500 || options.Gremlins is < 0 or > 100 || !Enum.IsDefined(options.Method))
            throw new ArgumentOutOfRangeException(nameof(options));
        next ??= D6;
        int Next()
        {
            var value = next();
            if (value is < 1 or > 6) throw new InvalidOperationException("Invalid die result.");
            return value;
        }
        var dice = new List<int>();
        for (var i = 0; i < options.Dice; i++)
        {
            int value;
            do
            {
                if (dice.Count >= 10000) throw new InvalidOperationException("Too many exploding dice.");
                value = Next(); dice.Add(value);
            } while (options.Exploding && value == 6);
        }
        var hits = CountHits(dice, options);
        var ones = dice.Count(v => v <= GlitchValue(options));
        var threshold = Math.Max(1, (options.VariableGlitch ? hits + 1 : (options.Dice + 1) / 2) - options.Gremlins);
        int? bubble = null;
        if (options.Bubble && (options.VariableGlitch || (ones == threshold - 1 && options.Dice % 2 == 0)))
        {
            bubble = Next();
            if (bubble <= GlitchValue(options)) ones++;
        }
        var glitch = ones >= threshold;
        return new PoolRoll(dice.ToArray(), bubble, hits, threshold, glitch, glitch && hits == 0);
    }
}
