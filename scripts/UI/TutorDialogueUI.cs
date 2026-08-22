using Godot;
using System;

/// <summary>
/// Dedicated presentation for pages that contain only the Tutor and authored
/// dialogue. Gameplay controls and SYSTEM telemetry never appear here.
/// </summary>
public partial class TutorDialogueUI : Control
{
    [Export(PropertyHint.Range, "1,60,1")]
    public float CharactersPerSecond { get; set; } = 10.0f;

    private Label _phaseLabel = null!;
    private Label _saveLabel = null!;
    private Label _playTimeLabel = null!;
    private RichTextLabel _dialogueText = null!;
    private RichTextLabel _supplementaryText = null!;
    private Button _backButton = null!;
    private double _visibleCharacterProgress;
    private bool _isTyping;

    public event Action? ContinueRequested;

    public event Action? BackToTitleRequested;

    public override void _Ready()
    {
        _phaseLabel = GetNode<Label>("SafeArea/Layout/Header/HeaderRow/PhaseLabel");
        _saveLabel = GetNode<Label>("SafeArea/Layout/Header/HeaderRow/SaveLabel");
        _playTimeLabel = GetNode<Label>(
            "SafeArea/Layout/Header/HeaderRow/PlayTimeLabel");
        _dialogueText = GetNode<RichTextLabel>(
            "SafeArea/Layout/Content/DialogueCard/DialogueVBox/DialogueText");
        _supplementaryText = GetNode<RichTextLabel>(
            "SafeArea/Layout/Content/DialogueCard/DialogueVBox/SupplementaryText");
        _backButton = GetNode<Button>("SafeArea/Layout/Footer/BackButton");

        _backButton.Pressed += () => BackToTitleRequested?.Invoke();
    }

    public override void _Process(double delta)
    {
        if (!_isTyping || !Visible)
        {
            return;
        }

        _visibleCharacterProgress += CharactersPerSecond * delta;
        int totalCharacters = _dialogueText.GetTotalCharacterCount();
        int visibleCharacters = Math.Min(
            totalCharacters,
            (int)Math.Floor(_visibleCharacterProgress));
        _dialogueText.VisibleCharacters = visibleCharacters;

        if (visibleCharacters >= totalCharacters)
        {
            CompleteTyping();
        }
    }

    public override void _Input(InputEvent @event)
    {
        if (!Visible
            || @event is not InputEventMouseButton mouseButton
            || mouseButton.ButtonIndex != MouseButton.Left
            || !mouseButton.Pressed
            || _backButton.GetGlobalRect().HasPoint(mouseButton.GlobalPosition))
        {
            return;
        }

        GetViewport().SetInputAsHandled();

        if (_isTyping)
        {
            CompleteTyping();
            return;
        }

        ContinueRequested?.Invoke();
    }

    public void ShowDialogue(
        DemoPhase phase,
        DialogueLine line,
        int lineIndex,
        int totalLines)
    {
        _phaseLabel.Text = phase switch
        {
            DemoPhase.Background => "BACKGROUND / EXPLANATION",
            DemoPhase.BashTutorial => "RULE / BASH",
            DemoPhase.RuleTransition => "RULE / LIMIT BASH",
            _ => phase.ToString().ToUpperInvariant()
        };
        _saveLabel.Text = $"SAVE · STABLE   {lineIndex + 1:00}/{totalLines:00}";
        BeginTyping(line.Text);
        _supplementaryText.Visible = false;
    }

    public void ShowSummary(SessionStats stats)
    {
        _phaseLabel.Text = "SUMMARY";
        _saveLabel.Text = "SAVE · STABLE";
        BeginTyping("The test is complete. Here is the final summary for this save.");
        _supplementaryText.Visible = true;
        _supplementaryText.Text =
            "[b]SESSION SUMMARY[/b]\n\n"
            + $"Standard Bash: {stats.BashWins} wins / {stats.BashLosses} losses, "
            + $"{stats.BashTurnsPlayed} action turns total.\n\n"
            + $"Limit Bash: {stats.LimitBashPlayerWins} wins / "
            + $"{stats.LimitBashPlayerLosses} losses / {stats.LimitBashDraws} draws, "
            + $"{stats.LimitBashGamesCompleted} games and "
            + $"{stats.LimitBashRoundsPlayed} reveal rounds total.\n\n"
            + $"Completion reason: {stats.CompletionReason}";
    }

    public void SetElapsedPlayTime(long elapsedMilliseconds)
    {
        TimeSpan elapsed = TimeSpan.FromMilliseconds(
            Math.Max(0, elapsedMilliseconds));
        _playTimeLabel.Text = elapsed.TotalHours >= 1
            ? $"PLAY TIME · {(int)elapsed.TotalHours:00}:{elapsed.Minutes:00}:{elapsed.Seconds:00}"
            : $"PLAY TIME · {elapsed.Minutes:00}:{elapsed.Seconds:00}";
    }

    private void BeginTyping(string text)
    {
        _dialogueText.Text = text;
        _dialogueText.VisibleCharacters = 0;
        _visibleCharacterProgress = 0.0;
        _isTyping = _dialogueText.GetTotalCharacterCount() > 0;

        if (!_isTyping)
        {
            CompleteTyping();
        }
    }

    private void CompleteTyping()
    {
        _isTyping = false;
        _dialogueText.VisibleCharacters = -1;
    }
}
