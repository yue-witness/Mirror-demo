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
    public const string BashRoundStart = "bash_round_start";
    public const string BashPlayerConfirmed = "bash_player_confirmed";
    public const string BashTutorActed = "bash_tutor_acted";
    public const string BashTerminalApproach = "bash_terminal_approach";
    public const string LimitGameStart = "limit_game_start";
    public const string LimitChoiceLocked = "limit_choice_locked";
    public const string LimitReveal = "limit_reveal";
    public const string LimitTerminalApproach = "limit_terminal_approach";
    public const string Restore = "restore";
    public const string PlayerWin = "result_player_win";
    public const string PlayerLose = "result_player_lose";
    public const string Draw = "result_draw";

    public static IReadOnlyList<string> All { get; } = new[]
    {
        BashRoundStart,
        BashPlayerConfirmed,
        BashTutorActed,
        BashTerminalApproach,
        LimitGameStart,
        LimitChoiceLocked,
        LimitReveal,
        LimitTerminalApproach,
        Restore,
        PlayerWin,
        PlayerLose,
        Draw
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
