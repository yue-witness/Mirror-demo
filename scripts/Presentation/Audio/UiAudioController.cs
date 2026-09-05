using Godot;
using System;
using System.Collections.Generic;

/// <summary>
/// Central UI sound router. It discovers existing buttons without changing
/// their scene layout and reserves separate channels for overlapping cues.
/// </summary>
public partial class UiAudioController : Node
{

    private readonly HashSet<ulong> _connectedButtons = new();
    private AudioStreamPlayer _hoverPlayer = null!;
    private AudioStreamPlayer _actionPlayer = null!;
    private AudioStreamPlayer _eventPlayer = null!;
    private AudioStreamPlayer _extractionPlayer = null!;
    [Export]
    public AudioStream HoverSound { get; set; } = null!;
    [Export]
    public AudioStream SelectSound { get; set; } = null!;
    [Export]
    public AudioStream SubmitSound { get; set; } = null!;
    [Export]
    public AudioStream SuccessSound { get; set; } = null!;
    [Export]
    public AudioStream FailureSound { get; set; } = null!;
    [Export]
    public AudioStream DrawSound { get; set; } = null!;
    [Export]
    public AudioStream TransitionSound { get; set; } = null!;
    [Export]
    public AudioStream ExtractionSound { get; set; } = null!;
    private ulong _lastHoverTick;

    public override void _Ready()
    {
        _hoverPlayer = GetNode<AudioStreamPlayer>("HoverPlayer");
        _actionPlayer = GetNode<AudioStreamPlayer>("ActionPlayer");
        _eventPlayer = GetNode<AudioStreamPlayer>("EventPlayer");
        _extractionPlayer = GetNode<AudioStreamPlayer>("ExtractionPlayer");


        ConnectButtons(GetTree().CurrentScene);
        GetTree().NodeAdded += ConnectNode;
    }

    public override void _ExitTree()
    {
        if (GetTree() is SceneTree tree)
        {
            tree.NodeAdded -= ConnectNode;
        }

        ReleasePlayer(_hoverPlayer);
        ReleasePlayer(_actionPlayer);
        ReleasePlayer(_eventPlayer);
        ReleasePlayer(_extractionPlayer);
    }

    public void PlayResult(RoundOutcome outcome)
    {
        PlayOn(
            _eventPlayer,
            outcome switch
            {
                RoundOutcome.PlayerWin => SuccessSound,
                RoundOutcome.PlayerLose => FailureSound,
                _ => DrawSound
            });
    }

    public void PlayTransition()
    {
        _extractionPlayer.Stop();
        PlayOn(_eventPlayer, TransitionSound);
    }

    /// <summary>Fit one inward sweep to the actual node movement duration.</summary>
    public void PlayExtraction(float durationSeconds)
    {
        _extractionPlayer.PitchScale = (float)ExtractionSound.GetLength()
            / Math.Max(0.1f, durationSeconds);
        PlayOn(_extractionPlayer, ExtractionSound);
    }

    private void ConnectNode(Node node)
    {
        if (node is Button button)
        {
            ConnectButton(button);
        }
    }

    private void ConnectButtons(Node root)
    {
        if (root is Button rootButton)
        {
            ConnectButton(rootButton);
        }

        foreach (Node child in root.GetChildren())
        {
            ConnectButtons(child);
        }
    }

    private void ConnectButton(Button button)
    {
        ulong instanceId = button.GetInstanceId();
        if (!_connectedButtons.Add(instanceId))
        {
            return;
        }

        button.MouseEntered += () => PlayHover(button);
        button.Pressed += () => PlayPressed(button);
    }

    private void PlayHover(Button button)
    {
        if (!button.Visible || button.Disabled)
        {
            return;
        }

        ulong now = Time.GetTicksMsec();
        if (now - _lastHoverTick < 45)
        {
            return;
        }

        _lastHoverTick = now;
        PlayOn(_hoverPlayer, HoverSound);
    }

    private void PlayPressed(Button button)
    {
        bool isSubmission = button.Name.ToString().Contains(
            "Confirm",
            StringComparison.OrdinalIgnoreCase)
            || button.Name.ToString().Contains(
                "Continue",
                StringComparison.OrdinalIgnoreCase)
            || button.Name.ToString().Contains(
                "NewGame",
                StringComparison.OrdinalIgnoreCase);
        PlayOn(_actionPlayer, isSubmission ? SubmitSound : SelectSound);
    }

    private static void PlayOn(AudioStreamPlayer player, AudioStream stream)
    {
        player.Stop();
        player.Stream = stream;
        player.Play();
    }

    private static void ReleasePlayer(AudioStreamPlayer player)
    {
        if (!GodotObject.IsInstanceValid(player))
        {
            return;
        }

        player.Stop();
        player.Stream = null;
    }
}
