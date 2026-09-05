using Godot;
using System;

/// <summary>
/// Scene-owned border effect. The editor owns its material and placement;
/// only size-dependent shader geometry follows the parent control at runtime.
/// Portrait circles deliberately have no ParticleFrame child.
/// </summary>
[Tool]
public partial class ParticleFrame : ColorRect
{
    [Export]
    public Vector2 FrameMargin { get; set; } = new(16, 16);

    public override void _Ready()
    {
        Resized += UpdateGeometry;
        UpdateGeometry();
    }

    public override void _Process(double delta)
    {
        // Top-level overlays avoid PanelContainer's content sizing. They follow
        // the scene-authored parent rectangle, including layout and transitions.
        UpdateGeometry();
    }

    private void UpdateGeometry()
    {
        if (Material is not ShaderMaterial material
            || GetParent() is not Control target
            || target.Size.X <= 0 || target.Size.Y <= 0)
        {
            return;
        }

        Visible = target.IsVisibleInTree();
        GlobalPosition = target.GlobalPosition - FrameMargin;
        Size = target.Size + FrameMargin * 2.0f;

        StyleBox? style = target switch
        {
            Button button => button.GetThemeStylebox("normal"),
            PanelContainer panel => panel.GetThemeStylebox("panel"),
            Panel panel => panel.GetThemeStylebox("panel"),
            _ => null
        };

        if (style is not StyleBoxFlat border)
        {
            return;
        }

        float width = Math.Max(border.BorderWidthLeft, border.BorderWidthTop);
        float radius = Math.Max(border.CornerRadiusTopLeft, border.CornerRadiusTopRight);
        Vector2 margin = (Size - target.Size) * 0.5f;
        material.SetShaderParameter("aspect_ratio", Size.X / Size.Y);
        material.SetShaderParameter("border_inset_uv", new Vector2(
            (margin.X + width * 0.5f) / Size.X,
            (margin.Y + width * 0.5f) / Size.Y));
        material.SetShaderParameter("pixel_size_y", 2.0f / Size.Y);
        material.SetShaderParameter("corner_radius", radius * 2.0f / Size.Y);
    }
}
