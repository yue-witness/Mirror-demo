using System;
using Godot;

/// <summary>
/// Thin adapter between the Bash flow and the authored controls in main.tscn.
/// It never changes game state; it only emits input and renders state.
/// </summary>
public sealed class DemoUI
{
    private readonly Label _phaseLabel;
    private readonly Label _scoreLabel;
    private readonly Label _remainingLabel;
    private readonly Label _eventBanner;
    private readonly Label _speakerLabel;
    private readonly RichTextLabel _dialogueText;
    private readonly PileVisualizer _pileVisualizer;
    private readonly Button[] _takeButtons;
    private readonly Button _restartButton;

    public DemoUI(Control root)
    {
        _phaseLabel = root.GetNode<Label>("SafeArea/RootVBox/TopBar/PhaseLabel");
        _scoreLabel = root.GetNode<Label>("SafeArea/RootVBox/TopBar/ScoreLabel");
        _remainingLabel = root.GetNode<Label>(
            "SafeArea/RootVBox/GamePanel/GameVBox/RemainingLabel");
        _eventBanner = root.GetNode<Label>(
            "SafeArea/RootVBox/GamePanel/GameVBox/EventBanner");
        _pileVisualizer = root.GetNode<PileVisualizer>(
            "SafeArea/RootVBox/GamePanel/GameVBox/PileVisualizer");
        _speakerLabel = root.GetNode<Label>(
            "SafeArea/RootVBox/DialoguePanel/DialogueRow/DialogueVBox/SpeakerLabel");
        _dialogueText = root.GetNode<RichTextLabel>(
            "SafeArea/RootVBox/DialoguePanel/DialogueRow/DialogueVBox/DialogueText");

        _takeButtons = new[]
        {
            root.GetNode<Button>("SafeArea/RootVBox/ChoicePanel/Take1Button"),
            root.GetNode<Button>("SafeArea/RootVBox/ChoicePanel/Take2Button"),
            root.GetNode<Button>("SafeArea/RootVBox/ChoicePanel/Take3Button")
        };

        _restartButton = root.GetNode<Button>(
            "SafeArea/RootVBox/GamePanel/GameVBox/RestartButton");

        for (int index = 0; index < _takeButtons.Length; index++)
        {
            int amount = index + BashGame.MinimumTake;
            _takeButtons[index].Pressed += () => ChoicePressed?.Invoke(amount);
            _takeButtons[index].MouseEntered += () => ChoiceHoverStarted?.Invoke(amount);
            _takeButtons[index].MouseExited += () => ChoiceHoverEnded?.Invoke(amount);
        }

        _restartButton.Pressed += () => RestartPressed?.Invoke();
    }

    public event Action<int>? ChoicePressed;

    public event Action<int>? ChoiceHoverStarted;

    public event Action<int>? ChoiceHoverEnded;

    public event Action? RestartPressed;

    public void RenderBash(BashGame game, bool acceptPlayerInput)
    {
        _remainingLabel.Text = $"REMAINING: {game.Remaining}";
        _pileVisualizer.SetUnitCount(game.Remaining);

        for (int index = 0; index < _takeButtons.Length; index++)
        {
            int amount = index + BashGame.MinimumTake;
            _takeButtons[index].Disabled = !acceptPlayerInput
                || !game.IsPlayerTurn
                || !game.IsLegalMove(amount);
        }
    }

    public void ShowRoundStarted(int round, int playerWins, int aiWins)
    {
        _phaseLabel.Text = "BASH / PLAYER TURN";
        _scoreLabel.Text = $"ROUND {round:00}  /  P {playerWins} - AI {aiWins}";
        _eventBanner.Text = "PLAYER TURN: CHOOSE TAKE 1, 2, OR 3";
        _restartButton.Text = "RESTART ROUND";

        ShowDialogue(
            "MIRROR",
            "Remove one to three units. Whoever takes the final unit wins.\n"
            + "Your move, Subject S-17.");
    }

    public void ShowPlayerMove(int amount)
    {
        _phaseLabel.Text = "BASH / AI TURN";
        _eventBanner.Text = $"SUBJECT REMOVED {amount}. MIRROR IS CALCULATING...";

        ShowDialogue("SYSTEM", "Player action accepted. Input locked for the AI turn.");
    }

    public void ShowAIMove(int amount)
    {
        _phaseLabel.Text = "BASH / PLAYER TURN";
        _eventBanner.Text = $"MIRROR REMOVED {amount}. YOUR TURN.";

        ShowDialogue("MIRROR", $"I removed {amount}. Choose again.");
    }

    public void ShowWinner(GameResult result, int playerWins, int aiWins)
    {
        bool playerWon = result == GameResult.PlayerWin;
        string winner = playerWon ? "PLAYER" : "MIRROR";

        _phaseLabel.Text = "BASH / COMPLETE";
        _scoreLabel.Text = $"FINAL  /  P {playerWins} - AI {aiWins}";
        _eventBanner.Text = $"ROUND COMPLETE: {winner} WINS";
        _restartButton.Text = "PLAY AGAIN";

        ShowDialogue(
            playerWon ? "SYSTEM" : "MIRROR",
            playerWon
                ? "Subject S-17 took the final unit. Round complete."
                : "I took the final unit. Round complete.");
    }

    private void ShowDialogue(string speaker, string text)
    {
        _speakerLabel.Text = speaker;
        _dialogueText.Text = text;
    }
}
