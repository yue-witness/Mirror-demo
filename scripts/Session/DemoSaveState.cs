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
    SessionStatsSnapshot Stats,
    GameSnapshot? CurrentGame,
    long ElapsedPlayMilliseconds,
    bool IsComplete,
    string Checksum)
{
    public const int CurrentSchemaVersion = 2;
}

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
