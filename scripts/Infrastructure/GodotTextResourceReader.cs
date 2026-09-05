using Godot;
using System;
using System.IO;

/// <summary>
/// Reads authored text through Godot's virtual filesystem. Unlike System.IO,
/// this works both from loose editor files and from resources packed in a PCK.
/// </summary>
public static class GodotTextResourceReader
{
    public static string ReadAllText(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        if (!path.StartsWith("res://", StringComparison.Ordinal)
            && !path.StartsWith("user://", StringComparison.Ordinal))
        {
            return File.ReadAllText(path);
        }

        using Godot.FileAccess? file = Godot.FileAccess.Open(
            path,
            Godot.FileAccess.ModeFlags.Read);
        if (file is null)
        {
            throw new FileNotFoundException(
                $"Godot text resource could not be opened: {path}",
                path);
        }

        string text = file.GetAsText();
        Error error = file.GetError();
        if (error != Error.Ok)
        {
            throw new IOException(
                $"Godot text resource could not be read: {path} ({error}).");
        }

        return text;
    }
}
