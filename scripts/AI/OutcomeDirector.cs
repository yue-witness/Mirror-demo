using System;
using System.Collections.Generic;
using System.Linq;

public sealed class ControlledOutcomeUnavailableException : Exception
{
    public ControlledOutcomeUnavailableException()
        : base("No legal Tutor action can preserve the current outcome directive.")
    {
    }
}

/// <summary>
/// Chooses a legal Tutor action only after the player has locked a Limit Bash
/// choice. The bounded solver never mutates the live game while evaluating.
/// </summary>
public sealed class OutcomeDirector
{
    public static OutcomeDirective GetDirective(int gamesPlayed, float roll)
    {
        if (gamesPlayed < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(gamesPlayed));
        }

        if (gamesPlayed == 0)
        {
            return OutcomeDirective.PlayerWinOrDraw;
        }

        if (gamesPlayed == 1)
        {
            return OutcomeDirective.PlayerLoseOrDraw;
        }

        return roll < 0.5f
            ? OutcomeDirective.PlayerWinOrDraw
            : OutcomeDirective.PlayerLoseOrDraw;
    }

    public int ChooseAfterPlayerLock(
        LimitBashGame game,
        OutcomeDirective directive)
    {
        ArgumentNullException.ThrowIfNull(game);

        if (!game.PlayerChoiceLocked)
        {
            throw new InvalidOperationException("The player choice is not locked.");
        }

        var allowedMemo = new Dictionary<string, bool>();
        var exactMemo = new Dictionary<string, bool>();
        RoundOutcome preferred = directive == OutcomeDirective.PlayerWinOrDraw
            ? RoundOutcome.PlayerWin
            : RoundOutcome.PlayerLose;

        Candidate[] candidates = game.GetLegalTutorActions()
            .Select(take => EvaluateCandidate(
                game,
                take,
                directive,
                preferred,
                allowedMemo,
                exactMemo))
            .Where(candidate => candidate.PreservesDirective)
            .OrderByDescending(candidate => candidate.Score)
            .ThenBy(candidate => candidate.Take)
            .ToArray();

        if (candidates.Length == 0)
        {
            throw new ControlledOutcomeUnavailableException();
        }

        return candidates[0].Take;
    }

    public bool CanGuaranteeDirective(
        LimitBashGame game,
        OutcomeDirective directive)
    {
        ArgumentNullException.ThrowIfNull(game);

        if (game.PlayerChoiceLocked)
        {
            throw new InvalidOperationException(
                "Directive validation must begin before the player locks a choice.");
        }

        return CanGuaranteeAllowed(game, directive, new Dictionary<string, bool>());
    }

    private static Candidate EvaluateCandidate(
        LimitBashGame liveGame,
        int tutorTake,
        OutcomeDirective directive,
        RoundOutcome preferred,
        IDictionary<string, bool> allowedMemo,
        IDictionary<string, bool> exactMemo)
    {
        LimitBashGame simulation = liveGame.Clone();
        RoundOutcome outcome = simulation.CommitTutorChoice(tutorTake);

        if (outcome != RoundOutcome.Continue)
        {
            bool allowed = IsAllowed(outcome, directive);
            int terminalScore = outcome == preferred
                ? 400
                : outcome == RoundOutcome.Draw
                    ? 300
                    : 0;
            return new Candidate(tutorTake, allowed, terminalScore);
        }

        bool preserves = CanGuaranteeAllowed(simulation, directive, allowedMemo);
        bool guaranteesPreferred = preserves
            && CanGuaranteeExact(simulation, preferred, exactMemo);
        return new Candidate(
            tutorTake,
            preserves,
            guaranteesPreferred ? 250 : preserves ? 150 : 0);
    }

    private static bool CanGuaranteeAllowed(
        LimitBashGame game,
        OutcomeDirective directive,
        IDictionary<string, bool> memo)
    {
        string key = $"A|{directive}|{CreateStateKey(game)}";

        if (memo.TryGetValue(key, out bool cached))
        {
            return cached;
        }

        // Mark pessimistically before recursion. Remaining always decreases on
        // continuation, but this also protects against a malformed cycle.
        memo[key] = false;

        foreach (int playerTake in game.GetLegalPlayerActions())
        {
            bool responseExists = false;
            LimitBashGame locked = game.Clone();
            locked.LockPlayerChoice(playerTake);

            foreach (int tutorTake in locked.GetLegalTutorActions())
            {
                LimitBashGame next = locked.Clone();
                RoundOutcome outcome = next.CommitTutorChoice(tutorTake);

                if (outcome == RoundOutcome.Continue
                    ? CanGuaranteeAllowed(next, directive, memo)
                    : IsAllowed(outcome, directive))
                {
                    responseExists = true;
                    break;
                }
            }

            if (!responseExists)
            {
                return false;
            }
        }

        memo[key] = true;
        return true;
    }

    private static bool CanGuaranteeExact(
        LimitBashGame game,
        RoundOutcome expected,
        IDictionary<string, bool> memo)
    {
        string key = $"E|{expected}|{CreateStateKey(game)}";

        if (memo.TryGetValue(key, out bool cached))
        {
            return cached;
        }

        memo[key] = false;

        foreach (int playerTake in game.GetLegalPlayerActions())
        {
            bool responseExists = false;
            LimitBashGame locked = game.Clone();
            locked.LockPlayerChoice(playerTake);

            foreach (int tutorTake in locked.GetLegalTutorActions())
            {
                LimitBashGame next = locked.Clone();
                RoundOutcome outcome = next.CommitTutorChoice(tutorTake);

                if (outcome == RoundOutcome.Continue
                    ? CanGuaranteeExact(next, expected, memo)
                    : outcome == expected)
                {
                    responseExists = true;
                    break;
                }
            }

            if (!responseExists)
            {
                return false;
            }
        }

        memo[key] = true;
        return true;
    }

    private static bool IsAllowed(
        RoundOutcome outcome,
        OutcomeDirective directive)
    {
        return directive switch
        {
            OutcomeDirective.PlayerWinOrDraw =>
                outcome is RoundOutcome.PlayerWin or RoundOutcome.Draw,
            OutcomeDirective.PlayerLoseOrDraw =>
                outcome is RoundOutcome.PlayerLose or RoundOutcome.Draw,
            _ => false
        };
    }

    private static string CreateStateKey(LimitBashGame game)
    {
        ChoicePair? latestDifference = game.ChoicePairs
            .Reverse()
            .Cast<ChoicePair?>()
            .FirstOrDefault(pair => pair!.Value.IsDifferent);

        string difference = latestDifference.HasValue
            ? $"{latestDifference.Value.PlayerTake},{latestDifference.Value.TutorTake}"
            : "-";

        return $"{game.Remaining}|{game.PlayerPrevious?.ToString() ?? "-"}|"
            + $"{game.TutorPrevious?.ToString() ?? "-"}|{difference}";
    }

    private readonly record struct Candidate(
        int Take,
        bool PreservesDirective,
        int Score);
}
