using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

/// <summary>
/// Records current-session behavior, maintains the derived player profile, and
/// persists every completed interaction through the configured profile store.
/// </summary>
public sealed class PlayerModel
{
    private readonly IPlayerProfileStore _store;
    private string _currentSessionId = string.Empty;

    public PlayerModel(IPlayerProfileStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        Profile = _store.LoadOrCreate();

        NormalizeLoadedProfile();
        RecalculateAggregates();
    }

    public PlayerProfile Profile { get; }

    public string? LoadWarning => _store.LastLoadWarning;

    public string? LastPersistenceError { get; private set; }

    public event Action<string>? PersistenceFailed;

    public string BeginSession()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;

        // A session left open in a previous process indicates an unclean exit.
        foreach (PlayerSessionRecord session in Profile.Sessions.Where(
            session => !session.EndedAtUtc.HasValue))
        {
            session.EndedAtUtc = now;
            session.EndReason = "interrupted";

            AddBehavior(
                PlayerBehaviorType.SessionEnded,
                occurredAtUtc: now,
                metadata: new Dictionary<string, string>
                {
                    ["reason"] = "interrupted"
                },
                sessionId: session.SessionId);
        }

        _currentSessionId = Guid.NewGuid().ToString("N");
        Profile.Sessions.Add(new PlayerSessionRecord
        {
            SessionId = _currentSessionId,
            StartedAtUtc = now
        });

        AddBehavior(PlayerBehaviorType.SessionStarted, occurredAtUtc: now);
        RecalculateAggregates();
        Persist();
        return _currentSessionId;
    }

    public void EndSession(string reason)
    {
        if (string.IsNullOrEmpty(_currentSessionId))
        {
            return;
        }

        DateTimeOffset now = DateTimeOffset.UtcNow;
        PlayerSessionRecord? session = Profile.Sessions.LastOrDefault(
            item => item.SessionId == _currentSessionId);

        if (session is not null && !session.EndedAtUtc.HasValue)
        {
            session.EndedAtUtc = now;
            session.EndReason = string.IsNullOrWhiteSpace(reason)
                ? "application_exit"
                : reason;
        }

        AddBehavior(
            PlayerBehaviorType.SessionEnded,
            occurredAtUtc: now,
            metadata: new Dictionary<string, string>
            {
                ["reason"] = session?.EndReason ?? reason
            });

        RecalculateAggregates();
        Persist();
        _currentSessionId = string.Empty;
    }

    public void RecordRoundStarted(int roundIndex, bool restarted, int previousRemaining)
    {
        if (restarted)
        {
            AddBehavior(
                PlayerBehaviorType.RoundRestarted,
                roundIndex: roundIndex - 1,
                remaining: previousRemaining);
        }

        AddBehavior(
            PlayerBehaviorType.RoundStarted,
            roundIndex: roundIndex,
            remaining: BashGame.DefaultInitialUnits);

        RecalculateAggregates();
        Persist();
    }

    public void RecordChoiceWindowOpened(int roundIndex, int turnIndex, int remaining)
    {
        AddBehavior(
            PlayerBehaviorType.ChoiceWindowOpened,
            roundIndex: roundIndex,
            turnIndex: turnIndex,
            remaining: remaining);
        Persist();
    }

    public void RecordChoiceWindowAbandoned(
        int roundIndex,
        int turnIndex,
        double decisionSeconds,
        int remaining,
        string reason)
    {
        AddBehavior(
            PlayerBehaviorType.ChoiceWindowAbandoned,
            roundIndex: roundIndex,
            turnIndex: turnIndex,
            durationMilliseconds: (long)Math.Round(Math.Max(0, decisionSeconds) * 1000),
            remaining: remaining,
            metadata: new Dictionary<string, string>
            {
                ["reason"] = string.IsNullOrWhiteSpace(reason) ? "abandoned" : reason
            });
        Persist();
    }

    public void RecordHoverStarted(int roundIndex, int turnIndex, int choice, int remaining)
    {
        AddBehavior(
            PlayerBehaviorType.ChoiceHoverStarted,
            roundIndex: roundIndex,
            turnIndex: turnIndex,
            choice: choice,
            remaining: remaining);
        Persist();
    }

    public void RecordHoverEnded(
        int roundIndex,
        int turnIndex,
        int choice,
        long durationMilliseconds,
        int remaining)
    {
        AddBehavior(
            PlayerBehaviorType.ChoiceHoverEnded,
            roundIndex: roundIndex,
            turnIndex: turnIndex,
            choice: choice,
            durationMilliseconds: Math.Max(0, durationMilliseconds),
            remaining: remaining);

        RecalculateAggregates();
        Persist();
    }

    public void RecordChoice(
        int roundIndex,
        int turnIndex,
        int choice,
        double decisionSeconds,
        IReadOnlyDictionary<int, long> hoverMilliseconds,
        int remainingBefore,
        int remainingAfter,
        int? publicPrediction = null)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        bool wasReversal = publicPrediction.HasValue && choice != publicPrediction.Value;
        var hoverCopy = hoverMilliseconds.ToDictionary(
            item => item.Key,
            item => Math.Max(0, item.Value));

        Profile.History.Add(new ChoiceRecord
        {
            ChoiceIndex = Profile.History.Count == 0
                ? 1
                : Profile.History.Max(item => item.ChoiceIndex) + 1,
            SessionId = RequireCurrentSession(),
            RoundIndex = roundIndex,
            TurnIndex = turnIndex,
            Choice = choice,
            DecisionSeconds = Math.Max(0, decisionSeconds),
            HoverMilliseconds = hoverCopy,
            PublicPrediction = publicPrediction,
            WasReversal = wasReversal,
            RemainingBefore = remainingBefore,
            RemainingAfter = remainingAfter,
            OccurredAtUtc = now
        });

        var metadata = new Dictionary<string, string>
        {
            ["decisionSeconds"] = Math.Max(0, decisionSeconds)
                .ToString("0.000", CultureInfo.InvariantCulture),
            ["remainingBefore"] = remainingBefore.ToString(CultureInfo.InvariantCulture),
            ["remainingAfter"] = remainingAfter.ToString(CultureInfo.InvariantCulture)
        };

        foreach ((int hoverChoice, long milliseconds) in hoverCopy)
        {
            metadata[$"hoverMilliseconds.{hoverChoice}"] = milliseconds
                .ToString(CultureInfo.InvariantCulture);
        }

        if (publicPrediction.HasValue)
        {
            metadata["publicPrediction"] = publicPrediction.Value
                .ToString(CultureInfo.InvariantCulture);
            metadata["wasReversal"] = wasReversal.ToString(CultureInfo.InvariantCulture);
        }

        AddBehavior(
            PlayerBehaviorType.ChoiceSelected,
            occurredAtUtc: now,
            roundIndex: roundIndex,
            turnIndex: turnIndex,
            choice: choice,
            remaining: remainingAfter,
            metadata: metadata);

        RecalculateAggregates();
        Persist();
    }

    public void RecordRoundCompleted(int roundIndex, GameResult result)
    {
        AddBehavior(
            PlayerBehaviorType.RoundCompleted,
            roundIndex: roundIndex,
            remaining: 0,
            metadata: new Dictionary<string, string>
            {
                ["result"] = result.ToString()
            });

        RecalculateAggregates();
        Persist();
    }

    private void AddBehavior(
        PlayerBehaviorType type,
        DateTimeOffset? occurredAtUtc = null,
        int? roundIndex = null,
        int? turnIndex = null,
        int? choice = null,
        long? durationMilliseconds = null,
        int? remaining = null,
        Dictionary<string, string>? metadata = null,
        string? sessionId = null)
    {
        long nextSequence = Profile.BehaviorHistory.Count == 0
            ? 1
            : Profile.BehaviorHistory.Max(item => item.Sequence) + 1;

        Profile.BehaviorHistory.Add(new PlayerBehaviorRecord
        {
            Sequence = nextSequence,
            SessionId = sessionId ?? RequireCurrentSession(type),
            Type = type,
            OccurredAtUtc = occurredAtUtc ?? DateTimeOffset.UtcNow,
            RoundIndex = roundIndex,
            TurnIndex = turnIndex,
            Choice = choice,
            DurationMilliseconds = durationMilliseconds,
            Remaining = remaining,
            Metadata = metadata ?? new Dictionary<string, string>()
        });
    }

    private string RequireCurrentSession(
        PlayerBehaviorType type = PlayerBehaviorType.ChoiceSelected)
    {
        if (!string.IsNullOrEmpty(_currentSessionId))
        {
            return _currentSessionId;
        }

        if (type == PlayerBehaviorType.SessionStarted && Profile.Sessions.Count > 0)
        {
            return Profile.Sessions[^1].SessionId;
        }

        throw new InvalidOperationException("BeginSession must be called before recording behavior.");
    }

    private void NormalizeLoadedProfile()
    {
        Profile.SchemaVersion = PlayerProfile.CurrentSchemaVersion;
        Profile.SubjectId = string.IsNullOrWhiteSpace(Profile.SubjectId)
            ? "S-17"
            : Profile.SubjectId;
        Profile.Sessions ??= new List<PlayerSessionRecord>();
        Profile.History ??= new List<ChoiceRecord>();
        Profile.BehaviorHistory ??= new List<PlayerBehaviorRecord>();
        Profile.ChoiceCounts ??= new Dictionary<int, int>();
        Profile.TotalHoverMillisecondsByChoice ??= new Dictionary<int, long>();

        foreach (ChoiceRecord choice in Profile.History)
        {
            choice.HoverMilliseconds ??= new Dictionary<int, long>();
        }

        foreach (PlayerBehaviorRecord behavior in Profile.BehaviorHistory)
        {
            behavior.Metadata ??= new Dictionary<string, string>();
        }
    }

    private void RecalculateAggregates()
    {
        Profile.TotalSessions = Profile.Sessions.Count;
        Profile.ChoiceCounts = Enumerable.Range(
                BashGame.MinimumTake,
                BashGame.MaximumTake)
            .ToDictionary(
                choice => choice,
                choice => Profile.History.Count(record => record.Choice == choice));

        int choiceCount = Profile.History.Count;
        Profile.MaxTakeBias = choiceCount == 0
            ? 0
            : Profile.ChoiceCounts[BashGame.MaximumTake] / (float)choiceCount;

        Profile.AverageDecisionSeconds = choiceCount == 0
            ? 0
            : Profile.History.Average(record => record.DecisionSeconds);
        Profile.FastestDecisionSeconds = choiceCount == 0
            ? 0
            : Profile.History.Min(record => record.DecisionSeconds);
        Profile.SlowestDecisionSeconds = choiceCount == 0
            ? 0
            : Profile.History.Max(record => record.DecisionSeconds);

        Profile.ConsecutiveMax = 0;
        for (int index = choiceCount - 1;
            index >= 0 && Profile.History[index].Choice == BashGame.MaximumTake;
            index--)
        {
            Profile.ConsecutiveMax++;
        }

        List<ChoiceRecord> predicted = Profile.History
            .Where(record => record.PublicPrediction.HasValue)
            .ToList();
        Profile.ReversalTendency = predicted.Count == 0
            ? 0
            : predicted.Count(record => record.WasReversal) / (float)predicted.Count;
        Profile.Predictability = CalculatePredictability();

        Profile.TotalHoverMillisecondsByChoice = Enumerable.Range(
                BashGame.MinimumTake,
                BashGame.MaximumTake)
            .ToDictionary(choice => choice, _ => 0L);

        foreach (PlayerBehaviorRecord behavior in Profile.BehaviorHistory.Where(
            record => record.Type == PlayerBehaviorType.ChoiceHoverEnded
                && record.Choice.HasValue
                && record.DurationMilliseconds.HasValue))
        {
            int choice = behavior.Choice!.Value;
            if (Profile.TotalHoverMillisecondsByChoice.ContainsKey(choice))
            {
                Profile.TotalHoverMillisecondsByChoice[choice] +=
                    Math.Max(0, behavior.DurationMilliseconds!.Value);
            }
        }

        Profile.TotalRoundsStarted = CountBehaviors(PlayerBehaviorType.RoundStarted);
        Profile.TotalRoundsCompleted = CountBehaviors(PlayerBehaviorType.RoundCompleted);
        Profile.TotalRoundRestarts = CountBehaviors(PlayerBehaviorType.RoundRestarted);
        Profile.TotalPlayerWins = CountRoundResults(GameResult.PlayerWin);
        Profile.TotalAIWins = CountRoundResults(GameResult.AIWin);
        Profile.UpdatedAtUtc = DateTimeOffset.UtcNow;
    }

    private float CalculatePredictability()
    {
        if (Profile.History.Count < 2)
        {
            return 0.34f;
        }

        float highestChoiceFrequency = Enumerable.Range(
                BashGame.MinimumTake,
                BashGame.MaximumTake)
            .Max(choice => Profile.History.Count(record => record.Choice == choice))
            / (float)Profile.History.Count;

        float repeatRate = Profile.History
            .Zip(
                Profile.History.Skip(1),
                (first, second) => first.Choice == second.Choice ? 1f : 0f)
            .DefaultIfEmpty(0)
            .Average();

        return Math.Clamp(
            0.25f + (highestChoiceFrequency * 0.45f) + (repeatRate * 0.30f),
            0.33f,
            0.95f);
    }

    private int CountBehaviors(PlayerBehaviorType type)
    {
        return Profile.BehaviorHistory.Count(record => record.Type == type);
    }

    private int CountRoundResults(GameResult result)
    {
        string expected = result.ToString();
        return Profile.BehaviorHistory.Count(record =>
            record.Type == PlayerBehaviorType.RoundCompleted
            && record.Metadata.TryGetValue("result", out string? actual)
            && actual == expected);
    }

    private void Persist()
    {
        Profile.UpdatedAtUtc = DateTimeOffset.UtcNow;

        try
        {
            _store.Save(Profile);
            LastPersistenceError = null;
        }
        catch (Exception exception) when (
            exception is IOException
            or UnauthorizedAccessException
            or InvalidOperationException)
        {
            LastPersistenceError = exception.Message;
            PersistenceFailed?.Invoke(exception.Message);
        }
    }
}
