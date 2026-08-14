using System;
using System.Collections.Generic;

/// <summary>
/// Tracks cumulative hover time for the currently open choice window using a
/// monotonic clock, so wall-clock changes cannot create negative durations.
/// </summary>
public sealed class HoverTracker
{
    private readonly Dictionary<int, long> _totalMilliseconds = new();
    private readonly Dictionary<int, long> _enteredAtMilliseconds = new();

    public bool Enter(int choice, long? timestampMilliseconds = null)
    {
        if (_enteredAtMilliseconds.ContainsKey(choice))
        {
            return false;
        }

        _enteredAtMilliseconds[choice] = timestampMilliseconds ?? Environment.TickCount64;
        return true;
    }

    public long? Exit(int choice, long? timestampMilliseconds = null)
    {
        if (!_enteredAtMilliseconds.TryGetValue(choice, out long startedAt))
        {
            return null;
        }

        long endedAt = timestampMilliseconds ?? Environment.TickCount64;
        long duration = Math.Max(0, endedAt - startedAt);
        _totalMilliseconds[choice] = _totalMilliseconds.GetValueOrDefault(choice) + duration;
        _enteredAtMilliseconds.Remove(choice);
        return duration;
    }

    public Dictionary<int, long> CompleteActiveHovers(long? timestampMilliseconds = null)
    {
        long endedAt = timestampMilliseconds ?? Environment.TickCount64;
        var completed = new Dictionary<int, long>();

        foreach ((int choice, long startedAt) in _enteredAtMilliseconds)
        {
            long duration = Math.Max(0, endedAt - startedAt);
            _totalMilliseconds[choice] = _totalMilliseconds.GetValueOrDefault(choice) + duration;
            completed[choice] = duration;
        }

        _enteredAtMilliseconds.Clear();
        return completed;
    }

    public Dictionary<int, long> Snapshot()
    {
        return new Dictionary<int, long>(_totalMilliseconds);
    }

    public void Reset()
    {
        _totalMilliseconds.Clear();
        _enteredAtMilliseconds.Clear();
    }
}
