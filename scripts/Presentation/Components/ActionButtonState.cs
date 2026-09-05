using Godot;

/// <summary>
/// Keeps an editor-authored lock overlay in sync with the native button.
/// Captions, fonts, hit area and overlay geometry remain in the scene.
/// </summary>
public partial class ActionButtonState : Button
{
    private TextureRect _disabledCross = null!;

    public override void _Ready()
    {
        _disabledCross = GetNode<TextureRect>("DisabledCross");
        Synchronize();
    }

    public override void _Process(double delta)
    {
        Synchronize();
    }

    private void Synchronize()
    {
        _disabledCross.Visible = Disabled;
    }
}
