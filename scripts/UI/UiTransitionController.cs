using Godot;
using System;

/// <summary>
/// Masks primary-screen cuts with a short phosphor veil and scanning line.
/// The flow controller still changes state synchronously, preserving save and
/// test semantics while the next screen is revealed smoothly.
/// </summary>
public partial class UiTransitionController : Control
{
    [Export(PropertyHint.Range, "0.1,1.0,0.01")]
    public float DurationSeconds { get; set; } = 0.42f;

    private ColorRect _veil = null!;
    private ColorRect _scanLine = null!;
    private UiAudioController _audio = null!;
    private double _elapsed;
    private bool _running;

    public override void _Ready()
    {
        _veil = GetNode<ColorRect>("Veil");
        _scanLine = GetNode<ColorRect>("ScanLine");
        _audio = GetNode<UiAudioController>("../UiAudioController");
        FinishTransition();
    }

    public override void _Process(double delta)
    {
        if (!_running)
        {
            return;
        }

        _elapsed += delta;
        float progress = Mathf.Clamp(
            (float)(_elapsed / Math.Max(0.01f, DurationSeconds)),
            0.0f,
            1.0f);
        float eased = 1.0f - Mathf.Pow(1.0f - progress, 3.0f);

        _veil.Modulate = new Color(1.0f, 1.0f, 1.0f, 1.0f - eased);
        _scanLine.Modulate = new Color(
            1.0f,
            1.0f,
            1.0f,
            Mathf.Sin(progress * Mathf.Pi));
        _scanLine.Position = new Vector2(
            0.0f,
            Mathf.Lerp(-12.0f, Size.Y + 12.0f, eased));

        if (progress >= 1.0f)
        {
            FinishTransition();
        }
    }

    public void Play()
    {
        _elapsed = 0.0;
        _running = true;
        Visible = true;
        MouseFilter = MouseFilterEnum.Stop;
        _veil.Modulate = Colors.White;
        _scanLine.Modulate = new Color(1.0f, 1.0f, 1.0f, 0.0f);
        _scanLine.Position = new Vector2(0.0f, -12.0f);
        _audio.PlayTransition();
    }

    private void FinishTransition()
    {
        _running = false;
        Visible = false;
        MouseFilter = MouseFilterEnum.Ignore;
    }
}
