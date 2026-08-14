using System;
using System.Collections.Generic;

public enum PlayerBehaviorType
{
    SessionStarted,
    SessionEnded,
    RoundStarted,
    RoundRestarted,
    RoundCompleted,
    ChoiceWindowOpened,
    ChoiceWindowAbandoned,
    ChoiceHoverStarted,
    ChoiceHoverEnded,
    ChoiceSelected
}

/// <summary>
/// One confirmed player choice and the observations available at that moment.
/// </summary>
public sealed class ChoiceRecord
{
    public long ChoiceIndex { get; set; }

    public string SessionId { get; set; } = string.Empty;

    public int RoundIndex { get; set; }

    public int TurnIndex { get; set; }

    public int Choice { get; set; }

    public double DecisionSeconds { get; set; }

    public Dictionary<int, long> HoverMilliseconds { get; set; } = new();

    public int? PublicPrediction { get; set; }

    public bool WasReversal { get; set; }

    public int RemainingBefore { get; set; }

    public int RemainingAfter { get; set; }

    public DateTimeOffset OccurredAtUtc { get; set; }
}

/// <summary>
/// Append-only event used to preserve player interactions that do not
/// necessarily result in a confirmed choice, such as hover and restart.
/// </summary>
public sealed class PlayerBehaviorRecord
{
    public long Sequence { get; set; }

    public string SessionId { get; set; } = string.Empty;

    public PlayerBehaviorType Type { get; set; }

    public DateTimeOffset OccurredAtUtc { get; set; }

    public int? RoundIndex { get; set; }

    public int? TurnIndex { get; set; }

    public int? Choice { get; set; }

    public long? DurationMilliseconds { get; set; }

    public int? Remaining { get; set; }

    public Dictionary<string, string> Metadata { get; set; } = new();
}

public sealed class PlayerSessionRecord
{
    public string SessionId { get; set; } = string.Empty;

    public DateTimeOffset StartedAtUtc { get; set; }

    public DateTimeOffset? EndedAtUtc { get; set; }

    public string EndReason { get; set; } = string.Empty;
}

/// <summary>
/// Persistent player history plus derived values used by later AI and dialogue.
/// All timestamps are UTC so records remain comparable across launches.
/// </summary>
public sealed class PlayerProfile
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    public string SubjectId { get; set; } = "S-17";

    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    public List<PlayerSessionRecord> Sessions { get; set; } = new();

    public List<ChoiceRecord> History { get; set; } = new();

    public List<PlayerBehaviorRecord> BehaviorHistory { get; set; } = new();

    public Dictionary<int, int> ChoiceCounts { get; set; } = new();

    public Dictionary<int, long> TotalHoverMillisecondsByChoice { get; set; } = new();

    public int TotalSessions { get; set; }

    public int TotalRoundsStarted { get; set; }

    public int TotalRoundsCompleted { get; set; }

    public int TotalRoundRestarts { get; set; }

    public int TotalPlayerWins { get; set; }

    public int TotalAIWins { get; set; }

    public float MaxTakeBias { get; set; }

    public float ReversalTendency { get; set; }

    public float Predictability { get; set; } = 0.34f;

    public double AverageDecisionSeconds { get; set; }

    public double FastestDecisionSeconds { get; set; }

    public double SlowestDecisionSeconds { get; set; }

    public int ConsecutiveMax { get; set; }
}
