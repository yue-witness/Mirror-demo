using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Gameplay and result presentation only. Dialogue-only phases use the
/// dedicated TutorDialogueUI scene.
/// </summary>
public partial class GameplayHUD : Control
{
    private Control _safeArea = null!;
    private Label _phaseBanner = null!;
    private Label _playTimeLabel = null!;
    private Label _leftTitle = null!;
    private RichTextLabel _leftDetails = null!;
    private Label _remainingLabel = null!;
    private Label _selectionLabel = null!;
    private RichTextLabel _dialogueText = null!;
    private Label _systemStatus = null!;
    private RichTextLabel _systemLog = null!;
    private Button[] _choiceButtons = null!;
    private Button _confirmButton = null!;
    private Button _continueButton = null!;
    private Button _backButton = null!;
    private Control _chapterOverlay = null!;
    private Label _chapterNumber = null!;
    private Label _chapterTitle = null!;

    public event Action<int>? ChoiceSelected;

    public event Action? ConfirmRequested;

    public event Action? ContinueRequested;

    public event Action? BackToTitleRequested;

    public event Action? ChapterContinueRequested;

    public override void _Ready()
    {
        _safeArea = GetNode<Control>("SafeArea");
        _phaseBanner = GetNode<Label>("SafeArea/Layout/Header/HeaderRow/PhaseBanner");
        _playTimeLabel = GetNode<Label>(
            "SafeArea/Layout/Header/HeaderRow/PlayTimeLabel");
        _leftTitle = GetNode<Label>("SafeArea/Layout/Content/LeftStatus/LeftVBox/Title");
        _leftDetails = GetNode<RichTextLabel>(
            "SafeArea/Layout/Content/LeftStatus/LeftVBox/Details");
        _remainingLabel = GetNode<Label>(
            "SafeArea/Layout/Content/Center/RemainingCard/RemainingVBox/RemainingValue");
        _selectionLabel = GetNode<Label>(
            "SafeArea/Layout/Content/Center/RemainingCard/RemainingVBox/SelectionLabel");
        _dialogueText = GetNode<RichTextLabel>(
            "SafeArea/Layout/Content/Center/DialoguePanel/DialogueVBox/Text");
        _systemStatus = GetNode<Label>(
            "SafeArea/Layout/Content/RightLog/RightVBox/Status");
        _systemLog = GetNode<RichTextLabel>(
            "SafeArea/Layout/Content/RightLog/RightVBox/Log");
        _confirmButton = GetNode<Button>("SafeArea/Layout/Content/Center/ConfirmButton");
        _continueButton = GetNode<Button>("SafeArea/Layout/Content/Center/ContinueButton");
        _backButton = GetNode<Button>("SafeArea/Layout/Footer/BackButton");
        _chapterOverlay = GetNode<Control>("ChapterOverlay");
        _chapterNumber = GetNode<Label>(
            "ChapterOverlay/ChapterGlass/ChapterVBox/ChapterNumber");
        _chapterTitle = GetNode<Label>(
            "ChapterOverlay/ChapterGlass/ChapterVBox/ChapterTitle");
        _choiceButtons = new[]
        {
            GetNode<Button>("SafeArea/Layout/Content/Center/ChoiceRow/Choice1"),
            GetNode<Button>("SafeArea/Layout/Content/Center/ChoiceRow/Choice2"),
            GetNode<Button>("SafeArea/Layout/Content/Center/ChoiceRow/Choice3")
        };

        for (int index = 0; index < _choiceButtons.Length; index++)
        {
            int choice = index + 1;
            _choiceButtons[index].Pressed += () => ChoiceSelected?.Invoke(choice);
        }

        _confirmButton.Pressed += () => ConfirmRequested?.Invoke();
        _continueButton.Pressed += () => ContinueRequested?.Invoke();
        _backButton.Pressed += () => BackToTitleRequested?.Invoke();
    }

    public override void _Input(InputEvent @event)
    {
        if (!_chapterOverlay.Visible
            || @event is not InputEventMouseButton mouseButton
            || mouseButton.ButtonIndex != MouseButton.Left
            || !mouseButton.Pressed)
        {
            return;
        }

        GetViewport().SetInputAsHandled();
        ChapterContinueRequested?.Invoke();
    }

    public void ShowChapter(string number, string title)
    {
        _chapterNumber.Text = number;
        _chapterTitle.Text = title;
        _safeArea.Visible = false;
        _chapterOverlay.Visible = true;
    }

    public void HideChapter()
    {
        _chapterOverlay.Visible = false;
        _safeArea.Visible = true;
    }

    public void SetElapsedPlayTime(long elapsedMilliseconds)
    {
        TimeSpan elapsed = TimeSpan.FromMilliseconds(
            Math.Max(0, elapsedMilliseconds));
        _playTimeLabel.Text = elapsed.TotalHours >= 1
            ? $"PLAY TIME · {(int)elapsed.TotalHours:00}:{elapsed.Minutes:00}:{elapsed.Seconds:00}"
            : $"PLAY TIME · {elapsed.Minutes:00}:{elapsed.Seconds:00}";
    }

    public void ShowBash(
        BashGame game,
        int bashRoundIndex,
        SessionStats stats,
        int turns,
        int? selectedChoice,
        bool inputOpen,
        string systemLog,
        string tutorDialogue)
    {
        _phaseBanner.Text = $"CHAPTER 1.1 / BASH ROUND {bashRoundIndex}";
        _systemStatus.Text = game.CurrentTurn == Actor.Player
            ? inputOpen
                ? "STATUS · PLAYER TURN · INPUT OPEN"
                : "STATUS · PLAYER TURN · LOCKED"
            : "STATUS · TUTOR TURN · ACTING";
        _leftTitle.Text = "ROUND STATUS";
        _leftDetails.Text =
            $"• Starting units: {game.InitialUnits}\n\n"
            + $"• Remaining: {game.Remaining}\n\n"
            + $"• {(game.CurrentTurn == Actor.Player ? "Player's turn" : "Tutor's turn")}\n\n"
            + "• Taking the last unit loses";
        _remainingLabel.Text = game.Remaining.ToString("00");
        _selectionLabel.Text = selectedChoice.HasValue
            ? $"SELECTED: TAKE {selectedChoice.Value}"
            : "NO SELECTION";
        _dialogueText.Text = tutorDialogue;
        _systemLog.Text = $"{systemLog}\n\nACTIONS THIS ROUND: {turns}";

        int[] legal = Enumerable.Range(1, 3).Where(game.CanTake).ToArray();
        ConfigureChoices(legal, selectedChoice, locked: !inputOpen);
        _confirmButton.Visible = true;
        _confirmButton.Disabled = !inputOpen || !selectedChoice.HasValue;
        _confirmButton.Text = selectedChoice.HasValue
            ? $"CONFIRM · TAKE {selectedChoice.Value}"
            : "SELECT FIRST";
        _continueButton.Visible = false;
    }

    public void ShowLimitBash(
        LimitBashGame game,
        int gameIndex,
        SessionStats stats,
        int? selectedChoice,
        bool inputOpen,
        bool waiting,
        string systemLog,
        string tutorDialogue,
        ChoicePair? pendingReveal = null)
    {
        _phaseBanner.Text = "CHAPTER 1.2 / LIMIT BASH";
        _systemStatus.Text = waiting
            ? "STATUS · PLAYER LOCKED · WAITING TO REVEAL"
            : inputOpen
                ? "STATUS · WAITING FOR PLAYER"
                : "STATUS · REVEALING · INPUT LOCKED";
        _leftTitle.Text = "CURRENT STATUS";
        _leftDetails.Text =
            $"• Starting units: {game.InitialUnits}\n\n"
            + $"• Remaining: {game.Remaining}\n\n"
            + $"• Player's last choice: {FormatPrevious(game.PlayerPrevious)}\n\n"
            + $"• Tutor's last choice: {FormatPrevious(game.TutorPrevious)}";
        _remainingLabel.Text = game.Remaining.ToString("00");
        _selectionLabel.Text = selectedChoice.HasValue
            ? waiting
                ? $"PLAYER CHOICE: {selectedChoice.Value} · LOCKED"
                : $"PLAYER CHOICE: {selectedChoice.Value}"
            : "CHOOSE 1 / 2 / 3";
        _dialogueText.Text = tutorDialogue;
        _systemLog.Text =
            $"{systemLog}\n\n"
            + "[EXECUTION LOG]\n"
            + $"{FormatChoiceLog(game.ChoicePairs, pendingReveal)}\n\n"
            + $"TOTAL WINS: {stats.LimitBashPlayerWins} / 2\n"
            + $"CONSECUTIVE DRAWS: {stats.ConsecutiveLimitBashDraws} / 2";

        ConfigureChoices(game.GetLegalPlayerActions(), selectedChoice, !inputOpen);
        _confirmButton.Visible = true;
        _confirmButton.Disabled = !inputOpen || !selectedChoice.HasValue;
        _confirmButton.Text = waiting
            ? "LOCKED · WAITING"
            : selectedChoice.HasValue
                ? $"CONFIRM · TAKE {selectedChoice.Value}"
                : "SELECT FIRST";
        _continueButton.Visible = false;
    }

    public void ShowLimitReveal(
        LimitBashGame game,
        int gameIndex,
        SessionStats stats,
        int playerTake,
        int tutorTake,
        string tutorDialogue)
    {
        ShowLimitBash(
            game,
            gameIndex,
            stats,
            playerTake,
            inputOpen: false,
            waiting: false,
            systemLog: $"SIMULTANEOUS REVEAL: PLAYER {playerTake} / TUTOR {tutorTake}",
            tutorDialogue: tutorDialogue,
            pendingReveal: new ChoicePair(
                playerTake,
                tutorTake,
                game.Remaining,
                Math.Max(0, game.Remaining - playerTake - tutorTake)));
        _selectionLabel.Text = $"PLAYER {playerTake}  ·  TUTOR {tutorTake}";
        _confirmButton.Visible = false;
    }

    public void ShowRoundResult(
        GameKind game,
        RoundOutcome outcome,
        int gameIndex,
        int rounds,
        SessionStats stats,
        bool willContinue,
        string tutorDialogue,
        ChoicePair? finalChoice = null,
        IReadOnlyList<ChoicePair>? choiceHistory = null)
    {
        string preservedSystemLog = _systemLog.Text.Trim();
        string gameName = game == GameKind.Bash ? "BASH" : "LIMIT BASH";
        string result = FormatOutcome(outcome);

        _phaseBanner.Text = $"{gameName} / GAME RESULT";
        _systemStatus.Text = willContinue
            ? "STATUS · GAME COMPLETE · WAITING TO CONTINUE"
            : "STATUS · END CONDITION REACHED";
        _leftTitle.Text = "GAME STATISTICS";
        _leftDetails.Text =
            $"• {FormatOutcomeDescription(outcome)}\n\n"
            + $"• Total wins: {stats.LimitBashPlayerWins} / 2\n\n"
            + $"• Consecutive draws: {stats.ConsecutiveLimitBashDraws} / 2\n\n"
            + $"• Rounds this game: {rounds}"
            + (finalChoice.HasValue
                ? $"\n\n• Final round: Player {finalChoice.Value.PlayerTake}"
                    + $" / Tutor {finalChoice.Value.TutorTake}"
                : string.Empty);
        _remainingLabel.Text = result;
        _selectionLabel.Text = finalChoice.HasValue
            ? $"FINAL · PLAYER {finalChoice.Value.PlayerTake}"
                + $" / TUTOR {finalChoice.Value.TutorTake}"
            : $"{gameName} · {result}";
        _dialogueText.Text = tutorDialogue;
        string resultMessage = willContinue
            ? "The end condition has not been reached. Continue to the next game."
            : "Continue to the final summary.";
        _systemLog.Text = game == GameKind.LimitBash
            ? $"{resultMessage}\n\n[EXECUTION LOG]\n"
                + FormatChoiceLog(choiceHistory ?? Array.Empty<ChoicePair>())
            : string.IsNullOrWhiteSpace(preservedSystemLog)
                ? resultMessage
                : $"{preservedSystemLog}\n\n{resultMessage}";

        ConfigureChoices(Array.Empty<int>(), null, locked: true);
        _confirmButton.Visible = false;
        _continueButton.Visible = true;
        _continueButton.Text = willContinue
            ? "CONTINUE"
            : "VIEW FINAL SUMMARY";
        _continueButton.GrabFocus();
    }

    private void ConfigureChoices(
        IEnumerable<int> legalChoices,
        int? selectedChoice,
        bool locked)
    {
        HashSet<int> legal = legalChoices.ToHashSet();

        for (int index = 0; index < _choiceButtons.Length; index++)
        {
            int choice = index + 1;
            bool selected = selectedChoice == choice;
            Button button = _choiceButtons[index];
            button.Visible = legal.Count > 0;
            button.ButtonPressed = selected;
            button.Disabled = !selected && (locked || !legal.Contains(choice));
            button.MouseFilter = locked
                ? MouseFilterEnum.Ignore
                : MouseFilterEnum.Stop;
            button.FocusMode = locked
                ? FocusModeEnum.None
                : FocusModeEnum.All;
            button.Scale = selected ? new Vector2(1.035f, 1.035f) : Vector2.One;
            button.ZIndex = selected ? 2 : 0;
            button.Text = selected
                ? $"✓ {choice}\nSELECTED"
                : $"{choice}\nTAKE {choice}";
        }
    }

    private static string FormatPrevious(int? previous)
    {
        return previous?.ToString() ?? "—";
    }

    private static string FormatChoiceLog(
        IReadOnlyList<ChoicePair> choicePairs,
        ChoicePair? pendingReveal = null)
    {
        var entries = choicePairs
            .Select((pair, index) => FormatChoiceEntry(pair, index + 1))
            .ToList();

        if (pendingReveal.HasValue)
        {
            entries.Add(FormatChoiceEntry(
                pendingReveal.Value,
                choicePairs.Count + 1,
                revealing: true));
        }

        return entries.Count > 0
            ? string.Join("\n", entries)
            : "NO ROUNDS REVEALED YET.";
    }

    private static string FormatChoiceEntry(
        ChoicePair pair,
        int roundIndex,
        bool revealing = false)
    {
        string state = revealing ? "  [REVEALING]" : string.Empty;
        return $"R{roundIndex:00}  PLAYER {pair.PlayerTake} / TUTOR {pair.TutorTake}"
            + $"  {pair.RemainingBefore} → {pair.RemainingAfter}{state}";
    }

    private static string FormatOutcome(RoundOutcome outcome)
    {
        return outcome switch
        {
            RoundOutcome.PlayerWin => "PLAYER WIN",
            RoundOutcome.PlayerLose => "PLAYER LOSE",
            RoundOutcome.Draw => "DRAW",
            _ => "IN PROGRESS"
        };
    }

    private static string FormatOutcomeDescription(RoundOutcome outcome)
    {
        return outcome switch
        {
            RoundOutcome.PlayerWin => "Player victory",
            RoundOutcome.PlayerLose => "Player defeat",
            RoundOutcome.Draw => "Game drawn",
            _ => "In progress"
        };
    }
}
