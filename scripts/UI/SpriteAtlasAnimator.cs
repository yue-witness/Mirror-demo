using Godot;
using System;

/// <summary>
/// Animates a regular sprite atlas inside a TextureRect while keeping the
/// character in the existing Control-based UI layout.
/// </summary>
public partial class SpriteAtlasAnimator : TextureRect
{
    [Export(PropertyHint.Range, "1,16,1")]
    public int Columns { get; set; } = 4;

    [Export(PropertyHint.Range, "1,16,1")]
    public int Rows { get; set; } = 1;

    [Export(PropertyHint.Range, "0,15,1")]
    public int StateRow { get; set; }

    [Export(PropertyHint.Range, "0.1,24,0.1")]
    public float FramesPerSecond { get; set; } = 4.0f;

    [Export(PropertyHint.Range, "0,24,0.5")]
    public float HoverAmplitude { get; set; } = 4.0f;

    [Export(PropertyHint.Range, "0.05,2,0.05")]
    public float HoverCyclesPerSecond { get; set; } = 0.35f;

    private Texture2D? _atlas;
    private double _elapsed;
    private int _visibleFrame = -1;
    private Vector2 _layoutPosition;

    public override void _Ready()
    {
        _atlas = Texture;
        NormalizeGridForKnownAtlas();
        _layoutPosition = Position;
        PivotOffset = Size / 2.0f;
        Resized += () => PivotOffset = Size / 2.0f;
        UpdateVisibleFrame(force: true);
    }

    private void NormalizeGridForKnownAtlas()
    {
        if (_atlas is null)
        {
            return;
        }

        Columns = 4;
        Rows = _atlas.ResourcePath.EndsWith(
            "tutor_states.png",
            StringComparison.OrdinalIgnoreCase)
            ? 3
            : 1;
        StateRow = Math.Clamp(StateRow, 0, Rows - 1);
    }

    public override void _Process(double delta)
    {
        if (!Visible || _atlas is null)
        {
            return;
        }

        _elapsed += delta;
        UpdateVisibleFrame(force: false);
        float hover = Mathf.Sin(
            (float)(_elapsed * Math.Tau * HoverCyclesPerSecond));
        Position = _layoutPosition + Vector2.Up * hover * HoverAmplitude;
    }

    public void ConfigureAtlas(
        Texture2D atlas,
        int columns,
        int rows,
        int stateRow = 0)
    {
        _atlas = atlas;
        Columns = Math.Max(1, columns);
        Rows = Math.Max(1, rows);
        SetState(stateRow);
    }

    public void SetState(int stateRow)
    {
        StateRow = Math.Clamp(stateRow, 0, Math.Max(0, Rows - 1));
        _elapsed = 0.0;
        _visibleFrame = -1;
        UpdateVisibleFrame(force: true);
    }

    private void UpdateVisibleFrame(bool force)
    {
        if (_atlas is null)
        {
            return;
        }

        int frame = (int)Math.Floor(_elapsed * FramesPerSecond) % Columns;

        if (!force && frame == _visibleFrame)
        {
            return;
        }

        _visibleFrame = frame;
        float frameWidth = _atlas.GetWidth() / (float)Columns;
        float frameHeight = _atlas.GetHeight() / (float)Rows;
        Texture = new AtlasTexture
        {
            Atlas = _atlas,
            Region = new Rect2(
                frame * frameWidth,
                StateRow * frameHeight,
                frameWidth,
                frameHeight)
        };
    }
}
