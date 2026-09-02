using Godot;
using System;

/// <summary>
/// Draws the Tutor portrait ring as a high-resolution antialiased arc.
/// StyleBoxFlat clamps rounded-corner detail, which becomes visibly faceted
/// at the large dialogue portrait size.
/// </summary>
public partial class SmoothPortraitFrame : PanelContainer
{
    [Export(PropertyHint.Range, "64,512,16")]
    public int SegmentCount { get; set; } = 256;

    [Export(PropertyHint.Range, "1,8,0.5")]
    public float RingWidth { get; set; } = 3.0f;

    [Export]
    public Color RingColor { get; set; } = new("38ff3b");

    [Export(PropertyHint.Range, "0,16,0.5")]
    public float GlowWidth { get; set; } = 5.0f;

    public override void _Ready()
    {
        Resized += QueueRedraw;
        QueueRedraw();
    }

    public override void _Draw()
    {
        float inset = Math.Max(RingWidth, GlowWidth) * 0.5f + 1.5f;
        float radius = Math.Max(1.0f, Math.Min(Size.X, Size.Y) * 0.5f - inset);
        Vector2 center = Size * 0.5f;
        int points = Math.Max(64, SegmentCount);

        if (GlowWidth > RingWidth)
        {
            Color glow = RingColor;
            glow.A *= 0.22f;
            DrawArc(
                center,
                radius,
                0.0f,
                Mathf.Tau,
                points,
                glow,
                GlowWidth,
                antialiased: true);
        }

        DrawArc(
            center,
            radius,
            0.0f,
            Mathf.Tau,
            points,
            RingColor,
            RingWidth,
            antialiased: true);
    }

    public int GetRingSegmentCount()
    {
        return Math.Max(64, SegmentCount);
    }
}
