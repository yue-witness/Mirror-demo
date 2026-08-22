using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Pure rules for simultaneous-choice Limit Bash.
/// </summary>
public sealed class LimitBashGame
{
    public const int MinimumInitialUnits = 20;
    public const int MaximumInitialUnits = 30;
    public const int MinimumTake = 1;
    public const int MaximumTake = 3;

    private readonly List<ChoicePair> _choicePairs = new();

    public int InitialUnits { get; private set; }

    public int Remaining { get; private set; }

    public int RoundIndex => _choicePairs.Count + 1;

    public int? PlayerPrevious { get; private set; }

    public int? TutorPrevious { get; private set; }

    public int? LockedPlayerTake { get; private set; }

    public bool PlayerChoiceLocked => LockedPlayerTake.HasValue;

    public RoundOutcome Result { get; private set; } = RoundOutcome.Continue;

    public bool IsGameOver => Result != RoundOutcome.Continue;

    public IReadOnlyList<ChoicePair> ChoicePairs => _choicePairs;

    public void Start(int initialUnits)
    {
        if (initialUnits < MinimumInitialUnits
            || initialUnits > MaximumInitialUnits)
        {
            throw new ArgumentOutOfRangeException(
                nameof(initialUnits),
                $"Limit Bash must start between {MinimumInitialUnits} and "
                + $"{MaximumInitialUnits} units.");
        }

        InitialUnits = initialUnits;
        Remaining = initialUnits;
        PlayerPrevious = null;
        TutorPrevious = null;
        LockedPlayerTake = null;
        Result = RoundOutcome.Continue;
        _choicePairs.Clear();
    }

    public void Restore(
        int initialUnits,
        int remaining,
        int? playerPrevious,
        int? tutorPrevious,
        IEnumerable<ChoicePair> choicePairs,
        RoundOutcome result = RoundOutcome.Continue)
    {
        ArgumentNullException.ThrowIfNull(choicePairs);

        if (initialUnits < MinimumInitialUnits
            || initialUnits > MaximumInitialUnits
            || remaining < 0
            || remaining > initialUnits)
        {
            throw new InvalidOperationException("The restored Limit Bash state is invalid.");
        }

        InitialUnits = initialUnits;
        Remaining = remaining;
        PlayerPrevious = playerPrevious;
        TutorPrevious = tutorPrevious;
        LockedPlayerTake = null;
        Result = result;
        _choicePairs.Clear();
        _choicePairs.AddRange(choicePairs);
    }

    public IReadOnlyList<int> GetLegalPlayerActions()
    {
        return GetLegalActions(PlayerPrevious);
    }

    public IReadOnlyList<int> GetLegalTutorActions()
    {
        return GetLegalActions(TutorPrevious);
    }

    public void LockPlayerChoice(int amount)
    {
        if (IsGameOver)
        {
            throw new InvalidOperationException("The Limit Bash game has already ended.");
        }

        if (PlayerChoiceLocked)
        {
            throw new InvalidOperationException("The player choice is already locked.");
        }

        if (!GetLegalPlayerActions().Contains(amount))
        {
            throw new InvalidOperationException(
                $"The player cannot repeat or submit the illegal choice {amount}.");
        }

        LockedPlayerTake = amount;
    }

    public RoundOutcome CommitTutorChoice(int tutorTake)
    {
        if (!LockedPlayerTake.HasValue)
        {
            throw new InvalidOperationException(
                "The Tutor cannot act before the player choice is locked.");
        }

        if (!GetLegalTutorActions().Contains(tutorTake))
        {
            throw new InvalidOperationException(
                $"The Tutor cannot repeat or submit the illegal choice {tutorTake}.");
        }

        int playerTake = LockedPlayerTake.Value;
        int remainingBefore = Remaining;
        int totalTake = playerTake + tutorTake;

        if (totalTake < Remaining)
        {
            Remaining -= totalTake;
            _choicePairs.Add(new ChoicePair(
                playerTake,
                tutorTake,
                remainingBefore,
                Remaining));
            PlayerPrevious = playerTake;
            TutorPrevious = tutorTake;
            LockedPlayerTake = null;
            return RoundOutcome.Continue;
        }

        Remaining = 0;
        _choicePairs.Add(new ChoicePair(
            playerTake,
            tutorTake,
            remainingBefore,
            Remaining));
        PlayerPrevious = playerTake;
        TutorPrevious = tutorTake;
        LockedPlayerTake = null;
        Result = ResolveTerminalOutcome();
        return Result;
    }

    public LimitBashGame Clone()
    {
        var clone = new LimitBashGame();
        clone.Restore(
            InitialUnits,
            Remaining,
            PlayerPrevious,
            TutorPrevious,
            _choicePairs,
            Result);

        if (LockedPlayerTake.HasValue)
        {
            clone.LockPlayerChoice(LockedPlayerTake.Value);
        }

        return clone;
    }

    private IReadOnlyList<int> GetLegalActions(int? previous)
    {
        if (IsGameOver)
        {
            return Array.Empty<int>();
        }

        return Enumerable.Range(MinimumTake, MaximumTake)
            .Where(amount => amount != previous)
            .ToArray();
    }

    private RoundOutcome ResolveTerminalOutcome()
    {
        ChoicePair current = _choicePairs[^1];

        if (current.IsDifferent)
        {
            return current.LargerActor == Actor.Player
                ? RoundOutcome.PlayerLose
                : RoundOutcome.PlayerWin;
        }

        for (int index = _choicePairs.Count - 2; index >= 0; index--)
        {
            ChoicePair earlier = _choicePairs[index];

            if (!earlier.IsDifferent)
            {
                continue;
            }

            return earlier.LargerActor == Actor.Player
                ? RoundOutcome.PlayerLose
                : RoundOutcome.PlayerWin;
        }

        return RoundOutcome.Draw;
    }
}
