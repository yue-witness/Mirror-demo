using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

// Subtitle, speech, result and reveal presentation for the same HUD scene.
public partial class GameplayHUD
{
    private void StartResultAnimation(RoundOutcome outcome)
    {
        StopResultAnimation();
        _resultAwaitingSkip = true;
        _uiAudio.PlayResult(outcome);
        _resultOverlay.Visible = true;
        _resultLabel.Text = FormatOutcome(outcome);
        _resultLabel.PivotOffset = _resultLabel.Size / 2.0f;
        _resultLabel.Scale = new Vector2(0.82f, 0.82f);

        _resultTween = CreateTween().SetLoops();
        _resultTween.TweenProperty(
                _resultLabel,
                "scale",
                new Vector2(1.01f, 1.01f),
                1.2)
            .SetTrans(Tween.TransitionType.Back)
            .SetEase(Tween.EaseType.Out);
        _resultTween.TweenProperty(
                _resultLabel,
                "scale",
                new Vector2(0.99f, 0.99f),
                1.2)
            .SetTrans(Tween.TransitionType.Sine)
            .SetEase(Tween.EaseType.InOut);

        if (!_isTutorTyping)
        {
            ShowCompletionCue();
        }
    }

    private void StopResultAnimation()
    {
        if (_resultTween is not null
            && GodotObject.IsInstanceValid(_resultTween))
        {
            _resultTween.Kill();
        }

        _resultTween = null;
        _resultAwaitingSkip = false;
        HideCompletionCue();
        if (GodotObject.IsInstanceValid(_remainingLabel))
        {
            _resultOverlay.Visible = false;
            _resultLabel.Scale = Vector2.One;
            _resultLabel.RemoveThemeColorOverride("font_color");
        }
    }

    private void SetTutorDialogue(string lineId, string text)
    {
        if (string.IsNullOrWhiteSpace(lineId)
            || string.IsNullOrWhiteSpace(text))
        {
            StopTutorPresentation();
            _dialogueText.Text = string.Empty;
            _dialogueText.VisibleCharacters = -1;
            _tutorPortrait.SetState(0);
            HideCompletionCue();
            return;
        }

        if (lineId == _currentTutorLineId && text == _currentTutorText)
        {
            _tutorPortrait.SetState((int)TutorPresentationPolicy.ResolveEmotion(
                lineId,
                text));
            return;
        }

        if (!_speechPlayer.CanPresentDialogue(lineId))
        {
            _pendingTutorLineId = lineId;
            _pendingTutorText = text;
            return;
        }

        _pendingTutorLineId = string.Empty;
        _pendingTutorText = string.Empty;
        _currentTutorLineId = lineId;
        _currentTutorText = text;
        _dialogueText.Text = $"[center]{text.Replace("[", "[lb]")}[/center]";
        _tutorPortrait.SetState((int)TutorPresentationPolicy.ResolveEmotion(
            lineId,
            text));
        HideCompletionCue();
        _dialogueText.VisibleCharacters = 0;
        _tutorVisibleCharacterProgress = 0.0;

        float duration = _speechPlayer.PlayDialogue(lineId, "TUTOR", text);
        int totalCharacters = _dialogueText.GetTotalCharacterCount();
        _tutorCharactersPerSecond = duration > 0.0f
            ? totalCharacters / duration
            : DefaultTutorCharactersPerSecond;
        _isTutorTyping = totalCharacters > 0;

        if (!_isTutorTyping)
        {
            CompleteTutorTyping();
        }
    }

    private void CompleteTutorTyping()
    {
        _isTutorTyping = false;
        _dialogueText.VisibleCharacters = -1;

        if (_resultAwaitingSkip)
        {
            ShowCompletionCue();
        }
    }

    private void StopTutorPresentation()
    {
        _speechPlayer.StopDialogue();
        _pendingTutorLineId = string.Empty;
        _pendingTutorText = string.Empty;
        _currentTutorLineId = string.Empty;
        _currentTutorText = string.Empty;
        _isTutorTyping = false;
        HideCompletionCue();
    }

    private void RequestBackToTitle()
    {
        HideLimitPresentation();
        StopTutorPresentation();
        BackToTitleRequested?.Invoke();
    }

    private void ShowLimitRevealResult(int playerTake, int tutorTake)
    {
        HideLimitRevealResult();
        _limitRevealResult.Text =
            $"[center][color=#{_latticeView.PlayerPreview.ToHtml()}]PLAYER −{playerTake} ANCHORS[/color]\n"
            + $"[color=#{_latticeView.TutorPreview.ToHtml()}]TUTOR −{tutorTake} ANCHORS[/color][/center]";
        _limitRevealResult.Position = _limitRevealRestingPosition;
        _limitRevealResult.Modulate = Colors.White;
        _limitRevealResult.Visible = true;

        _limitRevealTween = CreateTween();
        _limitRevealTween.TweenProperty(
                _limitRevealResult,
                "position:y",
                _limitRevealRestingPosition.Y - 42.0f,
                RevealNumberHoldSeconds)
            .SetTrans(Tween.TransitionType.Cubic)
            .SetEase(Tween.EaseType.Out);
        _limitRevealTween.Parallel().TweenProperty(
                _limitRevealResult,
                "modulate:a",
                0.0f,
                RevealNumberHoldSeconds * 0.30f)
            .SetDelay(RevealNumberHoldSeconds * 0.70f)
            .SetTrans(Tween.TransitionType.Sine)
            .SetEase(Tween.EaseType.In);
    }

    private void HideLimitPresentation()
    {
        HideTutorCommitmentStatus();
        HideLimitRevealResult();
    }

    private void HideTutorCommitmentStatus()
    {
        if (GodotObject.IsInstanceValid(_tutorCommitmentStatus))
        {
            _tutorCommitmentStatus.Visible = false;
        }
    }

    private void HideLimitRevealResult()
    {
        if (_limitRevealTween is not null
            && GodotObject.IsInstanceValid(_limitRevealTween))
        {
            _limitRevealTween.Kill();
        }

        _limitRevealTween = null;
        if (GodotObject.IsInstanceValid(_limitRevealResult))
        {
            _limitRevealResult.Visible = false;
            _limitRevealResult.Position = _limitRevealRestingPosition;
            _limitRevealResult.Modulate = Colors.White;
        }
    }

    private void HandleForcedTutorialAction()
    {
        if (_forcedChoiceTutorial.Stage
            == ForcedChoiceTutorialStage.SelectB)
        {
            ChoiceSelected?.Invoke(2);
            return;
        }

        if (_forcedChoiceTutorial.Stage
            == ForcedChoiceTutorialStage.Confirm)
        {
            ConfirmRequested?.Invoke();
        }
    }

    private void ShowCompletionCue()
    {
        HideCompletionCue();
        _completionCue.Visible = true;
        float restingY = _completionCue.Position.Y;
        _completionTween = CreateTween().SetLoops();
        _completionTween.TweenProperty(
                _completionCue,
                "position:y",
                restingY + 7.0f,
                0.34)
            .SetTrans(Tween.TransitionType.Sine)
            .SetEase(Tween.EaseType.InOut);
        _completionTween.TweenProperty(
                _completionCue,
                "position:y",
                restingY,
                0.34)
            .SetTrans(Tween.TransitionType.Sine)
            .SetEase(Tween.EaseType.InOut);
    }

    private void HideCompletionCue()
    {
        if (_completionTween is not null
            && GodotObject.IsInstanceValid(_completionTween))
        {
            _completionTween.Kill();
        }

        _completionTween = null;
        if (GodotObject.IsInstanceValid(_completionCue))
        {
            _completionCue.Visible = false;
        }
    }

    private static string FormatPrevious(int? previous)
    {
        return previous?.ToString() ?? "NONE";
    }

    private static string FormatChoiceLog(
        IReadOnlyList<ChoicePair> choicePairs,
        ChoicePair? pendingReveal = null)
    {
        var entries = choicePairs
            .Select((pair, index) => FormatChoiceEntry(pair, index + 1))
            .ToList();

        if (pendingReveal.HasValue)
        {
            entries.Add(FormatChoiceEntry(
                pendingReveal.Value,
                choicePairs.Count + 1,
                revealing: true));
        }

        return entries.Count > 0
            ? string.Join("\n", entries)
            : "Your choices will appear here after each reveal.";
    }

    private static string FormatChoiceEntry(
        ChoicePair pair,
        int roundIndex,
        bool revealing = false)
    {
        string state = revealing ? "  [REVEALING]" : string.Empty;
        return $"[b]R{roundIndex:00}[/b]   You {pair.PlayerTake} · Tutor {pair.TutorTake}\n"
            + $"Anchors {pair.RemainingBefore} → {pair.RemainingAfter}{state}\n";
    }

    private static string FormatOutcome(RoundOutcome outcome)
    {
        return outcome switch
        {
            RoundOutcome.PlayerWin => "PLAYER WIN",
            RoundOutcome.PlayerLose => "PLAYER LOSE",
            RoundOutcome.Draw => "DRAW",
            _ => "IN PROGRESS"
        };
    }

    private static string FormatOutcomeDescription(RoundOutcome outcome)
    {
        return outcome switch
        {
            RoundOutcome.PlayerWin => "Tutor synchronization lost",
            RoundOutcome.PlayerLose => "Player synchronization lost",
            RoundOutcome.Draw => "Lattice balanced",
            _ => "In progress"
        };
    }
}
