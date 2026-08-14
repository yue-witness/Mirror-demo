using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;

public interface IPlayerProfileStore
{
    string? LastLoadWarning { get; }

    PlayerProfile LoadOrCreate();

    void Save(PlayerProfile profile);
}

/// <summary>
/// Stores the profile as readable JSON. Writes use a temporary file followed
/// by a same-directory move so a shutdown cannot leave a half-written profile.
/// </summary>
public sealed class JsonPlayerProfileStore : IPlayerProfileStore
{
    private const int MaximumSaveAttempts = 4;

    private readonly string _profilePath;
    private readonly string _backupPath;
    private readonly JsonSerializerOptions _jsonOptions;
    private bool _primaryProfileWasInvalid;

    public JsonPlayerProfileStore(string profilePath)
    {
        if (string.IsNullOrWhiteSpace(profilePath))
        {
            throw new ArgumentException("A profile path is required.", nameof(profilePath));
        }

        _profilePath = Path.GetFullPath(profilePath);
        _backupPath = _profilePath + ".bak";
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true
        };
        _jsonOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
    }

    public string? LastLoadWarning { get; private set; }

    public PlayerProfile LoadOrCreate()
    {
        LastLoadWarning = null;

        if (TryLoad(_profilePath, out PlayerProfile? profile, out string? primaryError))
        {
            return profile!;
        }

        if (File.Exists(_profilePath))
        {
            _primaryProfileWasInvalid = true;

            try
            {
                string quarantinedPath = QuarantineCorruptFile(_profilePath);
                LastLoadWarning =
                    $"Primary profile was invalid and moved to {quarantinedPath}.";
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException)
            {
                LastLoadWarning = "Primary profile was invalid but could not be quarantined: "
                    + exception.Message;
            }
        }

        if (TryLoad(_backupPath, out profile, out string? backupError))
        {
            LastLoadWarning = string.IsNullOrEmpty(LastLoadWarning)
                ? "Loaded the backup player profile."
                : LastLoadWarning + " Loaded the backup player profile.";
            return profile!;
        }

        if (File.Exists(_backupPath))
        {
            try
            {
                string quarantinedPath = QuarantineCorruptFile(_backupPath);
                LastLoadWarning = string.IsNullOrEmpty(LastLoadWarning)
                    ? $"Backup profile was invalid and moved to {quarantinedPath}."
                    : LastLoadWarning
                        + $" Backup profile was invalid and moved to {quarantinedPath}.";
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException)
            {
                LastLoadWarning = string.IsNullOrEmpty(LastLoadWarning)
                    ? "Backup profile was invalid but could not be quarantined: "
                        + exception.Message
                    : LastLoadWarning
                        + " Backup profile was invalid but could not be quarantined: "
                        + exception.Message;
            }
        }

        if (!string.IsNullOrEmpty(primaryError) || !string.IsNullOrEmpty(backupError))
        {
            LastLoadWarning ??= "No valid stored profile was available; created a new profile.";
        }

        return new PlayerProfile();
    }

    public void Save(PlayerProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        string? directory = Path.GetDirectoryName(_profilePath);
        if (string.IsNullOrEmpty(directory))
        {
            throw new InvalidOperationException("The player profile path has no parent directory.");
        }

        Directory.CreateDirectory(directory);

        string temporaryPath = _profilePath + ".tmp";
        string json = JsonSerializer.Serialize(profile, _jsonOptions);

        for (int attempt = 1; attempt <= MaximumSaveAttempts; attempt++)
        {
            try
            {
                using (var stream = new FileStream(
                    temporaryPath,
                    FileMode.Create,
                    FileAccess.Write,
                    FileShare.None))
                using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
                {
                    writer.Write(json);
                    writer.Flush();
                    stream.Flush(flushToDisk: true);
                }

                if (File.Exists(_profilePath) && !_primaryProfileWasInvalid)
                {
                    File.Copy(_profilePath, _backupPath, overwrite: true);
                }

                File.Move(temporaryPath, _profilePath, overwrite: true);
                _primaryProfileWasInvalid = false;
                return;
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException)
            {
                TryDeleteTemporaryFile(temporaryPath);

                if (attempt == MaximumSaveAttempts)
                {
                    throw;
                }

                // OneDrive and antivirus scanners may briefly hold the replaced
                // file. A short bounded delay preserves synchronous durability
                // without making UI input noticeably slower.
                Thread.Sleep(20 * attempt);
            }
        }
    }

    private bool TryLoad(
        string path,
        out PlayerProfile? profile,
        out string? error)
    {
        profile = null;
        error = null;

        if (!File.Exists(path))
        {
            return false;
        }

        try
        {
            string json = File.ReadAllText(path, Encoding.UTF8);
            profile = JsonSerializer.Deserialize<PlayerProfile>(json, _jsonOptions);

            if (profile is null)
            {
                error = "The stored JSON contained no profile.";
                return false;
            }

            if (profile.SchemaVersion > PlayerProfile.CurrentSchemaVersion)
            {
                error = $"Profile schema {profile.SchemaVersion} is newer than supported schema "
                    + $"{PlayerProfile.CurrentSchemaVersion}.";
                profile = null;
                return false;
            }

            return true;
        }
        catch (Exception exception) when (
            exception is IOException
            or UnauthorizedAccessException
            or JsonException)
        {
            error = exception.Message;
            return false;
        }
    }

    private static string QuarantineCorruptFile(string path)
    {
        string timestamp = DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss-fff");
        string quarantinedPath = $"{path}.corrupt-{timestamp}";
        File.Move(path, quarantinedPath);
        return quarantinedPath;
    }

    private static void TryDeleteTemporaryFile(string temporaryPath)
    {
        try
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            // The next retry uses FileMode.Create and will replace this file as
            // soon as the transient lock is released.
        }
    }
}
