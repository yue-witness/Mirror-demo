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
            VerifyTutorSpeechConfiguration();
            VerifyUiAudioAssets();
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
        Assert(dialogue.GetAll().All(line => !line.Text.Contains(
                "online",
                StringComparison.OrdinalIgnoreCase)),
            "Dialogue must remain grounded in the physical evaluation chamber.");

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

        Assert(configuredLines == 140,
            $"The approved Tutor script must contain 140 lines, got {configuredLines}.");

        Assert(dialogue.GetRandomPool(TutorDialoguePool.GuidedInputSelectB)
                .Single().Text.Contains("middle option: B", StringComparison.Ordinal)
            && dialogue.GetRandomPool(TutorDialoguePool.GuidedInputConfirm)
                .Single().Text.Contains("press CONFIRM", StringComparison.Ordinal),
            "The mandatory two-step input instructions are incomplete.");

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

    private static void VerifyTutorSpeechConfiguration()
    {
        string root = FindProjectRoot();
        DialogueRepository dialogue = DialogueRepository.Load(
            Path.Combine(root, "data", "dialogue", "intro.json"),
            Path.Combine(root, "data", "dialogue", "tutorial.json"));
        TutorSpeechCatalog speech = TutorSpeechCatalog.Load(
            Path.Combine(root, "assets", "audio", "tutor", "manifest.json"));

        Assert(speech.Count == 196,
            $"Tutor speech manifest must contain 196 rendered cues, got {speech.Count}.");
        Assert(TutorPresentationPolicy.ResolveSpeechMode(
                "choice_hesitation_wait") == TutorSpeechMode.Silent,
            "Repeated choice-time chatter must remain text-only.");
        Assert(TutorPresentationPolicy.ResolveSpeechMode(
                "summary_complete") == TutorSpeechMode.Essential,
            "Narrative outcomes must retain Tutor speech.");
        Assert(TutorPresentationPolicy.ResolveSpeechMode(
                "background_arrival") == TutorSpeechMode.Essential,
            "The revised physical-space arrival must use its regenerated cue.");
        Assert(TutorPresentationPolicy.ResolveSpeechMode(
                "guided_input_select_b") == TutorSpeechMode.Essential
            && TutorPresentationPolicy.ResolveSpeechMode(
                "guided_input_confirm") == TutorSpeechMode.Essential,
            "Both mandatory guided-input steps must retain Tutor speech.");
        Assert(TutorPresentationPolicy.ResolveSpeechMode(
                "bash_confirm_turn") == TutorSpeechMode.Essential,
            "Tutor Bash actions must retain one voiced commitment cue.");
        Assert(TutorPresentationPolicy.ResolveEmotion(
                "bash_r2_win_satisfied",
                "You passed. I am satisfied.") == TutorEmotion.Encouraging,
            "Successful Tutor dialogue must select the encouraging portrait row.");
        Assert(TutorPresentationPolicy.ResolveEmotion(
                "bash_loss_tier_1",
                "Round evaluation: unsuccessful.") == TutorEmotion.Stern,
            "Failure dialogue must select the stern portrait row.");
        DialogueLine arrival = dialogue.Get(DemoPhase.Background)
            .Single(line => line.Id == "background_arrival");
        TutorSpeechCue? arrivalCue = speech.Find(arrival.Id, arrival.Text);
        Assert(arrivalCue is not null
            && arrivalCue.AudioPath.EndsWith(
                "background_arrival.ogg",
                StringComparison.Ordinal),
            "The revised physical-space arrival has no matching generated cue.");

        foreach (DialogueLine line in dialogue.GetAll())
        {
            if (line.Speaker == "S-17")
            {
                Assert(speech.Find(line.Id, line.Text) is null,
                    "S-17 must not have a generated voice cue.");
                continue;
            }

            string placeholder = line.Text.Contains("{turn_count}", StringComparison.Ordinal)
                ? "{turn_count}"
                : line.Text.Contains("{reveal_count}", StringComparison.Ordinal)
                    ? "{reveal_count}"
                    : string.Empty;
            int maximum = string.IsNullOrEmpty(placeholder) ? 1 : 20;

            for (int value = 1; value <= maximum; value++)
            {
                string renderedText = string.IsNullOrEmpty(placeholder)
                    ? line.Text
                    : line.Text.Replace(
                        placeholder,
                        value.ToString(),
                        StringComparison.Ordinal);
                TutorSpeechCue? cue = speech.Find(line.Id, renderedText);
                Assert(cue is not null,
                    $"Tutor speech cue is missing for {line.Id}: {renderedText}");

                string relativeAudioPath = cue!.AudioPath
                    .Replace("res://", string.Empty, StringComparison.Ordinal)
                    .Replace('/', Path.DirectorySeparatorChar);
                Assert(File.Exists(Path.Combine(root, relativeAudioPath)),
                    $"Tutor speech asset does not exist: {cue.AudioPath}");
                Assert(Math.Abs(
                        cue.CharactersPerSecond
                        - renderedText.Length / cue.DurationSeconds) < 0.02f,
                    $"Tutor text speed does not match audio duration for {line.Id}.");
            }
        }
    }

    private static void VerifyUiAudioAssets()
    {
        string root = FindProjectRoot();
        string audioRoot = Path.Combine(
            root,
            "assets",
            "audio",
            "ui");
        string[] fileNames =
        {
            "hover.wav",
            "select.wav",
            "submit.wav",
            "success.wav",
            "failure.wav",
            "draw.wav",
            "transition.wav"
        };

        foreach (string fileName in fileNames)
        {
            string path = Path.Combine(audioRoot, fileName);
            Assert(File.Exists(path), $"UI sound asset is missing: {fileName}");
            byte[] bytes = File.ReadAllBytes(path);
            Assert(bytes.Length > 44
                && Encoding.ASCII.GetString(bytes, 0, 4) == "RIFF"
                && Encoding.ASCII.GetString(bytes, 8, 4) == "WAVE",
                $"UI sound asset is not a valid PCM WAV file: {fileName}");
        }

        string musicPath = Path.Combine(
            root,
            "assets",
            "audio",
            "bgm",
            "exploration_theme.ogg");
        Assert(File.Exists(musicPath),
            "The approved Exploration Theme BGM is missing.");
        byte[] musicBytes = File.ReadAllBytes(musicPath);
        Assert(musicBytes.Length > 1_000_000
            && Encoding.ASCII.GetString(musicBytes, 0, 4) == "OggS",
            "The approved BGM is not a valid OGG container.");

        string mainScene = File.ReadAllText(
            Path.Combine(root, "scenes", "main.tscn"));
        Assert(mainScene.Contains("BackgroundMusicPlayer", StringComparison.Ordinal)
            && mainScene.Contains("bus = &\"Music\"", StringComparison.Ordinal),
            "The main scene does not route the approved BGM to the Music bus.");

        string musicController = File.ReadAllText(Path.Combine(
            root,
            "scripts",
            "Audio",
            "BackgroundMusicPlayer.cs"));
        Assert(musicController.Contains("music.Loop = true", StringComparison.Ordinal)
            && musicController.Contains("DuckedVolumeDb = -28.0f", StringComparison.Ordinal),
            "The BGM controller no longer loops or ducks beneath Tutor speech.");
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
