using System;
using System.Linq;

/// <summary>
/// A reproducible, fixed Bash opponent. The supplied selector comes from the
/// session RNG, so a saved seed and cursor replay the complete game.
/// </summary>
public sealed class StrategyEngine
{
    public int ChooseBashMove(BashGame game, int selector)
    {
        ArgumentNullException.ThrowIfNull(game);

        if (game.IsGameOver)
        {
            throw new InvalidOperationException("The Tutor cannot act after settlement.");
        }

        if (game.CurrentTurn != Actor.Tutor)
        {
            throw new InvalidOperationException("The Tutor cannot act during the player turn.");
        }

        int[] legal = Enumerable.Range(BashGame.MinimumTake, BashGame.MaximumTake)
            .Where(game.CanTake)
            .ToArray();

        // The fixed opponent sometimes takes the optimal misere move and
        // sometimes follows its seeded exploration order. It remains legal and
        // reproducible without becoming an unbeatable tutorial gate.
        int optimalTake = (game.Remaining - 1) % (BashGame.MaximumTake + 1);

        if (selector % 3 == 0 && legal.Contains(optimalTake))
        {
            return optimalTake;
        }

        int normalized = Math.Abs(selector == int.MinValue ? int.MaxValue : selector);
        return legal[normalized % legal.Length];
    }
}
