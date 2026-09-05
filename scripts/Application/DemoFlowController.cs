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
    private const double LimitTutorCommitmentPauseSeconds = 0.90;
    private const double LimitTutorRevealExplanationSeconds = 1.35;
    private const int MaximumControlledRestarts = 3;
    private const int DialogueHistoryLimit = 2;
    private const int SelectionDialoguePercent = 65;

    private readonly StrategyEngine _strategy = new();
    private readonly OutcomeDirector _outcomeDirector = new();
    private readonly Dictionary<string, List<string>> _dialogueHistory = new(
        StringComparer.OrdinalIgnoreCase);

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
    private bool _playClockRunning;

    [Export(PropertyHint.File, "*.json")]
    public string SavePath { get; set; } = "user://project_mirror/demo_save.json";

    public DemoPhase CurrentPhase => _phase;

    public override void _Ready()
    {
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
        int seed = unchecked(
            (int)(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() & int.MaxValue));
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

}
