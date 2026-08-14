/// <summary>
/// Describes a rule-level action without coupling game logic to Godot input.
/// </summary>
public readonly record struct GameAction(string Type, int Value)
{
    public const string BashTake = "bash_take";

    public static GameAction Take(int amount)
    {
        return new GameAction(BashTake, amount);
    }
}
