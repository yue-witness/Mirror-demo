using Godot;
using System;

/// <summary>
/// The two mandatory actions in the first playable turn. Hidden is also used
/// after the guided input has been committed.
/// </summary>
public enum ForcedChoiceTutorialStage
{
    Hidden,
    SelectB,
    Confirm
}

/// <summary>
/// Blocks the gameplay HUD while framing one permitted action plus the
/// persistent SAVE &amp; BACK escape. The overlay remains visually transparent
/// and owns hit testing so no covered control can receive an accidental click.
/// </summary>
public partial class ForcedChoiceTutorialOverlay : Control
{
    private const float SpotlightPadding = 12.0f;
    private Vector2 _pointerSize;

    private Button _choiceB = null!;
    private Button _confirmButton = null!;
    private Button _backButton = null!;
    private Panel _highlightFrame = null!;
    private TextureRect _pointer = null!;
    private Control _focusTarget = null!;
    private ForcedChoiceTutorialStage _stage;
    private Tween? _pointerTween;
    private Tween? _rejectTween;

    public event Action? FocusActionRequested;

    public event Action? SaveAndBackRequested;

    public ForcedChoiceTutorialStage Stage => _stage;

    public override void _Ready()
    {
        _choiceB = GetNode<Button>(
            "../SafeArea/Layout/Content/Center/ActionRow/ChoiceStack/ChoiceRow/Choice2");
        _confirmButton = GetNode<Button>(
            "../SafeArea/Layout/Content/Center/ActionRow/ConfirmButton");
        _backButton = GetNode<Button>(
            "../SafeArea/Layout/Content/RightColumn/BackButton");
        _highlightFrame = GetNode<Panel>("HighlightFrame");
        _pointer = GetNode<TextureRect>("Pointer");
        _pointerSize = _pointer.Size;

        Resized += RefreshLayout;
        VisibilityChanged += HandleVisibilityChanged;
        HideStage();
    }

    public override void _GuiInput(InputEvent @event)
    {
        if (_stage == ForcedChoiceTutorialStage.Hidden
            || @event is not InputEventMouseButton mouse
            || mouse.ButtonIndex != MouseButton.Left
            || !mouse.Pressed)
        {
            return;
        }

        Vector2 globalPosition = mouse.GlobalPosition;
        if (ExpandedGlobalRect(_backButton).HasPoint(globalPosition))
        {
            AcceptEvent();
            SaveAndBackRequested?.Invoke();
            return;
        }

        if (ExpandedGlobalRect(_focusTarget).HasPoint(globalPosition))
        {
            AcceptEvent();
            FocusActionRequested?.Invoke();
            return;
        }

        AcceptEvent();
        PlayRejectedInputCue();
    }

    public void ShowStage(ForcedChoiceTutorialStage stage)
    {
        if (stage == ForcedChoiceTutorialStage.Hidden)
        {
            HideStage();
            return;
        }

        _stage = stage;
        _focusTarget = stage == ForcedChoiceTutorialStage.SelectB
            ? _choiceB
            : _confirmButton;
        _highlightFrame.Visible = stage == ForcedChoiceTutorialStage.SelectB;
        Visible = true;
        GrabFocus();
        CallDeferred(nameof(RefreshLayout));
        StartPointerPulse();
    }

    public void HideStage()
    {
        _stage = ForcedChoiceTutorialStage.Hidden;
        _highlightFrame.Visible = false;
        Visible = false;
        StopTweens();
    }

    private void RefreshLayout()
    {
        if (!Visible
            || _stage == ForcedChoiceTutorialStage.Hidden
            || Size.X <= 0.0f
            || Size.Y <= 0.0f)
        {
            return;
        }

        Rect2 overlayGlobal = GetGlobalRect();
        Rect2 focusRect = ToLocalRect(
            ExpandedGlobalRect(_focusTarget),
            overlayGlobal.Position);

        _highlightFrame.Position = focusRect.Position;
        _highlightFrame.Size = focusRect.Size;

        Vector2 pointerPosition = new(
            focusRect.GetCenter().X - _pointerSize.X / 2.0f,
            Math.Max(86.0f, focusRect.Position.Y - _pointerSize.Y + 8.0f));
        _pointer.Position = pointerPosition;
        _pointer.Size = _pointerSize;
        _pointer.PivotOffset = _pointerSize / 2.0f;
    }

    private Rect2 ExpandedGlobalRect(Control control)
    {
        return control.GetGlobalRect().Grow(SpotlightPadding);
    }

    private static Rect2 ToLocalRect(Rect2 globalRect, Vector2 overlayOrigin)
    {
        return new Rect2(globalRect.Position - overlayOrigin, globalRect.Size);
    }

    private void StartPointerPulse()
    {
        if (_pointerTween is not null
            && GodotObject.IsInstanceValid(_pointerTween))
        {
            _pointerTween.Kill();
        }

        _pointer.Scale = Vector2.One;
        _pointer.Modulate = Colors.White;
        _pointerTween = CreateTween().SetLoops();
        _pointerTween.TweenProperty(
                _pointer,
                "scale",
                new Vector2(1.08f, 1.08f),
                0.46)
            .SetTrans(Tween.TransitionType.Sine)
            .SetEase(Tween.EaseType.InOut);
        _pointerTween.Parallel().TweenProperty(
                _pointer,
                "modulate:a",
                0.72f,
                0.46)
            .SetTrans(Tween.TransitionType.Sine)
            .SetEase(Tween.EaseType.InOut);
        _pointerTween.TweenProperty(
                _pointer,
                "scale",
                Vector2.One,
                0.46)
            .SetTrans(Tween.TransitionType.Sine)
            .SetEase(Tween.EaseType.InOut);
        _pointerTween.Parallel().TweenProperty(
                _pointer,
                "modulate:a",
                1.0f,
                0.46)
            .SetTrans(Tween.TransitionType.Sine)
            .SetEase(Tween.EaseType.InOut);
    }

    private void PlayRejectedInputCue()
    {
        if (_rejectTween is not null
            && GodotObject.IsInstanceValid(_rejectTween))
        {
            _rejectTween.Kill();
        }

        CanvasItem rejectedTarget = _highlightFrame.Visible
            ? _highlightFrame
            : _pointer;
        rejectedTarget.Modulate = Colors.White;
        _rejectTween = CreateTween();
        _rejectTween.TweenProperty(
                rejectedTarget,
                "modulate",
                new Color(1.0f, 0.32f, 0.38f, 1.0f),
                0.08)
            .SetTrans(Tween.TransitionType.Sine);
        _rejectTween.TweenProperty(
                rejectedTarget,
                "modulate",
                Colors.White,
                0.18)
            .SetTrans(Tween.TransitionType.Sine);
    }

    private void HandleVisibilityChanged()
    {
        if (Visible)
        {
            CallDeferred(nameof(RefreshLayout));
        }
    }

    private void StopTweens()
    {
        if (_pointerTween is not null
            && GodotObject.IsInstanceValid(_pointerTween))
        {
            _pointerTween.Kill();
        }

        if (_rejectTween is not null
            && GodotObject.IsInstanceValid(_rejectTween))
        {
            _rejectTween.Kill();
        }

        _pointerTween = null;
        _rejectTween = null;
    }
}
