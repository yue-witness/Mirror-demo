using Godot;
using System;

/// <summary>
/// Animates LLM-extracted glow layers over the powered-down background. Only
/// 2D Control transforms are used; the horizontal squash simulates a turn in
/// depth without introducing a 3D scene.
/// </summary>
public partial class BackgroundVfx : Control
{
    private TextureRect _containerGlow = null!;
    private TextureRect _scannerGlow = null!;
    private double _elapsed;

    public override void _Ready()
    {
        _containerGlow = GetNode<TextureRect>("ContainerGlow");
        _scannerGlow = GetNode<TextureRect>("ScannerGlow");
        ConfigurePivot(_containerGlow);
        ConfigurePivot(_scannerGlow);
        _containerGlow.Resized += () => ConfigurePivot(_containerGlow);
        _scannerGlow.Resized += () => ConfigurePivot(_scannerGlow);
    }

    public override void _Process(double delta)
    {
        _elapsed += delta;
        float time = (float)_elapsed;
        float containerPulse = (Mathf.Sin(time * 1.25f) + 1.0f) * 0.5f;
        float scannerPulse = (Mathf.Sin(time * 1.8f + 0.7f) + 1.0f) * 0.5f;
        float simulatedTurn = (Mathf.Sin(time * 0.62f) + 1.0f) * 0.5f;

        _containerGlow.Rotation = Mathf.Sin(time * 0.37f) * 0.018f;
        _containerGlow.Scale = new Vector2(
            Mathf.Lerp(0.74f, 1.0f, simulatedTurn),
            Mathf.Lerp(0.99f, 1.018f, containerPulse));
        _containerGlow.Modulate = new Color(
            0.82f,
            1.0f,
            0.92f,
            Mathf.Lerp(0.42f, 0.70f, containerPulse));

        _scannerGlow.Rotation = Mathf.Sin(time * 0.48f) * 0.042f;
        _scannerGlow.Scale = Vector2.One
            * Mathf.Lerp(0.99f, 1.018f, scannerPulse);
        _scannerGlow.Modulate = new Color(
            0.78f,
            1.0f,
            1.0f,
            Mathf.Lerp(0.26f, 0.48f, scannerPulse));
    }

    private static void ConfigurePivot(Control control)
    {
        control.PivotOffset = control.Size / 2.0f;
    }
}
