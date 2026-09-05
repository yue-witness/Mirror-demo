using Godot;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

// Bash responsibilities of the single demo flow controller.
public partial class DemoFlowController
{
    private void StartBashRound(int roundIndex)
    {
        _flowVersion++;
        _phase = roundIndex == 1
            ? DemoPhase.BashGame1Round1
            : DemoPhase.BashGame1Round2;
        _bashRoundIndex = roundIndex;
        _currentGameTurns = 0;
        _selectedChoice = null;
        _inputLocked = roundIndex == 2;
        _activeBriefingLineId = string.Empty;
        _pendingGameStart = PendingGameStart.None;
        _pendingLimitDirective = null;
        _limitBash = null;
        ShowPrimaryScreen(_hud);

        int[] candidates = roundIndex == 1
            ? _rules.Bash.Round1InitialUnits
            : _rules.Bash.Round2InitialUnits;
        int initialUnits = candidates[_sessionRandom.Next(0, candidates.Length)];
        _bash = new BashGame();
        _bash.Start(
            initialUnits,
            roundIndex == 1 ? Actor.Player : Actor.Tutor);

        _forcedChoiceTutorialStage = RequiresForcedChoiceTutorial()
            ? ForcedChoiceTutorialStage.SelectB
            : ForcedChoiceTutorialStage.Hidden;

        if (_forcedChoiceTutorialStage
            == ForcedChoiceTutorialStage.SelectB)
        {
            SetTutorDialogue(TutorDialoguePool.GuidedInputSelectB);
        }

        ResetTurnDialogueState();
        WriteCheckpoint();
        RenderBash(
            "A fresh Stability Lattice is active in the chamber.",
            _forcedChoiceTutorialStage == ForcedChoiceTutorialStage.Hidden
                ? TutorDialoguePool.BashState
                : null);

        if (_bash.CurrentTurn == Actor.Tutor)
        {
            RunBashTutorTurn(_flowVersion);
        }
    }

    private void ConfirmBashChoice(int choice)
    {
        if (_bash is null
            || _bash.CurrentTurn != Actor.Player
            || !_bash.CanTake(choice))
        {
            return;
        }

        if (_forcedChoiceTutorialStage
            == ForcedChoiceTutorialStage.Confirm)
        {
            _forcedChoiceTutorialStage = ForcedChoiceTutorialStage.Hidden;
        }

        _inputLocked = true;
        RenderBash(
            $"Player request locked: {choice} orbiting anchors moving to the core.",
            suppressTutorDialogue: true);
        _hud.BeginBashPlayerExtraction(choice);
        ResolveBashPlayerTurn(_flowVersion, choice);
    }

    private async void ResolveBashPlayerTurn(int expectedVersion, int choice)
    {
        await ToSignal(
            GetTree().CreateTimer(
                GameplayHUD.BashPlayerExtractionSeconds),
            SceneTreeTimer.SignalName.Timeout);

        if (expectedVersion != _flowVersion
            || _bash is null
            || _bash.IsGameOver
            || _bash.CurrentTurn != Actor.Player
            || !_bash.CanTake(choice))
        {
            return;
        }

        _currentGameTurns++;
        RoundOutcome outcome = _bash.ApplyTake(Actor.Player, choice);
        _selectedChoice = null;

        if (outcome != RoundOutcome.Continue)
        {
            RenderBash(
                $"Player disengaged {choice}; the final orbiting anchor was disengaged.",
                suppressTutorDialogue: true);
            FinishBash(outcome);
            return;
        }

        RenderBash(
            $"Player disengaged {choice}; {_bash.Remaining} orbiting anchors remain active.",
            TutorDialoguePool.BashPlayerConfirmed);
        RunBashTutorTurn(expectedVersion);
    }

    private async void RunBashTutorTurn(int expectedVersion)
    {
        if (_bash is null || _bash.CurrentTurn != Actor.Tutor)
        {
            return;
        }

        if (!_currentTutorDialogueId.StartsWith(
                "bash_confirm_",
                StringComparison.OrdinalIgnoreCase))
        {
            SetTutorDialogueById("bash_confirm_turn");
            RenderBash("Tutor control accepted. Target analysis is active.");
        }

        await ToSignal(
            GetTree().CreateTimer(TutorDelaySeconds),
            SceneTreeTimer.SignalName.Timeout);

        if (expectedVersion != _flowVersion
            || _bash is null
            || _bash.IsGameOver
            || _bash.CurrentTurn != Actor.Tutor)
        {
            return;
        }

        int selector = _sessionRandom.Next(0, int.MaxValue);
        int choice = _strategy.ChooseBashMove(_bash, selector);
        _hud.ShowBashTutorSelection(choice);

        double selectionHoldSeconds = Math.Max(
            0.65,
            _hud.CurrentTutorSpeechDurationSeconds
                - TutorDelaySeconds
                - GameplayHUD.BashTutorExtractionSeconds);
        await ToSignal(
            GetTree().CreateTimer(selectionHoldSeconds),
            SceneTreeTimer.SignalName.Timeout);

        if (expectedVersion != _flowVersion
            || _bash is null
            || _bash.IsGameOver
            || _bash.CurrentTurn != Actor.Tutor)
        {
            return;
        }

        _hud.BeginBashTutorExtraction();
        await ToSignal(
            GetTree().CreateTimer(
                GameplayHUD.BashTutorExtractionSeconds),
            SceneTreeTimer.SignalName.Timeout);

        if (expectedVersion != _flowVersion
            || _bash is null
            || _bash.IsGameOver
            || _bash.CurrentTurn != Actor.Tutor)
        {
            return;
        }

        _currentGameTurns++;
        RoundOutcome outcome = _bash.ApplyTake(Actor.Tutor, choice);

        if (outcome != RoundOutcome.Continue)
        {
            RenderBash(
                $"Tutor disengaged {choice}; the final orbiting anchor was disengaged.");
            FinishBash(outcome);
            return;
        }

        _inputLocked = false;
        ResetTurnDialogueState();
        WriteCheckpoint();
        RenderBash(
            $"Tutor disengaged {choice}; {_bash.Remaining} orbiting anchors remain active.",
            _bash.Remaining <= BashGame.MaximumTake + 1
                ? TutorDialoguePool.BashTerminalApproach
                : _bashRoundIndex == 1
                    ? TutorDialoguePool.BashRoundOneTutorActed
                    : TutorDialoguePool.BashRoundTwoTutorActed);
    }

    private void RenderBash(
        string log,
        string? dialoguePool = null,
        bool suppressTutorDialogue = false)
    {
        if (_bash is null)
        {
            return;
        }

        if (suppressTutorDialogue)
        {
            ClearCurrentTutorDialogue();
        }
        else
        {
            UpdateTutorDialogue(dialoguePool, TutorDialoguePool.BashState);
        }

        _hud.ShowBash(
            _bash,
            _bashRoundIndex,
            _stats,
            _currentGameTurns,
            _selectedChoice,
            inputOpen: !_inputLocked && _bash.CurrentTurn == Actor.Player,
            systemLog: log,
            tutorDialogueId: _currentTutorDialogueId,
            tutorDialogue: _currentTutorDialogue);
        _hud.ShowForcedChoiceTutorial(_forcedChoiceTutorialStage);
    }

    private void FinishBash(RoundOutcome outcome)
    {
        if (_bash is null)
        {
            return;
        }

        _inputLocked = false;
        _stats.RecordBash(outcome, _currentGameTurns);

        if (outcome == RoundOutcome.PlayerLose)
        {
            if (_bashRoundIndex == 1)
            {
                _bashRoundOneFailures++;
            }
            else
            {
                _bashRoundTwoFailures++;
            }
        }

        _lastSettledGame = GameKind.Bash;
        _lastOutcome = outcome;
        _lastGameIndex = _bashRoundIndex;
        _lastGameTurns = _currentGameTurns;
        _phase = DemoPhase.RoundResult;
        SetBashResultDialogue(outcome);
        WriteCheckpoint();
        _hud.ShowRoundResult(
            GameKind.Bash,
            outcome,
            _bashRoundIndex,
            _currentGameTurns,
            _stats,
            willContinue: true,
            tutorDialogueId: _currentTutorDialogueId,
            tutorDialogue: _currentTutorDialogue,
            choiceHistory: _bash.History);
    }
}
