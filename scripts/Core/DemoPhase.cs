public enum DemoPhase
{
    TitleScreen,
    Background,
    BashTutorial,
    BashRound2Intro,
    BashRetryBriefing,
    BashGame1Round1,
    BashGame1Round2,
    RuleTransition,
    LimitGameBriefing,
    LimitRestartBriefing,
    LimitBash,
    RoundResult,
    Summary,
    Complete
}

/// <summary>
/// Serializable destination for one-page Tutor briefings that must complete
/// before a new playable lattice is created.
/// </summary>
public enum PendingGameStart
{
    None,
    BashRound1,
    BashRound2,
    LimitBash,
    LimitBashPreservingDirective
}
