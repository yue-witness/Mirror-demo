using Godot;

/// <summary>
/// Applies the shared phosphor dot-matrix treatment to text, button captions,
/// disabled-action crosses, and the short fluorescent divider lines.
/// </summary>
public partial class DotMatrixTextStyler : Node
{
    private const string TextMaterialPath =
        "res://themes/DotMatrixTextMaterial.tres";
    private const string LineMaterialPath =
        "res://themes/DotMatrixLineMaterial.tres";

    public override void _Ready()
    {
        ShaderMaterial? textTemplate = GD.Load<ShaderMaterial>(TextMaterialPath);
        ShaderMaterial? lineTemplate = GD.Load<ShaderMaterial>(LineMaterialPath);

        if (textTemplate is null || lineTemplate is null)
        {
            GD.PushWarning("One or more dot-matrix materials could not be loaded.");
            return;
        }

        ApplyRecursively(GetTree().CurrentScene, textTemplate, lineTemplate);
    }

    private static void ApplyRecursively(
        Node node,
        ShaderMaterial textTemplate,
        ShaderMaterial lineTemplate)
    {
        if (node is Button button)
        {
            AddButtonTextLayer(button, textTemplate);
            return;
        }

        if (node is Label or RichTextLabel)
        {
            ((CanvasItem)node).Material =
                (ShaderMaterial)textTemplate.Duplicate();
        }
        else if (node is ColorRect colorRect
            && colorRect.Name.ToString() is "Rule" or "LaserRule")
        {
            colorRect.Material = (ShaderMaterial)lineTemplate.Duplicate();
        }

        foreach (Node child in node.GetChildren())
        {
            ApplyRecursively(child, textTemplate, lineTemplate);
        }
    }

    private static void AddButtonTextLayer(
        Button button,
        ShaderMaterial textTemplate)
    {
        if (button.HasNode("DotMatrixText"))
        {
            return;
        }

        bool isGameplayAction = button.GetPath().ToString().Contains("ActionRow");
        var textLayer = new DotMatrixButtonText();
        button.AddChild(textLayer);
        textLayer.Configure(button, textTemplate, isGameplayAction);
    }
}
