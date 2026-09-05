using Godot;
using System;

/// <summary>
/// Title-screen presentation and input only. Routing remains in the flow
/// controller.
/// </summary>
public partial class TitleScreen : Control
{
    private Button _newGameButton = null!;
    private Button _continueButton = null!;
    private Button _quitButton = null!;
    private Label _saveStatus = null!;
    private Label _warningLabel = null!;
    private Control _overwriteOverlay = null!;
    private Button _overwriteConfirm = null!;
    private Button _overwriteCancel = null!;
    private bool _hasActiveSave;
    private bool _routingLocked;

    [Export]
    public bool ReducedMotion { get; set; }

    public event Action? NewGameRequested;

    public event Action? ContinueRequested;

    public event Action? QuitRequested;

    public override void _Ready()
    {
        _newGameButton = GetNode<Button>("MenuGlass/MenuVBox/NewGameButton");
        _continueButton = GetNode<Button>("MenuGlass/MenuVBox/ContinueButton");
        _quitButton = GetNode<Button>("MenuGlass/MenuVBox/QuitButton");
        _saveStatus = GetNode<Label>("MenuGlass/MenuVBox/SaveStatus");
        _warningLabel = GetNode<Label>("MenuGlass/MenuVBox/WarningLabel");
        _overwriteOverlay = GetNode<Control>("OverwriteOverlay");
        _overwriteConfirm = GetNode<Button>(
            "OverwriteOverlay/Center/ConfirmGlass/ConfirmVBox/ConfirmButton");
        _overwriteCancel = GetNode<Button>(
            "OverwriteOverlay/Center/ConfirmGlass/ConfirmVBox/CancelButton");

        _newGameButton.Pressed += OnNewGamePressed;
        _continueButton.Pressed += () => AnimateRoute(
            _continueButton,
            () => ContinueRequested?.Invoke());
        _quitButton.Pressed += () => AnimateRoute(
            _quitButton,
            () => QuitRequested?.Invoke());
        _overwriteConfirm.Pressed += () =>
        {
            _overwriteOverlay.Visible = false;
            AnimateRoute(_newGameButton, () => NewGameRequested?.Invoke());
        };
        _overwriteCancel.Pressed += () =>
        {
            _overwriteOverlay.Visible = false;
            _newGameButton.GrabFocus();
        };

        foreach (Button button in new[]
        {
            _newGameButton,
            _continueButton,
            _quitButton,
            _overwriteConfirm,
            _overwriteCancel
        })
        {
            ConnectFocusAnimation(button);
        }
    }

    public void Configure(SaveSlotInfo? activeSave, string? warning)
    {
        _hasActiveSave = activeSave is not null;
        _continueButton.Visible = _hasActiveSave;
        _warningLabel.Visible = !string.IsNullOrWhiteSpace(warning);
        _warningLabel.Text = warning ?? string.Empty;

        if (activeSave is null)
        {
            _saveStatus.Text = "NO ACTIVE SAVE";
        }
        else
        {
            DateTimeOffset updated = DateTimeOffset
                .FromUnixTimeMilliseconds(activeSave.UpdatedUnixMs)
                .ToLocalTime();
            _saveStatus.Text =
                $"SAVED · {FormatPhase(activeSave.Phase)} · {updated:MM-dd HH:mm}";
        }

        SetRoutingLocked(false);
        Callable.From(ApplyDefaultFocus).CallDeferred();
    }

    public void SetLoading(bool loading)
    {
        SetRoutingLocked(loading);
        _saveStatus.Text = loading
            ? "VALIDATING SAVE..."
            : _saveStatus.Text;
    }

    private void OnNewGamePressed()
    {
        if (_routingLocked)
        {
            return;
        }

        if (_hasActiveSave)
        {
            _overwriteOverlay.Visible = true;
            _overwriteCancel.GrabFocus();
            return;
        }

        AnimateRoute(_newGameButton, () => NewGameRequested?.Invoke());
    }

    private async void AnimateRoute(Button button, Action route)
    {
        if (_routingLocked)
        {
            return;
        }

        SetRoutingLocked(true);

        if (!ReducedMotion)
        {
            Tween tween = CreateTween();
            tween.SetTrans(Tween.TransitionType.Quad);
            tween.SetEase(Tween.EaseType.Out);
            tween.TweenProperty(button, "scale", new Vector2(0.98f, 0.98f), 0.09);
            tween.TweenProperty(button, "scale", Vector2.One, 0.09);
            await ToSignal(tween, Tween.SignalName.Finished);
        }

        route();
    }

    private void ConnectFocusAnimation(Button button)
    {
        button.MouseEntered += () => AnimateFocus(button, focused: true);
        button.MouseExited += () => AnimateFocus(button, focused: false);
        button.FocusEntered += () => AnimateFocus(button, focused: true);
        button.FocusExited += () => AnimateFocus(button, focused: false);
        button.Resized += () => button.PivotOffset = button.Size / 2f;
    }

    private void AnimateFocus(Button button, bool focused)
    {
        if (ReducedMotion || button.Disabled)
        {
            button.Scale = Vector2.One;
            return;
        }

        Tween tween = CreateTween();
        tween.SetParallel();
        tween.SetTrans(Tween.TransitionType.Quad);
        tween.SetEase(Tween.EaseType.Out);
        tween.TweenProperty(
            button,
            "scale",
            focused ? new Vector2(1.02f, 1.02f) : Vector2.One,
            0.18);
        tween.TweenProperty(
            button,
            "modulate",
            focused ? new Color(1.08f, 1.08f, 1.08f, 1f) : Colors.White,
            0.18);
    }

    private void SetRoutingLocked(bool locked)
    {
        _routingLocked = locked;
        _newGameButton.Disabled = locked;
        _continueButton.Disabled = locked;
        _quitButton.Disabled = locked;
    }

    private void ApplyDefaultFocus()
    {
        if (_hasActiveSave && _continueButton.Visible)
        {
            _continueButton.GrabFocus();
        }
        else
        {
            _newGameButton.GrabFocus();
        }
    }

    private static string FormatPhase(DemoPhase phase)
    {
        return phase switch
        {
            DemoPhase.Background => "BACKGROUND",
            DemoPhase.BashTutorial => "BASH TUTORIAL",
            DemoPhase.BashGame1Round1 => "BASH ROUND 1",
            DemoPhase.BashGame1Round2 => "BASH ROUND 2",
            DemoPhase.RuleTransition => "RULE TRANSITION",
            DemoPhase.LimitBash => "Limit Bash",
            DemoPhase.RoundResult => "RESULT",
            DemoPhase.Summary => "SUMMARY",
            _ => phase.ToString()
        };
    }
}
