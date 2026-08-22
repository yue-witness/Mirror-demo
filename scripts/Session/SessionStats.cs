using System;

/// <summary>
/// Session-only rule facts and settlement counters used by the HUD and save.
/// </summary>
public sealed class SessionStats
{
    public int Wins { get; private set; }

    public int Losses { get; private set; }

    public int Draws { get; private set; }

    public int BashWins { get; private set; }

    public int BashLosses { get; private set; }

    public int BashRoundsCompleted { get; private set; }

    public int BashTurnsPlayed { get; private set; }

    public int LimitBashGamesCompleted { get; private set; }

    public int LimitBashPlayerWins { get; private set; }

    public int LimitBashPlayerLosses { get; private set; }

    public int LimitBashDraws { get; private set; }

    public int ConsecutiveLimitBashDraws { get; private set; }

    public int LimitBashRoundsPlayed { get; private set; }

    public int TotalRoundsPlayed => BashTurnsPlayed + LimitBashRoundsPlayed;

    public bool IsLimitBashComplete =>
        LimitBashPlayerWins >= 2 || ConsecutiveLimitBashDraws >= 2;

    public string CompletionReason => LimitBashPlayerWins >= 2
        ? "The player reached two total wins"
        : ConsecutiveLimitBashDraws >= 2
            ? "Two consecutive draws revealed a non-losing strategy"
            : "Not yet complete";

    public void RecordBash(RoundOutcome outcome, int turns)
    {
        if (outcome is RoundOutcome.Continue or RoundOutcome.Draw)
        {
            throw new ArgumentOutOfRangeException(
                nameof(outcome),
                "Original Bash must settle as a player win or loss.");
        }

        BashTurnsPlayed += Math.Max(0, turns);

        if (outcome == RoundOutcome.PlayerWin)
        {
            Wins++;
            BashWins++;
            BashRoundsCompleted++;
        }
        else
        {
            Losses++;
            BashLosses++;
        }
    }

    public void RecordLimitBash(RoundOutcome outcome, int rounds)
    {
        if (outcome == RoundOutcome.Continue)
        {
            throw new ArgumentOutOfRangeException(
                nameof(outcome),
                "A Limit Bash game must be settled before recording.");
        }

        LimitBashGamesCompleted++;
        LimitBashRoundsPlayed += Math.Max(0, rounds);

        if (outcome == RoundOutcome.PlayerWin)
        {
            Wins++;
            LimitBashPlayerWins++;
            ConsecutiveLimitBashDraws = 0;
        }
        else if (outcome == RoundOutcome.PlayerLose)
        {
            Losses++;
            LimitBashPlayerLosses++;
            ConsecutiveLimitBashDraws = 0;
        }
        else
        {
            Draws++;
            LimitBashDraws++;
            ConsecutiveLimitBashDraws++;
        }
    }

    public SessionStatsSnapshot ToSnapshot()
    {
        return new SessionStatsSnapshot(
            Wins,
            Losses,
            Draws,
            BashWins,
            BashLosses,
            BashRoundsCompleted,
            BashTurnsPlayed,
            LimitBashGamesCompleted,
            LimitBashPlayerWins,
            LimitBashPlayerLosses,
            LimitBashDraws,
            ConsecutiveLimitBashDraws,
            LimitBashRoundsPlayed);
    }

    public static SessionStats FromSnapshot(SessionStatsSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        return new SessionStats
        {
            Wins = snapshot.Wins,
            Losses = snapshot.Losses,
            Draws = snapshot.Draws,
            BashWins = snapshot.BashWins,
            BashLosses = snapshot.BashLosses,
            BashRoundsCompleted = snapshot.BashRoundsCompleted,
            BashTurnsPlayed = snapshot.BashTurnsPlayed,
            LimitBashGamesCompleted = snapshot.LimitBashGamesCompleted,
            LimitBashPlayerWins = snapshot.LimitBashPlayerWins,
            LimitBashPlayerLosses = snapshot.LimitBashPlayerLosses,
            LimitBashDraws = snapshot.LimitBashDraws,
            ConsecutiveLimitBashDraws = snapshot.ConsecutiveLimitBashDraws,
            LimitBashRoundsPlayed = snapshot.LimitBashRoundsPlayed
        };
    }
}

public sealed record SessionStatsSnapshot(
    int Wins,
    int Losses,
    int Draws,
    int BashWins,
    int BashLosses,
    int BashRoundsCompleted,
    int BashTurnsPlayed,
    int LimitBashGamesCompleted,
    int LimitBashPlayerWins,
    int LimitBashPlayerLosses,
    int LimitBashDraws,
    int ConsecutiveLimitBashDraws,
    int LimitBashRoundsPlayed);
