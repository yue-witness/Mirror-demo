using Godot;
using System;

/// <summary>
/// Adds the particle-frame shader to every existing bordered panel and button.
/// This keeps the effect resolution independent and avoids maintaining a large
/// library of pre-rendered frame textures for different control sizes.
/// </summary>
public partial class FrameParticleEffectsInstaller : Node
{
    private const string ShaderPath = "res://shaders/ui_particle_frame.gdshader";
    private const float TrailLength = 0.14f;
    private const float TrailDiffusion = 1.8f;
    private Shader _frameShader = null!;
    private int _installedFrameCount;

    public int InstalledFrameCount => _installedFrameCount;

    public override void _Ready()
    {
        _frameShader = GD.Load<Shader>(ShaderPath);
        Callable.From(InstallDeferred).CallDeferred();
    }

    private void InstallDeferred()
    {
        Node root = GetParent();
        InstallRecursively(root);
    }

    private void InstallRecursively(Node node)
    {
        foreach (Node child in node.GetChildren())
        {
            if (child is Control control
                && GetVisibleBorderStyle(control) is StyleBoxFlat borderStyle)
            {
                InstallFrame(control, borderStyle);
            }

            InstallRecursively(child);
        }
    }

    private void InstallFrame(Control target, StyleBoxFlat borderStyle)
    {
        if (target.HasNode("ParticleFrame"))
        {
            return;
        }

        var overlay = new ColorRect
        {
            Name = "ParticleFrame",
            Color = Colors.White,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            FocusMode = Control.FocusModeEnum.None,
            ZIndex = 120
        };
        target.AddChild(overlay);
        overlay.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        overlay.OffsetLeft = -8.0f;
        overlay.OffsetTop = -8.0f;
        overlay.OffsetRight = 8.0f;
        overlay.OffsetBottom = 8.0f;

        var material = new ShaderMaterial
        {
            Shader = _frameShader
        };
        float phase = Mathf.PosMod(_installedFrameCount * 0.173f, 1.0f);
        material.SetShaderParameter("phase_offset", phase);
        Color matrixColor = borderStyle.BorderColor;
        matrixColor.A = Math.Min(matrixColor.A, 0.62f);
        Color particleColor = matrixColor.Lerp(Colors.White, 0.68f);
        particleColor.A = 1.0f;
        material.SetShaderParameter("matrix_color", matrixColor);
        material.SetShaderParameter("particle_color", particleColor);
        material.SetShaderParameter("trail_length", TrailLength);
        material.SetShaderParameter("diffusion", TrailDiffusion);
        overlay.Material = material;

        UpdateAspectRatio(overlay, material);
        overlay.Resized += () => UpdateAspectRatio(overlay, material);
        _installedFrameCount++;
    }

    private static void UpdateAspectRatio(
        Control overlay,
        ShaderMaterial material)
    {
        float aspect = overlay.Size.Y <= 0.0f
            ? 1.0f
            : overlay.Size.X / overlay.Size.Y;
        material.SetShaderParameter("aspect_ratio", aspect);
    }

    private static StyleBoxFlat? GetVisibleBorderStyle(Control control)
    {
        StyleBox? style = control switch
        {
            PanelContainer panel => panel.GetThemeStylebox("panel"),
            Panel panel => panel.GetThemeStylebox("panel"),
            Button button => button.GetThemeStylebox("normal"),
            _ => null
        };

        bool visible = style is StyleBoxFlat flat
            && Math.Max(
                Math.Max(flat.BorderWidthLeft, flat.BorderWidthRight),
                Math.Max(flat.BorderWidthTop, flat.BorderWidthBottom)) > 0
            && flat.BorderColor.A > 0.01f;
        return visible ? (StyleBoxFlat)style! : null;
    }
}
