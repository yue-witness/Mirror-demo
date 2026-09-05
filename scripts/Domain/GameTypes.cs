using System;

public enum Actor
{
    Player,
    Tutor
}

public enum GameKind
{
    Bash,
    LimitBash
}

public enum RoundOutcome
{
    Continue,
    PlayerWin,
    PlayerLose,
    Draw
}

public enum OutcomeDirective
{
    PlayerWinOrDraw,
    PlayerLoseOrDraw
}

/// <summary>
/// One completed action-history entry. Limit Bash records both choices; Bash
/// records one actor's choice and zero for the actor who did not act this turn.
/// RemainingAfter is zero for a terminal action or reveal.
/// </summary>
public readonly record struct ChoicePair(
    int PlayerTake,
    int TutorTake,
    int RemainingBefore,
    int RemainingAfter)
{
    public bool IsDifferent => PlayerTake != TutorTake;

    public Actor LargerActor => PlayerTake > TutorTake
        ? Actor.Player
        : Actor.Tutor;
}
