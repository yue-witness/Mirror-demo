using Godot;
using System;
using System.Collections.Generic;

/// <summary>
/// Adds the particle-frame shader to every existing bordered panel and button.
/// This keeps the effect resolution independent and avoids maintaining a large
/// library of pre-rendered frame textures for different control sizes.
/// </summary>
public partial class FrameParticleEffectsInstaller : Node
{
    private const string ShaderPath = "res://shaders/ui_particle_frame.gdshader";
    private const float FrameMargin = 16.0f;
    private const float TrailLength = 0.22f;
    private const float TrailDiffusion = 3.0f;
    private const float ParticleWidthScale = 1.65f;
    private const float StaticMatrixAlpha = 0.18f;
    private Shader _frameShader = null!;
    private readonly List<FrameBinding> _bindings = new();
    private int _installedFrameCount;

    private sealed class FrameBinding
    {
        public required Control Target { get; init; }
        public required ColorRect Overlay { get; init; }
        public required ShaderMaterial Material { get; init; }
        public required float BorderWidth { get; init; }
        public required float CornerRadius { get; init; }
    }

    public int InstalledFrameCount => _installedFrameCount;

    public override void _Ready()
    {
        _frameShader = GD.Load<Shader>(ShaderPath);
        Callable.From(InstallDeferred).CallDeferred();
    }

    public override void _Process(double delta)
    {
        foreach (FrameBinding binding in _bindings)
        {
            UpdateFrameGeometry(binding);
        }
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
        overlay.TopLevel = true;

        var material = new ShaderMaterial
        {
            Shader = _frameShader
        };
        float phase = Mathf.PosMod(_installedFrameCount * 0.173f, 1.0f);
        material.SetShaderParameter("phase_offset", phase);
        Color matrixColor = borderStyle.BorderColor;
        matrixColor.A = Math.Min(matrixColor.A, StaticMatrixAlpha);
        Color particleColor = matrixColor.Lerp(
            new Color(0.64f, 1.0f, 0.68f, 1.0f),
            0.48f);
        particleColor.A = 1.0f;
        material.SetShaderParameter("matrix_color", matrixColor);
        material.SetShaderParameter("particle_color", particleColor);
        material.SetShaderParameter("trail_length", TrailLength);
        material.SetShaderParameter("diffusion", TrailDiffusion);
        material.SetShaderParameter("particle_width_scale", ParticleWidthScale);
        overlay.Material = material;

        var binding = new FrameBinding
        {
            Target = target,
            Overlay = overlay,
            Material = material,
            BorderWidth = GetMaximumBorderWidth(borderStyle),
            CornerRadius = GetMaximumCornerRadius(borderStyle)
        };
        _bindings.Add(binding);
        UpdateFrameGeometry(binding);
        _installedFrameCount++;
    }

    private static void UpdateFrameGeometry(FrameBinding binding)
    {
        Control target = binding.Target;
        ColorRect overlay = binding.Overlay;
        ShaderMaterial material = binding.Material;
        overlay.Visible = target.IsVisibleInTree();
        if (!overlay.Visible)
        {
            return;
        }

        overlay.GlobalPosition = target.GlobalPosition
            - new Vector2(FrameMargin, FrameMargin);
        overlay.Size = target.Size
            + new Vector2(FrameMargin * 2.0f, FrameMargin * 2.0f);

        float aspect = overlay.Size.Y <= 0.0f
            ? 1.0f
            : overlay.Size.X / overlay.Size.Y;
        material.SetShaderParameter("aspect_ratio", aspect);
        if (overlay.Size.X <= 0.0f || overlay.Size.Y <= 0.0f)
        {
            return;
        }

        float borderCenterInset = FrameMargin + binding.BorderWidth * 0.5f;
        material.SetShaderParameter("border_inset_uv", new Vector2(
            borderCenterInset / overlay.Size.X,
            borderCenterInset / overlay.Size.Y));
        material.SetShaderParameter(
            "pixel_size_y",
            2.0f / overlay.Size.Y);
        material.SetShaderParameter(
            "corner_radius",
            binding.CornerRadius * 2.0f / overlay.Size.Y);
    }

    private static float GetMaximumBorderWidth(StyleBoxFlat style)
    {
        return Math.Max(
            Math.Max(style.BorderWidthLeft, style.BorderWidthRight),
            Math.Max(style.BorderWidthTop, style.BorderWidthBottom));
    }

    private static float GetMaximumCornerRadius(StyleBoxFlat style)
    {
        return Math.Max(
            Math.Max(style.CornerRadiusTopLeft, style.CornerRadiusTopRight),
            Math.Max(style.CornerRadiusBottomLeft, style.CornerRadiusBottomRight));
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
