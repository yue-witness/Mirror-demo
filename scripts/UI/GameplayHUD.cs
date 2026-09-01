using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Gameplay and result presentation only. Dialogue-only phases use the
/// dedicated TutorDialogueUI scene.
/// </summary>
public partial class GameplayHUD : Control
{
    private const float DefaultTutorCharactersPerSecond = 10.0f;
    public const float BashTutorExtractionSeconds = 0.72f;
    public const float LimitRevealPresentationSeconds = 1.15f;

    private Control _safeArea = null!;
    private Label _phaseBanner = null!;
    private Label _playTimeLabel = null!;
    private Label _leftTitle = null!;
    private RichTextLabel _leftDetails = null!;
    private Label _remainingLabel = null!;
    private Label _selectionLabel = null!;
    private StabilityLatticeView _latticeView = null!;
    private Control _resultOverlay = null!;
    private Label _resultLabel = null!;
    private SpriteAtlasAnimator _tutorPortrait = null!;
    private RichTextLabel _dialogueText = null!;
    private RichTextLabel _tutorCommitmentStatus = null!;
    private Label _completionCue = null!;
    private Label _systemStatus = null!;
    private RichTextLabel _systemLog = null!;
    private Button[] _choiceButtons = null!;
    private Button _confirmButton = null!;
    private Button _backButton = null!;
    private RichTextLabel _limitRevealResult = null!;
    private ForcedChoiceTutorialOverlay _forcedChoiceTutorial = null!;
    private Control _chapterOverlay = null!;
    private Label _chapterNumber = null!;
    private Label _chapterTitle = null!;
    private TutorSpeechPlayer _speechPlayer = null!;
    private UiAudioController _uiAudio = null!;
    private string _choiceVerb = "DISENGAGE";
    private string _currentTutorLineId = string.Empty;
    private string _currentTutorText = string.Empty;
    private double _tutorVisibleCharacterProgress;
    private float _tutorCharactersPerSecond;
    private bool _isTutorTyping;
    private bool _resultAwaitingSkip;
    private Tween? _resultTween;
    private Tween? _completionTween;
    private Tween? _limitRevealTween;
    private Vector2 _limitRevealRestingPosition;

    public float CurrentTutorSpeechDurationSeconds =>
        _speechPlayer.CurrentDurationSeconds;

    public event Action<int>? ChoiceSelected;

    public event Action? ConfirmRequested;

    public event Action? ResultAdvanceRequested;

    public event Action? BackToTitleRequested;

    public event Action? ChapterContinueRequested;

    public override void _Ready()
    {
        _safeArea = GetNode<Control>("SafeArea");
        _phaseBanner = GetNode<Label>("SafeArea/Layout/Header/HeaderRow/PhaseBanner");
        _playTimeLabel = GetNode<Label>(
            "SafeArea/Layout/Header/HeaderRow/PlayTimeLabel");
        _leftTitle = GetNode<Label>(
            "SafeArea/Layout/Content/LeftColumn/LeftStatus/LeftVBox/Title");
        _leftDetails = GetNode<RichTextLabel>(
            "SafeArea/Layout/Content/LeftColumn/LeftStatus/LeftVBox/Details");
        _remainingLabel = GetNode<Label>(
            "SafeArea/Layout/Content/Center/RemainingCard/RemainingVBox/StateRow/"
            + "ActiveStack/RemainingValue");
        _selectionLabel = GetNode<Label>(
            "SafeArea/Layout/Content/Center/RemainingCard/RemainingVBox/StateRow/"
            + "SelectionStack/SelectionLabel");
        _latticeView = GetNode<StabilityLatticeView>(
            "SafeArea/Layout/Content/Center/RemainingCard/RemainingVBox/StateRow/"
            + "LatticeView");
        _resultOverlay = GetNode<Control>("ResultOverlay");
        _resultLabel = GetNode<Label>("ResultOverlay/ResultLabel");
        _tutorPortrait = GetNode<SpriteAtlasAnimator>(
            "SafeArea/Layout/Content/LeftColumn/TutorCard/TutorVBox/"
            + "PortraitFrame/PortraitTexture");
        _dialogueText = GetNode<RichTextLabel>(
            "SafeArea/Layout/Content/Center/DialoguePanel/DialogueVBox/Text");
        _tutorCommitmentStatus = GetNode<RichTextLabel>(
            "SafeArea/Layout/Content/Center/DialoguePanel/DialogueVBox/Text/"
            + "TutorCommitmentStatus");
        _completionCue = GetNode<Label>(
            "SafeArea/Layout/Content/Center/DialoguePanel/DialogueVBox/Text/"
            + "CompletionCue");
        _systemStatus = GetNode<Label>(
            "SafeArea/Layout/Content/RightColumn/RightLog/RightVBox/Status");
        _systemLog = GetNode<RichTextLabel>(
            "SafeArea/Layout/Content/RightColumn/RightLog/RightVBox/Log");
        _confirmButton = GetNode<Button>(
            "SafeArea/Layout/Content/Center/ActionRow/ConfirmButton");
        _backButton = GetNode<Button>(
            "SafeArea/Layout/Content/RightColumn/BackButton");
        _limitRevealResult = GetNode<RichTextLabel>(
            "SafeArea/Layout/Content/Center/RemainingCard/RemainingVBox/"
            + "StateRow/LatticeView/LimitRevealResult");
        _limitRevealRestingPosition = _limitRevealResult.Position;
        _forcedChoiceTutorial = GetNode<ForcedChoiceTutorialOverlay>(
            "ForcedChoiceTutorial");
        _chapterOverlay = GetNode<Control>("ChapterOverlay");
        _chapterNumber = GetNode<Label>(
            "ChapterOverlay/ChapterGlass/ChapterVBox/ChapterNumber");
        _chapterTitle = GetNode<Label>(
            "ChapterOverlay/ChapterGlass/ChapterVBox/ChapterTitle");
        _speechPlayer = GetNode<TutorSpeechPlayer>("../TutorSpeechPlayer");
        _uiAudio = GetNode<UiAudioController>("../UiAudioController");
        _choiceButtons = new[]
        {
            GetNode<Button>(
                "SafeArea/Layout/Content/Center/ActionRow/ChoiceStack/ChoiceRow/Choice1"),
            GetNode<Button>(
                "SafeArea/Layout/Content/Center/ActionRow/ChoiceStack/ChoiceRow/Choice2"),
            GetNode<Button>(
                "SafeArea/Layout/Content/Center/ActionRow/ChoiceStack/ChoiceRow/Choice3")
        };

        for (int index = 0; index < _choiceButtons.Length; index++)
        {
            int choice = index + 1;
            _choiceButtons[index].Pressed += () => ChoiceSelected?.Invoke(choice);
        }

        _confirmButton.Pressed += () => ConfirmRequested?.Invoke();
        _backButton.Pressed += RequestBackToTitle;
        _forcedChoiceTutorial.FocusActionRequested +=
            HandleForcedTutorialAction;
        _forcedChoiceTutorial.SaveAndBackRequested += RequestBackToTitle;
    }

    public override void _Process(double delta)
    {
        if (!_isTutorTyping || !Visible || _chapterOverlay.Visible)
        {
            return;
        }

        _tutorVisibleCharacterProgress += _tutorCharactersPerSecond * delta;
        int totalCharacters = _dialogueText.GetTotalCharacterCount();
        int visibleCharacters = Math.Min(
            totalCharacters,
            (int)Math.Floor(_tutorVisibleCharacterProgress));
        _dialogueText.VisibleCharacters = visibleCharacters;

        if (visibleCharacters >= totalCharacters)
        {
            CompleteTutorTyping();
        }
    }

    public override void _Input(InputEvent @event)
    {
        if (@event is not InputEventMouseButton mouseButton
            || mouseButton.ButtonIndex != MouseButton.Left
            || !mouseButton.Pressed)
        {
            return;
        }

        if (_chapterOverlay.Visible)
        {
            GetViewport().SetInputAsHandled();
            ChapterContinueRequested?.Invoke();
            return;
        }

        if (_resultAwaitingSkip
            && !_backButton.GetGlobalRect().HasPoint(mouseButton.GlobalPosition))
        {
            if (_isTutorTyping)
            {
                CompleteTutorTyping();
                GetViewport().SetInputAsHandled();
                return;
            }

            _resultAwaitingSkip = false;
            StopResultAnimation();
            _speechPlayer.StopDialogue();
            GetViewport().SetInputAsHandled();
            ResultAdvanceRequested?.Invoke();
        }
    }

    public void ShowChapter(string number, string title)
    {
        HideLimitPresentation();
        StopResultAnimation();
        StopTutorPresentation();
        _chapterNumber.Text = number;
        _chapterTitle.Text = title;
        _safeArea.Visible = false;
        _chapterOverlay.Visible = true;
    }

    public void HideChapter()
    {
        _chapterOverlay.Visible = false;
        _safeArea.Visible = true;
    }

    public void SetElapsedPlayTime(long elapsedMilliseconds)
    {
        TimeSpan elapsed = TimeSpan.FromMilliseconds(
            Math.Max(0, elapsedMilliseconds));
        _playTimeLabel.Text = elapsed.TotalHours >= 1
            ? $"PLAY TIME · {(int)elapsed.TotalHours:00}:{elapsed.Minutes:00}:{elapsed.Seconds:00}"
            : $"PLAY TIME · {elapsed.Minutes:00}:{elapsed.Seconds:00}";
    }

    public void ShowBash(
        BashGame game,
        int bashRoundIndex,
        SessionStats stats,
        int turns,
        int? selectedChoice,
        bool inputOpen,
        string systemLog,
        string tutorDialogueId,
        string tutorDialogue)
    {
        HideLimitPresentation();
        StopResultAnimation();
        _phaseBanner.Text = $"CHAPTER 1.1 / BASH ROUND {bashRoundIndex}";
        _systemStatus.Text = game.CurrentTurn == Actor.Player
            ? inputOpen
                ? "STATUS · PLAYER TURN · INPUT OPEN"
                : "STATUS · PLAYER TURN · LOCKED"
            : "STATUS · TUTOR TURN · ACTING";
        _leftTitle.Text = "ROUND STATUS";
        _leftDetails.Text =
            $"• Starting anchors: {game.InitialUnits}\n\n"
            + $"• Active anchors: {game.Remaining}\n\n"
            + $"• {(game.CurrentTurn == Actor.Player ? "Player's turn" : "Tutor's turn")}\n\n"
            + "• Disengaging the keystone loses synchronization";
        _remainingLabel.Text = game.Remaining.ToString("00");
        _selectionLabel.Text = selectedChoice.HasValue
            ? $"STAGED: DISENGAGE {selectedChoice.Value}"
            : "NO REQUEST STAGED";
        _latticeView.ShowState(
            game.InitialUnits,
            game.Remaining,
            selectedChoice,
            requestLocked: !inputOpen && selectedChoice.HasValue,
            limitMode: false);
        SetTutorDialogue(tutorDialogueId, tutorDialogue);
        _systemLog.Text = $"{systemLog}\n\nACTIONS THIS ROUND: {turns}";

        _choiceVerb = "DISENGAGE";
        int[] legal = Enumerable.Range(1, 3).Where(game.CanTake).ToArray();
        ConfigureChoices(legal, selectedChoice, locked: !inputOpen);
        _confirmButton.Visible = true;
        _confirmButton.Disabled = !inputOpen || !selectedChoice.HasValue;
        _confirmButton.Text = selectedChoice.HasValue
            ? $"CONFIRM\nDISENGAGE {selectedChoice.Value}"
            : "SELECT\nFIRST";
    }

    public void ShowBashTutorSelection(int choice)
    {
        _systemStatus.Text = "STATUS · TUTOR TARGET LOCKED";
        _selectionLabel.Text = $"TUTOR TARGET · DISENGAGE {choice}";
        _latticeView.ShowTutorSelection(choice);
    }

    public void BeginBashTutorExtraction()
    {
        _systemStatus.Text = "STATUS · TUTOR EXTRACTION IN PROGRESS";
        _latticeView.AnimateTutorRemoval(BashTutorExtractionSeconds);
    }

    public void ShowLimitBash(
        LimitBashGame game,
        int gameIndex,
        SessionStats stats,
        int? selectedChoice,
        bool inputOpen,
        bool waiting,
        string systemLog,
        string tutorDialogueId,
        string tutorDialogue,
        ChoicePair? pendingReveal = null)
    {
        HideLimitPresentation();
        StopResultAnimation();
        _phaseBanner.Text = "CHAPTER 1.2 / LIMIT BASH";
        _systemStatus.Text = waiting
            ? "STATUS · PLAYER LOCKED · WAITING TO REVEAL"
            : inputOpen
                ? "STATUS · WAITING FOR PLAYER"
                : "STATUS · REVEALING · INPUT LOCKED";
        _leftTitle.Text = "CURRENT STATUS";
        _leftDetails.Text =
            $"• Starting anchors: {game.InitialUnits}\n\n"
            + $"• Active anchors: {game.Remaining}\n\n"
            + $"• Player's last request: {FormatPrevious(game.PlayerPrevious)}\n\n"
            + $"• Tutor's last request: {FormatPrevious(game.TutorPrevious)}";
        _remainingLabel.Text = game.Remaining.ToString("00");
        _selectionLabel.Text = selectedChoice.HasValue
            ? waiting
                ? $"PLAYER REQUEST: {selectedChoice.Value} · LOCKED"
                : $"PLAYER REQUEST: {selectedChoice.Value}"
            : "REQUEST 1 / 2 / 3";
        _latticeView.ShowState(
            game.InitialUnits,
            game.Remaining,
            selectedChoice,
            requestLocked: waiting,
            limitMode: true);
        SetTutorDialogue(tutorDialogueId, tutorDialogue);
        _systemLog.Text =
            $"{systemLog}\n\n"
            + "[EXECUTION LOG]\n"
            + $"{FormatChoiceLog(game.ChoicePairs, pendingReveal)}\n\n"
            + $"TOTAL WINS: {stats.LimitBashPlayerWins} / 2\n"
            + $"CONSECUTIVE DRAWS: {stats.ConsecutiveLimitBashDraws} / 2";

        _choiceVerb = "REQUEST";
        ConfigureChoices(game.GetLegalPlayerActions(), selectedChoice, !inputOpen);
        _confirmButton.Visible = true;
        _confirmButton.Disabled = !inputOpen || !selectedChoice.HasValue;
        _confirmButton.Text = waiting
            ? "LOCKED\nWAITING"
            : selectedChoice.HasValue
                ? $"CONFIRM\nREQUEST {selectedChoice.Value}"
                : "SELECT\nFIRST";
    }

    public void ShowLimitTutorCommitted()
    {
        HideLimitRevealResult();
        _systemStatus.Text = "STATUS · BOTH REQUESTS LOCKED · REVEAL PENDING";
        _tutorCommitmentStatus.Text =
            "[center][color=#66f4ff]◆ TUTOR SELECTION COMPLETE ◆[/color]\n"
            + "[color=#9df9ff]COMMITMENT SEALED · VALUE HIDDEN[/color][/center]";
        _tutorCommitmentStatus.Visible = true;
    }

    public void ShowLimitReveal(
        LimitBashGame game,
        int gameIndex,
        SessionStats stats,
        int playerTake,
        int tutorTake,
        string tutorDialogueId,
        string tutorDialogue)
    {
        ShowLimitBash(
            game,
            gameIndex,
            stats,
            playerTake,
            inputOpen: false,
            waiting: false,
            systemLog: $"SIMULTANEOUS REVEAL: PLAYER {playerTake} / TUTOR {tutorTake}",
            tutorDialogueId: tutorDialogueId,
            tutorDialogue: tutorDialogue,
            pendingReveal: new ChoicePair(
                playerTake,
                tutorTake,
                game.Remaining,
                Math.Max(0, game.Remaining - playerTake - tutorTake)));
        HideTutorCommitmentStatus();
        _latticeView.ShowLimitReveal(
            playerTake,
            tutorTake,
            LimitRevealPresentationSeconds);
        _selectionLabel.Text = $"REVEAL · PLAYER {playerTake}  ·  TUTOR {tutorTake}";
        _systemStatus.Text = "STATUS · SIMULTANEOUS REVEAL IN PROGRESS";
        _confirmButton.Visible = true;
        _confirmButton.Disabled = true;
        _confirmButton.Text = "TUTOR\nACTING";
        ShowLimitRevealResult(playerTake, tutorTake);
    }

    public void ShowRoundResult(
        GameKind game,
        RoundOutcome outcome,
        int gameIndex,
        int rounds,
        SessionStats stats,
        bool willContinue,
        string tutorDialogueId,
        string tutorDialogue,
        ChoicePair? finalChoice = null,
        IReadOnlyList<ChoicePair>? choiceHistory = null)
    {
        HideLimitPresentation();
        HideForcedChoiceTutorial();
        string preservedSystemLog = _systemLog.Text.Trim();
        string gameName = game == GameKind.Bash ? "BASH" : "LIMIT BASH";
        string result = FormatOutcome(outcome);

        _phaseBanner.Text = $"{gameName} / GAME RESULT";
        _systemStatus.Text = willContinue
            ? "STATUS · RESULT ANIMATION · CLICK TO SKIP"
            : "STATUS · END CONDITION · CLICK TO SKIP";
        _leftTitle.Text = "GAME STATISTICS";
        _leftDetails.Text = game == GameKind.Bash
            ? $"• {FormatOutcomeDescription(outcome)}\n\n"
                + $"• Actions this round: {rounds}\n\n"
                + "• Terminal event: keystone disengaged"
            : $"• {FormatOutcomeDescription(outcome)}\n\n"
                + $"• Total wins: {stats.LimitBashPlayerWins} / 2\n\n"
                + $"• Consecutive draws: {stats.ConsecutiveLimitBashDraws} / 2\n\n"
                + $"• Reveals this game: {rounds}"
                + (finalChoice.HasValue
                    ? $"\n\n• Final requests: Player {finalChoice.Value.PlayerTake}"
                        + $" / Tutor {finalChoice.Value.TutorTake}"
                    : string.Empty);
        _remainingLabel.Text = string.Empty;
        _latticeView.ShowResult(outcome);
        _selectionLabel.Text = finalChoice.HasValue
            ? $"FINAL REQUESTS · PLAYER {finalChoice.Value.PlayerTake}"
                + $" / TUTOR {finalChoice.Value.TutorTake}"
            : $"{gameName} · {result}";
        SetTutorDialogue(tutorDialogueId, tutorDialogue);
        string resultMessage = willContinue
            ? "The end condition has not been reached. Click anywhere to continue."
            : "Click anywhere to view the final summary.";
        _systemLog.Text = game == GameKind.LimitBash
            ? $"{resultMessage}\n\n[EXECUTION LOG]\n"
                + FormatChoiceLog(choiceHistory ?? Array.Empty<ChoicePair>())
            : string.IsNullOrWhiteSpace(preservedSystemLog)
                ? resultMessage
                : $"{preservedSystemLog}\n\n{resultMessage}";

        ConfigureChoices(Array.Empty<int>(), null, locked: true);
        _confirmButton.Visible = false;
        StartResultAnimation(outcome);
    }

    /// <summary>
    /// Locks the normal action row and delegates the one permitted click to the
    /// full-screen tutorial overlay. SAVE &amp; BACK remains available there.
    /// </summary>
    public void ShowForcedChoiceTutorial(ForcedChoiceTutorialStage stage)
    {
        if (stage == ForcedChoiceTutorialStage.Hidden)
        {
            HideForcedChoiceTutorial();
            return;
        }

        foreach (Button button in _choiceButtons)
        {
            // The transparent tutorial layer owns input. Keep the buttons'
            // normal visual state intact so the rest of the HUD is not dimmed.
            button.MouseFilter = MouseFilterEnum.Ignore;
            button.FocusMode = FocusModeEnum.None;
        }

        _confirmButton.MouseFilter = MouseFilterEnum.Ignore;
        _confirmButton.FocusMode = FocusModeEnum.None;

        _forcedChoiceTutorial.ShowStage(stage);
    }

    public void HideForcedChoiceTutorial()
    {
        if (GodotObject.IsInstanceValid(_forcedChoiceTutorial))
        {
            _forcedChoiceTutorial.HideStage();
        }

        // The tutorial overlay temporarily owns pointer input while it frames
        // the required action. Restore normal hit testing when that gate ends;
        // otherwise Confirm remains visually enabled but ignores every click
        // for the rest of the session.
        foreach (Button button in _choiceButtons)
        {
            button.MouseFilter = button.Disabled
                ? MouseFilterEnum.Ignore
                : MouseFilterEnum.Stop;
            button.FocusMode = button.Disabled
                ? FocusModeEnum.None
                : FocusModeEnum.All;
        }

        _confirmButton.MouseFilter = _confirmButton.Disabled
            ? MouseFilterEnum.Ignore
            : MouseFilterEnum.Stop;
        _confirmButton.FocusMode = _confirmButton.Disabled
            ? FocusModeEnum.None
            : FocusModeEnum.All;
    }

    private void ConfigureChoices(
        IEnumerable<int> legalChoices,
        int? selectedChoice,
        bool locked)
    {
        HashSet<int> legal = legalChoices.ToHashSet();

        for (int index = 0; index < _choiceButtons.Length; index++)
        {
            int choice = index + 1;
            bool selected = selectedChoice == choice;
            Button button = _choiceButtons[index];
            button.Visible = legal.Count > 0;
            button.ButtonPressed = selected;
            button.Disabled = locked || !legal.Contains(choice);
            button.MouseFilter = locked
                ? MouseFilterEnum.Ignore
                : MouseFilterEnum.Stop;
            button.FocusMode = locked
                ? FocusModeEnum.None
                : FocusModeEnum.All;
            button.Scale = Vector2.One;
            button.ZIndex = 0;
            string optionLetter = ((char)('A' + index)).ToString();
            button.Text = selected
                ? $"{optionLetter}\nSTAGED"
                : $"{optionLetter}\n{_choiceVerb} {choice}";
        }
    }

    private void StartResultAnimation(RoundOutcome outcome)
    {
        StopResultAnimation();
        _resultAwaitingSkip = true;
        _uiAudio.PlayResult(outcome);
        _resultOverlay.Visible = true;
        _resultLabel.Text = FormatOutcome(outcome);
        _resultLabel.AddThemeColorOverride(
            "font_color",
            outcome switch
            {
                RoundOutcome.PlayerWin => new Color("ffd21f"),
                RoundOutcome.PlayerLose => new Color("ff0038"),
                _ => new Color("ffc21f")
            });
        _resultLabel.PivotOffset = _resultLabel.Size / 2.0f;
        _resultLabel.Scale = new Vector2(0.82f, 0.82f);

        _resultTween = CreateTween().SetLoops();
        _resultTween.TweenProperty(
                _resultLabel,
                "scale",
                new Vector2(1.06f, 1.06f),
                0.55)
            .SetTrans(Tween.TransitionType.Back)
            .SetEase(Tween.EaseType.Out);
        _resultTween.TweenProperty(
                _resultLabel,
                "scale",
                new Vector2(0.94f, 0.94f),
                0.55)
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
            $"[center][color=#ffcb55]PLAYER −{playerTake} ANCHORS[/color]\n"
            + $"[color=#66f4ff]TUTOR −{tutorTake} ANCHORS[/color][/center]";
        _limitRevealResult.Position = _limitRevealRestingPosition;
        _limitRevealResult.Modulate = Colors.White;
        _limitRevealResult.Visible = true;

        _limitRevealTween = CreateTween();
        _limitRevealTween.TweenProperty(
                _limitRevealResult,
                "position:y",
                _limitRevealRestingPosition.Y - 28.0f,
                LimitRevealPresentationSeconds)
            .SetTrans(Tween.TransitionType.Cubic)
            .SetEase(Tween.EaseType.Out);
        _limitRevealTween.Parallel().TweenProperty(
                _limitRevealResult,
                "modulate:a",
                0.0f,
                LimitRevealPresentationSeconds * 0.42f)
            .SetDelay(LimitRevealPresentationSeconds * 0.58f)
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
            : "NO ROUNDS REVEALED YET.";
    }

    private static string FormatChoiceEntry(
        ChoicePair pair,
        int roundIndex,
        bool revealing = false)
    {
        string state = revealing ? "  [REVEALING]" : string.Empty;
        return $"R{roundIndex:00}  PLAYER {pair.PlayerTake} / TUTOR {pair.TutorTake}"
            + $"  {pair.RemainingBefore} → {pair.RemainingAfter}{state}";
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
