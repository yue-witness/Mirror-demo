using Godot;
using System;

/// <summary>
/// Plays the approved ambient score on a dedicated Music bus. Tutor speech
/// temporarily lowers the music so narration remains the authoritative cue.
/// </summary>
public partial class BackgroundMusicPlayer : AudioStreamPlayer
{
    public const float NormalVolumeDb = -20.0f;
    public const float DuckedVolumeDb = -28.0f;

    private const float FadeSpeedDbPerSecond = 16.0f;

    private AudioStreamPlayer _tutorSpeech = null!;

    public bool IsDuckingTutorSpeech { get; private set; }

    public float TargetVolumeDb => IsDuckingTutorSpeech
        ? DuckedVolumeDb
        : NormalVolumeDb;

    public override void _Ready()
    {
        _tutorSpeech = GetNode<AudioStreamPlayer>("../TutorSpeechPlayer");
        Bus = "Music";
        VolumeDb = NormalVolumeDb;

        if (Stream is not AudioStreamOggVorbis music)
        {
            throw new InvalidOperationException(
                "Background music must use an imported OGG Vorbis stream.");
        }

        music.Loop = true;
        Play();
        SetProcess(true);
    }

    public override void _Process(double delta)
    {
        IsDuckingTutorSpeech = _tutorSpeech.Playing
            && _tutorSpeech.Stream is not null;
        VolumeDb = Mathf.MoveToward(
            VolumeDb,
            TargetVolumeDb,
            FadeSpeedDbPerSecond * (float)delta);
    }

    public override void _ExitTree()
    {
        Stop();
    }
}
