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
    [Export(PropertyHint.MultilineText)]
    public string BashRules { get; set; } = "";

    [Export(PropertyHint.MultilineText)]
    public string LimitRules { get; set; } = "";

    private static string FormatBashHistory(IReadOnlyList<ChoicePair> history)
    {
        return history.Count == 0
            ? "Your choices will appear here after each turn."
            : string.Join("\n\n", history.Select((entry, index) =>
                $"[b]{index + 1:00}  {(entry.PlayerTake > 0 ? "You" : "Tutor")} −{Math.Max(entry.PlayerTake, entry.TutorTake)}[/b]\n"
                + $"Anchors {entry.RemainingBefore} → {entry.RemainingAfter}"));
    }

    private const float DefaultTutorCharactersPerSecond = 10.0f;
    public const float BashPlayerExtractionSeconds = 0.62f;
    public const float BashTutorExtractionSeconds = 0.72f;
    public const float LimitRevealPresentationSeconds = 1.15f;
    [Export(PropertyHint.Range, "1,6,0.1")]
    public float RevealNumberHoldSeconds { get; set; } = 3.0f;

    [Export(PropertyHint.MultilineText)]
    public string TutorSealedText { get; set; } = "";

    [Export(PropertyHint.MultilineText)]
    public string BothSealedText { get; set; } = "";

    private Control _safeArea = null!;
    private Label _phaseBanner = null!;
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
    private RichTextLabel _choiceHistory = null!;
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
    private string _pendingTutorLineId = string.Empty;
    private string _pendingTutorText = string.Empty;
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
            "SafeArea/Layout/Content/Center/DialoguePanel/DialogueVBox/"
            + "TutorCommitmentStatus");
        _completionCue = GetNode<Label>(
            "SafeArea/Layout/Content/Center/DialoguePanel/DialogueVBox/Text/"
            + "CompletionCue");
        _choiceHistory = GetNode<RichTextLabel>(
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
        VisibilityChanged += () =>
        {
            if (!Visible)
            {
                StopTutorPresentation();
            }
        };
    }

    public override void _Process(double delta)
    {
        // Keep the latest context-sensitive feedback until the current voice
        // finishes. A screen change or essential line clears this pending cue.
        if (Visible && !_chapterOverlay.Visible
            && !string.IsNullOrEmpty(_pendingTutorLineId)
            && _speechPlayer.CanPresentDialogue(_pendingTutorLineId))
        {
            SetTutorDialogue(_pendingTutorLineId, _pendingTutorText);
        }

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
        _phaseBanner.Text = game.CurrentTurn == Actor.Player
            ? "BASH · YOUR TURN"
            : "BASH · TUTOR'S TURN";
        _leftTitle.Text = "HOW TO PLAY";
        _leftDetails.Text = BashRules;
        _remainingLabel.Text = game.Remaining.ToString("00");
        _selectionLabel.Text = selectedChoice.HasValue
            ? $"Selected: {selectedChoice.Value}"
            : "Choose 1, 2 or 3";
        _latticeView.ShowState(
            game.InitialUnits,
            game.Remaining,
            selectedChoice,
            requestLocked: !inputOpen && selectedChoice.HasValue,
            limitMode: false);
        SetTutorDialogue(tutorDialogueId, tutorDialogue);
        _choiceHistory.Text = FormatBashHistory(game.History);

        _choiceVerb = "DISENGAGE";
        int[] legal = Enumerable.Range(1, 3).Where(game.CanTake).ToArray();
        ConfigureChoices(legal, selectedChoice, locked: !inputOpen);
        ConfigureConfirmButton(
            visible: true,
            enabled: inputOpen && selectedChoice.HasValue,
            text: selectedChoice.HasValue
                ? $"CONFIRM\nTAKE {selectedChoice.Value}"
                : "SELECT\nFIRST");
    }

    public void ShowBashTutorSelection(int choice)
    {
        _selectionLabel.Text = $"Tutor chose {choice}";
        _latticeView.ShowTutorSelection(choice);
    }

    public void BeginBashPlayerExtraction(int choice)
    {
        _selectionLabel.Text = $"Taking {choice}…";
        _latticeView.AnimatePlayerRemoval(choice, BashPlayerExtractionSeconds);
        _uiAudio.PlayExtraction(BashPlayerExtractionSeconds);
    }

    public void BeginBashTutorExtraction()
    {
        _latticeView.AnimateTutorRemoval(BashTutorExtractionSeconds);
        _uiAudio.PlayExtraction(BashTutorExtractionSeconds);
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
        _phaseBanner.Text = "LIMIT BASH · CHOOSE TOGETHER";
        _leftTitle.Text = "HOW TO PLAY";
        _leftDetails.Text = LimitRules;
        _remainingLabel.Text = game.Remaining.ToString("00");
        _selectionLabel.Text = selectedChoice.HasValue
            ? waiting
                ? $"PLAYER REQUEST: {selectedChoice.Value} · LOCKED"
                : $"PLAYER REQUEST: {selectedChoice.Value}"
            : "Choose 1, 2 or 3";
        _latticeView.ShowState(
            game.InitialUnits,
            game.Remaining,
            selectedChoice,
            requestLocked: waiting,
            limitMode: true);
        SetTutorDialogue(tutorDialogueId, tutorDialogue);
        _choiceHistory.Text = FormatChoiceLog(game.ChoicePairs, pendingReveal);

        _choiceVerb = "REQUEST";
        ConfigureChoices(game.GetLegalPlayerActions(), selectedChoice, !inputOpen);
        ConfigureConfirmButton(
            visible: true,
            enabled: inputOpen && selectedChoice.HasValue,
            text: waiting
                ? "LOCKED\nWAITING"
                : selectedChoice.HasValue
                    ? $"CONFIRM\nREQUEST {selectedChoice.Value}"
                    : "SELECT\nFIRST");

        _tutorCommitmentStatus.Text = waiting ? BothSealedText : TutorSealedText;
        _tutorCommitmentStatus.Visible = true;
    }

    public void ShowLimitTutorCommitted()
    {
        HideLimitRevealResult();
        _tutorCommitmentStatus.Text = BothSealedText;
        _tutorCommitmentStatus.Visible = true;
    }

    public void ShowLimitTutorChoiceRevealed(
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
            systemLog: $"TUTOR REQUEST REVEALED: {tutorTake} · PLAYER REQUEST STILL LOCKED",
            tutorDialogueId: tutorDialogueId,
            tutorDialogue: tutorDialogue,
            pendingReveal: new ChoicePair(
                playerTake,
                tutorTake,
                game.Remaining,
                Math.Max(0, game.Remaining - playerTake - tutorTake)));
        HideTutorCommitmentStatus();
        _latticeView.ShowLimitTutorSelection(tutorTake);
        _selectionLabel.Text = $"TUTOR REVEALED · REQUEST {tutorTake}";
        ConfigureConfirmButton(
            visible: true,
            enabled: false,
            text: "REVEAL\nPAUSED");
    }

    public void BeginLimitExtraction(int playerTake, int tutorTake)
    {
        HideTutorCommitmentStatus();
        HideLimitRevealResult();
        _latticeView.ShowLimitReveal(
            playerTake,
            tutorTake,
            LimitRevealPresentationSeconds);
        _uiAudio.PlayExtraction(LimitRevealPresentationSeconds);
        _selectionLabel.Text = $"EXECUTING · PLAYER {playerTake}  ·  TUTOR {tutorTake}";
        ConfigureConfirmButton(
            visible: true,
            enabled: false,
            text: "BOTH\nACTING");
    }

    public void ShowLimitRevealNumbers(int playerTake, int tutorTake)
    {
        _selectionLabel.Text = $"REVEAL · PLAYER {playerTake}  ·  TUTOR {tutorTake}";
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
        string preservedHistory = _choiceHistory.Text.Trim();
        string gameName = game == GameKind.Bash ? "BASH" : "LIMIT BASH";
        string result = FormatOutcome(outcome);

        _phaseBanner.Text = $"{gameName} / GAME RESULT";
        _leftTitle.Text = "RESULT";
        _leftDetails.Text = $"{FormatOutcomeDescription(outcome)}\n\nClick to continue.";
        _remainingLabel.Text = string.Empty;
        _latticeView.ShowResult(outcome);
        _selectionLabel.Text = finalChoice.HasValue
            ? $"FINAL REQUESTS · PLAYER {finalChoice.Value.PlayerTake}"
                + $" / TUTOR {finalChoice.Value.TutorTake}"
            : $"{gameName} · {result}";
        SetTutorDialogue(tutorDialogueId, tutorDialogue);
        _choiceHistory.Text = choiceHistory is not null
            ? game == GameKind.LimitBash
                ? FormatChoiceLog(choiceHistory)
                : FormatBashHistory(choiceHistory)
            : preservedHistory;

        ConfigureChoices(Array.Empty<int>(), null, locked: true);
        ConfigureConfirmButton(
            visible: false,
            enabled: false,
            text: string.Empty);
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

    /// <summary>
    /// Keeps the Confirm button's visual state and actual input state in sync.
    /// Godot does not restore MouseFilter or FocusMode when Disabled changes,
    /// so every screen render must configure all of them together.
    /// </summary>
    private void ConfigureConfirmButton(
        bool visible,
        bool enabled,
        string text)
    {
        _confirmButton.Visible = visible;
        _confirmButton.Disabled = !enabled;
        _confirmButton.MouseFilter = visible && enabled
            ? MouseFilterEnum.Stop
            : MouseFilterEnum.Ignore;
        _confirmButton.FocusMode = visible && enabled
            ? FocusModeEnum.All
            : FocusModeEnum.None;
        _confirmButton.Text = text;
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
                ? $"{optionLetter}\nSELECTED {choice}"
                : $"{optionLetter}\n{_choiceVerb} {choice}";
        }
    }

}
