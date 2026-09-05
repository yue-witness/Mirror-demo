using Godot;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

// LimitBash responsibilities of the single demo flow controller.
public partial class DemoFlowController
{
    private void StartLimitBashGame(bool preserveDirective = false)
    {
        _flowVersion++;
        _phase = DemoPhase.LimitBash;
        _bash = null;
        _limitGameIndex = _stats.LimitBashGamesCompleted + 1;
        _currentGameTurns = 0;
        _selectedChoice = null;
        _inputLocked = false;
        _activeBriefingLineId = string.Empty;
        _pendingGameStart = PendingGameStart.None;
        ShowPrimaryScreen(_hud);

        if (!preserveDirective)
        {
            _limitDirective = OutcomeDirector.GetDirective(
                _stats.LimitBashGamesCompleted,
                _sessionRandom.NextSingle());
            _controlledRestarts = 0;
        }
        else if (_pendingLimitDirective.HasValue)
        {
            _limitDirective = _pendingLimitDirective.Value;
        }

        _pendingLimitDirective = null;

        int initialUnits = _sessionRandom.Next(
            _rules.LimitBash.MinimumInitialUnits,
            _rules.LimitBash.MaximumInitialUnits + 1);
        _limitBash = new LimitBashGame();
        _limitBash.Start(initialUnits);

        if (!_outcomeDirector.CanGuaranteeDirective(_limitBash, _limitDirective))
        {
            RestartControlledLimitGame(
                "The initial state cannot reach an allowed outcome.");
            return;
        }

        ResetTurnDialogueState();
        WriteCheckpoint();
        RenderLimitBash(
            waiting: false,
            log: "A fresh simultaneous-request lattice is active in the chamber.",
            dialoguePool: TutorDialoguePool.LimitState);
    }

    private async void ConfirmLimitBashChoice(int choice)
    {
        if (_limitBash is null
            || !_limitBash.GetLegalPlayerActions().Contains(choice))
        {
            return;
        }

        _inputLocked = true;
        _selectedChoice = choice;
        _limitBash.LockPlayerChoice(choice);
        RenderLimitBash(
            waiting: true,
            log: "Player request locked. Waiting for the simultaneous reveal.",
            suppressTutorDialogue: true);

        int expectedVersion = _flowVersion;

        // Hold the visible lock state before resolving the Tutor's actual
        // response. The bounded solver is deliberately run only after this
        // presentation pause, so the player can read the hand-off and the
        // subsequent reveal always follows the same deterministic sequence.
        await ToSignal(
            GetTree().CreateTimer(
                LimitTutorCommitmentPauseSeconds),
            SceneTreeTimer.SignalName.Timeout);

        if (expectedVersion != _flowVersion
            || _limitBash is null
            || !_limitBash.PlayerChoiceLocked)
        {
            return;
        }

        int tutorChoice;
        try
        {
            tutorChoice = _outcomeDirector.ChooseAfterPlayerLock(
                _limitBash,
                _limitDirective);
        }
        catch (ControlledOutcomeUnavailableException)
        {
            RestartControlledLimitGame(
                "No legal action can preserve this game's allowed outcomes.");
            return;
        }

        RevealLimitRound(expectedVersion, choice, tutorChoice);
    }

    private async void RevealLimitRound(
        int expectedVersion,
        int playerChoice,
        int tutorChoice)
    {
        await ToSignal(
            GetTree().CreateTimer(
                LimitTutorCommitmentPauseSeconds),
            SceneTreeTimer.SignalName.Timeout);

        if (expectedVersion != _flowVersion || _limitBash is null)
        {
            return;
        }

        SetTutorDialogueById("limit_reveal_tutor_choice");
        _hud.ShowLimitTutorChoiceRevealed(
            _limitBash,
            _limitGameIndex,
            _stats,
            playerChoice,
            tutorChoice,
            _currentTutorDialogueId,
            _currentTutorDialogue);

        await ToSignal(
            GetTree().CreateTimer(
                Math.Max(
                    LimitTutorRevealExplanationSeconds,
                    _hud.CurrentTutorSpeechDurationSeconds)),
            SceneTreeTimer.SignalName.Timeout);

        if (expectedVersion != _flowVersion
            || _limitBash is null
            || !_limitBash.PlayerChoiceLocked)
        {
            return;
        }

        _hud.BeginLimitExtraction(playerChoice, tutorChoice);

        await ToSignal(
            GetTree().CreateTimer(
                GameplayHUD.LimitRevealPresentationSeconds),
            SceneTreeTimer.SignalName.Timeout);

        if (expectedVersion != _flowVersion
            || _limitBash is null
            || !_limitBash.PlayerChoiceLocked)
        {
            return;
        }

        _hud.ShowLimitRevealNumbers(playerChoice, tutorChoice);

        await ToSignal(
            GetTree().CreateTimer(
                GameplayHUD.LimitRevealNumberHoldSeconds),
            SceneTreeTimer.SignalName.Timeout);

        if (expectedVersion != _flowVersion
            || _limitBash is null
            || !_limitBash.PlayerChoiceLocked)
        {
            return;
        }

        RoundOutcome outcome = _limitBash.CommitTutorChoice(tutorChoice);
        _currentGameTurns++;
        _selectedChoice = null;

        if (outcome != RoundOutcome.Continue)
        {
            FinishLimitBash(outcome);
            return;
        }

        _inputLocked = false;
        ResetTurnDialogueState();
        WriteCheckpoint();
        RenderLimitBash(
            waiting: false,
            log: $"REVEAL: PLAYER {playerChoice} / TUTOR {tutorChoice}; "
                + $"{_limitBash.Remaining} orbiting anchors remain active.",
            dialoguePool: _limitBash.Remaining <= 6
                ? TutorDialoguePool.LimitTerminalApproach
                : TutorDialoguePool.LimitReveal);
    }

    private void RenderLimitBash(
        bool waiting,
        string log,
        string? dialoguePool = null,
        bool suppressTutorDialogue = false)
    {
        if (_limitBash is null)
        {
            return;
        }

        if (suppressTutorDialogue)
        {
            ClearCurrentTutorDialogue();
        }
        else
        {
            UpdateTutorDialogue(dialoguePool, TutorDialoguePool.LimitState);
        }

        _hud.ShowLimitBash(
            _limitBash,
            _limitGameIndex,
            _stats,
            _selectedChoice,
            inputOpen: !_inputLocked,
            waiting: waiting,
            systemLog: log,
            tutorDialogueId: _currentTutorDialogueId,
            tutorDialogue: _currentTutorDialogue);
    }

    private void FinishLimitBash(RoundOutcome outcome)
    {
        if (_limitBash is null)
        {
            return;
        }

        _inputLocked = false;
        _stats.RecordLimitBash(outcome, _currentGameTurns);
        _lastSettledGame = GameKind.LimitBash;
        _lastOutcome = outcome;
        _lastGameIndex = _limitGameIndex;
        _lastGameTurns = _currentGameTurns;
        _phase = DemoPhase.RoundResult;
        SetLimitResultDialogue(outcome);
        WriteCheckpoint();
        _hud.ShowRoundResult(
            GameKind.LimitBash,
            outcome,
            _limitGameIndex,
            _currentGameTurns,
            _stats,
            willContinue: !_stats.IsLimitBashComplete,
            tutorDialogueId: _currentTutorDialogueId,
            tutorDialogue: _currentTutorDialogue,
            finalChoice: _limitBash.ChoicePairs.Count > 0
                ? _limitBash.ChoicePairs[^1]
                : null,
            choiceHistory: _limitBash.ChoicePairs);
    }

    private void RestartControlledLimitGame(string reason)
    {
        _controlledRestarts++;

        if (_controlledRestarts > MaximumControlledRestarts)
        {
            GD.PushError(reason);
            ShowTitleScreen(
                "The Limit Bash state could not be safely restored. "
                    + "The current game was not counted.");
            return;
        }

        GD.PushWarning($"Limit Bash controlled restart {_controlledRestarts}: {reason}");
        EnterBriefing(
            DemoPhase.LimitRestartBriefing,
            TutorDialoguePool.LimitRestart,
            PendingGameStart.LimitBashPreservingDirective,
            _limitDirective);
    }
}
