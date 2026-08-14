using Godot;
using System;
using System.Collections.Generic;
using System.IO;

/// <summary>
/// Owns the Milestone A flow: start, alternating turns, settlement, and replay.
/// </summary>
public partial class DemoFlowController : Control
{
    private const double AIThinkDelaySeconds = 0.45;

    private readonly BashGame _bash = new();
    private readonly StrategyEngine _strategy = new();
    private readonly HoverTracker _hoverTracker = new();

    private DemoUI _ui = null!;
    private PlayerModel _playerModel = null!;
    private int _roundNumber;
    private int _playerWins;
    private int _aiWins;
    private int _roundVersion;
    private int _playerTurnIndex;
    private long _choiceOpenedAtMilliseconds;
    private bool _inputLocked;
    private bool _choiceWindowOpen;
    private bool _sessionEnded;

    [Export(PropertyHint.File, "*.json")]
    public string ProfileSavePath { get; set; } =
        "user://project_mirror/player_profile.json";

    public override void _Ready()
    {
        string persistentPath = ResolvePersistentPath(ProfileSavePath);
        var store = new JsonPlayerProfileStore(persistentPath);
        _playerModel = new PlayerModel(store);
        _playerModel.PersistenceFailed += OnPersistenceFailed;
        _playerModel.BeginSession();

        if (!string.IsNullOrEmpty(_playerModel.LoadWarning))
        {
            GD.PushWarning(_playerModel.LoadWarning);
        }

        _ui = new DemoUI(this);
        _ui.ChoicePressed += OnTakePressed;
        _ui.ChoiceHoverStarted += OnChoiceHoverStarted;
        _ui.ChoiceHoverEnded += OnChoiceHoverEnded;
        _ui.RestartPressed += StartRound;

        StartRound();
    }

    public override void _ExitTree()
    {
        EndSession("application_exit");
    }

    private void StartRound()
    {
        bool restarted = _roundNumber > 0 && !_bash.IsGameOver;
        int previousRemaining = _bash.Remaining;
        CloseChoiceWindow(restarted ? "round_restart" : "new_round");

        // Incrementing the version invalidates any AI timer from an old round.
        _roundVersion++;
        _roundNumber++;
        _playerTurnIndex = 1;
        _inputLocked = false;

        _bash.StartGame();
        _playerModel.RecordRoundStarted(
            _roundNumber,
            restarted,
            previousRemaining);
        _ui.ShowRoundStarted(_roundNumber, _playerWins, _aiWins);
        OpenChoiceWindow();
        _ui.RenderBash(_bash, acceptPlayerInput: true);
    }

    private void OnTakePressed(int amount)
    {
        if (_inputLocked
            || _bash.IsGameOver
            || !_bash.IsPlayerTurn
            || !_bash.IsLegalMove(amount))
        {
            return;
        }

        // Lock synchronously before mutating state so rapid clicks cannot queue
        // a second player action while the AI turn is pending.
        _inputLocked = true;
        CompleteActiveHovers();
        _choiceWindowOpen = false;

        int remainingBefore = _bash.Remaining;
        double decisionSeconds = Math.Max(
            0,
            (System.Environment.TickCount64 - _choiceOpenedAtMilliseconds) / 1000.0);
        Dictionary<int, long> hoverSnapshot = _hoverTracker.Snapshot();

        _bash.ApplyPlayerMove(amount);
        _playerModel.RecordChoice(
            _roundNumber,
            _playerTurnIndex,
            amount,
            decisionSeconds,
            hoverSnapshot,
            remainingBefore,
            _bash.Remaining);
        _hoverTracker.Reset();

        _ui.ShowPlayerMove(amount);
        _ui.RenderBash(_bash, acceptPlayerInput: false);

        if (_bash.IsGameOver)
        {
            FinishRound();
            return;
        }

        RunAITurn(_roundVersion);
    }

    private async void RunAITurn(int expectedRoundVersion)
    {
        await ToSignal(
            GetTree().CreateTimer(AIThinkDelaySeconds),
            SceneTreeTimer.SignalName.Timeout);

        if (expectedRoundVersion != _roundVersion || _bash.IsGameOver)
        {
            return;
        }

        int amount = _strategy.ChooseBashMove(_bash);
        _bash.ApplyAIMove(amount);
        _ui.ShowAIMove(amount);

        if (_bash.IsGameOver)
        {
            FinishRound();
            return;
        }

        _inputLocked = false;
        _playerTurnIndex++;
        OpenChoiceWindow();
        _ui.RenderBash(_bash, acceptPlayerInput: true);
    }

    private void FinishRound()
    {
        _inputLocked = true;

        if (_bash.GetResult() == GameResult.PlayerWin)
        {
            _playerWins++;
        }
        else if (_bash.GetResult() == GameResult.AIWin)
        {
            _aiWins++;
        }

        _playerModel.RecordRoundCompleted(_roundNumber, _bash.GetResult());
        _ui.RenderBash(_bash, acceptPlayerInput: false);
        _ui.ShowWinner(_bash.GetResult(), _playerWins, _aiWins);
    }

    private void OpenChoiceWindow()
    {
        _hoverTracker.Reset();
        _choiceOpenedAtMilliseconds = System.Environment.TickCount64;
        _choiceWindowOpen = true;
        _playerModel.RecordChoiceWindowOpened(
            _roundNumber,
            _playerTurnIndex,
            _bash.Remaining);
    }

    private void OnChoiceHoverStarted(int choice)
    {
        if (_inputLocked || _bash.IsGameOver || !_bash.IsPlayerTurn)
        {
            return;
        }

        if (_hoverTracker.Enter(choice))
        {
            _playerModel.RecordHoverStarted(
                _roundNumber,
                _playerTurnIndex,
                choice,
                _bash.Remaining);
        }
    }

    private void OnChoiceHoverEnded(int choice)
    {
        long? duration = _hoverTracker.Exit(choice);
        if (!duration.HasValue)
        {
            return;
        }

        _playerModel.RecordHoverEnded(
            _roundNumber,
            _playerTurnIndex,
            choice,
            duration.Value,
            _bash.Remaining);
    }

    private void CompleteActiveHovers()
    {
        Dictionary<int, long> completed = _hoverTracker.CompleteActiveHovers();

        foreach ((int choice, long duration) in completed)
        {
            _playerModel.RecordHoverEnded(
                _roundNumber,
                _playerTurnIndex,
                choice,
                duration,
                _bash.Remaining);
        }
    }

    private void EndSession(string reason)
    {
        if (_sessionEnded || _playerModel is null)
        {
            return;
        }

        _sessionEnded = true;
        CloseChoiceWindow(reason);
        _playerModel.EndSession(reason);
    }

    private void CloseChoiceWindow(string reason)
    {
        CompleteActiveHovers();

        if (!_choiceWindowOpen || _roundNumber <= 0)
        {
            return;
        }

        double decisionSeconds = Math.Max(
            0,
            (System.Environment.TickCount64 - _choiceOpenedAtMilliseconds) / 1000.0);
        _playerModel.RecordChoiceWindowAbandoned(
            _roundNumber,
            _playerTurnIndex,
            decisionSeconds,
            _bash.Remaining,
            reason);
        _choiceWindowOpen = false;
        _hoverTracker.Reset();
    }

    private static string ResolvePersistentPath(string configuredPath)
    {
        if (string.IsNullOrWhiteSpace(configuredPath))
        {
            throw new InvalidOperationException("ProfileSavePath cannot be empty.");
        }

        if (configuredPath.StartsWith("user://", StringComparison.OrdinalIgnoreCase)
            || configuredPath.StartsWith("res://", StringComparison.OrdinalIgnoreCase))
        {
            return ProjectSettings.GlobalizePath(configuredPath);
        }

        return Path.GetFullPath(configuredPath);
    }

    private static void OnPersistenceFailed(string message)
    {
        GD.PushError($"Player profile could not be saved: {message}");
    }
}
