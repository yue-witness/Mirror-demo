using Godot;
using System;
using System.Collections.Generic;

/// <summary>
/// Animates a regular sprite atlas inside a TextureRect while keeping the
/// character in the existing Control-based UI layout.
/// </summary>
public partial class SpriteAtlasAnimator : TextureRect
{
    private const float VisibleAlphaThreshold = 0.06f;

    private static readonly Dictionary<string, Vector2[]> VisibleCenterCache = new();

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
    private TextureRect _frameDisplay = null!;
    private Vector2[] _visibleCenterOffsets = Array.Empty<Vector2>();
    private double _elapsed;
    private int _visibleFrame = -1;
    private bool _redEyeAnomalyRequested;
    private ShaderMaterial? _redEyeMaterial;

    public bool RedEyeAnomalyActive => _redEyeAnomalyRequested;

    public override void _Ready()
    {
        _atlas = Texture;
        Texture = null;
        CreateFrameDisplay();
        NormalizeGridForKnownAtlas();
        RefreshVisibleCenterOffsets();
        PivotOffset = Size / 2.0f;
        Resized += RefreshLayout;
        Scale = Vector2.One;
        UpdateVisibleFrame(force: true);
        ApplyRedEyeMaterial();
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
        float pulse = Mathf.Sin(
            (float)(_elapsed * Math.Tau * HoverCyclesPerSecond));
        float scale = 1.0f + pulse * HoverAmplitude * 0.0015f;
        Scale = Vector2.One * scale;
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
        RefreshVisibleCenterOffsets();
        SetState(stateRow);
    }

    /// <summary>
    /// Enables the final Tutor anomaly without tinting the portrait body or
    /// its surrounding circular frame. The material affects only the two eye
    /// regions and pulses their existing luminous pixels toward red.
    /// </summary>
    public void SetRedEyeAnomaly(bool enabled)
    {
        _redEyeAnomalyRequested = enabled;
        ApplyRedEyeMaterial();
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
                frameHeight),
            FilterClip = true
        };

        _frameDisplay.Texture = Texture;
        ApplyVisibleContentCenter(frame, frameWidth, frameHeight);
        UpdateRedEyeFrameUv(frame);
    }

    private void CreateFrameDisplay()
    {
        _frameDisplay = new TextureRect
        {
            Name = "FrameDisplay",
            MouseFilter = MouseFilterEnum.Ignore,
            ExpandMode = ExpandModeEnum.IgnoreSize,
            StretchMode = StretchModeEnum.KeepAspectCentered
        };
        AddChild(_frameDisplay);
        MoveChild(_frameDisplay, 0);
        RefreshLayout();
    }

    private void RefreshLayout()
    {
        PivotOffset = Size / 2.0f;

        if (!GodotObject.IsInstanceValid(_frameDisplay))
        {
            return;
        }

        _frameDisplay.Size = Size;

        if (_atlas is null || _visibleFrame < 0)
        {
            _frameDisplay.Position = Vector2.Zero;
            return;
        }

        float frameWidth = _atlas.GetWidth() / (float)Columns;
        float frameHeight = _atlas.GetHeight() / (float)Rows;
        ApplyVisibleContentCenter(_visibleFrame, frameWidth, frameHeight);
    }

    private void ApplyVisibleContentCenter(
        int frame,
        float frameWidth,
        float frameHeight)
    {
        int centerIndex = StateRow * Columns + frame;
        if (centerIndex < 0 || centerIndex >= _visibleCenterOffsets.Length)
        {
            _frameDisplay.Position = Vector2.Zero;
            return;
        }

        // KeepAspectCentered uses one uniform scale for a square portrait. The
        // atlas offsets are therefore converted from source pixels with the
        // same scale before moving only the internal display child. The outer
        // Control remains exactly centred in its circular layout frame.
        float displayScale = Math.Min(
            Size.X / Math.Max(1.0f, frameWidth),
            Size.Y / Math.Max(1.0f, frameHeight));
        _frameDisplay.Position = -_visibleCenterOffsets[centerIndex] * displayScale;
    }

    private void RefreshVisibleCenterOffsets()
    {
        if (_atlas is null)
        {
            _visibleCenterOffsets = Array.Empty<Vector2>();
            return;
        }

        string cacheKey = $"{_atlas.ResourcePath}|{Columns}|{Rows}";
        if (!VisibleCenterCache.TryGetValue(cacheKey, out _visibleCenterOffsets!))
        {
            _visibleCenterOffsets = MeasureVisibleCenterOffsets(
                _atlas,
                Columns,
                Rows);
            VisibleCenterCache[cacheKey] = _visibleCenterOffsets;
        }
    }

    private static Vector2[] MeasureVisibleCenterOffsets(
        Texture2D atlas,
        int columns,
        int rows)
    {
        var offsets = new Vector2[columns * rows];
        Image image = atlas.GetImage();
        if (image.IsEmpty())
        {
            return offsets;
        }

        int frameWidth = image.GetWidth() / columns;
        int frameHeight = image.GetHeight() / rows;
        for (int row = 0; row < rows; row++)
        {
            for (int column = 0; column < columns; column++)
            {
                int minX = frameWidth;
                int minY = frameHeight;
                int maxX = -1;
                int maxY = -1;

                for (int y = 0; y < frameHeight; y++)
                {
                    for (int x = 0; x < frameWidth; x++)
                    {
                        Color pixel = image.GetPixel(
                            column * frameWidth + x,
                            row * frameHeight + y);
                        if (pixel.A < VisibleAlphaThreshold)
                        {
                            continue;
                        }

                        minX = Math.Min(minX, x);
                        minY = Math.Min(minY, y);
                        maxX = Math.Max(maxX, x);
                        maxY = Math.Max(maxY, y);
                    }
                }

                if (maxX < minX || maxY < minY)
                {
                    continue;
                }

                Vector2 visibleCenter = new(
                    (minX + maxX + 1) * 0.5f,
                    (minY + maxY + 1) * 0.5f);
                offsets[row * columns + column] = visibleCenter
                    - new Vector2(frameWidth, frameHeight) * 0.5f;
            }
        }

        return offsets;
    }

    private void ApplyRedEyeMaterial()
    {
        if (!GodotObject.IsInstanceValid(_frameDisplay))
        {
            return;
        }

        if (!_redEyeAnomalyRequested)
        {
            _frameDisplay.Material = null;
            return;
        }

        if (_redEyeMaterial is null)
        {
            var shader = ResourceLoader.Load<Shader>(
                "res://shaders/tutor_red_eye.gdshader");
            _redEyeMaterial = new ShaderMaterial
            {
                Shader = shader
            };
        }

        _frameDisplay.Material = _redEyeMaterial;
        UpdateRedEyeFrameUv(Math.Max(0, _visibleFrame));
    }

    private void UpdateRedEyeFrameUv(int frame)
    {
        if (_redEyeMaterial is null)
        {
            return;
        }

        _redEyeMaterial.SetShaderParameter(
            "frame_uv_origin",
            new Vector2(
                frame / (float)Math.Max(1, Columns),
                StateRow / (float)Math.Max(1, Rows)));
        _redEyeMaterial.SetShaderParameter(
            "frame_uv_size",
            new Vector2(
                1.0f / Math.Max(1, Columns),
                1.0f / Math.Max(1, Rows)));
    }
}
