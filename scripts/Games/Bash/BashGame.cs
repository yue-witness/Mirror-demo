using System;

/// <summary>
/// Pure Bash rules. This class deliberately has no Godot dependencies so the
/// turn sequence and win conditions can be tested outside the scene tree.
/// </summary>
public sealed class BashGame : IGame
{
    public const int DefaultInitialUnits = 15;
    public const int MinimumTake = 1;
    public const int MaximumTake = 3;

    public int Remaining { get; private set; }

    public bool IsPlayerTurn { get; private set; }

    public bool IsGameOver => Result != GameResult.InProgress;

    public GameResult Result { get; private set; } = GameResult.InProgress;

    public void StartGame()
    {
        Start(DefaultInitialUnits, playerFirst: true);
    }

    public void Start(int initialUnits = DefaultInitialUnits, bool playerFirst = true)
    {
        if (initialUnits < MinimumTake)
        {
            throw new ArgumentOutOfRangeException(
                nameof(initialUnits),
                "A Bash round must start with at least one unit.");
        }

        Remaining = initialUnits;
        IsPlayerTurn = playerFirst;
        Result = GameResult.InProgress;
    }

    public bool IsLegalMove(int amount)
    {
        return !IsGameOver
            && amount >= MinimumTake
            && amount <= MaximumTake
            && amount <= Remaining;
    }

    public void ApplyPlayerMove(int amount)
    {
        EnsureMoveIsLegal(amount, expectedPlayerTurn: true, actorName: "player");

        Remaining -= amount;

        if (Remaining == 0)
        {
            Result = GameResult.PlayerWin;
            return;
        }

        IsPlayerTurn = false;
    }

    public void ApplyAIMove(int amount)
    {
        EnsureMoveIsLegal(amount, expectedPlayerTurn: false, actorName: "AI");

        Remaining -= amount;

        if (Remaining == 0)
        {
            Result = GameResult.AIWin;
            return;
        }

        IsPlayerTurn = true;
    }

    public GameResult GetResult()
    {
        return Result;
    }

    private void EnsureMoveIsLegal(
        int amount,
        bool expectedPlayerTurn,
        string actorName)
    {
        if (IsGameOver)
        {
            throw new InvalidOperationException("The Bash round has already ended.");
        }

        if (IsPlayerTurn != expectedPlayerTurn)
        {
            throw new InvalidOperationException($"It is not the {actorName} turn.");
        }

        if (!IsLegalMove(amount))
        {
            throw new InvalidOperationException(
                $"The {actorName} cannot take {amount} from {Remaining} remaining units.");
        }
    }
}
