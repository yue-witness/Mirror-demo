using System.Text;

internal static class Program
{
    private static int _assertions;

    private static int Main()
    {
        try
        {
            VerifyFormalRuleConfiguration();
            VerifyDialogueConfiguration();
            VerifyMisereBash();
            VerifyLimitBashSettlement();
            VerifyOutcomeDirectives();
            VerifySessionCompletion();
            VerifySaveRecovery();

            Console.WriteLine(
                $"Formal demo domain tests passed: {_assertions} assertions.");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 1;
        }
    }

    private static void VerifyDialogueConfiguration()
    {
        string root = FindProjectRoot();
        DialogueRepository dialogue = DialogueRepository.Load(
            Path.Combine(root, "data", "dialogue", "intro.json"),
            Path.Combine(root, "data", "dialogue", "tutorial.json"));

        Assert(dialogue.Get(DemoPhase.Background).Count == 6,
            "The Stability Lattice introduction must contain six complete pages.");
        Assert(dialogue.Get(DemoPhase.Background)
                .Any(line => line.Speaker == "S-17"),
            "The introduction must include a visible S-17 response.");
        Assert(dialogue.Get(DemoPhase.Background)
                .Any(line => line.Speaker == "TUTOR"),
            "The introduction must retain Tutor-authored dialogue.");
        Assert(dialogue.Get(DemoPhase.BashTutorial).Count == 6,
            "The standard Bash tutorial must contain six complete pages.");
        Assert(dialogue.Get(DemoPhase.BashRound2Intro).Count == 3,
            "The initiative-shift briefing must contain three complete pages.");
        Assert(dialogue.Get(DemoPhase.RuleTransition).Count == 7,
            "The Limit Bash transition must contain seven complete pages.");
        Assert(dialogue.Get(DemoPhase.Summary).Count == 4,
            "The final evaluation must contain four complete pages.");

        int configuredLines = 26;

        foreach (string poolId in TutorDialoguePool.All)
        {
            IReadOnlyList<DialogueLine> pool = dialogue.GetRandomPool(poolId);
            configuredLines += pool.Count;
            Assert(pool.Count >= 1,
                $"Tutor dialogue pool {poolId} must not be empty.");
            Assert(pool.All(line => line.Speaker == "TUTOR"),
                $"Tutor dialogue pool {poolId} contains the wrong speaker.");
        }

        Assert(configuredLines == 138,
            $"The approved Tutor script must contain 138 lines, got {configuredLines}.");

        DialogueLine first = dialogue.PickRandom(
            TutorDialoguePool.BashState,
            selector: 7);
        DialogueLine repeated = dialogue.PickRandom(
            TutorDialoguePool.BashState,
            selector: 7);
        Assert(first.Id == repeated.Id,
            "A stable dialogue selector must reproduce the same line.");
    }

    private static void VerifyFormalRuleConfiguration()
    {
        string root = FindProjectRoot();
        RuleConfiguration rules = RuleConfiguration.Load(
            Path.Combine(root, "data", "rules", "bash.json"),
            Path.Combine(root, "data", "rules", "limit_bash.json"));

        Assert(rules.Bash.Round1InitialUnits.SequenceEqual(new[] { 12, 16, 20 }),
            "Bash round 1 candidates differ from the formal design.");
        Assert(rules.Bash.Round2InitialUnits.SequenceEqual(new[] { 13, 15, 17 }),
            "Bash round 2 candidates differ from the formal design.");
        Assert(rules.LimitBash.MinimumInitialUnits == 20
            && rules.LimitBash.MaximumInitialUnits == 30,
            "Limit Bash initial range differs from the formal design.");
    }

    private static void VerifyMisereBash()
    {
        var game = new BashGame();
        game.Start(4, Actor.Player);

        Assert(game.ApplyTake(Actor.Player, 3) == RoundOutcome.Continue,
            "A non-final Bash take must continue.");
        Assert(game.ApplyTake(Actor.Tutor, 1) == RoundOutcome.PlayerWin,
            "The actor taking the final Bash unit must lose.");
        Assert(game.Remaining == 0, "A settled Bash game must have zero remaining.");

        var guard = new BashGame();
        guard.Start(2, Actor.Tutor);
        AssertThrows<InvalidOperationException>(
            () => guard.ApplyTake(Actor.Player, 1),
            "Bash must reject an action by the wrong actor.");
        Assert(!guard.CanTake(3), "Bash must reject taking more than remains.");
    }

    private static void VerifyLimitBashSettlement()
    {
        var noRepeat = new LimitBashGame();
        noRepeat.Start(20);
        Play(noRepeat, 1, 2);
        Assert(noRepeat.Remaining == 17, "A non-terminal reveal must subtract both choices.");
        Assert(!noRepeat.GetLegalPlayerActions().Contains(1),
            "The player must not repeat their previous choice.");
        Assert(!noRepeat.GetLegalTutorActions().Contains(2),
            "The Tutor must not repeat its previous choice.");

        var different = new LimitBashGame();
        different.Start(20);
        Play(different, 1, 1);
        Play(different, 2, 2);
        Play(different, 3, 3);
        Play(different, 2, 1);
        RoundOutcome differentOutcome = Play(different, 3, 2);
        Assert(differentOutcome == RoundOutcome.PlayerLose,
            "At the terminal reveal, the larger differing choice must lose.");

        var rollback = new LimitBashGame();
        rollback.Start(20);
        Play(rollback, 3, 1);
        Play(rollback, 2, 2);
        Play(rollback, 3, 3);
        Play(rollback, 2, 2);
        RoundOutcome rollbackOutcome = Play(rollback, 3, 3);
        Assert(rollbackOutcome == RoundOutcome.PlayerLose,
            "Equal terminal choices must use the nearest earlier difference.");

        var allEqual = new LimitBashGame();
        allEqual.Start(20);
        Play(allEqual, 1, 1);
        Play(allEqual, 2, 2);
        Play(allEqual, 3, 3);
        Play(allEqual, 2, 2);
        Play(allEqual, 1, 1);
        RoundOutcome draw = Play(allEqual, 2, 2);
        Assert(draw == RoundOutcome.Draw,
            "An all-equal Limit Bash history must settle as a draw.");
    }

    private static void VerifyOutcomeDirectives()
    {
        Assert(OutcomeDirector.GetDirective(0, 0.99f)
            == OutcomeDirective.PlayerWinOrDraw,
            "The first Limit Bash game must be player-win-or-draw.");
        Assert(OutcomeDirector.GetDirective(1, 0.01f)
            == OutcomeDirective.PlayerLoseOrDraw,
            "The second Limit Bash game must be player-lose-or-draw.");

        var random = new SessionRandom(772_774);
        int favorable = 0;
        const int sampleCount = 10_000;

        for (int index = 0; index < sampleCount; index++)
        {
            if (OutcomeDirector.GetDirective(2, random.NextSingle())
                == OutcomeDirective.PlayerWinOrDraw)
            {
                favorable++;
            }
        }

        float ratio = favorable / (float)sampleCount;
        Assert(ratio is > 0.47f and < 0.53f,
            $"Post-game-two directives should be approximately 50/50, got {ratio:P1}.");

        var director = new OutcomeDirector();

        foreach (OutcomeDirective directive in Enum.GetValues<OutcomeDirective>())
        {
            for (int initial = 20; initial <= 30; initial++)
            {
                var game = new LimitBashGame();
                game.Start(initial);
                Assert(director.CanGuaranteeDirective(game, directive),
                    $"Directive {directive} is not guaranteed from N={initial}.");

                int cursor = initial % 3;

                while (!game.IsGameOver)
                {
                    int[] legal = game.GetLegalPlayerActions().ToArray();
                    int playerTake = legal[cursor % legal.Length];
                    cursor++;
                    game.LockPlayerChoice(playerTake);
                    int tutorTake = director.ChooseAfterPlayerLock(game, directive);
                    Assert(game.GetLegalTutorActions().Contains(tutorTake),
                        "Outcome director submitted an illegal Tutor choice.");
                    game.CommitTutorChoice(tutorTake);
                    Assert(game.RoundIndex < 20,
                        "Controlled Limit Bash did not terminate within the bounded state space.");
                }

                Assert(IsAllowed(game.Result, directive),
                    $"Directive {directive} produced disallowed result {game.Result}.");
            }
        }
    }

    private static void VerifySessionCompletion()
    {
        var winStats = new SessionStats();
        winStats.RecordLimitBash(RoundOutcome.PlayerWin, 4);
        winStats.RecordLimitBash(RoundOutcome.PlayerLose, 3);
        winStats.RecordLimitBash(RoundOutcome.PlayerWin, 5);
        Assert(winStats.IsLimitBashComplete,
            "Two cumulative player wins must complete Limit Bash.");

        var drawStats = new SessionStats();
        drawStats.RecordLimitBash(RoundOutcome.Draw, 4);
        drawStats.RecordLimitBash(RoundOutcome.PlayerLose, 3);
        Assert(drawStats.ConsecutiveLimitBashDraws == 0,
            "A non-draw must reset consecutive draws.");
        drawStats.RecordLimitBash(RoundOutcome.Draw, 4);
        drawStats.RecordLimitBash(RoundOutcome.Draw, 4);
        Assert(drawStats.IsLimitBashComplete,
            "Two consecutive draws must complete Limit Bash.");
    }

    private static void VerifySaveRecovery()
    {
        string root = Path.Combine(
            Path.GetTempPath(),
            "project-mirror-formal-tests",
            Guid.NewGuid().ToString("N"));
        string path = Path.Combine(root, "demo_save.json");

        try
        {
            var service = new SaveGameService(path);
            DemoSaveState first = CreateSave("first", updated: 1, complete: false);
            DemoSaveState second = CreateSave("second", updated: 2, complete: false);
            service.WriteAtomic(first);
            service.WriteAtomic(second);

            Assert(service.LoadActive()?.SaveId == "second",
                "The primary active save was not loaded.");
            File.WriteAllText(path, "{ corrupt", Encoding.UTF8);
            DemoSaveState? recovered = service.LoadActive();
            Assert(recovered?.SaveId == "first",
                "A corrupt primary save was not recovered from backup.");
            Assert(recovered?.ElapsedPlayMilliseconds == 12_345,
                "The accumulated play time was not restored from backup.");
            Assert(service.LastWarning?.Contains("recovery backup") == true,
                "Backup recovery was not disclosed.");
            Assert(Directory.GetFiles(root, "*.corrupt-*.json").Length == 1,
                "The corrupt primary save was not quarantined.");

            string completePath = Path.Combine(root, "complete.json");
            var completeService = new SaveGameService(completePath);
            completeService.WriteAtomic(CreateSave("complete", updated: 3, complete: true));
            Assert(completeService.LoadActive() is null,
                "A completed save must not appear as an active Continue slot.");
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private static DemoSaveState CreateSave(string id, long updated, bool complete)
    {
        return new DemoSaveState(
            DemoSaveState.CurrentSchemaVersion,
            id,
            DemoPhase.Background,
            updated,
            42,
            0,
            0,
            string.Empty,
            string.Empty,
            PendingGameStart.None,
            0,
            0,
            0,
            0,
            0,
            Array.Empty<DialoguePoolHistorySnapshot>(),
            null,
            new SessionStats().ToSnapshot(),
            null,
            12_345,
            complete,
            string.Empty);
    }

    private static RoundOutcome Play(LimitBashGame game, int player, int tutor)
    {
        game.LockPlayerChoice(player);
        return game.CommitTutorChoice(tutor);
    }

    private static bool IsAllowed(
        RoundOutcome outcome,
        OutcomeDirective directive)
    {
        return directive == OutcomeDirective.PlayerWinOrDraw
            ? outcome is RoundOutcome.PlayerWin or RoundOutcome.Draw
            : outcome is RoundOutcome.PlayerLose or RoundOutcome.Draw;
    }

    private static string FindProjectRoot()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);

        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "project.godot")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the Godot project root.");
    }

    private static void Assert(bool condition, string message)
    {
        _assertions++;

        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private static void AssertThrows<T>(Action action, string message)
        where T : Exception
    {
        _assertions++;

        try
        {
            action();
        }
        catch (T)
        {
            return;
        }

        throw new InvalidOperationException(message);
    }
}
