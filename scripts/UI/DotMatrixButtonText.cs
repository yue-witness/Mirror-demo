using Godot;

/// <summary>
/// Draws a button's caption through the phosphor shader while leaving the
/// button panel itself crisp. Gameplay action buttons also receive a large,
/// shader-driven X when they cannot be selected.
/// </summary>
public partial class DotMatrixButtonText : Label
{
    private static readonly StringName FontColor = "font_color";
    private static readonly StringName FontHoverColor = "font_hover_color";
    private static readonly StringName FontPressedColor = "font_pressed_color";
    private static readonly StringName FontHoverPressedColor =
        "font_hover_pressed_color";
    private static readonly StringName FontFocusColor = "font_focus_color";
    private static readonly StringName FontDisabledColor = "font_disabled_color";

    private Button _button = null!;
    private Label? _disabledCross;
    private Color _normalColor;
    private Color _hoverColor;
    private Color _pressedColor;
    private Color _hoverPressedColor;
    private Color _disabledColor;

    public void Configure(
        Button button,
        ShaderMaterial textMaterial,
        bool showDisabledCross)
    {
        _button = button;
        Name = "DotMatrixText";
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;
        HorizontalAlignment = button.Alignment;
        VerticalAlignment = VerticalAlignment.Center;
        Material = (ShaderMaterial)textMaterial.Duplicate();

        StyleBox normalStyle = button.GetThemeStylebox("normal");
        OffsetLeft = normalStyle.GetContentMargin(Side.Left);
        OffsetTop = normalStyle.GetContentMargin(Side.Top);
        OffsetRight = -normalStyle.GetContentMargin(Side.Right);
        OffsetBottom = -normalStyle.GetContentMargin(Side.Bottom);

        _normalColor = button.GetThemeColor(FontColor);
        _hoverColor = button.GetThemeColor(FontHoverColor);
        _pressedColor = button.GetThemeColor(FontPressedColor);
        _hoverPressedColor = button.GetThemeColor(FontHoverPressedColor);
        _disabledColor = button.GetThemeColor(FontDisabledColor);

        int fontSize = button.GetThemeFontSize("font_size");
        AddThemeFontSizeOverride("font_size", fontSize);
        AddThemeConstantOverride("outline_size", 2);
        AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, 0.82f));

        if (showDisabledCross)
        {
            button.ClipContents = true;
            _disabledCross = new Label
            {
                Name = "DisabledCross",
                Text = "X",
                MouseFilter = MouseFilterEnum.Ignore,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Material = (ShaderMaterial)textMaterial.Duplicate()
            };
            bool isCircularConfirm = button.Name == "ConfirmButton";
            _disabledCross.AddThemeFontSizeOverride(
                "font_size",
                isCircularConfirm ? 184 : 132);
            _disabledCross.AddThemeConstantOverride("outline_size", 3);
            _disabledCross.AddThemeColorOverride(
                "font_outline_color",
                new Color(0, 0, 0, 0.92f));
            button.AddChild(_disabledCross);
            _disabledCross.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
            _disabledCross.OffsetTop = -30.0f;
            _disabledCross.OffsetBottom = -30.0f;
            _disabledCross.PivotOffset = _disabledCross.Size / 2.0f;
            _disabledCross.Scale = isCircularConfirm
                ? new Vector2(1.42f, 1.65f)
                : new Vector2(3.8f, 1.7f);
        }

        Color transparent = new(0, 0, 0, 0);
        button.AddThemeColorOverride(FontColor, transparent);
        button.AddThemeColorOverride(FontHoverColor, transparent);
        button.AddThemeColorOverride(FontPressedColor, transparent);
        button.AddThemeColorOverride(FontHoverPressedColor, transparent);
        button.AddThemeColorOverride(FontFocusColor, transparent);
        button.AddThemeColorOverride(FontDisabledColor, transparent);

        SynchronizePresentation();
    }

    public override void _Process(double delta)
    {
        SynchronizePresentation();
    }

    private void SynchronizePresentation()
    {
        if (!GodotObject.IsInstanceValid(_button))
        {
            return;
        }

        Text = _button.Disabled ? string.Empty : _button.Text;
        Visible = _button.Visible;
        HorizontalAlignment = _button.Alignment;

        Color color = _button.Disabled
            ? _disabledColor
            : _button.ButtonPressed && _button.IsHovered()
                ? _hoverPressedColor
                : _button.ButtonPressed
                    ? _pressedColor
                    : _button.IsHovered()
                        ? _hoverColor
                        : _normalColor;
        AddThemeColorOverride(FontColor, color);

        if (_disabledCross is not null)
        {
            bool isCircularConfirm = _button.Name == "ConfirmButton";
            _disabledCross.PivotOffset = _disabledCross.Size / 2.0f;
            _disabledCross.Scale = isCircularConfirm
                ? new Vector2(1.42f, 1.65f)
                : new Vector2(3.8f, 1.7f);
            _disabledCross.Visible = _button.Visible && _button.Disabled;
            _disabledCross.AddThemeColorOverride(FontColor, _normalColor);
        }
    }
}
