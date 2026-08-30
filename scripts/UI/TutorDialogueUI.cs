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
    private PanelContainer _portrait = null!;
    private Label _portraitText = null!;
    private Label _tutorName = null!;
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
        _portrait = GetNode<PanelContainer>(
            "SafeArea/Layout/Content/TutorCard/TutorVBox/Portrait");
        _portraitText = GetNode<Label>(
            "SafeArea/Layout/Content/TutorCard/TutorVBox/Portrait/PortraitText");
        _tutorName = GetNode<Label>(
            "SafeArea/Layout/Content/TutorCard/TutorVBox/TutorName");
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
            DemoPhase.BashRound2Intro => "BASH / INITIATIVE SHIFT",
            DemoPhase.BashRetryBriefing => "BASH / RECALIBRATION",
            DemoPhase.RuleTransition => "RULE / LIMIT BASH",
            DemoPhase.LimitGameBriefing => "LIMIT BASH / NEW LATTICE",
            DemoPhase.LimitRestartBriefing => "LIMIT BASH / STATE RECOVERY",
            _ => phase.ToString().ToUpperInvariant()
        };
        _saveLabel.Text = $"SAVE · STABLE   {lineIndex + 1:00}/{totalLines:00}";
        SetRedEye(active: false);
        BeginTyping(line.Text);
        _supplementaryText.Visible = false;
    }

    public void ShowSummary(
        DialogueLine line,
        int lineIndex,
        int totalLines,
        SessionStats stats,
        bool redEye)
    {
        _phaseLabel.Text = "SUMMARY";
        _saveLabel.Text = $"SAVE · STABLE   {lineIndex + 1:00}/{totalLines:00}";
        SetRedEye(redEye);
        BeginTyping(line.Text);
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

    private void SetRedEye(bool active)
    {
        if (!active)
        {
            _portrait.RemoveThemeStyleboxOverride("panel");
            _portraitText.RemoveThemeColorOverride("font_color");
            _tutorName.Text = "THE TUTOR · ONLINE";
            return;
        }

        var anomalyStyle = new StyleBoxFlat
        {
            BgColor = new Color("fff4f5"),
            BorderColor = new Color("ff314d"),
            BorderWidthLeft = 4,
            BorderWidthTop = 4,
            BorderWidthRight = 4,
            BorderWidthBottom = 4,
            CornerRadiusTopLeft = 90,
            CornerRadiusTopRight = 90,
            CornerRadiusBottomRight = 90,
            CornerRadiusBottomLeft = 90,
            ShadowColor = new Color("b000244d"),
            ShadowSize = 24
        };
        _portrait.AddThemeStyleboxOverride("panel", anomalyStyle);
        _portraitText.AddThemeColorOverride("font_color", new Color("ff1738"));
        _tutorName.Text = "THE TUTOR · SIGNAL ANOMALY";
    }
}
