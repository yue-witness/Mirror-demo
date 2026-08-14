public enum GameResult
{
    InProgress,
    PlayerWin,
    AIWin
}

/// <summary>
/// Minimal lifecycle shared by playable game rules.
/// </summary>
public interface IGame
{
    void StartGame();

    bool IsGameOver { get; }

    GameResult GetResult();
}
