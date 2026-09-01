using Godot;
using System;
using System.Collections.Generic;

/// <summary>
/// Central UI sound router. It discovers existing buttons without changing
/// their scene layout and reserves separate channels for overlapping cues.
/// </summary>
public partial class UiAudioController : Node
{
    private const string AudioRoot = "res://assets/audio/ui/";

    private readonly HashSet<ulong> _connectedButtons = new();
    private AudioStreamPlayer _hoverPlayer = null!;
    private AudioStreamPlayer _actionPlayer = null!;
    private AudioStreamPlayer _eventPlayer = null!;
    private AudioStream _hover = null!;
    private AudioStream _select = null!;
    private AudioStream _submit = null!;
    private AudioStream _success = null!;
    private AudioStream _failure = null!;
    private AudioStream _draw = null!;
    private AudioStream _transition = null!;
    private ulong _lastHoverTick;

    public override void _Ready()
    {
        _hoverPlayer = CreatePlayer("HoverPlayer", -10.0f);
        _actionPlayer = CreatePlayer("ActionPlayer", -7.0f);
        _eventPlayer = CreatePlayer("EventPlayer", -5.0f);

        _hover = LoadStream("hover.wav");
        _select = LoadStream("select.wav");
        _submit = LoadStream("submit.wav");
        _success = LoadStream("success.wav");
        _failure = LoadStream("failure.wav");
        _draw = LoadStream("draw.wav");
        _transition = LoadStream("transition.wav");

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
    }

    public void PlayResult(RoundOutcome outcome)
    {
        PlayOn(
            _eventPlayer,
            outcome switch
            {
                RoundOutcome.PlayerWin => _success,
                RoundOutcome.PlayerLose => _failure,
                _ => _draw
            });
    }

    public void PlayTransition()
    {
        PlayOn(_eventPlayer, _transition);
    }

    private AudioStreamPlayer CreatePlayer(string playerName, float volumeDb)
    {
        var player = new AudioStreamPlayer
        {
            Name = playerName,
            VolumeDb = volumeDb
        };
        AddChild(player);
        return player;
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
        PlayOn(_hoverPlayer, _hover);
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
        PlayOn(_actionPlayer, isSubmission ? _submit : _select);
    }

    private AudioStream LoadStream(string fileName)
    {
        return ResourceLoader.Load<AudioStream>(AudioRoot + fileName)
            ?? throw new InvalidOperationException(
                $"UI audio asset could not be loaded: {AudioRoot}{fileName}");
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
