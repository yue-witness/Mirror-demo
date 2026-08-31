using Godot;

/// <summary>
/// Applies the shared phosphor dot-matrix shader to text-only canvas items.
/// Buttons are intentionally excluded so their panel and hover drawing remains
/// crisp; their text still inherits the fluorescent theme color.
/// </summary>
public partial class DotMatrixTextStyler : Node
{
    private const string MaterialPath =
        "res://themes/DotMatrixTextMaterial.tres";

    public override void _Ready()
    {
        ShaderMaterial? template = GD.Load<ShaderMaterial>(MaterialPath);

        if (template is null)
        {
            GD.PushWarning($"Dot-matrix text material was not found: {MaterialPath}");
            return;
        }

        ApplyRecursively(GetTree().CurrentScene, template);
    }

    private static void ApplyRecursively(
        Node node,
        ShaderMaterial template)
    {
        if (node is Label or RichTextLabel)
        {
            ((CanvasItem)node).Material =
                (ShaderMaterial)template.Duplicate();
        }

        foreach (Node child in node.GetChildren())
        {
            ApplyRecursively(child, template);
        }
    }
}
