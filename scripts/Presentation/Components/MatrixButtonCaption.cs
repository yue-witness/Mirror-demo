using Godot;

/// <summary>
/// An editor-authored text layer lets the shared dot shader affect only glyphs,
/// preserving the button's coloured background and native input semantics.
/// </summary>
[Tool]
public partial class MatrixButtonCaption : Label
{
    private Button _button = null!;

    public override void _Ready()
    {
        _button = GetParent<Button>();
        Synchronize();
    }

    public override void _Process(double delta)
    {
        Synchronize();
    }

    private void Synchronize()
    {
        if (Text != _button.Text)
        {
            Text = _button.Text;
        }
    }
}
