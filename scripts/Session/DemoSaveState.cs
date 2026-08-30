using System;
using System.Collections.Generic;

public sealed record DemoSaveState(
    int SchemaVersion,
    string SaveId,
    DemoPhase ResumePhase,
    long UpdatedUnixMs,
    int SessionSeed,
    int RngStep,
    int DialogueIndex,
    string ActiveBriefingLineId,
    string CurrentTutorDialogue,
    PendingGameStart PendingGameStart,
    int BashRoundIndex,
    int LimitGameIndex,
    int BashRoundOneFailures,
    int BashRoundTwoFailures,
    int DialogueStep,
    IReadOnlyList<DialoguePoolHistorySnapshot> DialogueHistory,
    OutcomeDirective? PendingLimitDirective,
    SessionStatsSnapshot Stats,
    GameSnapshot? CurrentGame,
    long ElapsedPlayMilliseconds,
    bool IsComplete,
    string Checksum)
{
    public const int CurrentSchemaVersion = 3;
}

public sealed record DialoguePoolHistorySnapshot(
    string PoolId,
    IReadOnlyList<string> RecentLineIds);

public sealed record GameSnapshot(
    GameKind Game,
    int InitialUnits,
    int Remaining,
    Actor CurrentTurn,
    int BashRoundIndex,
    int LimitGameIndex,
    int RoundIndex,
    int? PlayerPrevious,
    int? TutorPrevious,
    OutcomeDirective? Directive,
    IReadOnlyList<ChoicePair> ChoicePairs,
    RoundOutcome Result);

public sealed record SaveSlotInfo(
    string SaveId,
    DemoPhase Phase,
    long UpdatedUnixMs);
