using Godot;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

/// <summary>
/// The only owner of demo phase transitions. Rule objects settle games, UI
/// objects emit intent, and the save service only reads or writes snapshots.
/// </summary>
public partial class DemoFlowController : Control
{
    private const double TutorDelaySeconds = 0.45;
    private const double RevealDelaySeconds = 0.42;
    private const int MaximumControlledRestarts = 3;
    private const int DialogueHistoryLimit = 2;
    private const int SelectionDialoguePercent = 35;
    private const string BackgroundTexturePath =
        "res://assets/backgrounds/command_chamber_static_scanner.png";

    private readonly StrategyEngine _strategy = new();
    private readonly OutcomeDirector _outcomeDirector = new();
    private readonly Dictionary<string, List<string>> _dialogueHistory = new(
        StringComparer.OrdinalIgnoreCase);

    private TextureRect _background = null!;
    private TitleScreen _titleScreen = null!;
    private GameplayHUD _hud = null!;
    private TutorDialogueUI _dialogueUI = null!;
    private TutorSpeechPlayer _speechPlayer = null!;
    private UiTransitionController _uiTransition = null!;
    private Control? _activePrimaryScreen;
    private SaveGameService _saveService = null!;
    private DialogueRepository _dialogues = null!;
    private RuleConfiguration _rules = null!;
    private SessionStats _stats = new();
    private SessionRandom _sessionRandom = new(1);
    private BashGame? _bash;
    private LimitBashGame? _limitBash;
    private DemoPhase _phase = DemoPhase.TitleScreen;
    private OutcomeDirective _limitDirective;
    private int _dialogueIndex;
    private int _bashRoundIndex;
    private int _limitGameIndex;
    private int _currentGameTurns;
    private int _controlledRestarts;
    private int _flowVersion;
    private int? _selectedChoice;
    private bool _inputLocked;
    private ForcedChoiceTutorialStage _forcedChoiceTutorialStage;
    private bool _chapterPending;
    private bool _selectionDialogueShown;
    private bool _revisionDialogueShown;
    private bool _hesitationDialogueShown;
    private ulong _hesitationDueTicks;
    private string _activeBriefingLineId = string.Empty;
    private string _currentTutorDialogueId = string.Empty;
    private string _currentTutorDialogue = string.Empty;
    private PendingGameStart _pendingGameStart;
    private OutcomeDirective? _pendingLimitDirective;
    private int _bashRoundOneFailures;
    private int _bashRoundTwoFailures;
    private int _dialogueStep;
    private string _saveId = string.Empty;
    private GameKind _lastSettledGame;
    private RoundOutcome _lastOutcome;
    private int _lastGameIndex;
    private int _lastGameTurns;
    private long _elapsedBeforeCurrentRunMs;
    private ulong _currentRunStartedTicks;
    private long _lastRenderedElapsedSecond = -1;
    private bool _playClockRunning;

    [Export(PropertyHint.File, "*.json")]
    public string SavePath { get; set; } = "user://project_mirror/demo_save.json";

    [Export]
    public bool FastMode { get; set; }

    [Export]
    public int TestSeed { get; set; }

    public DemoPhase CurrentPhase => _phase;

    public override void _Ready()
    {
        _background = GetNode<TextureRect>("Background");
        _titleScreen = GetNode<TitleScreen>("TitleScreen");
        _hud = GetNode<GameplayHUD>("GameplayHUD");
        _dialogueUI = GetNode<TutorDialogueUI>("TutorDialogueUI");
        _speechPlayer = GetNode<TutorSpeechPlayer>("TutorSpeechPlayer");
        _uiTransition = GetNode<UiTransitionController>("UiTransitionOverlay");

        _saveService = new SaveGameService(ResolvePersistentPath(SavePath));
        _dialogues = DialogueRepository.Load(
            GodotTextResourceReader.ReadAllText,
            "res://data/dialogue/intro.json",
            "res://data/dialogue/tutorial.json");
        _rules = RuleConfiguration.Load(
            GodotTextResourceReader.ReadAllText,
            "res://data/rules/bash.json",
            "res://data/rules/limit_bash.json");

        _titleScreen.NewGameRequested += StartNewGame;
        _titleScreen.ContinueRequested += ContinueGame;
        _titleScreen.QuitRequested += () => GetTree().Quit();
        _hud.ChoiceSelected += SelectChoice;
        _hud.ConfirmRequested += ConfirmChoice;
        _hud.ResultAdvanceRequested += AdvanceCurrentPage;
        _hud.BackToTitleRequested += BackToTitle;
        _hud.ChapterContinueRequested += ContinueChapter;
        _dialogueUI.ContinueRequested += AdvanceCurrentPage;
        _dialogueUI.BackToTitleRequested += BackToTitle;

        ShowTitleScreen();
    }

    private void ShowPrimaryScreen(Control screen)
    {
        if (_activePrimaryScreen != screen)
        {
            _uiTransition.Play();
        }

        _titleScreen.Visible = screen == _titleScreen;
        _hud.Visible = screen == _hud;
        _dialogueUI.Visible = screen == _dialogueUI;
        _activePrimaryScreen = screen;
    }

    public override void _Process(double delta)
    {
        if (_playClockRunning)
        {
            long elapsed = GetElapsedPlayMilliseconds();
            long second = elapsed / 1000;

            if (second != _lastRenderedElapsedSecond)
            {
                _lastRenderedElapsedSecond = second;
                _hud.SetElapsedPlayTime(elapsed);
                _dialogueUI.SetElapsedPlayTime(elapsed);
            }
        }

        TryShowHesitationDialogue();
    }

    public override void _ExitTree()
    {
        if (GodotObject.IsInstanceValid(_speechPlayer))
        {
            _speechPlayer.StopDialogue();
        }

        if (_phase != DemoPhase.TitleScreen
            && _phase != DemoPhase.Complete
            && !_inputLocked)
        {
            TryWriteCheckpoint();
        }
    }

    private void ShowTitleScreen(string? extraWarning = null)
    {
        StopPlayClock();
        _flowVersion++;
        _phase = DemoPhase.TitleScreen;
        _inputLocked = false;
        _forcedChoiceTutorialStage = ForcedChoiceTutorialStage.Hidden;
        _selectedChoice = null;
        _chapterPending = false;
        _currentTutorDialogueId = string.Empty;
        _currentTutorDialogue = string.Empty;
        _speechPlayer.StopDialogue();
        _hud.HideForcedChoiceTutorial();
        _background.Texture = GD.Load<Texture2D>(BackgroundTexturePath);
        ShowPrimaryScreen(_titleScreen);

        DemoSaveState? active = _saveService.LoadActive();
        string? warning = CombineWarnings(_saveService.LastWarning, extraWarning);
        SaveSlotInfo? slot = active is null
            ? null
            : new SaveSlotInfo(active.SaveId, active.ResumePhase, active.UpdatedUnixMs);
        _titleScreen.Configure(slot, warning);
    }

    private void StartNewGame()
    {
        _saveService.DeleteActive();
        _stats = new SessionStats();
        int seed = TestSeed != 0
            ? TestSeed
            : unchecked((int)(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() & int.MaxValue));
        _sessionRandom = new SessionRandom(seed);
        _saveId = Guid.NewGuid().ToString("N");
        _bash = null;
        _limitBash = null;
        _controlledRestarts = 0;
        _dialogueHistory.Clear();
        _activeBriefingLineId = string.Empty;
        _currentTutorDialogueId = string.Empty;
        _currentTutorDialogue = string.Empty;
        _pendingGameStart = PendingGameStart.None;
        _pendingLimitDirective = null;
        _bashRoundOneFailures = 0;
        _bashRoundTwoFailures = 0;
        _dialogueStep = 0;
        _forcedChoiceTutorialStage = ForcedChoiceTutorialStage.Hidden;
        StartPlayClock(0);
        EnterDialoguePhase(DemoPhase.Background, showChapter: true);
    }

    private void ContinueGame()
    {
        _titleScreen.SetLoading(true);
        DemoSaveState? state = _saveService.LoadActive();

        if (state is null)
        {
            ShowTitleScreen(
                _saveService.LastWarning ?? "No restorable active save was found.");
            return;
        }

        try
        {
            RestoreState(state);
        }
        catch (Exception exception) when (
            exception is InvalidOperationException
            or ArgumentOutOfRangeException
            or IOException)
        {
            GD.PushError($"Save restore failed: {exception}");
            ShowTitleScreen(
                "The save state is inconsistent and cannot continue. "
                    + "A recovery backup has been preserved.");
        }
    }

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
        _background.Texture = GD.Load<Texture2D>(BackgroundTexturePath);
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
        _background.Texture = GD.Load<Texture2D>(BackgroundTexturePath);
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
        _background.Texture = GD.Load<Texture2D>(BackgroundTexturePath);

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

    private void SelectChoice(int choice)
    {
        if (_inputLocked)
        {
            return;
        }

        if (_forcedChoiceTutorialStage
                == ForcedChoiceTutorialStage.SelectB
            && choice != 2)
        {
            return;
        }

        if (_forcedChoiceTutorialStage
            == ForcedChoiceTutorialStage.Confirm)
        {
            return;
        }

        if (_phase is DemoPhase.BashGame1Round1 or DemoPhase.BashGame1Round2)
        {
            if (_bash is null
                || _bash.CurrentTurn != Actor.Player
                || !_bash.CanTake(choice))
            {
                return;
            }

            int? previousChoice = _selectedChoice;
            _selectedChoice = choice;

            if (_forcedChoiceTutorialStage
                == ForcedChoiceTutorialStage.SelectB)
            {
                _forcedChoiceTutorialStage =
                    ForcedChoiceTutorialStage.Confirm;
                SetTutorDialogue(TutorDialoguePool.GuidedInputConfirm);
                RenderBash(
                    "Guided request B is staged. Confirmation is required.");
                return;
            }

            UpdateChoiceStageDialogue(
                previousChoice,
                choice,
                TutorDialoguePool.BashFirstSelection);
            RenderBash("Anchor request staged. Waiting for confirmation.");
            return;
        }

        if (_phase == DemoPhase.LimitBash)
        {
            if (_limitBash is null
                || !_limitBash.GetLegalPlayerActions().Contains(choice))
            {
                return;
            }

            int? previousChoice = _selectedChoice;
            _selectedChoice = choice;
            UpdateChoiceStageDialogue(
                previousChoice,
                choice,
                TutorDialoguePool.LimitFirstSelection);
            RenderLimitBash(
                waiting: false,
                log: "Anchor request staged. Waiting for confirmation.");
        }
    }

    private void ConfirmChoice()
    {
        if (_inputLocked || !_selectedChoice.HasValue)
        {
            return;
        }

        if (_forcedChoiceTutorialStage
            == ForcedChoiceTutorialStage.SelectB)
        {
            return;
        }

        if (_forcedChoiceTutorialStage
                == ForcedChoiceTutorialStage.Confirm
            && _selectedChoice.Value != 2)
        {
            return;
        }

        if (_phase is DemoPhase.BashGame1Round1 or DemoPhase.BashGame1Round2)
        {
            ConfirmBashChoice(_selectedChoice.Value);
        }
        else if (_phase == DemoPhase.LimitBash)
        {
            ConfirmLimitBashChoice(_selectedChoice.Value);
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
        _currentGameTurns++;
        RoundOutcome outcome = _bash.ApplyTake(Actor.Player, choice);
        _selectedChoice = null;

        if (outcome != RoundOutcome.Continue)
        {
            RenderBash(
                $"Player disengaged {choice}; the keystone anchor was disengaged.",
                suppressTutorDialogue: true);
            FinishBash(outcome);
            return;
        }

        RenderBash(
            $"Player disengaged {choice}; {_bash.Remaining} anchors remain active.",
            suppressTutorDialogue: true);
        RunBashTutorTurn(_flowVersion);
    }

    private async void RunBashTutorTurn(int expectedVersion)
    {
        if (_bash is null || _bash.CurrentTurn != Actor.Tutor)
        {
            return;
        }

        await ToSignal(
            GetTree().CreateTimer(FastMode ? 0.01 : TutorDelaySeconds),
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
        _currentGameTurns++;
        RoundOutcome outcome = _bash.ApplyTake(Actor.Tutor, choice);

        if (outcome != RoundOutcome.Continue)
        {
            RenderBash(
                $"Tutor disengaged {choice}; the keystone anchor was disengaged.");
            FinishBash(outcome);
            return;
        }

        _inputLocked = false;
        ResetTurnDialogueState();
        WriteCheckpoint();
        RenderBash(
            $"Tutor disengaged {choice}; {_bash.Remaining} anchors remain active.",
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
        _background.Texture = GD.Load<Texture2D>(BackgroundTexturePath);
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
            tutorDialogue: _currentTutorDialogue);
    }

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
        _background.Texture = GD.Load<Texture2D>(BackgroundTexturePath);

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

    private void ConfirmLimitBashChoice(int choice)
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

        RevealLimitRound(_flowVersion, choice, tutorChoice);
    }

    private async void RevealLimitRound(
        int expectedVersion,
        int playerChoice,
        int tutorChoice)
    {
        await ToSignal(
            GetTree().CreateTimer(FastMode ? 0.01 : TutorDelaySeconds),
            SceneTreeTimer.SignalName.Timeout);

        if (expectedVersion != _flowVersion || _limitBash is null)
        {
            return;
        }

        ClearCurrentTutorDialogue();
        _hud.ShowLimitReveal(
            _limitBash,
            _limitGameIndex,
            _stats,
            playerChoice,
            tutorChoice,
            _currentTutorDialogueId,
            _currentTutorDialogue);

        await ToSignal(
            GetTree().CreateTimer(FastMode ? 0.01 : RevealDelaySeconds),
            SceneTreeTimer.SignalName.Timeout);

        if (expectedVersion != _flowVersion || _limitBash is null)
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
                + $"{_limitBash.Remaining} anchors remain active.",
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
        _background.Texture = GD.Load<Texture2D>(BackgroundTexturePath);
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

    private void AdvanceAfterResult()
    {
        if (_lastSettledGame == GameKind.Bash)
        {
            if (_lastOutcome == RoundOutcome.PlayerWin)
            {
                if (_lastGameIndex == 1)
                {
                    EnterDialoguePhase(DemoPhase.BashRound2Intro);
                }
                else
                {
                    EnterDialoguePhase(DemoPhase.RuleTransition);
                }
            }
            else
            {
                int failureCount = _lastGameIndex == 1
                    ? _bashRoundOneFailures
                    : _bashRoundTwoFailures;
                EnterBriefing(
                    DemoPhase.BashRetryBriefing,
                    GetBashRetryPool(_lastGameIndex, failureCount),
                    _lastGameIndex == 1
                        ? PendingGameStart.BashRound1
                        : PendingGameStart.BashRound2);
            }

            return;
        }

        if (_stats.IsLimitBashComplete)
        {
            ShowSummary();
        }
        else
        {
            int upcomingGame = _stats.LimitBashGamesCompleted + 1;
            string pool = upcomingGame switch
            {
                2 => TutorDialoguePool.LimitGameTwoBegin,
                3 => TutorDialoguePool.LimitGameThreeBegin,
                _ => TutorDialoguePool.LimitLateBegin
            };
            EnterBriefing(
                DemoPhase.LimitGameBriefing,
                pool,
                PendingGameStart.LimitBash);
        }
    }

    private void ShowSummary()
    {
        _flowVersion++;
        _phase = DemoPhase.Summary;
        _dialogueIndex = 0;
        _activeBriefingLineId = string.Empty;
        _pendingGameStart = PendingGameStart.None;
        _pendingLimitDirective = null;
        _bash = null;
        _limitBash = null;
        _selectedChoice = null;
        _inputLocked = false;
        _background.Texture = GD.Load<Texture2D>(BackgroundTexturePath);
        ShowPrimaryScreen(_dialogueUI);
        WriteCheckpoint();
        RenderDialogue();
    }

    private void CompleteDemo()
    {
        _phase = DemoPhase.Complete;
        _inputLocked = true;
        StopPlayClock();
        WriteCheckpoint(isComplete: true);
        ShowTitleScreen();
    }

    private void BackToTitle()
    {
        if (_inputLocked)
        {
            return;
        }

        StopPlayClock();
        WriteCheckpoint();
        ShowTitleScreen();
    }

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
            _background.Texture = GD.Load<Texture2D>(BackgroundTexturePath);
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
                snapshot.Result);
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
            _background.Texture = GD.Load<Texture2D>(BackgroundTexturePath);

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
                choiceHistory: snapshot.Game == GameKind.LimitBash
                    ? snapshot.ChoicePairs
                    : null);
            return;
        }

        _background.Texture = GD.Load<Texture2D>(BackgroundTexturePath);
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

    private DialogueLine PickTutorLine(string poolId)
    {
        IReadOnlyList<DialogueLine> pool = _dialogues.GetRandomPool(poolId);

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
                Array.Empty<ChoicePair>(),
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
        _lastRenderedElapsedSecond = -1;
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
