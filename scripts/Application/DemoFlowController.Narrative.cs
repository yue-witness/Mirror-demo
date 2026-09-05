using Godot;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

// Narrative responsibilities of the single demo flow controller.
public partial class DemoFlowController
{
    private void EnterDialoguePhase(DemoPhase phase, bool showChapter = false)
    {
        _flowVersion++;
        _phase = phase;
        _dialogueIndex = 0;
        _activeBriefingLineId = string.Empty;
        _pendingGameStart = PendingGameStart.None;
        _pendingLimitDirective = null;
        _selectedChoice = null;
        _inputLocked = false;
        _bash = null;
        _limitBash = null;
        WriteCheckpoint();

        if (showChapter)
        {
            ShowPrimaryScreen(_hud);
            _chapterPending = true;

            if (phase == DemoPhase.Background)
            {
                _hud.ShowChapter(
                    "Chapter 0",
                    "Background");
            }
            else
            {
                _hud.ShowChapter(
                    "Chapter 1",
                    "LEARNING");
            }

            return;
        }

        RenderDialogue();
    }

    private void ContinueChapter()
    {
        if (!_chapterPending)
        {
            return;
        }

        _chapterPending = false;
        _hud.HideChapter();
        RenderDialogue();
    }

    private void RenderDialogue()
    {
        IReadOnlyList<DialogueLine> lines = GetActiveDialogueLines();

        if (lines.Count == 0)
        {
            throw new InvalidOperationException($"Phase {_phase} contains no dialogue.");
        }

        _dialogueIndex = Math.Clamp(_dialogueIndex, 0, lines.Count - 1);
        ShowPrimaryScreen(_dialogueUI);
        DialogueLine line = lines[_dialogueIndex];

        if (_phase == DemoPhase.Summary)
        {
            _dialogueUI.ShowSummary(
                line,
                _dialogueIndex,
                lines.Count,
                _stats,
                redEye: line.Id == "summary_meta_observer");
        }
        else
        {
            _dialogueUI.ShowDialogue(
                _phase,
                line,
                _dialogueIndex,
                lines.Count);
        }
    }

    private void AdvanceCurrentPage()
    {
        if (_inputLocked)
        {
            return;
        }

        switch (_phase)
        {
            case DemoPhase.Background:
            case DemoPhase.BashTutorial:
            case DemoPhase.BashRound2Intro:
            case DemoPhase.BashRetryBriefing:
            case DemoPhase.RuleTransition:
            case DemoPhase.LimitGameBriefing:
            case DemoPhase.LimitRestartBriefing:
            case DemoPhase.Summary:
                AdvanceDialogue();
                break;

            case DemoPhase.RoundResult:
                AdvanceAfterResult();
                break;

        }
    }

    private void AdvanceDialogue()
    {
        IReadOnlyList<DialogueLine> lines = GetActiveDialogueLines();
        _dialogueIndex++;

        if (_dialogueIndex < lines.Count)
        {
            WriteCheckpoint();
            RenderDialogue();
            return;
        }

        switch (_phase)
        {
            case DemoPhase.Background:
                EnterDialoguePhase(DemoPhase.BashTutorial, showChapter: true);
                break;

            case DemoPhase.BashTutorial:
                StartBashRound(roundIndex: 1);
                break;

            case DemoPhase.BashRound2Intro:
                StartBashRound(roundIndex: 2);
                break;

            case DemoPhase.BashRetryBriefing:
            case DemoPhase.LimitGameBriefing:
            case DemoPhase.LimitRestartBriefing:
                StartPendingGame();
                break;

            case DemoPhase.RuleTransition:
                StartLimitBashGame();
                break;

            case DemoPhase.Summary:
                CompleteDemo();
                break;
        }
    }

    private IReadOnlyList<DialogueLine> GetActiveDialogueLines()
    {
        if (_phase is DemoPhase.BashRetryBriefing
            or DemoPhase.LimitGameBriefing
            or DemoPhase.LimitRestartBriefing)
        {
            if (string.IsNullOrWhiteSpace(_activeBriefingLineId))
            {
                throw new InvalidOperationException(
                    $"Phase {_phase} has no selected briefing dialogue.");
            }

            return new[] { _dialogues.GetById(_activeBriefingLineId) };
        }

        return _dialogues.Get(_phase);
    }

    private void EnterBriefing(
        DemoPhase phase,
        string poolId,
        PendingGameStart pendingStart,
        OutcomeDirective? pendingDirective = null)
    {
        _flowVersion++;
        _phase = phase;
        _dialogueIndex = 0;
        _activeBriefingLineId = PickTutorLine(poolId).Id;
        _pendingGameStart = pendingStart;
        _pendingLimitDirective = pendingDirective;
        _selectedChoice = null;
        _inputLocked = false;
        _bash = null;
        _limitBash = null;
        ShowPrimaryScreen(_dialogueUI);
        WriteCheckpoint();
        RenderDialogue();
    }

    private void StartPendingGame()
    {
        PendingGameStart pendingStart = _pendingGameStart;
        OutcomeDirective? pendingDirective = _pendingLimitDirective;
        _activeBriefingLineId = string.Empty;
        _pendingGameStart = PendingGameStart.None;
        _pendingLimitDirective = null;

        switch (pendingStart)
        {
            case PendingGameStart.BashRound1:
                StartBashRound(roundIndex: 1);
                break;

            case PendingGameStart.BashRound2:
                StartBashRound(roundIndex: 2);
                break;

            case PendingGameStart.LimitBash:
                StartLimitBashGame();
                break;

            case PendingGameStart.LimitBashPreservingDirective:
                _pendingLimitDirective = pendingDirective;
                StartLimitBashGame(preserveDirective: true);
                break;

            default:
                throw new InvalidOperationException(
                    "The briefing has no pending game destination.");
        }
    }

    private void UpdateTutorDialogue(
        string? dialoguePool,
        string fallbackPool)
    {
        if (!string.IsNullOrWhiteSpace(dialoguePool))
        {
            SetTutorDialogue(dialoguePool);
        }
        else if (string.IsNullOrWhiteSpace(_currentTutorDialogue))
        {
            SetTutorDialogue(fallbackPool);
        }
    }

    private void ClearCurrentTutorDialogue()
    {
        _currentTutorDialogueId = string.Empty;
        _currentTutorDialogue = string.Empty;
    }

    private void SetBashResultDialogue(RoundOutcome outcome)
    {
        string pool;

        if (outcome == RoundOutcome.PlayerWin)
        {
            pool = _bashRoundIndex == 1
                ? TutorDialoguePool.BashRoundOneWin
                : TutorDialoguePool.BashRoundTwoWin;
        }
        else if (outcome == RoundOutcome.PlayerLose)
        {
            int failures = _bashRoundIndex == 1
                ? _bashRoundOneFailures
                : _bashRoundTwoFailures;
            pool = failures switch
            {
                <= 1 => TutorDialoguePool.BashLossTier1,
                2 => TutorDialoguePool.BashLossTier2,
                _ => TutorDialoguePool.BashLossTier3
            };
        }
        else
        {
            throw new InvalidOperationException(
                "Standard Bash does not support a draw result.");
        }

        SetTutorDialogue(pool);
    }

    private void SetLimitResultDialogue(RoundOutcome outcome)
    {
        ChoicePair? finalPair = _limitBash?.ChoicePairs.Count > 0
            ? _limitBash.ChoicePairs[^1]
            : null;
        bool directComparison = finalPair.HasValue
            && finalPair.Value.PlayerTake != finalPair.Value.TutorTake;
        string pool = outcome switch
        {
            RoundOutcome.PlayerWin => directComparison
                ? TutorDialoguePool.LimitWinDirect
                : TutorDialoguePool.LimitWinHistory,
            RoundOutcome.PlayerLose => directComparison
                ? TutorDialoguePool.LimitLossDirect
                : TutorDialoguePool.LimitLossHistory,
            RoundOutcome.Draw => _stats.IsLimitBashComplete
                ? TutorDialoguePool.LimitDrawCompletion
                : TutorDialoguePool.LimitDraw,
            _ => throw new InvalidOperationException(
                "A result dialogue requires a settled outcome.")
        };

        SetTutorDialogue(pool);
    }

    private void SetTutorDialogue(string poolId)
    {
        DialogueLine line = PickTutorLine(poolId);
        _currentTutorDialogueId = line.Id;
        _currentTutorDialogue = FormatDialogue(line.Text);
    }

    private void SetTutorDialogueById(string lineId)
    {
        DialogueLine line = _dialogues.GetById(lineId);
        _currentTutorDialogueId = line.Id;
        _currentTutorDialogue = FormatDialogue(line.Text);
    }

    private DialogueLine PickTutorLine(string poolId)
    {
        IReadOnlyList<DialogueLine> pool = _dialogues.GetRandomPool(poolId)
            .Where(IsDialogueApplicable)
            .ToArray();

        if (pool.Count == 0)
        {
            throw new InvalidDataException(
                $"Dialogue pool {poolId} is missing or empty.");
        }

        if (!_dialogueHistory.TryGetValue(poolId, out List<string>? recent))
        {
            recent = new List<string>();
            _dialogueHistory[poolId] = recent;
        }

        DialogueLine[] candidates = pool
            .Where(line => !recent.Contains(line.Id, StringComparer.Ordinal))
            .ToArray();

        if (candidates.Length == 0)
        {
            candidates = pool.ToArray();
        }

        int remaining = _bash?.Remaining ?? _limitBash?.Remaining ?? 0;
        uint selector = 2_166_136_261u;

        MixDialogueSelector(ref selector, _sessionRandom.Seed);
        MixDialogueSelector(ref selector, _dialogueStep++);
        MixDialogueSelector(ref selector, (int)_phase);
        MixDialogueSelector(ref selector, _bashRoundIndex);
        MixDialogueSelector(ref selector, _limitGameIndex);
        MixDialogueSelector(ref selector, _currentGameTurns);
        MixDialogueSelector(ref selector, remaining);

        foreach (char character in poolId)
        {
            MixDialogueSelector(ref selector, character);
        }

        DialogueLine selected = candidates[(int)(selector % (uint)candidates.Length)];
        recent.Add(selected.Id);

        while (recent.Count > DialogueHistoryLimit)
        {
            recent.RemoveAt(0);
        }

        return selected;
    }

    private bool IsDialogueApplicable(DialogueLine line)
    {
        // This recorded line explicitly says that both revealed values match.
        // Do not draw it from the random pool after unequal requests.
        return line.Id != "limit_reveal_equal"
            || (_limitBash is not null
                && _limitBash.ChoicePairs.Count > 0
                && !_limitBash.ChoicePairs[^1].IsDifferent);
    }

    private string FormatDialogue(string text)
    {
        return text
            .Replace(
                "{turn_count}",
                _currentGameTurns.ToString(),
                StringComparison.Ordinal)
            .Replace(
                "{reveal_count}",
                _currentGameTurns.ToString(),
                StringComparison.Ordinal);
    }

    private string ResolveTutorDialogueId(string renderedText)
    {
        DialogueLine? matchingLine = _dialogues.GetAll()
            .FirstOrDefault(line => line.Speaker == "TUTOR"
                && FormatDialogue(line.Text) == renderedText);

        if (matchingLine is null)
        {
            GD.PushWarning(
                $"Could not restore a Tutor dialogue ID for: {renderedText}");
            return string.Empty;
        }

        return matchingLine.Id;
    }

    private void UpdateChoiceStageDialogue(
        int? previousChoice,
        int currentChoice,
        string firstSelectionPool)
    {
        if (!previousChoice.HasValue)
        {
            if (!_selectionDialogueShown && ShouldShowSelectionDialogue(currentChoice))
            {
                SetTutorDialogue(firstSelectionPool);
            }

            _selectionDialogueShown = true;
            return;
        }

        if (previousChoice.Value != currentChoice && !_revisionDialogueShown)
        {
            _revisionDialogueShown = true;
            SetTutorDialogue(TutorDialoguePool.ChoiceRevision);
        }
    }

    private bool ShouldShowSelectionDialogue(int choice)
    {
        uint selector = 2_166_136_261u;
        MixDialogueSelector(ref selector, _sessionRandom.Seed);
        MixDialogueSelector(ref selector, _dialogueStep++);
        MixDialogueSelector(ref selector, (int)_phase);
        MixDialogueSelector(ref selector, _currentGameTurns);
        MixDialogueSelector(ref selector, choice);
        return selector % 100u < SelectionDialoguePercent;
    }

    private void ResetTurnDialogueState()
    {
        _selectionDialogueShown = false;
        _revisionDialogueShown = false;
        _hesitationDialogueShown = false;
        uint spread = unchecked((uint)(_sessionRandom.Seed + _dialogueStep));
        _hesitationDueTicks = Time.GetTicksMsec() + 8_000u + spread % 4_001u;
    }

    private bool RequiresForcedChoiceTutorial()
    {
        return _phase == DemoPhase.BashGame1Round1
            && _bashRoundIndex == 1
            && _bashRoundOneFailures == 0
            && _currentGameTurns == 0
            && _bash?.CurrentTurn == Actor.Player;
    }

    private void TryShowHesitationDialogue()
    {
        if (_hesitationDialogueShown
            || _inputLocked
            || _speechPlayer.Playing
            || _selectedChoice.HasValue
            || Time.GetTicksMsec() < _hesitationDueTicks)
        {
            return;
        }

        bool bashInputOpen = (_phase is DemoPhase.BashGame1Round1
                or DemoPhase.BashGame1Round2)
            && _bash is not null
            && _bash.CurrentTurn == Actor.Player;
        bool limitInputOpen = _phase == DemoPhase.LimitBash
            && _limitBash is not null;

        if (!bashInputOpen && !limitInputOpen)
        {
            return;
        }

        _hesitationDialogueShown = true;
        SetTutorDialogue(TutorDialoguePool.ChoiceHesitation);

        if (bashInputOpen)
        {
            RenderBash("The lattice is stable. Awaiting an anchor request.");
        }
        else
        {
            RenderLimitBash(
                waiting: false,
                log: "The lattice is stable. Awaiting a simultaneous request.");
        }
    }

    private static string GetBashRetryPool(int roundIndex, int failureCount)
    {
        int tier = Math.Clamp(failureCount, 1, 3);

        return (roundIndex, tier) switch
        {
            (1, 1) => TutorDialoguePool.BashRoundOneRetryHint1,
            (1, 2) => TutorDialoguePool.BashRoundOneRetryHint2,
            (1, _) => TutorDialoguePool.BashRoundOneRetryHint3,
            (2, 1) => TutorDialoguePool.BashRoundTwoRetryHint1,
            (2, 2) => TutorDialoguePool.BashRoundTwoRetryHint2,
            (2, _) => TutorDialoguePool.BashRoundTwoRetryHint3,
            _ => throw new ArgumentOutOfRangeException(nameof(roundIndex))
        };
    }

    private static void MixDialogueSelector(ref uint hash, int value)
    {
        hash ^= unchecked((uint)value);
        hash *= 16_777_619u;
    }
}
