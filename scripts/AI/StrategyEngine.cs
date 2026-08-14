using System;

/// <summary>
/// Selects a legal Bash action. On a winning position the AI leaves a multiple
/// of four; on a losing position it explores one of the legal moves.
/// </summary>
public sealed class StrategyEngine
{
    private readonly Random _random;

    public StrategyEngine(int? randomSeed = null)
    {
        _random = randomSeed.HasValue
            ? new Random(randomSeed.Value)
            : new Random();
    }

    public int ChooseBashMove(BashGame game)
    {
        ArgumentNullException.ThrowIfNull(game);

        if (game.IsGameOver)
        {
            throw new InvalidOperationException("The AI cannot move after the round ends.");
        }

        if (game.IsPlayerTurn)
        {
            throw new InvalidOperationException("The AI cannot move during the player turn.");
        }

        int maximumLegalTake = Math.Min(BashGame.MaximumTake, game.Remaining);
        int winningMove = game.Remaining % (BashGame.MaximumTake + 1);

        if (winningMove >= BashGame.MinimumTake && winningMove <= maximumLegalTake)
        {
            return winningMove;
        }

        return _random.Next(BashGame.MinimumTake, maximumLegalTake + 1);
    }
}
