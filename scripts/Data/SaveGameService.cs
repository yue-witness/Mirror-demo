using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;

/// <summary>
/// Stores one active demo snapshot with checksum validation and backup recovery.
/// </summary>
public sealed class SaveGameService
{
    private const int MaximumWriteAttempts = 4;

    private readonly string _primaryPath;
    private readonly string _backupPath;
    private readonly JsonSerializerOptions _jsonOptions;

    public SaveGameService(string primaryPath)
    {
        if (string.IsNullOrWhiteSpace(primaryPath))
        {
            throw new ArgumentException("A save path is required.", nameof(primaryPath));
        }

        _primaryPath = Path.GetFullPath(primaryPath);
        _backupPath = _primaryPath + ".bak";
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true
        };
        _jsonOptions.Converters.Add(
            new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
    }

    public string? LastWarning { get; private set; }

    public DemoSaveState? LoadLatest()
    {
        LastWarning = null;

        if (TryLoad(_primaryPath, out DemoSaveState? state, out string? primaryError))
        {
            return state;
        }

        if (File.Exists(_primaryPath))
        {
            TryQuarantine(_primaryPath, "Primary save was invalid");
        }

        if (TryLoad(_backupPath, out state, out string? backupError))
        {
            LastWarning = AppendWarning(LastWarning, "Loaded the recovery backup.");
            return state;
        }

        if (File.Exists(_backupPath))
        {
            TryQuarantine(_backupPath, "Backup save was invalid");
        }

        if (!string.IsNullOrEmpty(primaryError) || !string.IsNullOrEmpty(backupError))
        {
            LastWarning = AppendWarning(
                LastWarning,
                "No valid unfinished save could be recovered.");
        }

        return null;
    }

    public DemoSaveState? LoadActive()
    {
        DemoSaveState? state = LoadLatest();
        return state is { IsComplete: false } ? state : null;
    }

    public void WriteAtomic(DemoSaveState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        string? directory = Path.GetDirectoryName(_primaryPath);
        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new InvalidOperationException("The save path has no parent directory.");
        }

        Directory.CreateDirectory(directory);
        string temporaryPath = _primaryPath + ".tmp";
        DemoSaveState finalized = state with
        {
            SchemaVersion = DemoSaveState.CurrentSchemaVersion,
            Checksum = CalculateChecksum(state)
        };
        byte[] json = Encoding.UTF8.GetBytes(
            JsonSerializer.Serialize(finalized, _jsonOptions));

        for (int attempt = 1; attempt <= MaximumWriteAttempts; attempt++)
        {
            try
            {
                using (var stream = new FileStream(
                    temporaryPath,
                    FileMode.Create,
                    FileAccess.Write,
                    FileShare.None))
                {
                    stream.Write(json);
                    stream.Flush(flushToDisk: true);
                }

                if (File.Exists(_primaryPath))
                {
                    File.Replace(temporaryPath, _primaryPath, _backupPath);
                }
                else
                {
                    File.Move(temporaryPath, _primaryPath);
                }

                return;
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException)
            {
                TryDelete(temporaryPath);

                if (attempt == MaximumWriteAttempts)
                {
                    throw;
                }

                Thread.Sleep(20 * attempt);
            }
        }
    }

    public void DeleteActive()
    {
        TryDelete(_primaryPath);
        TryDelete(_backupPath);
        TryDelete(_primaryPath + ".tmp");
    }

    private bool TryLoad(
        string path,
        out DemoSaveState? state,
        out string? error)
    {
        state = null;
        error = null;

        if (!File.Exists(path))
        {
            return false;
        }

        try
        {
            string json = File.ReadAllText(path, Encoding.UTF8);
            state = JsonSerializer.Deserialize<DemoSaveState>(json, _jsonOptions);

            if (state is null)
            {
                error = "The save JSON contained no state.";
                return false;
            }

            if (state.SchemaVersion != DemoSaveState.CurrentSchemaVersion)
            {
                error = $"Unsupported save schema {state.SchemaVersion}.";
                state = null;
                return false;
            }

            string expected = CalculateChecksum(state);
            if (!CryptographicOperations.FixedTimeEquals(
                    Encoding.ASCII.GetBytes(expected),
                    Encoding.ASCII.GetBytes(state.Checksum ?? string.Empty)))
            {
                error = "The save checksum did not match.";
                state = null;
                return false;
            }

            if (string.IsNullOrWhiteSpace(state.SaveId)
                || state.Stats is null
                || state.DialogueHistory is null)
            {
                error = "The save is missing required fields.";
                state = null;
                return false;
            }

            return true;
        }
        catch (Exception exception) when (
            exception is IOException
            or UnauthorizedAccessException
            or JsonException
            or ArgumentException)
        {
            error = exception.Message;
            state = null;
            return false;
        }
    }

    private string CalculateChecksum(DemoSaveState state)
    {
        DemoSaveState unsigned = state with { Checksum = string.Empty };
        byte[] payload = Encoding.UTF8.GetBytes(
            JsonSerializer.Serialize(unsigned, _jsonOptions));
        return Convert.ToHexString(SHA256.HashData(payload));
    }

    private void TryQuarantine(string path, string reason)
    {
        try
        {
            string timestamp = DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss-fff");
            string directory = Path.GetDirectoryName(path)!;
            string fileName = Path.GetFileNameWithoutExtension(_primaryPath);
            string quarantined = Path.Combine(
                directory,
                $"{fileName}.corrupt-{timestamp}.json");
            File.Move(path, quarantined);
            LastWarning = AppendWarning(
                LastWarning,
                $"{reason} and was isolated as {Path.GetFileName(quarantined)}.");
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            LastWarning = AppendWarning(
                LastWarning,
                $"{reason}, but isolation failed: {exception.Message}");
        }
    }

    private static string AppendWarning(string? existing, string warning)
    {
        return string.IsNullOrWhiteSpace(existing)
            ? warning
            : existing + " " + warning;
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            // A later write retries the same explicit path. Deletion failure is
            // non-fatal here and never expands to a broader directory target.
        }
    }
}
