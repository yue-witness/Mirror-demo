using System;

/// <summary>
/// Pure rules for the original, misere Bash game. Taking the final unit loses.
/// The class has no Godot dependency so every rule can be tested in isolation.
/// </summary>
public sealed class BashGame
{
    public const int MinimumTake = 1;
    public const int MaximumTake = 3;

    public int InitialUnits { get; private set; }

    public int Remaining { get; private set; }

    public Actor CurrentTurn { get; private set; }

    public RoundOutcome Result { get; private set; } = RoundOutcome.Continue;

    public bool IsGameOver => Result != RoundOutcome.Continue;

    public void Start(int initialUnits, Actor firstActor)
    {
        if (initialUnits < MinimumTake)
        {
            throw new ArgumentOutOfRangeException(
                nameof(initialUnits),
                "A Bash game must start with at least one unit.");
        }

        InitialUnits = initialUnits;
        Remaining = initialUnits;
        CurrentTurn = firstActor;
        Result = RoundOutcome.Continue;
    }

    public void Restore(
        int initialUnits,
        int remaining,
        Actor currentTurn,
        RoundOutcome result = RoundOutcome.Continue)
    {
        if (initialUnits < MinimumTake
            || remaining < 0
            || remaining > initialUnits)
        {
            throw new ArgumentOutOfRangeException(
                nameof(remaining),
                "The restored Bash state is outside the legal range.");
        }

        if (remaining == 0 && result == RoundOutcome.Continue)
        {
            throw new InvalidOperationException(
                "A zero-remaining Bash state must contain a final result.");
        }

        InitialUnits = initialUnits;
        Remaining = remaining;
        CurrentTurn = currentTurn;
        Result = result;
    }

    public bool CanTake(int amount)
    {
        return !IsGameOver
            && amount >= MinimumTake
            && amount <= MaximumTake
            && amount <= Remaining;
    }

    public RoundOutcome ApplyTake(Actor actor, int amount)
    {
        if (IsGameOver)
        {
            throw new InvalidOperationException("The Bash game has already ended.");
        }

        if (actor != CurrentTurn)
        {
            throw new InvalidOperationException($"It is not {actor}'s turn.");
        }

        if (!CanTake(amount))
        {
            throw new InvalidOperationException(
                $"{actor} cannot take {amount} from {Remaining} remaining units.");
        }

        Remaining -= amount;

        if (Remaining == 0)
        {
            Result = actor == Actor.Player
                ? RoundOutcome.PlayerLose
                : RoundOutcome.PlayerWin;
            return Result;
        }

        CurrentTurn = actor == Actor.Player ? Actor.Tutor : Actor.Player;
        return RoundOutcome.Continue;
    }
}
