using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

public sealed record TutorSpeechCue(
    string LineId,
    string Text,
    string AudioPath,
    float DurationSeconds,
    int VisibleCharacterCount,
    float CharactersPerSecond,
    string Delivery);

/// <summary>
/// Loads exact rendered-text cues. Dynamic dialogue lines have one manifest
/// entry per possible rendered counter so spoken and displayed text agree.
/// </summary>
public sealed class TutorSpeechCatalog
{
    private readonly Dictionary<string, TutorSpeechCue> _cues = new(
        StringComparer.Ordinal);

    public int Count => _cues.Count;

    public static TutorSpeechCatalog Load(string path)
    {
        string json = File.ReadAllText(path);
        SpeechManifest? manifest = JsonSerializer.Deserialize<SpeechManifest>(
            json,
            new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

        if (manifest is null || manifest.SchemaVersion != 1)
        {
            throw new InvalidDataException(
                $"Tutor speech manifest {Path.GetFileName(path)} has an unsupported schema.");
        }

        if (!manifest.AiGenerated)
        {
            throw new InvalidDataException(
                "Tutor speech manifest must disclose AI-generated audio.");
        }

        var catalog = new TutorSpeechCatalog();
        foreach (SpeechManifestEntry entry in manifest.Entries)
        {
            if (string.IsNullOrWhiteSpace(entry.LineId)
                || string.IsNullOrWhiteSpace(entry.Text)
                || string.IsNullOrWhiteSpace(entry.AudioPath)
                || entry.DurationSeconds <= 0.0f
                || entry.VisibleCharacterCount <= 0
                || entry.CharactersPerSecond <= 0.0f)
            {
                throw new InvalidDataException(
                    $"Tutor speech manifest contains an invalid entry for {entry.LineId}.");
            }

            var cue = new TutorSpeechCue(
                entry.LineId,
                entry.Text,
                entry.AudioPath,
                entry.DurationSeconds,
                entry.VisibleCharacterCount,
                entry.CharactersPerSecond,
                entry.Delivery);
            if (!catalog._cues.TryAdd(CreateKey(cue.LineId, cue.Text), cue))
            {
                throw new InvalidDataException(
                    $"Tutor speech cue {cue.LineId} is configured more than once for the same text.");
            }
        }

        if (catalog.Count == 0)
        {
            throw new InvalidDataException("Tutor speech manifest is empty.");
        }

        return catalog;
    }

    public TutorSpeechCue? Find(string lineId, string renderedText)
    {
        return _cues.GetValueOrDefault(CreateKey(lineId, renderedText));
    }

    public static string CreateKey(string lineId, string renderedText)
    {
        return lineId + "\u001F" + renderedText;
    }

    private sealed class SpeechManifest
    {
        [JsonPropertyName("schema_version")]
        public int SchemaVersion { get; set; }

        [JsonPropertyName("ai_generated")]
        public bool AiGenerated { get; set; }

        [JsonPropertyName("entries")]
        public List<SpeechManifestEntry> Entries { get; set; } = new();
    }

    private sealed class SpeechManifestEntry
    {
        [JsonPropertyName("line_id")]
        public string LineId { get; set; } = string.Empty;

        [JsonPropertyName("text")]
        public string Text { get; set; } = string.Empty;

        [JsonPropertyName("audio_path")]
        public string AudioPath { get; set; } = string.Empty;

        [JsonPropertyName("duration_seconds")]
        public float DurationSeconds { get; set; }

        [JsonPropertyName("visible_character_count")]
        public int VisibleCharacterCount { get; set; }

        [JsonPropertyName("characters_per_second")]
        public float CharactersPerSecond { get; set; }

        [JsonPropertyName("delivery")]
        public string Delivery { get; set; } = string.Empty;
    }
}
