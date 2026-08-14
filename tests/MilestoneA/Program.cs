using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

internal static class Program
{
    private static int Main()
    {
        try
        {
            VerifyInitialRules();
            VerifyTurnGuards();
            VerifyBothWinPaths();
            VerifyTwentyCompleteRounds();
            VerifyHoverTracker();
            VerifyPlayerModelPersistenceAndRecovery();

            Console.WriteLine(
                "Milestone A + PlayerModel tests passed: 20/20 rounds, persistence, and recovery.");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Milestone A domain test failed: {exception.Message}");
            return 1;
        }
    }

    private static void VerifyInitialRules()
    {
        var game = new BashGame();
        game.StartGame();

        Assert(game.Remaining == 15, "A round must start with 15 units.");
        Assert(game.IsPlayerTurn, "The player must act first.");
        Assert(game.IsLegalMove(1), "TAKE 1 must be legal at the start.");
        Assert(game.IsLegalMove(2), "TAKE 2 must be legal at the start.");
        Assert(game.IsLegalMove(3), "TAKE 3 must be legal at the start.");

        game.Start(initialUnits: 2);
        Assert(!game.IsLegalMove(3), "TAKE 3 must be disabled at Remaining=2.");
    }

    private static void VerifyTurnGuards()
    {
        var game = new BashGame();
        game.StartGame();
        game.ApplyPlayerMove(1);

        Assert(!game.IsPlayerTurn, "A legal player move must pass control to the AI.");
        AssertThrows(() => game.ApplyPlayerMove(1), "A player double action must fail.");

        game.ApplyAIMove(1);
        Assert(game.IsPlayerTurn, "A legal AI move must return control to the player.");
        AssertThrows(() => game.ApplyAIMove(1), "An AI double action must fail.");
    }

    private static void VerifyBothWinPaths()
    {
        var playerWin = new BashGame();
        playerWin.Start(initialUnits: 1);
        playerWin.ApplyPlayerMove(1);

        Assert(playerWin.Remaining == 0, "A winning move must stop at zero.");
        Assert(playerWin.GetResult() == GameResult.PlayerWin, "Player win not recorded.");
        AssertThrows(() => playerWin.ApplyAIMove(1), "Input must stop after player win.");

        var aiWin = new BashGame();
        aiWin.Start(initialUnits: 2);
        aiWin.ApplyPlayerMove(1);
        aiWin.ApplyAIMove(1);

        Assert(aiWin.Remaining == 0, "An AI winning move must stop at zero.");
        Assert(aiWin.GetResult() == GameResult.AIWin, "AI win not recorded.");
        AssertThrows(() => aiWin.ApplyPlayerMove(1), "Input must stop after AI win.");
    }

    private static void VerifyTwentyCompleteRounds()
    {
        var playerRandom = new Random(772);
        var strategy = new StrategyEngine(randomSeed: 774);

        for (int round = 1; round <= 20; round++)
        {
            var game = new BashGame();
            game.StartGame();
            int actions = 0;

            while (!game.IsGameOver)
            {
                if (game.IsPlayerTurn)
                {
                    int maximumTake = Math.Min(BashGame.MaximumTake, game.Remaining);
                    int amount = playerRandom.Next(BashGame.MinimumTake, maximumTake + 1);
                    game.ApplyPlayerMove(amount);
                }
                else
                {
                    int amount = strategy.ChooseBashMove(game);
                    Assert(game.IsLegalMove(amount), $"Round {round}: AI chose an illegal move.");
                    game.ApplyAIMove(amount);
                }

                actions++;
                Assert(game.Remaining >= 0, $"Round {round}: Remaining became negative.");
                Assert(actions <= 15, $"Round {round}: the turn loop did not terminate.");
            }

            Assert(game.Remaining == 0, $"Round {round}: terminal Remaining must be zero.");
            Assert(
                game.GetResult() is GameResult.PlayerWin or GameResult.AIWin,
                $"Round {round}: winner was not recorded.");
        }
    }

    private static void VerifyHoverTracker()
    {
        var tracker = new HoverTracker();

        Assert(tracker.Enter(choice: 2, timestampMilliseconds: 1000),
            "The first hover enter must be accepted.");
        Assert(!tracker.Enter(choice: 2, timestampMilliseconds: 1100),
            "A duplicate hover enter must not reset its start time.");

        long? firstDuration = tracker.Exit(choice: 2, timestampMilliseconds: 1400);
        Assert(firstDuration == 400, "Hover exit duration was not measured correctly.");

        tracker.Enter(choice: 3, timestampMilliseconds: 2000);
        Dictionary<int, long> completed = tracker.CompleteActiveHovers(
            timestampMilliseconds: 2300);
        Assert(completed[3] == 300, "Active hover was not completed during shutdown/choice.");

        Dictionary<int, long> snapshot = tracker.Snapshot();
        Assert(snapshot[2] == 400, "Completed hover total for TAKE 2 was lost.");
        Assert(snapshot[3] == 300, "Completed hover total for TAKE 3 was lost.");
    }

    private static void VerifyPlayerModelPersistenceAndRecovery()
    {
        string testDirectory = Path.Combine(
            AppContext.BaseDirectory,
            "runtime-profile-tests",
            Guid.NewGuid().ToString("N"));
        string profilePath = Path.Combine(testDirectory, "player_profile.json");

        try
        {
            var model = new PlayerModel(new JsonPlayerProfileStore(profilePath));
            model.BeginSession();
            model.RecordRoundStarted(roundIndex: 1, restarted: false, previousRemaining: 0);
            model.RecordChoiceWindowOpened(roundIndex: 1, turnIndex: 1, remaining: 15);
            model.RecordHoverStarted(roundIndex: 1, turnIndex: 1, choice: 2, remaining: 15);
            model.RecordHoverEnded(
                roundIndex: 1,
                turnIndex: 1,
                choice: 2,
                durationMilliseconds: 1350,
                remaining: 15);
            model.RecordChoice(
                roundIndex: 1,
                turnIndex: 1,
                choice: 3,
                decisionSeconds: 1.75,
                hoverMilliseconds: new Dictionary<int, long>
                {
                    [2] = 1350,
                    [3] = 250
                },
                remainingBefore: 15,
                remainingAfter: 12,
                publicPrediction: 1);
            model.RecordChoiceWindowOpened(roundIndex: 1, turnIndex: 2, remaining: 12);
            model.RecordChoiceWindowAbandoned(
                roundIndex: 1,
                turnIndex: 2,
                decisionSeconds: 2.25,
                remaining: 12,
                reason: "test_restart");
            model.RecordRoundStarted(roundIndex: 2, restarted: true, previousRemaining: 12);
            model.RecordRoundCompleted(roundIndex: 2, result: GameResult.PlayerWin);
            model.EndSession("test_complete");

            Assert(File.Exists(profilePath), "PlayerModel did not create its persistent profile.");
            Assert(File.Exists(profilePath + ".bak"), "PlayerModel did not keep a backup profile.");
            Assert(model.Profile.History.Count == 1, "Confirmed choice was not recorded.");
            Assert(model.Profile.History[0].WasReversal, "Prediction reversal was not recorded.");
            Assert(model.Profile.MaxTakeBias == 1f, "MaxTakeBias was not recalculated.");
            Assert(model.Profile.ReversalTendency == 1f,
                "ReversalTendency was not recalculated.");
            Assert(model.Profile.TotalHoverMillisecondsByChoice[2] == 1350,
                "Hover duration aggregate was not recalculated.");
            Assert(model.Profile.TotalRoundsCompleted == 1,
                "Completed round aggregate was not recalculated.");
            Assert(model.Profile.TotalRoundRestarts == 1,
                "Round restart aggregate was not recalculated.");
            Assert(model.Profile.TotalPlayerWins == 1,
                "Player win aggregate was not recalculated.");

            PlayerBehaviorType[] requiredBehaviorTypes =
            {
                PlayerBehaviorType.SessionStarted,
                PlayerBehaviorType.SessionEnded,
                PlayerBehaviorType.RoundStarted,
                PlayerBehaviorType.RoundCompleted,
                PlayerBehaviorType.ChoiceWindowOpened,
                PlayerBehaviorType.ChoiceWindowAbandoned,
                PlayerBehaviorType.ChoiceHoverStarted,
                PlayerBehaviorType.ChoiceHoverEnded,
                PlayerBehaviorType.ChoiceSelected
            };

            foreach (PlayerBehaviorType type in requiredBehaviorTypes)
            {
                Assert(model.Profile.BehaviorHistory.Any(record => record.Type == type),
                    $"Behavior event {type} was not recorded.");
            }

            using (JsonDocument json = JsonDocument.Parse(File.ReadAllText(profilePath)))
            {
                Assert(json.RootElement.GetProperty("history").GetArrayLength() == 1,
                    "Saved JSON does not contain the choice history.");
                Assert(json.RootElement.GetProperty("behaviorHistory").GetArrayLength() >= 11,
                    "Saved JSON does not contain the behavior event history.");
            }

            var reloaded = new PlayerModel(new JsonPlayerProfileStore(profilePath));
            Assert(reloaded.Profile.History.Count == 1,
                "Choice history was lost after recreating PlayerModel.");
            Assert(reloaded.Profile.Sessions.Count == 1,
                "Session history was lost after recreating PlayerModel.");

            reloaded.BeginSession();
            reloaded.RecordRoundStarted(
                roundIndex: 3,
                restarted: false,
                previousRemaining: 0);
            reloaded.EndSession("reload_test_complete");

            Assert(reloaded.Profile.Sessions.Count == 2,
                "A second launch did not append a new session.");
            Assert(reloaded.Profile.TotalRoundRestarts == 1,
                "A mid-round restart was not preserved across reload.");

            var interrupted = new PlayerModel(new JsonPlayerProfileStore(profilePath));
            string interruptedSessionId = interrupted.BeginSession();

            var resumed = new PlayerModel(new JsonPlayerProfileStore(profilePath));
            resumed.BeginSession();

            PlayerSessionRecord interruptedSession = resumed.Profile.Sessions.Single(
                session => session.SessionId == interruptedSessionId);
            Assert(interruptedSession.EndReason == "interrupted",
                "An unclosed session was not marked as interrupted on next launch.");
            Assert(resumed.Profile.BehaviorHistory.Any(record =>
                    record.Type == PlayerBehaviorType.SessionEnded
                    && record.SessionId == interruptedSessionId
                    && record.Metadata.GetValueOrDefault("reason") == "interrupted"),
                "Interrupted session did not receive a synthetic end event.");
            resumed.EndSession("resume_test_complete");

            // Damage the primary file after a valid backup exists. Loading must
            // retain the previous history and quarantine the unreadable file.
            File.WriteAllText(profilePath, "{ not valid json", System.Text.Encoding.UTF8);
            var recoveryStore = new JsonPlayerProfileStore(profilePath);
            var recovered = new PlayerModel(recoveryStore);

            Assert(recovered.Profile.History.Count == 1,
                "Backup recovery lost the previously recorded choice.");
            Assert(recoveryStore.LastLoadWarning?.Contains("backup") == true,
                "Backup recovery did not report its fallback.");
            Assert(Directory.GetFiles(testDirectory, "*.corrupt-*", SearchOption.TopDirectoryOnly)
                    .Length == 1,
                "Corrupt primary profile was not quarantined.");
        }
        finally
        {
            if (Directory.Exists(testDirectory))
            {
                Directory.Delete(testDirectory, recursive: true);
            }
        }
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private static void AssertThrows(Action action, string message)
    {
        try
        {
            action();
        }
        catch (InvalidOperationException)
        {
            return;
        }

        throw new InvalidOperationException(message);
    }
}
