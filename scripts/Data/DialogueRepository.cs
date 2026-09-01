using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

public sealed record DialogueLine(
    string Id,
    string Speaker,
    string Text);

/// <summary>
/// Stable identifiers for authored Tutor lines shown during gameplay. Keeping
/// these IDs out of the controller prevents configuration keys from drifting.
/// </summary>
public static class TutorDialoguePool
{
    public const string BashRoundOneRetryHint1 = "bash_r1_retry_hint_1";
    public const string BashRoundOneRetryHint2 = "bash_r1_retry_hint_2";
    public const string BashRoundOneRetryHint3 = "bash_r1_retry_hint_3";
    public const string BashRoundTwoRetryHint1 = "bash_r2_retry_hint_1";
    public const string BashRoundTwoRetryHint2 = "bash_r2_retry_hint_2";
    public const string BashRoundTwoRetryHint3 = "bash_r2_retry_hint_3";
    public const string LimitGameTwoBegin = "limit_game_two_begin";
    public const string LimitGameThreeBegin = "limit_game_three_begin";
    public const string LimitLateBegin = "limit_late_begin";
    public const string LimitRestart = "limit_restart";
    public const string ChoiceRevision = "choice_revision";
    public const string ChoiceHesitation = "choice_hesitation";
    public const string BashState = "bash_state";
    public const string BashFirstSelection = "bash_first_selection";
    public const string BashPlayerConfirmed = "bash_player_confirmed";
    public const string BashRoundOneTutorActed = "bash_r1_tutor_acted";
    public const string BashRoundTwoTutorActed = "bash_r2_tutor_acted";
    public const string BashTerminalApproach = "bash_terminal_approach";
    public const string LimitState = "limit_state";
    public const string LimitFirstSelection = "limit_first_selection";
    public const string LimitChoiceLocked = "limit_choice_locked";
    public const string LimitReveal = "limit_reveal";
    public const string LimitTerminalApproach = "limit_terminal_approach";
    public const string Restore = "restore";
    public const string BashRoundOneWin = "bash_r1_win";
    public const string BashRoundTwoWin = "bash_r2_win";
    public const string BashLossTier1 = "bash_loss_tier_1";
    public const string BashLossTier2 = "bash_loss_tier_2";
    public const string BashLossTier3 = "bash_loss_tier_3";
    public const string LimitWinDirect = "limit_win_direct";
    public const string LimitWinHistory = "limit_win_history";
    public const string LimitLossDirect = "limit_loss_direct";
    public const string LimitLossHistory = "limit_loss_history";
    public const string LimitDraw = "limit_draw";
    public const string LimitDrawCompletion = "limit_draw_completion";

    public static IReadOnlyList<string> All { get; } = new[]
    {
        BashRoundOneRetryHint1,
        BashRoundOneRetryHint2,
        BashRoundOneRetryHint3,
        BashRoundTwoRetryHint1,
        BashRoundTwoRetryHint2,
        BashRoundTwoRetryHint3,
        LimitGameTwoBegin,
        LimitGameThreeBegin,
        LimitLateBegin,
        LimitRestart,
        ChoiceRevision,
        ChoiceHesitation,
        BashState,
        BashFirstSelection,
        BashPlayerConfirmed,
        BashRoundOneTutorActed,
        BashRoundTwoTutorActed,
        BashTerminalApproach,
        LimitState,
        LimitFirstSelection,
        LimitChoiceLocked,
        LimitReveal,
        LimitTerminalApproach,
        Restore,
        BashRoundOneWin,
        BashRoundTwoWin,
        BashLossTier1,
        BashLossTier2,
        BashLossTier3,
        LimitWinDirect,
        LimitWinHistory,
        LimitLossDirect,
        LimitLossHistory,
        LimitDraw,
        LimitDrawCompletion
    };
}

/// <summary>
/// Loads fixed dialogue by phase. JSON contains content only; it cannot execute
/// expressions or change the demo state machine.
/// </summary>
public sealed class DialogueRepository
{
    private readonly Dictionary<DemoPhase, List<DialogueLine>> _lines = new();
    private readonly Dictionary<string, List<DialogueLine>> _randomPools = new(
        StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _lineIds = new(StringComparer.Ordinal);

    public static DialogueRepository Load(params string[] paths)
    {
        var repository = new DialogueRepository();

        foreach (string path in paths)
        {
            repository.LoadFile(path);
        }

        return repository;
    }

    public IReadOnlyList<DialogueLine> Get(DemoPhase phase)
    {
        return _lines.TryGetValue(phase, out List<DialogueLine>? lines)
            ? lines
            : Array.Empty<DialogueLine>();
    }

    public IReadOnlyList<DialogueLine> GetRandomPool(string poolId)
    {
        return _randomPools.TryGetValue(poolId, out List<DialogueLine>? lines)
            ? lines
            : Array.Empty<DialogueLine>();
    }

    public IReadOnlyList<DialogueLine> GetAll()
    {
        return _lines.Values
            .SelectMany(lines => lines)
            .Concat(_randomPools.Values.SelectMany(lines => lines))
            .ToArray();
    }

    public DialogueLine GetById(string lineId)
    {
        DialogueLine? line = _lines.Values
            .SelectMany(lines => lines)
            .Concat(_randomPools.Values.SelectMany(lines => lines))
            .FirstOrDefault(candidate => candidate.Id == lineId);

        return line ?? throw new InvalidDataException(
            $"Dialogue line {lineId} does not exist.");
    }

    /// <summary>
    /// Selects a configured line from a caller-provided stable selector. The
    /// controller derives that selector from the session seed and public game
    /// state, so dialogue variation never consumes gameplay randomness.
    /// </summary>
    public DialogueLine PickRandom(string poolId, int selector)
    {
        IReadOnlyList<DialogueLine> lines = GetRandomPool(poolId);

        if (lines.Count == 0)
        {
            throw new InvalidDataException(
                $"Dialogue pool {poolId} is missing or empty.");
        }

        int index = unchecked((int)(unchecked((uint)selector) % (uint)lines.Count));
        return lines[index];
    }

    private void LoadFile(string path)
    {
        string json = File.ReadAllText(path);
        DialogueDocument? document = JsonSerializer.Deserialize<DialogueDocument>(
            json,
            new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

        if (document?.Phases is null)
        {
            throw new InvalidDataException(
                $"Dialogue file {Path.GetFileName(path)} has no phase data.");
        }

        foreach ((string phaseName, List<DialogueLine>? lines) in document.Phases)
        {
            if (!Enum.TryParse(phaseName, ignoreCase: true, out DemoPhase phase))
            {
                throw new InvalidDataException($"Unknown dialogue phase {phaseName}.");
            }

            if (_lines.ContainsKey(phase))
            {
                throw new InvalidDataException(
                    $"Dialogue phase {phaseName} is configured more than once.");
            }

            _lines[phase] = ValidateLines(lines, $"phase {phaseName}");
        }

        foreach ((string poolId, List<DialogueLine>? lines)
            in document.RandomPools ?? new Dictionary<string, List<DialogueLine>>())
        {
            if (string.IsNullOrWhiteSpace(poolId))
            {
                throw new InvalidDataException("Dialogue contains an unnamed pool.");
            }

            if (_randomPools.ContainsKey(poolId))
            {
                throw new InvalidDataException(
                    $"Dialogue pool {poolId} is configured more than once.");
            }

            _randomPools[poolId] = ValidateLines(lines, $"pool {poolId}");
        }
    }

    private List<DialogueLine> ValidateLines(
        List<DialogueLine>? lines,
        string location)
    {
        List<DialogueLine> validLines = (lines ?? new List<DialogueLine>())
            .Where(line => !string.IsNullOrWhiteSpace(line.Id)
                && !string.IsNullOrWhiteSpace(line.Speaker)
                && !string.IsNullOrWhiteSpace(line.Text))
            .ToList();

        if (validLines.Count == 0)
        {
            throw new InvalidDataException($"Dialogue {location} is empty.");
        }

        foreach (DialogueLine line in validLines)
        {
            if (!_lineIds.Add(line.Id))
            {
                throw new InvalidDataException(
                    $"Dialogue ID {line.Id} is configured more than once.");
            }
        }

        return validLines;
    }

    private sealed class DialogueDocument
    {
        public Dictionary<string, List<DialogueLine>>? Phases { get; set; }

        public Dictionary<string, List<DialogueLine>>? RandomPools { get; set; }
    }
}
