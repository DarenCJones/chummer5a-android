// SPDX-License-Identifier: GPL-3.0-or-later
using System.Security.Cryptography;

namespace Chummer.Portable;

public sealed record RollOptions(int Dice, bool Exploding = false, bool Cinematic = false,
    bool Rushed = false, int Gremlins = 0, bool VariableGlitch = false, bool Bubble = false);
public sealed record PoolRoll(int[] Dice, int? BubbleDie, int Hits, int GlitchThreshold, bool Glitch, bool CriticalGlitch);

/// <summary>Normal dice-pool mode follows the desktop DiceRoller thresholds and bubble-die behavior.</summary>
public static class DicePool
{
    public static int D6() => RandomNumberGenerator.GetInt32(1, 7);
    public static PoolRoll Roll(RollOptions options, Func<int>? next = null)
    {
        if (options.Dice is < 1 or > 500 || options.Gremlins is < 0 or > 100)
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
        var hits = dice.Count(v => v >= (options.Cinematic ? 4 : 5));
        var ones = dice.Count(v => v <= (options.Rushed ? 2 : 1));
        var threshold = Math.Max(1, (options.VariableGlitch ? hits + 1 : (options.Dice + 1) / 2) - options.Gremlins);
        int? bubble = null;
        if (options.Bubble && (options.VariableGlitch || (ones == threshold - 1 && options.Dice % 2 == 0)))
        {
            bubble = Next();
            if (bubble <= (options.Rushed ? 2 : 1)) ones++;
        }
        var glitch = ones >= threshold;
        return new PoolRoll(dice.ToArray(), bubble, hits, threshold, glitch, glitch && hits == 0);
    }
}
