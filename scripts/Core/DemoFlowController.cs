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

    private readonly StrategyEngine _strategy = new();
    private readonly OutcomeDirector _outcomeDirector = new();

    private TextureRect _background = null!;
    private TitleScreen _titleScreen = null!;
    private GameplayHUD _hud = null!;
    private TutorDialogueUI _dialogueUI = null!;
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
    private bool _chapterPending;
    private string _currentTutorDialogue = string.Empty;
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

        _saveService = new SaveGameService(ResolvePersistentPath(SavePath));
        _dialogues = DialogueRepository.Load(
            ResolveResourcePath("res://data/dialogue/intro.json"),
            ResolveResourcePath("res://data/dialogue/tutorial.json"));
        _rules = RuleConfiguration.Load(
            ResolveResourcePath("res://data/rules/bash.json"),
            ResolveResourcePath("res://data/rules/limit_bash.json"));

        _titleScreen.NewGameRequested += StartNewGame;
        _titleScreen.ContinueRequested += ContinueGame;
        _titleScreen.QuitRequested += () => GetTree().Quit();
        _hud.ChoiceSelected += SelectChoice;
        _hud.ConfirmRequested += ConfirmChoice;
        _hud.ContinueRequested += AdvanceCurrentPage;
        _hud.BackToTitleRequested += BackToTitle;
        _hud.ChapterContinueRequested += ContinueChapter;
        _dialogueUI.ContinueRequested += AdvanceCurrentPage;
        _dialogueUI.BackToTitleRequested += BackToTitle;

        ShowTitleScreen();
    }

    public override void _Process(double delta)
    {
        if (!_playClockRunning)
        {
            return;
        }

        long elapsed = GetElapsedPlayMilliseconds();
        long second = elapsed / 1000;

        if (second == _lastRenderedElapsedSecond)
        {
            return;
        }

        _lastRenderedElapsedSecond = second;
        _hud.SetElapsedPlayTime(elapsed);
        _dialogueUI.SetElapsedPlayTime(elapsed);
    }

    public override void _ExitTree()
    {
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
        _selectedChoice = null;
        _chapterPending = false;
        _currentTutorDialogue = string.Empty;
        _background.Texture = GD.Load<Texture2D>("res://assets/backgrounds/bright_lab.png");
        _titleScreen.Visible = true;
        _hud.Visible = false;
        _dialogueUI.Visible = false;

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
        _currentTutorDialogue = string.Empty;
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
        _selectedChoice = null;
        _inputLocked = false;
        _bash = null;
        _limitBash = null;
        _titleScreen.Visible = false;
        _hud.Visible = showChapter;
        _dialogueUI.Visible = false;
        _background.Texture = GD.Load<Texture2D>("res://assets/backgrounds/bright_lab.png");
        WriteCheckpoint();

        if (showChapter)
        {
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
        IReadOnlyList<DialogueLine> lines = _dialogues.Get(_phase);

        if (lines.Count == 0)
        {
            throw new InvalidOperationException($"Phase {_phase} contains no dialogue.");
        }

        _dialogueIndex = Math.Clamp(_dialogueIndex, 0, lines.Count - 1);
        _hud.Visible = false;
        _dialogueUI.Visible = true;
        _dialogueUI.ShowDialogue(
            _phase,
            lines[_dialogueIndex],
            _dialogueIndex,
            lines.Count);
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
            case DemoPhase.RuleTransition:
                AdvanceDialogue();
                break;

            case DemoPhase.RoundResult:
                AdvanceAfterResult();
                break;

            case DemoPhase.Summary:
                CompleteDemo();
                break;
        }
    }

    private void AdvanceDialogue()
    {
        IReadOnlyList<DialogueLine> lines = _dialogues.Get(_phase);
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

            case DemoPhase.RuleTransition:
                StartLimitBashGame();
                break;
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
        _limitBash = null;
        _titleScreen.Visible = false;
        _hud.Visible = true;
        _dialogueUI.Visible = false;
        _background.Texture = GD.Load<Texture2D>("res://assets/backgrounds/bright_lab.png");

        int[] candidates = roundIndex == 1
            ? _rules.Bash.Round1InitialUnits
            : _rules.Bash.Round2InitialUnits;
        int initialUnits = candidates[_sessionRandom.Next(0, candidates.Length)];
        _bash = new BashGame();
        _bash.Start(
            initialUnits,
            roundIndex == 1 ? Actor.Player : Actor.Tutor);

        WriteCheckpoint();
        RenderBash(
            "A new round has started.",
            TutorDialoguePool.BashRoundStart);

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

        if (_phase is DemoPhase.BashGame1Round1 or DemoPhase.BashGame1Round2)
        {
            if (_bash is null
                || _bash.CurrentTurn != Actor.Player
                || !_bash.CanTake(choice))
            {
                return;
            }

            _selectedChoice = choice;
            RenderBash("Waiting for player confirmation.");
            return;
        }

        if (_phase == DemoPhase.LimitBash)
        {
            if (_limitBash is null
                || !_limitBash.GetLegalPlayerActions().Contains(choice))
            {
                return;
            }

            _selectedChoice = choice;
            RenderLimitBash(
                waiting: false,
                log: "Waiting for player confirmation.");
        }
    }

    private void ConfirmChoice()
    {
        if (_inputLocked || !_selectedChoice.HasValue)
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

        _inputLocked = true;
        _currentGameTurns++;
        RoundOutcome outcome = _bash.ApplyTake(Actor.Player, choice);
        _selectedChoice = null;

        if (outcome != RoundOutcome.Continue)
        {
            RenderBash(
                $"Player took {choice}; 0 remaining. Final unit taken.");
            FinishBash(outcome);
            return;
        }

        RenderBash(
            $"Player took {choice}; {_bash.Remaining} remaining.",
            TutorDialoguePool.BashPlayerConfirmed);
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
                $"Tutor took {choice}; 0 remaining. Final unit taken.");
            FinishBash(outcome);
            return;
        }

        _inputLocked = false;
        WriteCheckpoint();
        RenderBash(
            $"Tutor took {choice}; {_bash.Remaining} remaining.",
            _bash.Remaining <= BashGame.MaximumTake + 1
                ? TutorDialoguePool.BashTerminalApproach
                : TutorDialoguePool.BashTutorActed);
    }

    private void RenderBash(string log, string? dialoguePool = null)
    {
        if (_bash is null)
        {
            return;
        }

        UpdateTutorDialogue(dialoguePool, TutorDialoguePool.BashRoundStart);

        _hud.ShowBash(
            _bash,
            _bashRoundIndex,
            _stats,
            _currentGameTurns,
            _selectedChoice,
            inputOpen: !_inputLocked && _bash.CurrentTurn == Actor.Player,
            systemLog: log,
            tutorDialogue: _currentTutorDialogue);
    }

    private void FinishBash(RoundOutcome outcome)
    {
        if (_bash is null)
        {
            return;
        }

        _inputLocked = false;
        _stats.RecordBash(outcome, _currentGameTurns);
        _lastSettledGame = GameKind.Bash;
        _lastOutcome = outcome;
        _lastGameIndex = _bashRoundIndex;
        _lastGameTurns = _currentGameTurns;
        _phase = DemoPhase.RoundResult;
        _background.Texture = GD.Load<Texture2D>("res://assets/backgrounds/result_shards.png");
        WriteCheckpoint();
        _hud.ShowRoundResult(
            GameKind.Bash,
            outcome,
            _bashRoundIndex,
            _currentGameTurns,
            _stats,
            willContinue: true,
            tutorDialogue: PickResultDialogue(outcome));
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
        _titleScreen.Visible = false;
        _hud.Visible = true;
        _dialogueUI.Visible = false;
        _background.Texture = GD.Load<Texture2D>("res://assets/backgrounds/bright_lab.png");

        if (!preserveDirective)
        {
            _limitDirective = OutcomeDirector.GetDirective(
                _stats.LimitBashGamesCompleted,
                _sessionRandom.NextSingle());
            _controlledRestarts = 0;
        }

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

        WriteCheckpoint();
        RenderLimitBash(
            waiting: false,
            log: "A new game has started.",
            dialoguePool: TutorDialoguePool.LimitGameStart);
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
            log: "Player choice locked. Waiting for the simultaneous reveal.",
            dialoguePool: TutorDialoguePool.LimitChoiceLocked);

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

        UpdateTutorDialogue(
            TutorDialoguePool.LimitReveal,
            TutorDialoguePool.LimitReveal);
        _hud.ShowLimitReveal(
            _limitBash,
            _limitGameIndex,
            _stats,
            playerChoice,
            tutorChoice,
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
        WriteCheckpoint();
        RenderLimitBash(
            waiting: false,
            log: $"REVEAL: PLAYER {playerChoice} / TUTOR {tutorChoice}; "
                + $"{_limitBash.Remaining} remaining.",
            dialoguePool: _limitBash.Remaining <= 6
                ? TutorDialoguePool.LimitTerminalApproach
                : TutorDialoguePool.LimitReveal);
    }

    private void RenderLimitBash(
        bool waiting,
        string log,
        string? dialoguePool = null)
    {
        if (_limitBash is null)
        {
            return;
        }

        UpdateTutorDialogue(dialoguePool, TutorDialoguePool.LimitGameStart);

        _hud.ShowLimitBash(
            _limitBash,
            _limitGameIndex,
            _stats,
            _selectedChoice,
            inputOpen: !_inputLocked,
            waiting: waiting,
            systemLog: log,
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
        _background.Texture = GD.Load<Texture2D>("res://assets/backgrounds/result_shards.png");
        WriteCheckpoint();
        _hud.ShowRoundResult(
            GameKind.LimitBash,
            outcome,
            _limitGameIndex,
            _currentGameTurns,
            _stats,
            willContinue: !_stats.IsLimitBashComplete,
            tutorDialogue: PickResultDialogue(outcome),
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
        StartLimitBashGame(preserveDirective: true);
    }

    private void AdvanceAfterResult()
    {
        if (_lastSettledGame == GameKind.Bash)
        {
            if (_lastOutcome == RoundOutcome.PlayerWin)
            {
                if (_lastGameIndex == 1)
                {
                    StartBashRound(roundIndex: 2);
                }
                else
                {
                    EnterDialoguePhase(DemoPhase.RuleTransition);
                }
            }
            else
            {
                StartBashRound(_lastGameIndex);
            }

            return;
        }

        if (_stats.IsLimitBashComplete)
        {
            ShowSummary();
        }
        else
        {
            StartLimitBashGame();
        }
    }

    private void ShowSummary()
    {
        _flowVersion++;
        _phase = DemoPhase.Summary;
        _bash = null;
        _limitBash = null;
        _selectedChoice = null;
        _inputLocked = false;
        _background.Texture = GD.Load<Texture2D>("res://assets/backgrounds/result_shards.png");
        _hud.Visible = false;
        _dialogueUI.Visible = true;
        WriteCheckpoint();
        _dialogueUI.ShowSummary(_stats);
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
        _stats = SessionStats.FromSnapshot(state.Stats);
        _sessionRandom = new SessionRandom(state.SessionSeed, state.RngStep);
        StartPlayClock(state.ElapsedPlayMilliseconds);
        _selectedChoice = null;
        _inputLocked = false;
        _chapterPending = false;
        _titleScreen.Visible = false;
        _hud.Visible = false;
        _dialogueUI.Visible = false;
        _hud.HideChapter();

        if (_phase is DemoPhase.Background
            or DemoPhase.BashTutorial
            or DemoPhase.RuleTransition)
        {
            _background.Texture = GD.Load<Texture2D>("res://assets/backgrounds/bright_lab.png");
            RenderDialogue();
            return;
        }

        if (_phase == DemoPhase.Summary)
        {
            _background.Texture = GD.Load<Texture2D>("res://assets/backgrounds/result_shards.png");
            _dialogueUI.Visible = true;
            _dialogueUI.ShowSummary(_stats);
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

        if (_phase == DemoPhase.RoundResult)
        {
            _hud.Visible = true;
            _lastSettledGame = snapshot.Game;
            _lastOutcome = snapshot.Result;
            _lastGameIndex = snapshot.Game == GameKind.Bash
                ? snapshot.BashRoundIndex
                : snapshot.LimitGameIndex;
            _lastGameTurns = snapshot.RoundIndex;
            _background.Texture = GD.Load<Texture2D>("res://assets/backgrounds/result_shards.png");
            _hud.ShowRoundResult(
                snapshot.Game,
                snapshot.Result,
                _lastGameIndex,
                _lastGameTurns,
                _stats,
                willContinue: snapshot.Game == GameKind.Bash
                    || !_stats.IsLimitBashComplete,
                tutorDialogue: PickResultDialogue(snapshot.Result),
                finalChoice: snapshot.Game == GameKind.LimitBash
                    && snapshot.ChoicePairs.Count > 0
                        ? snapshot.ChoicePairs[^1]
                        : null,
                choiceHistory: snapshot.Game == GameKind.LimitBash
                    ? snapshot.ChoicePairs
                    : null);
            return;
        }

        _background.Texture = GD.Load<Texture2D>("res://assets/backgrounds/bright_lab.png");
        _hud.Visible = true;

        if (snapshot.Game == GameKind.Bash && _bash is not null)
        {
            _inputLocked = _bash.CurrentTurn == Actor.Tutor;
            RenderBash(
                "Restored from a stable checkpoint.",
                TutorDialoguePool.Restore);

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
            _currentTutorDialogue = PickTutorDialogue(dialoguePool);
        }
        else if (string.IsNullOrWhiteSpace(_currentTutorDialogue))
        {
            _currentTutorDialogue = PickTutorDialogue(fallbackPool);
        }
    }

    private string PickResultDialogue(RoundOutcome outcome)
    {
        string pool = outcome switch
        {
            RoundOutcome.PlayerWin => TutorDialoguePool.PlayerWin,
            RoundOutcome.PlayerLose => TutorDialoguePool.PlayerLose,
            RoundOutcome.Draw => TutorDialoguePool.Draw,
            _ => throw new InvalidOperationException(
                "A result dialogue requires a settled outcome.")
        };
        return PickTutorDialogue(pool);
    }

    private string PickTutorDialogue(string poolId)
    {
        int remaining = _bash?.Remaining ?? _limitBash?.Remaining ?? 0;
        uint selector = 2_166_136_261u;

        MixDialogueSelector(ref selector, _sessionRandom.Seed);
        MixDialogueSelector(ref selector, (int)_phase);
        MixDialogueSelector(ref selector, _bashRoundIndex);
        MixDialogueSelector(ref selector, _limitGameIndex);
        MixDialogueSelector(ref selector, _currentGameTurns);
        MixDialogueSelector(ref selector, remaining);

        foreach (char character in poolId)
        {
            MixDialogueSelector(ref selector, character);
        }

        return _dialogues.PickRandom(poolId, unchecked((int)selector)).Text;
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

    private static string ResolveResourcePath(string path)
    {
        return path.StartsWith("res://", StringComparison.Ordinal)
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
