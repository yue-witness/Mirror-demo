using Godot;
using System;

/// <summary>
/// Plays the authored upper-container sequence over the powered-down chamber.
/// The lower scanner remains part of the original static background so its
/// platform alignment cannot drift. Rotation, perspective, and local point
/// flicker for the upper cage are authored into the transparent atlas frames.
/// </summary>
public partial class BackgroundVfx : Control
{
    private const int FrameCount = 30;
    private const int ContainerColumns = 6;
    private const int ContainerRows = 5;
    private const float ContainerFramesPerSecond = 15.0f;

    private TextureRect _containerGlow = null!;
    private Texture2D _containerAtlasSource = null!;
    private AtlasTexture _containerFrame = null!;
    private double _elapsed;
    private int _visibleContainerFrame = -1;

    public override void _Ready()
    {
        _containerGlow = GetNode<TextureRect>("ContainerGlow");
        _containerAtlasSource = _containerGlow.Texture;
        _containerFrame = new AtlasTexture { Atlas = _containerAtlasSource };
        _containerGlow.Texture = _containerFrame;
        _containerGlow.Modulate = new Color(0.84f, 0.97f, 1.0f, 0.97f);
        UpdateFrames(force: true);
    }

    public override void _Process(double delta)
    {
        _elapsed += delta;
        UpdateFrames(force: false);
    }

    private void UpdateFrames(bool force)
    {
        int containerIndex = (int)Math.Floor(
            _elapsed * ContainerFramesPerSecond) % FrameCount;
        if (force || containerIndex != _visibleContainerFrame)
        {
            _visibleContainerFrame = containerIndex;
            SetAtlasRegion(
                _containerFrame,
                _containerAtlasSource,
                ContainerColumns,
                ContainerRows,
                containerIndex);
        }
    }

    private static void SetAtlasRegion(
        AtlasTexture frame,
        Texture2D atlas,
        int columns,
        int rows,
        int frameIndex)
    {
        float frameWidth = atlas.GetWidth() / (float)columns;
        float frameHeight = atlas.GetHeight() / (float)rows;
        int column = frameIndex % columns;
        int row = frameIndex / columns;
        frame.Region = new Rect2(
            column * frameWidth,
            row * frameHeight,
            frameWidth,
            frameHeight);
    }
}
