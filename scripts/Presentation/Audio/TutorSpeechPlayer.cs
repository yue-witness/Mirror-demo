using Godot;
using System;

/// <summary>
/// Owns Tutor speech playback so dialogue-only pages and gameplay share one
/// audio channel. Essential cues replace the previous cue; optional feedback
/// waits for a quiet opportunity and never interrupts an active explanation.
/// </summary>
public partial class TutorSpeechPlayer : AudioStreamPlayer
{
    [Export(PropertyHint.Range, "0,10,0.25")]
    public float StandardSpeechGapSeconds { get; set; } = 2.5f;

    public bool CanPresentDialogue(string lineId)
    {
        return TutorPresentationPolicy.ResolveSpeechMode(lineId) != TutorSpeechMode.Standard
            || (!Playing && (_lastVoiceStartedTicks == 0
                || Time.GetTicksMsec() - _lastVoiceStartedTicks
                    >= (ulong)(StandardSpeechGapSeconds * 1000.0f)));
    }

    [Export(PropertyHint.File, "*.json")]
    public string ManifestPath { get; set; } =
        "res://assets/audio/tutor/manifest.json";

    private TutorSpeechCatalog? _catalog;
    private string _currentKey = string.Empty;
    private float _currentDurationSeconds;
    private ulong _lastVoiceStartedTicks;

    public string CurrentLineId { get; private set; } = string.Empty;

    public float CurrentDurationSeconds => _currentDurationSeconds;

    public override void _Ready()
    {
        _catalog = TutorSpeechCatalog.Load(
            ManifestPath,
            GodotTextResourceReader.ReadAllText);
    }

    public override void _ExitTree()
    {
        StopDialogue();
    }

    /// <summary>
    /// Plays the exact cue for a rendered line and returns its measured length.
    /// S-17 is intentionally silent and clears any active Tutor cue.
    /// </summary>
    public float PlayDialogue(string lineId, string speaker, string text)
    {
        if (IsS17(speaker) || _catalog is null)
        {
            StopDialogue();
            return 0.0f;
        }

        TutorSpeechMode speechMode =
            TutorPresentationPolicy.ResolveSpeechMode(lineId);
        ulong now = Time.GetTicksMsec();
        // Refusing optional chatter must never stop an active explanation.
        if (!CanPresentDialogue(lineId))
        {
            return 0.0f;
        }

        if (speechMode == TutorSpeechMode.Silent)
        {
            StopDialogue();
            return 0.0f;
        }

        TutorSpeechCue? cue = _catalog.Find(lineId, text);
        if (cue is null)
        {
            GD.PushWarning(
                $"Tutor speech cue is missing for {lineId}: {text}");
            StopDialogue();
            return 0.0f;
        }

        string key = TutorSpeechCatalog.CreateKey(cue.LineId, cue.Text);
        if (key == _currentKey)
        {
            return _currentDurationSeconds;
        }

        Stop();
        Stream = LoadStream(cue.AudioPath);
        _currentKey = key;
        CurrentLineId = cue.LineId;
        _currentDurationSeconds = cue.DurationSeconds;
        _lastVoiceStartedTicks = now;
        Play();
        return _currentDurationSeconds;
    }

    public void StopDialogue(bool clearCue = true)
    {
        Stop();

        if (!clearCue)
        {
            return;
        }

        Stream = null;
        _currentKey = string.Empty;
        CurrentLineId = string.Empty;
        _currentDurationSeconds = 0.0f;
    }

    private AudioStream LoadStream(string resourcePath)
    {
        AudioStream stream = ResourceLoader.Load<AudioStream>(resourcePath)
            ?? throw new InvalidOperationException(
                $"Tutor speech asset could not be loaded: {resourcePath}");
        return stream;
    }

    private static bool IsS17(string speaker)
    {
        return speaker.Equals("S-17", StringComparison.OrdinalIgnoreCase)
            || speaker.Equals("S17", StringComparison.OrdinalIgnoreCase);
    }
}
