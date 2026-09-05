using Godot;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

// Persistence responsibilities of the single demo flow controller.
public partial class DemoFlowController
{
    private void RestoreState(DemoSaveState state)
    {
        _flowVersion++;
        _saveId = state.SaveId;
        _phase = state.ResumePhase;
        _dialogueIndex = state.DialogueIndex;
        _activeBriefingLineId = state.ActiveBriefingLineId;
        _currentTutorDialogueId = string.Empty;
        _currentTutorDialogue = state.CurrentTutorDialogue;
        _pendingGameStart = state.PendingGameStart;
        _pendingLimitDirective = state.PendingLimitDirective;
        _bashRoundIndex = state.BashRoundIndex;
        _limitGameIndex = state.LimitGameIndex;
        _bashRoundOneFailures = state.BashRoundOneFailures;
        _bashRoundTwoFailures = state.BashRoundTwoFailures;
        _dialogueStep = state.DialogueStep;
        _dialogueHistory.Clear();

        foreach (DialoguePoolHistorySnapshot history in state.DialogueHistory)
        {
            _dialogueHistory[history.PoolId] = history.RecentLineIds.ToList();
        }

        _stats = SessionStats.FromSnapshot(state.Stats);
        _sessionRandom = new SessionRandom(state.SessionSeed, state.RngStep);
        StartPlayClock(state.ElapsedPlayMilliseconds);
        _selectedChoice = null;
        _inputLocked = false;
        _forcedChoiceTutorialStage = ForcedChoiceTutorialStage.Hidden;
        _chapterPending = false;
        _activePrimaryScreen = null;
        _titleScreen.Visible = false;
        _hud.Visible = false;
        _dialogueUI.Visible = false;
        _hud.HideChapter();

        if (_phase is DemoPhase.Background
            or DemoPhase.BashTutorial
            or DemoPhase.BashRound2Intro
            or DemoPhase.BashRetryBriefing
            or DemoPhase.RuleTransition
            or DemoPhase.LimitGameBriefing
            or DemoPhase.LimitRestartBriefing
            or DemoPhase.Summary)
        {
            RenderDialogue();
            return;
        }

        GameSnapshot snapshot = state.CurrentGame
            ?? throw new InvalidOperationException("The saved phase requires game state.");

        if (snapshot.Game == GameKind.Bash)
        {
            _bash = new BashGame();
            _bash.Restore(
                snapshot.InitialUnits,
                snapshot.Remaining,
                snapshot.CurrentTurn,
                snapshot.Result,
                snapshot.ChoicePairs);
            _bashRoundIndex = snapshot.BashRoundIndex;
            _currentGameTurns = snapshot.RoundIndex;
            _limitBash = null;

            if (RequiresForcedChoiceTutorial())
            {
                _forcedChoiceTutorialStage =
                    ForcedChoiceTutorialStage.SelectB;
            }
        }
        else
        {
            _limitBash = new LimitBashGame();
            _limitBash.Restore(
                snapshot.InitialUnits,
                snapshot.Remaining,
                snapshot.PlayerPrevious,
                snapshot.TutorPrevious,
                snapshot.ChoicePairs,
                snapshot.Result);
            _limitDirective = snapshot.Directive
                ?? throw new InvalidOperationException("Limit Bash directive is missing.");
            _limitGameIndex = snapshot.LimitGameIndex;
            _currentGameTurns = snapshot.RoundIndex;
            _bash = null;
        }

        if (!string.IsNullOrWhiteSpace(_currentTutorDialogue))
        {
            _currentTutorDialogueId = ResolveTutorDialogueId(
                _currentTutorDialogue);
        }

        if (_phase == DemoPhase.RoundResult)
        {
            ShowPrimaryScreen(_hud);
            _lastSettledGame = snapshot.Game;
            _lastOutcome = snapshot.Result;
            _lastGameIndex = snapshot.Game == GameKind.Bash
                ? snapshot.BashRoundIndex
                : snapshot.LimitGameIndex;
            _lastGameTurns = snapshot.RoundIndex;

            if (string.IsNullOrWhiteSpace(_currentTutorDialogue))
            {
                if (snapshot.Game == GameKind.Bash)
                {
                    SetBashResultDialogue(snapshot.Result);
                }
                else
                {
                    SetLimitResultDialogue(snapshot.Result);
                }
            }

            _hud.ShowRoundResult(
                snapshot.Game,
                snapshot.Result,
                _lastGameIndex,
                _lastGameTurns,
                _stats,
                willContinue: snapshot.Game == GameKind.Bash
                    || !_stats.IsLimitBashComplete,
                tutorDialogueId: _currentTutorDialogueId,
                tutorDialogue: _currentTutorDialogue,
                finalChoice: snapshot.Game == GameKind.LimitBash
                    && snapshot.ChoicePairs.Count > 0
                        ? snapshot.ChoicePairs[^1]
                        : null,
                choiceHistory: snapshot.ChoicePairs);
            return;
        }

        ShowPrimaryScreen(_hud);
        ResetTurnDialogueState();

        if (snapshot.Game == GameKind.Bash && _bash is not null)
        {
            _inputLocked = _bash.CurrentTurn == Actor.Tutor;

            if (_forcedChoiceTutorialStage
                == ForcedChoiceTutorialStage.SelectB)
            {
                SetTutorDialogue(TutorDialoguePool.GuidedInputSelectB);
            }

            RenderBash(
                "Restored from a stable checkpoint.",
                _forcedChoiceTutorialStage == ForcedChoiceTutorialStage.Hidden
                    ? TutorDialoguePool.Restore
                    : null);

            if (_inputLocked)
            {
                RunBashTutorTurn(_flowVersion);
            }
        }
        else if (_limitBash is not null)
        {
            RenderLimitBash(
                waiting: false,
                log: "Restored from a stable checkpoint.",
                dialoguePool: TutorDialoguePool.Restore);
        }
    }

    private void WriteCheckpoint(bool isComplete = false)
    {
        DemoSaveState state = new(
            DemoSaveState.CurrentSchemaVersion,
            _saveId,
            _phase,
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            _sessionRandom.Seed,
            _sessionRandom.Step,
            _dialogueIndex,
            _activeBriefingLineId,
            _currentTutorDialogue,
            _pendingGameStart,
            _bashRoundIndex,
            _limitGameIndex,
            _bashRoundOneFailures,
            _bashRoundTwoFailures,
            _dialogueStep,
            _dialogueHistory
                .OrderBy(entry => entry.Key, StringComparer.OrdinalIgnoreCase)
                .Select(entry => new DialoguePoolHistorySnapshot(
                    entry.Key,
                    entry.Value.ToArray()))
                .ToArray(),
            _pendingLimitDirective,
            _stats.ToSnapshot(),
            CreateGameSnapshot(),
            GetElapsedPlayMilliseconds(),
            isComplete,
            string.Empty);
        _saveService.WriteAtomic(state);
    }

    private void TryWriteCheckpoint()
    {
        try
        {
            WriteCheckpoint();
        }
        catch (Exception exception) when (
            exception is IOException
            or UnauthorizedAccessException
            or InvalidOperationException)
        {
            GD.PushError($"Checkpoint write failed: {exception.Message}");
        }
    }

    private GameSnapshot? CreateGameSnapshot()
    {
        if (_bash is not null)
        {
            return new GameSnapshot(
                GameKind.Bash,
                _bash.InitialUnits,
                _bash.Remaining,
                _bash.CurrentTurn,
                _bashRoundIndex,
                0,
                _currentGameTurns,
                null,
                null,
                null,
                _bash.History.ToArray(),
                _bash.Result);
        }

        if (_limitBash is not null)
        {
            return new GameSnapshot(
                GameKind.LimitBash,
                _limitBash.InitialUnits,
                _limitBash.Remaining,
                Actor.Player,
                0,
                _limitGameIndex,
                _currentGameTurns,
                _limitBash.PlayerPrevious,
                _limitBash.TutorPrevious,
                _limitDirective,
                _limitBash.ChoicePairs.ToArray(),
                _limitBash.Result);
        }

        return null;
    }

    private void StartPlayClock(long elapsedMilliseconds)
    {
        _elapsedBeforeCurrentRunMs = Math.Max(0, elapsedMilliseconds);
        _currentRunStartedTicks = Time.GetTicksMsec();
        _playClockRunning = true;
    }

    private void StopPlayClock()
    {
        if (!_playClockRunning)
        {
            return;
        }

        _elapsedBeforeCurrentRunMs = GetElapsedPlayMilliseconds();
        _playClockRunning = false;
    }

    private long GetElapsedPlayMilliseconds()
    {
        if (!_playClockRunning)
        {
            return _elapsedBeforeCurrentRunMs;
        }

        ulong currentTicks = Time.GetTicksMsec();
        ulong currentRun = currentTicks >= _currentRunStartedTicks
            ? currentTicks - _currentRunStartedTicks
            : 0;
        return _elapsedBeforeCurrentRunMs
            + unchecked((long)Math.Min(currentRun, (ulong)long.MaxValue));
    }

    private static string ResolvePersistentPath(string path)
    {
        return path.StartsWith("user://", StringComparison.Ordinal)
            || path.StartsWith("res://", StringComparison.Ordinal)
            ? ProjectSettings.GlobalizePath(path)
            : Path.GetFullPath(path);
    }

    private static string? CombineWarnings(string? first, string? second)
    {
        if (string.IsNullOrWhiteSpace(first))
        {
            return second;
        }

        if (string.IsNullOrWhiteSpace(second))
        {
            return first;
        }

        return first + " " + second;
    }
}
